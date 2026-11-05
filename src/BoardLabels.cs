using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SecretHistories.Spheres;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // Free text written directly on the tabletop. Labels are parented to the tabletop
    // sphere (behind the cards) so they pan and zoom with the table. They are global,
    // like the notes: the same board labels appear in every game, kept in
    // marginalia_board.json beside the saves.
    public static class BoardLabels
    {
        // In the order the menu goes through them. Labels written before there was a choice
        // have the first.
        public enum Background
        {
            Panel,
            Outline,
            Transparent,
            Parchment
        }

        public sealed class Data
        {
            public string Id;
            public string Text;
            public float X;
            public float Y;
            public float Size = 1f;
            public bool Folded;

            [JsonConverter(typeof(StringEnumConverter))]
            public Background Background;
        }

        // Cards are ~115 units tall; a size-1 label is about a quarter of that.
        private const float BaseFontSize = 28f;
        private const float WrapWidth = 900f;
        // Still something to click on the smallest labels.
        private const float MinArrowSize = 20f;
        public const float MinSize = 0.4f;
        public const float MaxSize = 6f;

        // Padding around the text, in units at size 1; the panel doubles as the hit area.
        private static readonly Vector2 Padding = new Vector2(14f, 8f);

        private static readonly Color Ink = new Color(0.93f, 0.87f, 0.73f, 1f);
        private static readonly Color PanelFill = new Color(0.07f, 0.06f, 0.05f, 0.78f);
        private static readonly Color PanelEdge = new Color(0.72f, 0.58f, 0.32f, 0.85f);
        // Picked out from the board list: a bright edge.
        private static readonly Color HighlightEdge = new Color(1f, 0.92f, 0.6f, 1f);

        private readonly struct Look
        {
            public readonly string Name;
            public readonly Color Fill, Edge, Ink;

            public Look(string name, Color fill, Color edge, Color ink)
            {
                Name = name;
                Fill = fill;
                Edge = edge;
                Ink = ink;
            }
        }

        // One for each Background, in the same order.
        private static readonly Look[] Looks =
        {
            new Look("dark panel", PanelFill, PanelEdge, Ink),
            new Look("outline", Color.clear, PanelEdge, Ink),
            new Look("transparent", Color.clear, Color.clear, Ink),
            new Look("parchment", new Color(0.87f, 0.8f, 0.64f, 0.96f), new Color(0.32f, 0.22f, 0.11f, 0.9f), new Color(0.13f, 0.09f, 0.05f, 1f))
        };

        private sealed class View
        {
            public RectTransform Panel;
            public TextMeshProUGUI Text;
            public LabelArrow Arrow;
            // It runs to more than one line, so there's something to fold.
            public bool Foldable;
        }

        private static List<Data> _labels = new List<Data>();
        private static readonly Dictionary<string, View> Views = new Dictionary<string, View>();
        private static RectTransform _root;
        private static TabletopSphere _sphere;
        private static float _nextSphereSearch;
        private static string _highlighted;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_board.json");

        public static void Load()
        {
            _labels = JsonFile.Read(FilePath, new List<Data>());
            // Reloaded (with newer data from Steam Cloud): draw them again.
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root.gameObject);
                _root = null;
                _nextSphereSearch = 0f;
            }
        }

        public static void Save()
        {
            JsonFile.Write(FilePath, _labels);
        }

        public static Data Get(string id) => _labels.FirstOrDefault(l => l.Id == id);

        public static IReadOnlyList<Data> All => _labels;

        // ---- The table

        // The table's surface, which cards are kept within, in the same units as the labels and
        // rulers (their layers sit at the sphere's origin). False when no game is on.
        public static bool TableRect(out Rect table)
        {
            table = default;
            if (_root == null || _sphere == null)
                return false;
            table = _sphere.GetRect();
            return table.width > 0f && table.height > 0f;
        }

        // Shifted so it lies on the table (or as much of it as fits).
        public static Rect KeepOnTable(Rect item)
        {
            if (!TableRect(out var table))
                return item;
            item.x = item.width >= table.width ? table.x : Mathf.Clamp(item.x, table.xMin, table.xMax - item.width);
            item.y = item.height >= table.height ? table.y : Mathf.Clamp(item.y, table.yMin, table.yMax - item.height);
            return item;
        }

        // Somewhere it can never be seen or reached: nothing of it on the table.
        public static bool OffTable(Rect item) => TableRect(out var table) && !table.Overlaps(item);

        // A point on the board where it is in the world, e.g. for the camera.
        public static Vector3 ToWorld(Vector2 board) => _root != null ? _root.TransformPoint(board) : Vector3.zero;

        // The middle of the screen, on the board.
        public static bool ViewCentre(out Vector2 board) =>
            ScreenToBoard(new Vector2(Screen.width / 2f, Screen.height / 2f), out board);

        // ---- Labels as a whole

        // Where a label is, around its centre; before it's drawn, a guess from its text.
        public static Rect Bounds(Data label)
        {
            var size = Views.TryGetValue(label.Id, out var view) && view != null
                ? view.Panel.sizeDelta
                : new Vector2(Mathf.Min(label.Text.Length * 14f, WrapWidth), 44f) * label.Size;
            return new Rect(new Vector2(label.X, label.Y) - size / 2f, size);
        }

        // Moved as asked, but kept on the table.
        public static void MoveOnTable(Data label, Vector2 position)
        {
            var bounds = Bounds(label);
            bounds.center = position;
            Move(label, KeepOnTable(bounds).center);
        }

        // Picks out one label (or none) with a bright edge.
        public static void Highlight(string id)
        {
            if (id == _highlighted)
                return;
            string old = _highlighted;
            _highlighted = id;
            Tint(old);
            Tint(id);
        }

        private static void Tint(string id)
        {
            if (id == null || !Views.TryGetValue(id, out var view) || view == null)
                return;
            var look = Looks[(int)(Get(id)?.Background ?? Background.Panel)];
            view.Panel.GetComponent<Image>().sprite = UiKit.Panel(look.Fill, id == _highlighted ? HighlightEdge : look.Edge);
            view.Text.color = look.Ink;
            view.Arrow.color = look.Ink;
        }

        public static string BackgroundName(Data label) => Looks[(int)label.Background].Name;

        // On to the next kind of background, and round again.
        public static void NextBackground(Data label)
        {
            label.Background = (Background)(((int)label.Background + 1) % Looks.Length);
            Tint(label.Id);
            Save();
        }

        public static void RemoveMany(IEnumerable<Data> labels)
        {
            foreach (var label in labels.ToList())
            {
                _labels.Remove(label);
                if (Views.TryGetValue(label.Id, out var view) && view != null)
                    UnityEngine.Object.Destroy(view.Panel.gameObject);
                Views.Remove(label.Id);
            }
            Save();
        }

        // Called every frame: (re)attaches to the tabletop whenever a game scene loads.
        public static void Tick()
        {
            if (_root != null || Time.unscaledTime < _nextSphereSearch)
                return;
            _nextSphereSearch = Time.unscaledTime + 1f;

            var sphere = UnityEngine.Object.FindObjectOfType<TabletopSphere>();
            if (sphere == null)
                return;

            Views.Clear();
            _sphere = sphere;
            var sphereRect = sphere.GetRectTransform();
            var rootObject = new GameObject("MarginaliaBoardLabels", typeof(RectTransform));
            rootObject.layer = sphere.gameObject.layer;
            _root = (RectTransform)rootObject.transform;
            _root.SetParent(sphereRect, false);
            _root.localPosition = Vector3.zero;
            _root.localScale = Vector3.one;
            _root.SetSiblingIndex(SiblingIndexAboveBackground(sphereRect));

            foreach (var label in _labels)
                Spawn(label);
        }

        // Just above the table background if it lives under the sphere, otherwise at
        // the very back; either way, beneath every card and verb.
        internal static int SiblingIndexAboveBackground(Transform sphere)
        {
            var background = sphere.GetComponentInChildren<TabletopBackground>(true);
            if (background == null)
                return 0;
            var t = background.transform;
            while (t.parent != null && t.parent != sphere)
                t = t.parent;
            return t.parent == sphere ? t.GetSiblingIndex() + 1 : 0;
        }

        public static bool IsAttached => _root != null;

        public static bool ScreenToBoard(Vector2 screen, out Vector2 local)
        {
            local = default;
            return _root != null &&
                   RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, Camera(), out local);
        }

        public static Data LabelAt(Vector2 screen)
        {
            if (_root == null)
                return null;
            var cam = Camera();
            // Later labels draw on top, so test them first.
            for (int i = _labels.Count - 1; i >= 0; i--)
            {
                if (Views.TryGetValue(_labels[i].Id, out var view) && view != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(view.Panel, screen, cam))
                    return _labels[i];
            }
            return null;
        }

        public static void Add(string text, Vector2 position)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            var label = new Data { Id = Guid.NewGuid().ToString("N"), Text = text.Trim(), X = position.x, Y = position.y };
            _labels.Add(label);
            Spawn(label);
            Save();
        }

        public static void SetText(Data label, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Remove(label);
                return;
            }
            label.Text = text.Trim();
            Refresh(label);
            Save();
        }

        public static void Resize(Data label, float factor)
        {
            label.Size = Mathf.Clamp(label.Size * factor, MinSize, MaxSize);
            Refresh(label);
            Save();
        }

        public static void Move(Data label, Vector2 position)
        {
            label.X = position.x;
            label.Y = position.y;
            if (Views.TryGetValue(label.Id, out var view) && view != null)
                view.Panel.localPosition = position;
        }

        public static bool CanFold(Data label) => Views.TryGetValue(label.Id, out var view) && view != null && view.Foldable;

        // Its top-right corner stays where it is, and the arrow with it, so the same spot
        // folds it and opens it again.
        public static void SetFolded(Data label, bool folded)
        {
            if (!CanFold(label) || label.Folded == folded)
                return;
            var view = Views[label.Id];
            var before = view.Panel.sizeDelta;
            label.Folded = folded;
            Refresh(label);
            var shift = (before - view.Panel.sizeDelta) / 2f;
            Move(label, new Vector2(label.X + shift.x, label.Y + shift.y));
            Save();
        }

        public static void Remove(Data label)
        {
            _labels.Remove(label);
            if (Views.TryGetValue(label.Id, out var view) && view != null)
                UnityEngine.Object.Destroy(view.Panel.gameObject);
            Views.Remove(label.Id);
            Save();
        }

        private static void Spawn(Data label)
        {
            if (_root == null)
                return;
            var panelObject = new GameObject("BoardLabel", typeof(RectTransform));
            panelObject.layer = _root.gameObject.layer;
            panelObject.transform.SetParent(_root, false);
            var panel = panelObject.AddComponent<Image>();
            panel.sprite = UiKit.Panel(PanelFill, PanelEdge);
            panel.type = Image.Type.Sliced;
            panel.pixelsPerUnitMultiplier = 0.5f; // 1px sprite edge -> 2 table units
            panel.color = Color.white;
            panel.raycastTarget = false;

            var textObject = new GameObject("Text", typeof(RectTransform));
            textObject.layer = panelObject.layer;
            textObject.transform.SetParent(panelObject.transform, false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            var font = UiKit.Font();
            if (font != null)
                text.font = font;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.richText = false;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;

            var arrowRect = UiKit.Rect("Arrow", panelObject.transform);
            arrowRect.anchorMin = arrowRect.anchorMax = arrowRect.pivot = Vector2.one;
            var arrow = arrowRect.gameObject.AddComponent<LabelArrow>();
            arrow.Label = label;

            Views[label.Id] = new View { Panel = (RectTransform)panelObject.transform, Text = text, Arrow = arrow };
            Refresh(label);
            Tint(label.Id);
        }

        private static void Refresh(Data label)
        {
            if (!Views.TryGetValue(label.Id, out var view) || view == null)
                return;
            var text = view.Text;
            float wrap = WrapWidth * label.Size;
            var padding = Padding * label.Size;
            text.fontSize = BaseFontSize * label.Size;
            text.enableWordWrapping = true;

            // Laid out in full first: that says what its first line is, and if there's more.
            var preferred = text.GetPreferredValues(label.Text, wrap, 0f);
            var textSize = new Vector2(Mathf.Min(preferred.x, wrap), preferred.y);
            Fit(view, textSize, padding, 0f);
            string first = FirstLine(text, label.Text);
            view.Foldable = first != null;
            float lineBox = view.Foldable ? text.GetPreferredValues(first, wrap, 0f).y + padding.y * 2f : 0f;

            float arrow = view.Foldable ? Mathf.Max(BaseFontSize * label.Size, MinArrowSize) : 0f;
            bool folded = view.Foldable && label.Folded;
            string shown = label.Text;
            if (folded)
            {
                // Plain dots: the game's font may not have the single character.
                shown = first + " ...";
                // It's one line however long, even with the dots.
                text.enableWordWrapping = false;
                textSize = text.GetPreferredValues(shown, wrap, 0f);
            }
            Fit(view, textSize, padding, arrow);
            text.text = shown;
            // Measuring leaves its own idea of the text behind, so draw it again.
            text.ForceMeshUpdate(true, true);

            view.Arrow.gameObject.SetActive(view.Foldable);
            if (view.Foldable)
            {
                // Level with the first line.
                var arrowRect = view.Arrow.rectTransform;
                arrowRect.sizeDelta = new Vector2(arrow, arrow);
                arrowRect.anchoredPosition = new Vector2(-padding.x / 2f, -Mathf.Max(lineBox - arrow, 0f) / 2f);
                view.Arrow.Folded = folded;
                view.Arrow.SetVerticesDirty();
            }
            view.Panel.localPosition = new Vector2(label.X, label.Y);
        }

        // The panel around text of this size, with room beside it for the arrow.
        private static void Fit(View view, Vector2 textSize, Vector2 padding, float arrow)
        {
            view.Text.rectTransform.offsetMin = padding;
            view.Text.rectTransform.offsetMax = -padding - new Vector2(arrow, 0f);
            view.Panel.sizeDelta = new Vector2(textSize.x + padding.x * 2f + arrow, Mathf.Max(textSize.y + padding.y * 2f, arrow));
        }

        // The first line as it's laid out in the text's current size, wrapped or not, or null
        // if that's all there is.
        private static string FirstLine(TextMeshProUGUI text, string source)
        {
            var info = text.GetTextInfo(source);
            if (info.lineCount < 2)
                return null;
            var line = info.lineInfo[0];
            var start = info.characterInfo[line.firstCharacterIndex];
            var end = info.characterInfo[line.lastCharacterIndex];
            int from = Mathf.Clamp(start.index, 0, source.Length);
            int to = Mathf.Clamp(end.index + end.stringLength, from, source.Length);
            string first = source.Substring(from, to - from).Trim();
            return first.Length > 0 ? first : null;
        }

        private static Camera Camera()
        {
            var canvas = _root.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas.worldCamera;
        }
    }

    // The arrow on a label of several lines: down to open it, up to fold it. The one part of
    // a label the game's pointer events can land on, since it sits under the cards anyway.
    internal class LabelArrow : MaskableGraphic, ICanvasRaycastFilter, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        public BoardLabels.Data Label;
        public bool Folded;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            float halfWidth = rect.width * 0.28f;
            float halfHeight = rect.height * 0.18f * (Folded ? 1f : -1f);
            var centre = rect.center;
            vh.AddVert(centre + new Vector2(-halfWidth, halfHeight), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(halfWidth, halfHeight), color, Vector2.zero);
            vh.AddVert(centre + new Vector2(0f, -halfHeight), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2);
        }

        // A card let go over the arrow has to land on the table. What catches it is beside the
        // labels, not above them, so a drop on the arrow would never get there.
        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera) =>
            Watchman.Get<Meniscate>()?.GetCurrentlyDraggedToken()?.IsValid() != true;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                BoardLabels.SetFolded(Label, !Label.Folded);
        }

        // Taken so they don't reach anything underneath.
        public void OnPointerDown(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) { }
    }

    public static class BoardMenuItems
    {
        private const float Step = 1.25f;

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.add",
                Order = 0,
                Label = _ => "Write on the board…",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = t => MarginaliaController.Instance.OpenEditor(
                    "New board label", "Written where you clicked", "",
                    text => BoardLabels.Add(text, t.BoardPosition))
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.edit",
                Order = 0,
                Label = _ => "Edit label…",
                IsVisible = t => t.Kind == TargetKind.BoardLabel,
                Execute = t => WithLabel(t, label => MarginaliaController.Instance.OpenEditor(
                    "Board label", "Alt + drag the label to move it", label.Text,
                    text => BoardLabels.SetText(label, text),
                    () => BoardLabels.Remove(label)))
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.fold",
                Order = 5,
                Label = t => BoardLabels.Get(t.Id)?.Folded == true ? "Open it out" : "Fold to its first line",
                IsVisible = t => t.Kind == TargetKind.BoardLabel && BoardLabels.Get(t.Id) is BoardLabels.Data label && BoardLabels.CanFold(label),
                Execute = t => WithLabel(t, label => BoardLabels.SetFolded(label, !label.Folded))
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.larger",
                Order = 10,
                Label = _ => "Larger",
                IsVisible = t => t.Kind == TargetKind.BoardLabel && BoardLabels.Get(t.Id)?.Size < BoardLabels.MaxSize,
                KeepOpen = true,
                Execute = t => WithLabel(t, label => BoardLabels.Resize(label, Step))
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.smaller",
                Order = 11,
                Label = _ => "Smaller",
                IsVisible = t => t.Kind == TargetKind.BoardLabel && BoardLabels.Get(t.Id)?.Size > BoardLabels.MinSize,
                KeepOpen = true,
                Execute = t => WithLabel(t, label => BoardLabels.Resize(label, 1f / Step))
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.background",
                Order = 12,
                Label = t => "Background: " + (BoardLabels.Get(t.Id) is BoardLabels.Data label ? BoardLabels.BackgroundName(label) : ""),
                IsVisible = t => t.Kind == TargetKind.BoardLabel && BoardLabels.Get(t.Id) != null,
                KeepOpen = true,
                Execute = t => WithLabel(t, BoardLabels.NextBackground)
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "board.remove",
                Order = 20,
                Label = _ => "Rub out",
                IsVisible = t => t.Kind == TargetKind.BoardLabel,
                Execute = t => WithLabel(t, BoardLabels.Remove)
            });
        }

        private static void WithLabel(MenuTarget target, Action<BoardLabels.Data> action)
        {
            var label = BoardLabels.Get(target.Id);
            if (label != null)
                action(label);
        }
    }
}
