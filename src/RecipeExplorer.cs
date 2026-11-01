using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Infrastructure;
using SecretHistories.UI;
using UnityEngine;
using Entry = MarginaliaMod.RecipeIndex.Entry;
using Row = MarginaliaMod.RecipeIndex.Row;
using RowKind = MarginaliaMod.RecipeIndex.RowKind;
using Chip = MarginaliaMod.RecipeIndex.Chip;
using ChipKind = MarginaliaMod.RecipeIndex.ChipKind;

namespace MarginaliaMod
{
    // A searchable book of every recipe, read from the game's own data. Each card,
    // aspect and recipe has a page: what makes it, what uses it, what comes before
    // and after. Clicking a chip goes to that page; Back returns.
    public static class RecipeExplorer
    {
        private const float MaxWidth = 1060f;
        private const float Pad = 16f;
        private const float ChipH = 22f;
        private const float Gap = 4f;
        private const float RowButtonW = 58f;
        private const int MaxShown = 300;
        private const string SearchControlName = "MarginaliaRecipeSearch";

        private enum PageKind { All, Element, Recipe }

        private sealed class Page
        {
            public PageKind Kind;
            public string Id;          // the element or recipe; for All, a verb to filter by
            public string Query = "";
            public int Tab;
            public Vector2 Scroll;
        }

        private sealed class Tab
        {
            public string Title;
            public List<Entry> Entries;
        }

        private sealed class Item
        {
            public Rect Rect;
            public string Text;
            public GUIStyle Style;
            public Action OnClick;
        }

        private sealed class Layout
        {
            public readonly List<Item> Items = new List<Item>();
            public float Height;
        }

        public static bool IsOpen { get; private set; }

        private static RecipeIndex _index;
        private static Page _page;
        private static readonly Stack<Page> History = new Stack<Page>();
        private static bool _startableOnly = true;
        private static List<Tab> _tabs;
        private static List<Entry> _shown;
        private static int _matched;
        private static string _shownKey;
        private static readonly Dictionary<Entry, Layout> Layouts = new Dictionary<Entry, Layout>();
        private static float _layoutWidth;
        private static Layout _header;
        private static float _headerWidth;
        private static bool _focusSearch;
        private static Action _pending;

        private static GUIStyle _rowTitle, _rowTitleFaded, _tag, _reason, _note, _arrow, _smallButton, _tabOn, _tabOff, _toggle, _count, _description;
        private static readonly Dictionary<ChipKind, GUIStyle> ChipStyles = new Dictionary<ChipKind, GUIStyle>();
        private static readonly Dictionary<ChipKind, GUIStyle> InertChipStyles = new Dictionary<ChipKind, GUIStyle>();

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "recipes.all",
                Order = 37,
                Label = _ => "Recipe explorer…",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = _ => Open(new Page { Kind = PageKind.All })
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "recipes.element",
                Order = 55,
                Label = _ => "Recipes with this…",
                IsVisible = t => t.Kind == TargetKind.Element,
                Execute = t => Open(new Page { Kind = PageKind.Element, Id = t.Id })
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "recipes.verb",
                Order = 55,
                Label = _ => "Recipes for this verb…",
                IsVisible = t => t.Kind == TargetKind.Verb,
                Execute = t => Open(new Page { Kind = PageKind.All, Id = t.Id })
            });
        }

        // ---- Navigation

        private static void Open(Page page)
        {
            _index = RecipeIndex.Current;
            if (_index == null)
            {
                NoonUtility.LogWarning("Marginalia: the compendium isn't loaded, so there are no recipes to show.");
                return;
            }
            History.Clear();
            IsOpen = true;
            Show(page);
        }

        private static void Go(Page page)
        {
            if (_page != null)
                History.Push(_page);
            Show(page);
        }

        private static void GoElement(string id)
        {
            if (_index.IsElement(id) && !(_page.Kind == PageKind.Element && _page.Id == id))
                Go(new Page { Kind = PageKind.Element, Id = id });
        }

        private static void GoRecipe(string id)
        {
            if (_index.RecipesById.ContainsKey(id) && !(_page.Kind == PageKind.Recipe && _page.Id == id))
                Go(new Page { Kind = PageKind.Recipe, Id = id });
        }

        private static void GoVerb(string verbId)
        {
            if (!(_page.Kind == PageKind.All && _page.Id == verbId))
                Go(new Page { Kind = PageKind.All, Id = verbId });
        }

        public static void Back()
        {
            if (History.Count > 0)
                Show(History.Pop());
        }

        private static void Show(Page page)
        {
            _page = page;
            _tabs = BuildTabs(page);
            _page.Tab = Mathf.Clamp(_page.Tab, 0, _tabs.Count - 1);
            _shownKey = null;
            _header = null;
            _focusSearch = true;
        }

        public static void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            GUIUtility.keyboardControl = 0;
            Watchman.Get<LocalNexus>()?.EnablePlayerZoom();
        }

        // Notes show on the rows, so they need laying out again after an edit.
        private static void Relayout()
        {
            Layouts.Clear();
            _header = null;
        }

        private static List<Tab> BuildTabs(Page page)
        {
            var tabs = new List<Tab>();
            switch (page.Kind)
            {
                case PageKind.All:
                    tabs.Add(new Tab
                    {
                        Title = "Recipes",
                        Entries = _index.Recipes
                            .Where(r => (!_startableOnly || r.Startable) &&
                                        (page.Id == null || SecretHistories.NoonExtensions.WildcardMatchId(page.Id, r.VerbId ?? "")))
                            .Select(r => new Entry { Row = r })
                            .ToList()
                    });
                    break;
                case PageKind.Element:
                    tabs.Add(new Tab { Title = "Made by", Entries = RecipeIndex.Get(_index.MadeBy, page.Id) });
                    tabs.Add(new Tab { Title = "Used by", Entries = RecipeIndex.Get(_index.UsedBy, page.Id) });
                    var element = _index.Element(page.Id);
                    if (element != null && !element.IsAspect)
                        tabs.Add(new Tab { Title = "Through its aspects", Entries = _index.ViaAspectsOf(page.Id) });
                    var holders = RecipeIndex.Get(_index.HeldBy, page.Id);
                    if (holders.Count > 0)
                        tabs.Add(new Tab { Title = "Cards with it", Entries = holders });
                    break;
                case PageKind.Recipe:
                    tabs.Add(new Tab { Title = "Comes after", Entries = RecipeIndex.Get(_index.ComesAfter, page.Id) });
                    tabs.Add(new Tab { Title = "Leads to", Entries = RecipeIndex.Get(_index.LeadsTo, page.Id) });
                    break;
            }
            return tabs;
        }

        private static void Filter()
        {
            string key = _page.Tab + "\n" + _page.Query;
            if (key == _shownKey)
                return;
            _shownKey = key;
            var terms = _page.Query.ToLowerInvariant().Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var matches = _tabs[_page.Tab].Entries.Where(e => RecipeIndex.Matches(e, terms));
            _shown = matches.Take(MaxShown).ToList();
            _matched = _shown.Count < MaxShown ? _shown.Count : _tabs[_page.Tab].Entries.Count(e => RecipeIndex.Matches(e, terms));
        }

        // ---- Notes and the wiki

        private static MenuTarget NoteTarget(Row row) =>
            row.Kind == RowKind.Recipe ? MenuTarget.ForRecipe(row.Id, row.Title) : MenuTarget.ForElement(row.Id, _index.ElementLabel(row.Id));

        private static MenuTarget PageTarget() =>
            _page.Kind == PageKind.Recipe ? NoteTarget(_index.RecipesById[_page.Id]) :
            _page.Kind == PageKind.Element ? MenuTarget.ForElement(_page.Id, _index.ElementLabel(_page.Id)) : null;

        private static void EditNote(MenuTarget target)
        {
            MarginaliaController.Instance.OpenEditor(target.Label, target.Subtitle, NoteStore.Get(target),
                text =>
                {
                    NoteStore.Set(target, text);
                    Relayout();
                },
                NoteStore.Has(target)
                    ? () =>
                    {
                        NoteStore.Delete(target);
                        Relayout();
                    }
                    : (Action)null);
        }

        private static void OpenWiki(MenuTarget target)
        {
            var name = target.Kind == TargetKind.Recipe ? RecipeIndex.WikiName(target.Label) : Wiki.CleanName(target.Label);
            MarginaliaController.Instance.OpenWiki(name);
        }

        // ---- Drawing

        public static void Draw(Rect screen, bool interactive)
        {
            if (!IsOpen)
                return;
            EnsureStyles();
            Filter();

            bool wasEnabled = GUI.enabled;
            GUI.enabled = interactive;
            GUI.Box(screen, GUIContent.none, Styles.Dim);

            float width = Mathf.Min(MaxWidth, screen.width - 32f);
            var rect = new Rect((screen.width - width) / 2f, 32f, width, screen.height - 64f);
            GUI.Box(rect, GUIContent.none, Styles.Panel);
            float x = rect.x + Pad, inner = width - Pad * 2f, y = rect.y + Pad;

            y = DrawHeader(x, y, inner);
            y = DrawSearch(x, y, inner);
            y = DrawTabs(x, y, inner);
            GUI.Box(new Rect(x, y - 4f, inner, 1f), GUIContent.none, Styles.Rule);
            DrawList(new Rect(x, y, inner, rect.yMax - Pad - y));

            GUI.enabled = wasEnabled;
            var pending = _pending;
            _pending = null;
            if (interactive)
                pending?.Invoke();
        }

        private static float DrawHeader(float x, float y, float inner)
        {
            // Buttons along the top right, counted from the right edge.
            const float bw = 84f, bh = 28f;
            float bx = x + inner - bw;
            if (GUI.Button(new Rect(bx, y, bw, bh), "Close", Styles.Button))
                _pending = Close;
            var target = PageTarget();
            if (target != null)
            {
                bx -= bw + 6f;
                if (GUI.Button(new Rect(bx, y, bw, bh), NoteStore.Has(target) ? "Note" : "+ Note", Styles.Button))
                    _pending = () => EditNote(target);
                bx -= bw + 6f;
                if (GUI.Button(new Rect(bx, y, bw, bh), "Wiki", Styles.Button))
                    _pending = () => OpenWiki(target);
            }
            bx -= bw + 6f;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && History.Count > 0;
            if (GUI.Button(new Rect(bx, y, bw, bh), "Back", Styles.Button))
                _pending = Back;
            GUI.enabled = wasEnabled;

            if (_header == null || !Mathf.Approximately(_headerWidth, inner))
            {
                _header = BuildHeader(inner, bx - x - 8f);
                _headerWidth = inner;
            }
            DrawItems(_header, x, y);
            return y + _header.Height + 8f;
        }

        private static Layout BuildHeader(float inner, float titleW)
        {
            var layout = new Layout();
            float y = 0f;
            string title, subtitle, description = null;
            List<Chip> chips = new List<Chip>(), gives = null, then = null;
            switch (_page.Kind)
            {
                case PageKind.Element:
                    var element = _index.Element(_page.Id);
                    title = _index.ElementLabel(_page.Id);
                    subtitle = (element != null && element.IsAspect ? "Aspect · " : "Card · ") + _page.Id;
                    if (element != null && element.Lifetime > 0)
                        subtitle += " · lasts " + element.Lifetime.ToString("0") + " s";
                    description = element != null ? RecipeIndex.Clean(element.Description) : null;
                    if (_index.Cards.TryGetValue(_page.Id, out var card))
                        chips = card.Needs;
                    break;
                case PageKind.Recipe:
                    var row = _index.RecipesById[_page.Id];
                    title = row.Title;
                    subtitle = "Recipe · " + row.Id + " · " + row.Tag + (row.Recipe.Warmup > 0 ? " · " + row.Recipe.Warmup + " s" : "") +
                               (row.Startable ? "" : " · follows another recipe");
                    description = RecipeIndex.Clean(row.Recipe.StartDescription);
                    var outcome = RecipeIndex.Clean(row.Recipe.Description);
                    if (!string.IsNullOrEmpty(outcome))
                        description = (string.IsNullOrEmpty(description) ? "" : description + "\n\n") + "Afterwards: " + outcome;
                    chips = row.Needs;
                    gives = row.Gives;
                    then = row.Then;
                    break;
                default:
                    title = _page.Id == null ? "Recipes" : "Recipes · " + _index.VerbLabel(_page.Id);
                    subtitle = _index.Recipes.Count + " recipes, from the game's own data. Click a card or aspect to see what makes it and what uses it.";
                    break;
            }

            var titleContent = new GUIContent(title);
            float titleH = Styles.Title.CalcHeight(titleContent, titleW);
            layout.Items.Add(new Item { Rect = new Rect(0f, y, titleW, titleH), Text = title, Style = Styles.Title });
            y += titleH;
            float subtitleH = Styles.Subtitle.CalcHeight(new GUIContent(subtitle), titleW);
            layout.Items.Add(new Item { Rect = new Rect(0f, y, titleW, subtitleH), Text = subtitle, Style = Styles.Subtitle });
            y += subtitleH;

            if (!string.IsNullOrEmpty(description))
            {
                description = Truncate(description, 700);
                float h = _description.CalcHeight(new GUIContent(description), inner);
                y += 6f;
                layout.Items.Add(new Item { Rect = new Rect(0f, y, inner, h), Text = description, Style = _description });
                y += h;
            }
            if (chips.Count > 0 || gives?.Count > 0 || then?.Count > 0)
                y = Flow(layout, chips, gives, then, 0f, y + 8f, inner);

            var target = PageTarget();
            var note = target != null ? NoteStore.Get(target) : null;
            if (!string.IsNullOrEmpty(note))
            {
                note = Truncate(note, 400);
                float h = _note.CalcHeight(new GUIContent(note), inner);
                y += 6f;
                layout.Items.Add(new Item { Rect = new Rect(0f, y, inner, h), Text = note, Style = _note });
                y += h;
            }
            layout.Height = y;
            return layout;
        }

        private static float DrawSearch(float x, float y, float inner)
        {
            const float fieldH = 32f;
            float toggleW = _page.Kind == PageKind.All ? 170f : 0f;
            float countW = 150f;
            var field = new Rect(x, y, inner - toggleW - countW - 12f, fieldH);
            GUI.Box(field, GUIContent.none, Styles.Field);
            GUI.SetNextControlName(SearchControlName);
            string query = GUI.TextField(field, _page.Query, Styles.TextField);
            if (query != _page.Query)
            {
                _page.Query = query;
                _page.Scroll = Vector2.zero;
                Filter();
            }
            if (string.IsNullOrEmpty(_page.Query))
                GUI.Label(new Rect(field.x + 12f, field.y, field.width - 12f, fieldH), "Search: names, cards, aspects… e.g. lantern edge", _reason);
            if (_focusSearch && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(SearchControlName);
                _focusSearch = false;
            }

            if (toggleW > 0f)
            {
                var toggle = new Rect(field.xMax + 6f, y, toggleW, fieldH);
                if (GUI.Button(toggle, _startableOnly ? "Showing startable" : "Showing all", _toggle))
                    _pending = () =>
                    {
                        _startableOnly = !_startableOnly;
                        _tabs = BuildTabs(_page);
                        _shownKey = null;
                    };
            }
            string count = _matched == 1 ? "1 match" : _matched + " matches";
            GUI.Label(new Rect(x + inner - countW, y, countW, fieldH), count, _count);
            return y + fieldH + 8f;
        }

        private static float DrawTabs(float x, float y, float inner)
        {
            if (_tabs.Count <= 1)
                return y + 4f;
            const float h = 28f;
            float tx = x;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var text = _tabs[i].Title + " (" + _tabs[i].Entries.Count + ")";
                var style = i == _page.Tab ? _tabOn : _tabOff;
                float w = style.CalcSize(new GUIContent(text)).x + 24f;
                int tab = i;
                if (GUI.Button(new Rect(tx, y, w, h), text, style) && tab != _page.Tab)
                    _pending = () =>
                    {
                        _page.Tab = tab;
                        _page.Scroll = Vector2.zero;
                        _shownKey = null;
                    };
                tx += w + 6f;
            }
            return y + h + 10f;
        }

        private static void DrawList(Rect area)
        {
            float width = area.width - 18f;
            if (!Mathf.Approximately(width, _layoutWidth))
            {
                Layouts.Clear();
                _layoutWidth = width;
            }

            float total = 0f;
            foreach (var entry in _shown)
                total += LayoutOf(entry, width).Height;
            bool more = _matched > _shown.Count;
            if (_shown.Count == 0 || more)
                total += 30f;

            _page.Scroll = GUI.BeginScrollView(area, _page.Scroll, new Rect(0f, 0f, width, Mathf.Max(total, area.height)), false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            float y = 0f;
            float top = _page.Scroll.y, bottom = _page.Scroll.y + area.height;
            foreach (var entry in _shown)
            {
                var layout = LayoutOf(entry, width);
                if (y + layout.Height >= top && y <= bottom)
                {
                    DrawItems(layout, 0f, y);
                    GUI.Box(new Rect(0f, y + layout.Height - 5f, width, 1f), GUIContent.none, Styles.Rule);
                }
                y += layout.Height;
            }
            if (_shown.Count == 0)
                GUI.Label(new Rect(4f, y, width, 30f), _tabs[_page.Tab].Entries.Count == 0 ? "Nothing here." : "Nothing matches that search.", Styles.Hint);
            else if (more)
                GUI.Label(new Rect(4f, y, width, 30f), "and " + (_matched - _shown.Count) + " more: narrow the search to see them", Styles.Hint);
            GUI.EndScrollView();
        }

        private static void DrawItems(Layout layout, float x, float y)
        {
            foreach (var item in layout.Items)
            {
                var r = new Rect(x + item.Rect.x, y + item.Rect.y, item.Rect.width, item.Rect.height);
                if (item.OnClick != null)
                {
                    if (GUI.Button(r, item.Text, item.Style))
                        _pending = item.OnClick;
                }
                else
                {
                    GUI.Label(r, item.Text, item.Style);
                }
            }
        }

        // ---- Laying out rows

        private static Layout LayoutOf(Entry entry, float width)
        {
            if (Layouts.TryGetValue(entry, out var cached))
                return cached;
            var layout = new Layout();
            var row = entry.Row;
            float y = 2f, x = 0f;

            // Tag and title on the first line, with Wiki and Note at the right.
            var tagContent = new GUIContent(row.Tag);
            float tagW = _tag.CalcSize(tagContent).x;
            layout.Items.Add(new Item
            {
                Rect = new Rect(x, y, tagW, 26f),
                Text = row.Tag,
                Style = _tag,
                OnClick = row.Kind == RowKind.Recipe ? () => GoVerb(row.VerbId) : (Action)null
            });
            x += tagW + 4f;

            bool hasLinks = row.Kind != RowKind.Change;
            float right = width - (hasLinks ? RowButtonW * 2f + 6f : 0f);
            var titleStyle = row.Kind == RowKind.Recipe && !row.Startable ? _rowTitleFaded : _rowTitle;
            float titleW = Mathf.Min(titleStyle.CalcSize(new GUIContent(row.Title)).x, right - x - 6f);
            layout.Items.Add(new Item
            {
                Rect = new Rect(x, y, titleW, 26f),
                Text = row.Title,
                Style = titleStyle,
                OnClick = row.Kind == RowKind.Recipe ? () => GoRecipe(row.Id) : (Action)(() => GoElement(row.Id))
            });

            MenuTarget target = null;
            if (hasLinks)
            {
                target = NoteTarget(row);
                var t = target;
                layout.Items.Add(new Item
                {
                    Rect = new Rect(width - RowButtonW * 2f - 6f, y + 2f, RowButtonW, ChipH),
                    Text = "Wiki",
                    Style = _smallButton,
                    OnClick = () => OpenWiki(t)
                });
                layout.Items.Add(new Item
                {
                    Rect = new Rect(width - RowButtonW, y + 2f, RowButtonW, ChipH),
                    Text = NoteStore.Has(t) ? "Note" : "+ Note",
                    Style = _smallButton,
                    OnClick = () => EditNote(t)
                });
            }
            y += 28f;

            if (!string.IsNullOrEmpty(entry.Reason))
            {
                float h = _reason.CalcHeight(new GUIContent(entry.Reason), width);
                layout.Items.Add(new Item { Rect = new Rect(0f, y, width, h), Text = entry.Reason, Style = _reason });
                y += h + 2f;
            }

            if (row.Needs.Count > 0 || row.Gives.Count > 0 || row.Then.Count > 0)
                y = Flow(layout, row.Needs, row.Gives, row.Then, 0f, y, width);

            var note = target != null ? NoteStore.Get(target) : null;
            if (!string.IsNullOrEmpty(note))
            {
                note = Truncate(note.Replace('\n', ' '), 200);
                float h = _note.CalcHeight(new GUIContent(note), width);
                layout.Items.Add(new Item { Rect = new Rect(0f, y + 2f, width, h), Text = note, Style = _note });
                y += h + 2f;
            }

            layout.Height = y + 10f;
            Layouts[entry] = layout;
            return layout;
        }

        // Chips left to right, wrapping: what it needs → what it gives, then what follows.
        private static float Flow(Layout layout, List<Chip> needs, List<Chip> gives, List<Chip> then, float x0, float y, float width)
        {
            float x = x0;

            void Place(string text, GUIStyle style, Action onClick)
            {
                float w = Mathf.Min(style.CalcSize(new GUIContent(text)).x, width);
                if (x > x0 && x + w > x0 + width)
                {
                    x = x0;
                    y += ChipH + Gap;
                }
                layout.Items.Add(new Item { Rect = new Rect(x, y, w, ChipH), Text = text, Style = style, OnClick = onClick });
                x += w + Gap;
            }

            void PlaceChip(Chip chip)
            {
                Action onClick = chip.ElementId != null && _index.IsElement(chip.ElementId) ? () => GoElement(chip.ElementId)
                    : chip.RecipeId != null ? () => GoRecipe(chip.RecipeId)
                    : (Action)null;
                Place(chip.Text, (onClick != null ? ChipStyles : InertChipStyles)[chip.Kind], onClick);
            }

            foreach (var chip in needs)
                PlaceChip(chip);
            if (gives != null && gives.Count > 0)
            {
                if (needs.Count > 0)
                    Place("→", _arrow, null);
                foreach (var chip in gives)
                    PlaceChip(chip);
            }
            if (then != null && then.Count > 0)
            {
                x = x0;
                y += ChipH + Gap;
                foreach (var chip in then)
                    PlaceChip(chip);
            }
            return y + ChipH + Gap;
        }

        private static string Truncate(string text, int max)
        {
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
        }

        // ---- Styles

        private static void EnsureStyles()
        {
            if (_rowTitle != null)
                return;
            var parchment = Styles.Item.normal.textColor;
            var faded = Styles.Subtitle.normal.textColor;
            var brass = new Color(0.72f, 0.58f, 0.32f, 1f);
            var ember = new Color(0.32f, 0.22f, 0.11f, 1f);
            var ink = new Color(0.07f, 0.06f, 0.05f, 1f);

            _rowTitle = new GUIStyle(Styles.Item)
            {
                fontSize = 16,
                fontStyle = UnityEngine.FontStyle.Bold,
                fixedHeight = 26f,
                padding = new RectOffset(6, 6, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            _rowTitleFaded = new GUIStyle(_rowTitle);
            _rowTitleFaded.normal.textColor = faded;

            _tag = new GUIStyle(Styles.Item)
            {
                fontSize = 12,
                fontStyle = UnityEngine.FontStyle.Bold,
                fixedHeight = 26f,
                padding = new RectOffset(8, 8, 0, 0),
                wordWrap = false
            };
            _tag.normal.textColor = brass;
            _tag.normal.background = Styles.Bordered(ink, new Color(brass.r, brass.g, brass.b, 0.45f));
            _tag.border = new RectOffset(1, 1, 1, 1);

            _reason = new GUIStyle(Styles.Hint) { fixedHeight = 0f, wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 13 };
            _note = new GUIStyle(Styles.Preview) { fontSize = 13, wordWrap = true };
            _note.normal.textColor = new Color(brass.r, brass.g, brass.b, 1f);
            _description = new GUIStyle(Styles.Preview) { fontSize = 14, wordWrap = true };
            _arrow = new GUIStyle(Styles.Item) { fontSize = 16, fixedHeight = ChipH, padding = new RectOffset(4, 4, 0, 0), alignment = TextAnchor.MiddleCenter };
            _arrow.hover.background = null;
            _smallButton = new GUIStyle(Styles.Button) { fontSize = 12, fixedHeight = ChipH };
            _tabOff = new GUIStyle(Styles.Button) { fontSize = 14, fixedHeight = 28f };
            _tabOn = new GUIStyle(_tabOff);
            _tabOn.normal.background = Styles.Bordered(ember, brass);
            _tabOn.normal.textColor = Color.white;
            _toggle = new GUIStyle(Styles.Button) { fontSize = 13, fixedHeight = 32f };
            _count = new GUIStyle(Styles.Hint) { alignment = TextAnchor.MiddleRight, fixedHeight = 32f };

            ChipStyles[ChipKind.Need] = MakeChip(new Color(0.16f, 0.12f, 0.08f, 1f), new Color(brass.r, brass.g, brass.b, 0.6f), parchment, UnityEngine.FontStyle.Normal);
            ChipStyles[ChipKind.Aspect] = ChipStyles[ChipKind.Need];
            ChipStyles[ChipKind.Lack] = MakeChip(ink, new Color(faded.r, faded.g, faded.b, 0.35f), faded, UnityEngine.FontStyle.Italic);
            ChipStyles[ChipKind.Give] = MakeChip(new Color(0.12f, 0.17f, 0.11f, 1f), new Color(0.55f, 0.7f, 0.42f, 0.7f), parchment, UnityEngine.FontStyle.Normal);
            ChipStyles[ChipKind.Take] = MakeChip(new Color(0.2f, 0.09f, 0.08f, 1f), new Color(0.78f, 0.38f, 0.32f, 0.7f), parchment, UnityEngine.FontStyle.Normal);
            ChipStyles[ChipKind.Slot] = MakeChip(ink, new Color(brass.r, brass.g, brass.b, 0.6f), parchment, UnityEngine.FontStyle.Italic);
            ChipStyles[ChipKind.Plain] = MakeChip(ink, new Color(faded.r, faded.g, faded.b, 0.35f), faded, UnityEngine.FontStyle.Normal);
            foreach (var pair in ChipStyles)
            {
                var inert = new GUIStyle(pair.Value);
                inert.hover = inert.normal;
                inert.active = inert.normal;
                InertChipStyles[pair.Key] = inert;
            }
        }

        private static GUIStyle MakeChip(Color fill, Color edge, Color text, UnityEngine.FontStyle fontStyle)
        {
            var style = new GUIStyle
            {
                fontSize = 13,
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = ChipH,
                padding = new RectOffset(8, 8, 0, 0),
                border = new RectOffset(1, 1, 1, 1),
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { background = Styles.Bordered(fill, edge), textColor = text }
            };
            style.hover.background = Styles.Bordered(Color.Lerp(fill, edge, 0.35f), edge);
            style.hover.textColor = Color.white;
            style.active.background = Styles.Bordered(edge, edge);
            style.active.textColor = new Color(0.07f, 0.06f, 0.05f, 1f);
            return style;
        }
    }
}
