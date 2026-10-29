using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.UI;
using TMPro;
using UnityEngine;

namespace MarginaliaMod
{
    // Shared look for the uGUI parts of the mod (board labels, wiki window): the game's
    // font and nine-sliced ink-and-brass panels.
    internal static class UiKit
    {
        public static readonly Color Ink = new Color(0.07f, 0.06f, 0.05f, 0.97f);
        public static readonly Color Brass = new Color(0.72f, 0.58f, 0.32f, 1f);
        public static readonly Color Parchment = new Color(0.93f, 0.87f, 0.73f, 1f);
        public static readonly Color Faded = new Color(0.66f, 0.6f, 0.5f, 1f);
        public static readonly Color Ember = new Color(0.32f, 0.22f, 0.11f, 1f);

        private static TMP_FontAsset _font;
        private static readonly Dictionary<string, Sprite> Panels = new Dictionary<string, Sprite>();

        // The game ships Philosopher for Latin scripts; fall back to TMP's default.
        public static TMP_FontAsset Font()
        {
            if (_font != null)
                return _font;
            var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            _font = fonts.FirstOrDefault(f => f.name.IndexOf("Philosopher", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? TMP_Settings.defaultFontAsset
                    ?? fonts.FirstOrDefault();
            NoonUtility.Log("Marginalia: using font " + (_font != null ? _font.name : "(none)") +
                            "; available: " + string.Join(", ", fonts.Select(f => f.name)));
            return _font;
        }

        // 3x3 texture with a 1px edge, nine-sliced so the edge stays thin at any size.
        public static Sprite Panel(Color fill, Color edge)
        {
            var key = ColorUtility.ToHtmlStringRGBA(fill) + ColorUtility.ToHtmlStringRGBA(edge);
            if (Panels.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var tex = new Texture2D(3, 3) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    tex.SetPixel(x, y, x == 1 && y == 1 ? fill : edge);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, 3f, 3f), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(1f, 1f, 1f, 1f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Panels[key] = sprite;
            return sprite;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // Fill the parent, inset by the given margins.
        public static RectTransform Stretch(this RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static TextMeshProUGUI Text(RectTransform rect, float size, Color color)
        {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = Font();
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }
    }
}
