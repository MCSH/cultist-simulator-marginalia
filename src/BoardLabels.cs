using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SecretHistories.Spheres;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // Free text written directly on the tabletop. Labels are parented to the tabletop
    // sphere (behind the cards) so they pan and zoom with the table. They are global,
    // like the notes: the same board labels appear in every game, kept in
    // marginalia_board.json beside the saves.
    public static class BoardLabels
    {
        public sealed class Data
        {
            public string Id;
            public string Text;
            public float X;
            public float Y;
            public float Size = 1f;
        }

        // Cards are ~115 units tall; a size-1 label is about a quarter of that.
        private const float BaseFontSize = 28f;
        private const float WrapWidth = 900f;
        public const float MinSize = 0.4f;
        public const float MaxSize = 6f;

        // Padding around the text, in units at size 1; the panel doubles as the hit area.
        private static readonly Vector2 Padding = new Vector2(14f, 8f);

        private static readonly Color Ink = new Color(0.93f, 0.87f, 0.73f, 1f);
        private static readonly Color PanelFill = new Color(0.07f, 0.06f, 0.05f, 0.78f);
        private static readonly Color PanelEdge = new Color(0.72f, 0.58f, 0.32f, 0.85f);

        private sealed class View
        {
            public RectTransform Panel;
            public TextMeshProUGUI Text;
        }

        private static List<Data> _labels = new List<Data>();
        private static readonly Dictionary<string, View> Views = new Dictionary<string, View>();
        private static RectTransform _root;
        private static float _nextSphereSearch;

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
            text.color = Ink;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;

            Views[label.Id] = new View { Panel = (RectTransform)panelObject.transform, Text = text };
            Refresh(label);
        }

        private static void Refresh(Data label)
        {
            if (!Views.TryGetValue(label.Id, out var view) || view == null)
                return;
            view.Text.text = label.Text;
            view.Text.fontSize = BaseFontSize * label.Size;
            var preferred = view.Text.GetPreferredValues(label.Text, WrapWidth * label.Size, 0f);
            var textSize = new Vector2(Mathf.Min(preferred.x, WrapWidth * label.Size), preferred.y);
            var padding = Padding * label.Size;
            view.Text.rectTransform.offsetMin = padding;
            view.Text.rectTransform.offsetMax = -padding;
            view.Panel.sizeDelta = textSize + padding * 2f;
            view.Panel.localPosition = new Vector2(label.X, label.Y);
        }

        private static Camera Camera()
        {
            var canvas = _root.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas.worldCamera;
        }
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
