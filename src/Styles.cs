using UnityEngine;

namespace MarginaliaMod
{
    // IMGUI styles in the game's ink-and-brass palette. Built lazily because GUI.skin
    // is only reachable from inside OnGUI.
    internal static class Styles
    {
        private static readonly Color Ink = new Color(0.07f, 0.06f, 0.05f, 0.97f);
        private static readonly Color Brass = new Color(0.72f, 0.58f, 0.32f, 1f);
        private static readonly Color Parchment = new Color(0.93f, 0.87f, 0.73f, 1f);
        private static readonly Color Faded = new Color(0.66f, 0.6f, 0.5f, 1f);
        private static readonly Color Ember = new Color(0.32f, 0.22f, 0.11f, 1f);

        public static GUIStyle Panel, Dim, Rule, Title, Subtitle, Preview, Item, Field, TextArea, Button, Hint;

        public static void Ensure()
        {
            if (Panel != null)
                return;

            Panel = new GUIStyle
            {
                normal = { background = Bordered(Ink, Brass) },
                border = new RectOffset(1, 1, 1, 1)
            };
            Dim = new GUIStyle { normal = { background = Solid(new Color(0f, 0f, 0f, 0.55f)) } };
            Rule = new GUIStyle { normal = { background = Solid(new Color(Brass.r, Brass.g, Brass.b, 0.4f)) } };

            Title = Text(19, Parchment, UnityEngine.FontStyle.Bold);
            Subtitle = Text(12, Faded, UnityEngine.FontStyle.Normal);
            Preview = Text(14, Parchment, UnityEngine.FontStyle.Italic);

            Item = Text(15, Parchment, UnityEngine.FontStyle.Normal);
            Item.fixedHeight = 30f;
            Item.alignment = TextAnchor.MiddleLeft;
            Item.padding = new RectOffset(10, 10, 0, 0);
            Item.hover.background = Solid(Ember);
            Item.hover.textColor = Color.white;
            Item.active.background = Solid(Brass);
            Item.active.textColor = Ink;

            Field = new GUIStyle
            {
                normal = { background = Bordered(new Color(0.12f, 0.1f, 0.08f, 1f), new Color(Brass.r, Brass.g, Brass.b, 0.5f)) },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(2, 2, 2, 2)
            };

            TextArea = new GUIStyle(GUI.skin.textArea)
            {
                fontSize = 16,
                wordWrap = true,
                richText = false,
                padding = new RectOffset(10, 10, 8, 8)
            };
            var clear = Solid(new Color(0f, 0f, 0f, 0f));
            TextArea.normal.background = TextArea.hover.background = TextArea.focused.background = TextArea.active.background = clear;
            TextArea.onNormal.background = TextArea.onHover.background = TextArea.onFocused.background = TextArea.onActive.background = clear;
            TextArea.normal.textColor = TextArea.hover.textColor = TextArea.focused.textColor = TextArea.active.textColor = Parchment;

            Button = new GUIStyle(Item) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
            Button.normal.background = Bordered(Ink, new Color(Brass.r, Brass.g, Brass.b, 0.7f));
            Button.border = new RectOffset(1, 1, 1, 1);

            Hint = Text(12, Faded, UnityEngine.FontStyle.Italic);
            Hint.alignment = TextAnchor.MiddleLeft;
            Hint.fixedHeight = 30f;
        }

        private static GUIStyle Text(int size, Color color, UnityEngine.FontStyle fontStyle)
        {
            return new GUIStyle
            {
                fontSize = size,
                fontStyle = fontStyle,
                wordWrap = true,
                richText = false,
                normal = { textColor = color }
            };
        }

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        // 3x3 texture with a 1px border, for use with GUIStyle.border = 1.
        private static Texture2D Bordered(Color fill, Color edge)
        {
            var tex = new Texture2D(3, 3) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    tex.SetPixel(x, y, x == 1 && y == 1 ? fill : edge);
            tex.Apply();
            return tex;
        }
    }
}
