using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarginaliaMod
{
    // A floating panel listing what's been put on the table: board labels, grids and lines.
    // Each can be found (the camera goes to it), fetched to the middle of the view, edited or
    // rubbed out, and several at once can be selected to fetch or delete. Anything entirely
    // off the table is marked, so it can be brought back or deleted; the camera only gets as
    // near as it's allowed. Drag it by its title bar, resize it by its corner.
    public static class BoardList
    {
        private const string LayoutName = "board";
        private const float DefaultWidth = 440f;
        private const float MinWidth = 380f;
        private const float MinHeight = 260f;
        private const float Pad = 10f;
        private const float TitleH = 30f;
        private const float RowH = 28f;
        private const float GripSize = 16f;
        private const float TipWidth = 280f;
        private const float FlashSeconds = 2.5f;
        private const string SearchControlName = "MarginaliaBoardSearch";

        private enum Kind { Label, Grid, Line }

        private enum Show { All, Labels, Grids, Lines, Off }

        private sealed class Item
        {
            public Kind Kind;
            public string Id;
            public string Text;
            public Rect Bounds;
            public bool Off;
            public BoardLabels.Data Label;
            public Rulers.Data Ruler;
        }

        private enum Grab { None, Move, Resize }

        // Open, as remembered between sessions; it only shows while a game is on the table.
        private static bool _open;
        private static bool _typing;

        public static bool IsOpen => _open && BoardLabels.IsAttached;

        // The search box has the keyboard, so the game's hotkeys should stay off.
        public static bool Typing => _typing && IsOpen;

        // In IMGUI's scaled coordinates. x < 0 until first placed, at the right edge.
        private static Rect _rect = new Rect(-1f, 420f, DefaultWidth, 0f);
        private static Grab _grab;
        private static Vector2 _grabOffset;
        private static PanelPointer _pointer;

        private static Show _show;
        private static string _query = "";
        private static Vector2 _scroll;
        private static readonly HashSet<string> Selected = new HashSet<string>();
        private static bool _confirmDelete;
        private static string _hovered;
        private static string _flashed;
        private static float _flashUntil;
        private static Action _pending;
        private static string _tip;

        private static List<Item> _items = new List<Item>();
        private static int _itemsFrame = -1;

        private static GUIStyle _title, _summary, _tabOn, _tabOff, _small, _danger, _kind, _text, _off, _checkOn, _checkOff, _placeholder,
            _searchField, _tipStyle, _empty;

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.list",
                Order = 38,
                Label = _ => IsOpen ? "Hide board list" : "Board list…",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler || t.Kind == TargetKind.BoardLabel,
                Execute = _ =>
                {
                    if (IsOpen)
                        Close();
                    else
                        Open();
                }
            });
        }

        public static bool Contains(Vector2 guiPoint) => IsOpen && (_grab != Grab.None || _rect.Contains(guiPoint));

        // Back where it was last time, open or not.
        public static void Restore()
        {
            var saved = PanelLayout.Get(LayoutName);
            if (saved == null)
                return;
            if (saved.X >= 0f)
                _rect = new Rect(saved.X, saved.Y, saved.Width > 0f ? saved.Width : DefaultWidth, Mathf.Max(0f, saved.Height));
            _open = saved.Open;
        }

        private static void SaveLayout() => PanelLayout.Save(LayoutName, _rect, _open);

        private static void Open()
        {
            _open = true;
            _confirmDelete = false;
            SaveLayout();
        }

        public static void Close()
        {
            if (!_open)
                return;
            _open = false;
            _grab = Grab.None;
            if (_typing)
                GUIUtility.keyboardControl = 0;
            _typing = false;
            BoardLabels.Highlight(null);
            Rulers.Highlight(null);
            SaveLayout();
        }

        // ---- What's on the table

        // Gathered once a frame, so every IMGUI event of a frame sees the same rows.
        private static List<Item> Items()
        {
            if (_itemsFrame == Time.frameCount)
                return _items;
            _itemsFrame = Time.frameCount;
            var items = new List<Item>();
            foreach (var label in BoardLabels.All)
            {
                var bounds = BoardLabels.Bounds(label);
                items.Add(new Item
                {
                    Kind = Kind.Label,
                    Id = label.Id,
                    Text = FirstLine(label.Text),
                    Bounds = bounds,
                    Off = BoardLabels.OffTable(bounds),
                    Label = label
                });
            }
            foreach (var ruler in Rulers.All)
            {
                var bounds = Rulers.Bounds(ruler);
                items.Add(new Item
                {
                    Kind = ruler.Shape == Rulers.Shape.Grid ? Kind.Grid : Kind.Line,
                    Id = ruler.Id,
                    Text = Rulers.Describe(ruler),
                    Bounds = bounds,
                    Off = BoardLabels.OffTable(bounds),
                    Ruler = ruler
                });
            }
            _items = items;
            // Forget selections of things that are gone.
            Selected.RemoveWhere(id => items.All(i => i.Id != id));
            if (Selected.Count == 0)
                _confirmDelete = false;
            return _items;
        }

        private static IEnumerable<Item> Shown(Show show) =>
            Items().Where(i => Matches(i, show) &&
                               (_query.Length == 0 || i.Text.IndexOf(_query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0));

        private static bool Matches(Item item, Show show)
        {
            switch (show)
            {
                case Show.Labels: return item.Kind == Kind.Label;
                case Show.Grids: return item.Kind == Kind.Grid;
                case Show.Lines: return item.Kind == Kind.Line;
                case Show.Off: return item.Off;
                default: return true;
            }
        }

        private static string FirstLine(string text)
        {
            text = (text ?? "").Trim();
            int newline = text.IndexOf('\n');
            return newline >= 0 ? text.Substring(0, newline).TrimEnd() + " …" : text;
        }

        // ---- Doing things

        // The camera goes to it, and it lights up for a moment.
        private static void GoTo(Item item)
        {
            Watchman.Get<CamOperator>()?.PointCameraAtPosition_CurrentZoom(BoardLabels.ToWorld(item.Bounds.center), 0.5f, null);
            _flashed = item.Id;
            _flashUntil = Time.unscaledTime + FlashSeconds;
        }

        // To the middle of the view (and onto the table), a little apart if there are several.
        private static void Fetch(IEnumerable<Item> items)
        {
            if (!BoardLabels.ViewCentre(out var centre))
                return;
            int n = 0;
            bool labels = false, rulers = false;
            foreach (var item in items)
            {
                var to = centre + new Vector2(24f, -24f) * n++;
                if (item.Label != null)
                {
                    BoardLabels.MoveOnTable(item.Label, to);
                    labels = true;
                }
                else if (item.Ruler != null)
                {
                    Rulers.MoveOnTable(item.Ruler, Rulers.Corner(item.Ruler) + (to - Rulers.Bounds(item.Ruler).center));
                    rulers = true;
                }
                _flashed = item.Id;
                _flashUntil = Time.unscaledTime + FlashSeconds;
            }
            if (labels)
                BoardLabels.Save();
            if (rulers)
                Rulers.Save();
        }

        private static void Edit(Item item)
        {
            if (item.Label != null)
            {
                var label = item.Label;
                MarginaliaController.Instance.OpenEditor("Board label", "Alt + drag the label to move it", label.Text,
                    text => BoardLabels.SetText(label, text),
                    () => BoardLabels.Remove(label));
            }
            else if (item.Ruler != null)
            {
                RulerMenuItems.OpenSizer(item.Ruler, false);
            }
        }

        private static void Delete(IEnumerable<Item> items)
        {
            var list = items.ToList();
            var labels = list.Where(i => i.Label != null).Select(i => i.Label).ToList();
            var rulers = list.Where(i => i.Ruler != null).Select(i => i.Ruler).ToList();
            if (labels.Count > 0)
                BoardLabels.RemoveMany(labels);
            if (rulers.Count > 0)
                Rulers.RemoveMany(rulers);
            foreach (var item in list)
                Selected.Remove(item.Id);
            _confirmDelete = false;
            _itemsFrame = -1;
        }

        // ---- Drawing

        // Clicking the board (or another panel) hands the keyboard back to the game. Called
        // before anything else is drawn, since whatever is drawn first may use up the click.
        public static void LetGoOfKeyboard(bool usable)
        {
            var e = Event.current;
            if (IsOpen && usable && Typing && e.type == EventType.MouseDown && !_rect.Contains(e.mousePosition))
                GUIUtility.keyboardControl = 0;
        }

        public static void Draw(Rect screen, bool interactive)
        {
            if (!IsOpen)
                return;
            EnsureStyles();
            var e = Event.current;
            _tip = null;
            _hovered = null;
            var items = Items();

            // A release outside the game window never reaches IMGUI.
            if (_grab != Grab.None && Mouse.current != null && !Mouse.current.leftButton.isPressed)
                EndGrab();
            Place(screen);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = interactive;
            if (interactive)
                HandleGrab(e, screen);

            GUI.Box(_rect, GUIContent.none, Styles.Panel);
            float x = _rect.x + Pad, inner = _rect.width - Pad * 2f, y = _rect.y + 6f;

            // Title bar: the handle to drag it by, and Close.
            GUI.Label(new Rect(x, y, inner - 34f, TitleH), "On the table", _title);
            if (Button(new Rect(x + inner - 26f, y + 3f, 26f, 24f), "×", _small, "Close"))
                _pending = Close;
            y += TitleH + 2f;

            int labels = items.Count(i => i.Kind == Kind.Label), grids = items.Count(i => i.Kind == Kind.Grid);
            int lines = items.Count(i => i.Kind == Kind.Line), off = items.Count(i => i.Off);
            string summary = Plural(labels, "label") + " · " + Plural(grids, "grid") + " · " + Plural(lines, "line") +
                             (off > 0 ? " · " + off + " off the table" : "");
            float summaryH = _summary.CalcHeight(new GUIContent(summary), inner);
            GUI.Label(new Rect(x, y, inner, summaryH), summary, off > 0 ? _off : _summary);
            y += summaryH + 6f;

            y = DrawSearch(x, y, inner);
            y = DrawTabs(x, y, inner, labels, grids, lines, off);
            y = DrawSelection(x, y, inner);
            GUI.Box(new Rect(x, y - 4f, inner, 1f), GUIContent.none, Styles.Rule);
            DrawRows(new Rect(x, y, inner, _rect.yMax - Pad - y));
            DrawGrip();
            DrawTip(screen);

            if (e.type == EventType.Repaint)
            {
                _typing = interactive && GUI.GetNameOfFocusedControl() == SearchControlName;
                // What's under the pointer in the list, or what was just found, lights up on the table.
                string lit = _hovered ?? (Time.unscaledTime < _flashUntil ? _flashed : null);
                BoardLabels.Highlight(lit != null && BoardLabels.Get(lit) != null ? lit : null);
                Rulers.Highlight(lit != null && Rulers.Get(lit) != null ? lit : null);
            }
            GUI.enabled = wasEnabled;

            var pending = _pending;
            _pending = null;
            if (interactive)
                pending?.Invoke();
        }

        private static string Plural(int n, string word) => n + " " + word + (n == 1 ? "" : "s");

        private static float DrawSearch(float x, float y, float inner)
        {
            const float fieldH = 26f, allW = 92f;
            var field = new Rect(x, y, inner - allW - 6f, fieldH);
            GUI.Box(field, GUIContent.none, Styles.Field);
            GUI.SetNextControlName(SearchControlName);
            string query = GUI.TextField(field, _query, _searchField);
            if (query != _query)
            {
                _query = query;
                _scroll = Vector2.zero;
            }
            if (string.IsNullOrEmpty(_query))
                GUI.Label(new Rect(field.x + 8f, field.y, field.width - 8f, fieldH), "Search labels…", _placeholder);

            // Selects everything listed below (or, once it all is, nothing).
            var shown = Shown(_show).ToList();
            bool all = shown.Count > 0 && shown.All(i => Selected.Contains(i.Id));
            if (Button(new Rect(field.xMax + 6f, y, allW, fieldH), all ? "select none" : "select all", _small,
                    all ? "Clear the selection" : "Select everything listed"))
                _pending = () =>
                {
                    if (all)
                        Selected.Clear();
                    else
                        Selected.UnionWith(shown.Select(i => i.Id));
                    _confirmDelete = false;
                };
            return y + fieldH + 6f;
        }

        private static float DrawTabs(float x, float y, float inner, int labels, int grids, int lines, int off)
        {
            const float h = 22f;
            var tabs = new List<(Show Show, string Text)>
            {
                (Show.All, "All " + _items.Count),
                (Show.Labels, "Labels " + labels),
                (Show.Grids, "Grids " + grids),
                (Show.Lines, "Lines " + lines)
            };
            if (off > 0 || _show == Show.Off)
                tabs.Add((Show.Off, "Off the table " + off));
            float tx = x, ty = y;
            foreach (var tab in tabs)
            {
                var style = tab.Show == _show ? _tabOn : _tabOff;
                float w = style.CalcSize(new GUIContent(tab.Text)).x + 16f;
                if (tx > x && tx + w > x + inner)
                {
                    tx = x;
                    ty += h + 4f;
                }
                var show = tab.Show;
                if (GUI.Button(new Rect(tx, ty, w, h), tab.Text, style) && show != _show)
                    _pending = () =>
                    {
                        _show = show;
                        _scroll = Vector2.zero;
                    };
                tx += w + 4f;
            }
            return ty + h + 8f;
        }

        // What can be done with the selection: bring it here, or delete it (asked twice).
        private static float DrawSelection(float x, float y, float inner)
        {
            if (Selected.Count == 0)
                return y;
            const float h = 24f;
            var chosen = _items.Where(i => Selected.Contains(i.Id)).ToList();
            float bx = x + inner;
            if (_confirmDelete)
            {
                bx -= 60f;
                if (Button(new Rect(bx, y, 60f, h), "keep", _small, "Don't delete them"))
                    _pending = () => _confirmDelete = false;
                bx -= 70f + 6f;
                if (Button(new Rect(bx, y, 70f, h), "delete", _danger, "Delete them for good"))
                    _pending = () => Delete(chosen);
                GUI.Label(new Rect(x, y, bx - x - 6f, h), "Delete " + Plural(chosen.Count, "thing") + " for good?", _off);
            }
            else
            {
                bx -= 64f;
                if (Button(new Rect(bx, y, 64f, h), "delete", _danger, "Delete the selected things"))
                    _pending = () => _confirmDelete = true;
                bx -= 96f + 6f;
                if (Button(new Rect(bx, y, 96f, h), "bring here", _small, "Move them to the middle of the view (onto the table)"))
                    _pending = () => Fetch(chosen);
                GUI.Label(new Rect(x, y, bx - x - 6f, h), chosen.Count + " selected", _summary);
            }
            return y + h + 8f;
        }

        private static void DrawRows(Rect area)
        {
            if (area.height < 20f)
                return;
            var shown = Shown(_show).ToList();
            float width = area.width - 16f;
            var e = Event.current;
            bool mouseIn = area.Contains(e.mousePosition);
            // Rows cut off at the list's edges mustn't take a press from outside it.
            bool wasEnabled = GUI.enabled;
            if (e.type == EventType.MouseDown && !mouseIn)
                GUI.enabled = false;
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0f, 0f, width, Mathf.Max(shown.Count * RowH + 4f, area.height)), false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            var mouse = e.mousePosition;
            float top = _scroll.y, bottom = _scroll.y + area.height;
            for (int n = 0; n < shown.Count; n++)
            {
                float y = n * RowH;
                if (y + RowH < top || y > bottom)
                    continue;
                var item = shown[n];
                var row = new Rect(0f, y, width, RowH);
                if (mouseIn && row.Contains(mouse))
                    _hovered = item.Id;
                DrawRow(item, row, mouseIn);
            }
            if (shown.Count == 0)
                GUI.Label(new Rect(4f, 4f, width - 8f, 40f), EmptyText(), _empty);
            GUI.EndScrollView();
            GUI.enabled = wasEnabled;
        }

        private static string EmptyText()
        {
            if (_items.Count == 0)
                return "Nothing on the table yet. Alt + right-click the table to write on it or add a grid or a line.";
            return _query.Length > 0 ? "Nothing matches that search." : "Nothing of this kind.";
        }

        private static void DrawRow(Item item, Rect row, bool tips)
        {
            float cy = row.y + (RowH - 20f) / 2f;
            float x = row.x + 2f;

            // Selected or not.
            bool selected = Selected.Contains(item.Id);
            if (Button(new Rect(x, cy + 2f, 16f, 16f), "", selected ? _checkOn : _checkOff, selected ? "Unselect" : "Select", tips))
                _pending = () =>
                {
                    if (!Selected.Remove(item.Id))
                        Selected.Add(item.Id);
                    _confirmDelete = false;
                };
            x += 22f;

            string kind = item.Kind == Kind.Label ? "label" : item.Kind == Kind.Grid ? "grid" : "line";
            GUI.Label(new Rect(x, row.y, 40f, RowH), kind, _kind);
            x += 42f;

            // Buttons at the end: go to it, bring it here, edit, rub out.
            float bx = row.xMax;
            bx -= 22f;
            if (Button(new Rect(bx, cy, 22f, 20f), "×", _danger, "Delete it", tips))
                _pending = () => Delete(new[] { item });
            bx -= 36f + 4f;
            if (Button(new Rect(bx, cy, 36f, 20f), "edit", _small, item.Kind == Kind.Label ? "Edit the text" : "Change its size", tips))
                _pending = () => Edit(item);
            bx -= 40f + 4f;
            if (Button(new Rect(bx, cy, 40f, 20f), "here", _small, "Bring it to the middle of the view", tips))
                _pending = () => Fetch(new[] { item });
            bx -= 30f + 4f;
            string goTip = item.Off ? "It's off the table: the camera goes as near as it can" : "Go to it";
            if (Button(new Rect(bx, cy, 30f, 20f), "go", _small, goTip, tips))
                _pending = () => GoTo(item);

            // Its text (click it to go there), and whether it's off the table.
            float offW = item.Off ? 76f : 0f;
            var textRect = new Rect(x, row.y, bx - x - 6f - offW, RowH);
            if (Button(textRect, item.Text, _text, goTip, tips))
                _pending = () => GoTo(item);
            if (_text.CalcSize(new GUIContent(item.Text)).x > textRect.width && tips)
                TipIfHovered(textRect, item.Text);
            if (item.Off)
                GUI.Label(new Rect(textRect.xMax + 4f, row.y, offW, RowH), "off the table", _off);
        }

        private static bool Button(Rect rect, string text, GUIStyle style, string tip, bool tips = true)
        {
            if (tips && tip != null)
                TipIfHovered(rect, tip);
            return GUI.Button(rect, text, style);
        }

        private static void TipIfHovered(Rect rect, string tip)
        {
            if (rect.Contains(Event.current.mousePosition))
                _tip = tip;
        }

        // Beside the pointer, kept on screen. Drawn from the style, not GUI.Box, so it takes no
        // IMGUI id on Repaint that other events don't.
        private static void DrawTip(Rect screen)
        {
            if (string.IsNullOrEmpty(_tip) || Event.current.type != EventType.Repaint)
                return;
            var content = new GUIContent(_tip);
            float w = Mathf.Min(_tipStyle.CalcSize(content).x, TipWidth);
            float h = _tipStyle.CalcHeight(content, w);
            var mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x + 14f, mouse.y + 18f, w, h);
            if (rect.xMax > screen.width)
                rect.x = mouse.x - w - 6f;
            if (rect.yMax > screen.height)
                rect.y = mouse.y - h - 6f;
            _tipStyle.Draw(rect, content, false, false, false, false);
        }

        private static void DrawGrip()
        {
            float gx = _rect.xMax - GripSize, gy = _rect.yMax - GripSize;
            foreach (var (dx, dy) in new[] { (11f, 3f), (7f, 7f), (11f, 7f), (3f, 11f), (7f, 11f), (11f, 11f) })
                GUI.Box(new Rect(gx + dx, gy + dy, 2f, 2f), GUIContent.none, Styles.Rule);
        }

        // ---- Placing and dragging

        // First shown at the right edge; always kept whole on screen.
        private static void Place(Rect screen)
        {
            if (_rect.height <= 0f)
                _rect.height = Mathf.Min(460f, screen.height - _rect.y - 40f);
            if (_rect.x < 0f)
                _rect.x = Mathf.Max(0f, screen.width - _rect.width - 24f);
            _rect.width = Mathf.Clamp(_rect.width, MinWidth, Mathf.Max(MinWidth, screen.width));
            _rect.height = Mathf.Clamp(_rect.height, MinHeight, Mathf.Max(MinHeight, screen.height));
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, screen.width - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, screen.height - _rect.height));
        }

        // Move by the title bar (away from Close), resize by the bottom-right corner.
        private static void HandleGrab(Event e, Rect screen)
        {
            var titleBar = new Rect(_rect.x, _rect.y, _rect.width - Pad - 30f, TitleH + 6f);
            var grip = new Rect(_rect.xMax - GripSize, _rect.yMax - GripSize, GripSize, GripSize);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _pointer.Begin(e.mousePosition);
                if (grip.Contains(e.mousePosition))
                {
                    _grab = Grab.Resize;
                    _grabOffset = new Vector2(_rect.xMax, _rect.yMax) - e.mousePosition;
                    e.Use();
                }
                else if (titleBar.Contains(e.mousePosition))
                {
                    _grab = Grab.Move;
                    _grabOffset = _rect.position - e.mousePosition;
                    e.Use();
                }
            }
            else if (_grab != Grab.None && e.type == EventType.MouseDrag)
            {
                var to = _pointer.Read(e.mousePosition, screen) + _grabOffset;
                if (_grab == Grab.Move)
                {
                    // Not past the left or top edge: x < 0 means "not placed yet".
                    _rect.position = new Vector2(Mathf.Max(0f, to.x), Mathf.Max(0f, to.y));
                }
                else
                {
                    _rect.width = Mathf.Clamp(to.x - _rect.x, MinWidth, Mathf.Max(MinWidth, screen.width - _rect.x));
                    _rect.height = Mathf.Clamp(to.y - _rect.y, MinHeight, Mathf.Max(MinHeight, screen.height - _rect.y));
                }
                e.Use();
            }
            else if (_grab != Grab.None && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                EndGrab();
            }
        }

        // Done moving or resizing: remember where it ended up.
        private static void EndGrab()
        {
            _grab = Grab.None;
            SaveLayout();
        }

        // ---- Styles

        private static void EnsureStyles()
        {
            if (_title != null)
                return;
            var parchment = Styles.Item.normal.textColor;
            var faded = Styles.Subtitle.normal.textColor;
            var brass = new Color(0.72f, 0.58f, 0.32f, 1f);
            var ember = new Color(0.32f, 0.22f, 0.11f, 1f);
            var ink = new Color(0.07f, 0.06f, 0.05f, 1f);
            var red = new Color(0.86f, 0.42f, 0.36f, 1f);

            _title = new GUIStyle(Styles.Title) { fontSize = 17, wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            _summary = new GUIStyle(Styles.Subtitle) { fontSize = 12, wordWrap = true, alignment = TextAnchor.MiddleLeft };
            _off = new GUIStyle(_summary);
            _off.normal.textColor = red;
            _empty = new GUIStyle(Styles.Hint) { fixedHeight = 0f, wordWrap = true, alignment = TextAnchor.UpperLeft };
            _tabOff = new GUIStyle(Styles.Button) { fontSize = 12, fixedHeight = 22f };
            _tabOn = new GUIStyle(_tabOff);
            _tabOn.normal.background = Styles.Bordered(ember, brass);
            _tabOn.normal.textColor = Color.white;
            _small = new GUIStyle(Styles.Button) { fontSize = 12, fixedHeight = 0f };
            _danger = new GUIStyle(_small);
            _danger.normal.textColor = red;
            _kind = new GUIStyle(Styles.Hint) { fontSize = 11, fixedHeight = 0f, alignment = TextAnchor.MiddleLeft };
            _kind.normal.textColor = brass;
            _text = new GUIStyle(Styles.Item)
            {
                fontSize = 13,
                fixedHeight = 0f,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(4, 4, 0, 0)
            };
            _checkOff = new GUIStyle
            {
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Styles.Bordered(ink, new Color(brass.r, brass.g, brass.b, 0.7f)) },
                hover = { background = Styles.Bordered(ember, brass) }
            };
            _checkOn = new GUIStyle(_checkOff)
            {
                normal = { background = Styles.Bordered(brass, parchment) },
                hover = { background = Styles.Bordered(parchment, brass) }
            };
            _placeholder = new GUIStyle(Styles.Hint) { fixedHeight = 0f, fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            _searchField = new GUIStyle(Styles.TextField) { fontSize = 14, padding = new RectOffset(8, 8, 4, 4) };
            _tipStyle = new GUIStyle(Styles.Item)
            {
                fontSize = 12,
                fixedHeight = 0f,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(7, 7, 4, 4),
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Styles.Bordered(new Color(0.11f, 0.09f, 0.07f, 0.98f), brass), textColor = parchment }
            };
        }
    }
}
