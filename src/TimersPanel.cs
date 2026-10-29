using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.UI;
using UnityEngine;

namespace MarginaliaMod
{
    // A small panel listing every running verb and decaying card, soonest first. Clicking
    // a row rings that token. It stays open while you play; drag it by its title.
    public static class TimersPanel
    {
        private const float Width = 330f;
        private const float Pad = 10f;
        private const float HeaderH = 34f;
        private const float TimeW = 80f;
        private const float MarkW = 56f;
        private const int MaxRows = 14;
        private const string RingGroup = "timers";
        private const float RingSeconds = 3f;

        public static bool IsOpen { get; private set; }

        // In IMGUI's scaled coordinates. x < 0 until first placed, at the right edge.
        private static Rect _rect = new Rect(-1f, 140f, Width, 100f);
        private static bool _dragging;
        private static Vector2 _dragOffset;
        private static float _ringUntil;
        private static GUIStyle _labelStyle, _timeStyle, _markStyle;

        private sealed class Row
        {
            public Token Token;
            public string Label;
            public float Remaining;
            public bool Pauses;
        }

        private static readonly List<Row> Rows = new List<Row>();
        private static int _more;
        private static int _rowsFrame = -1;

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "timers.toggle",
                Order = 36,
                Label = _ => IsOpen ? "Hide timers" : "Show timers",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = _ => IsOpen = !IsOpen
            });
        }

        public static bool Contains(Vector2 guiPoint) => IsOpen && (_dragging || _rect.Contains(guiPoint));

        private static void Refresh()
        {
            if (_rowsFrame == Time.frameCount)
                return;
            _rowsFrame = Time.frameCount;
            Rows.Clear();
            foreach (var placed in Table.Tokens())
            {
                if (placed.Rate <= 0f || placed.Hidden)
                    continue;
                switch (placed.Token.Payload)
                {
                    case ElementStack stack when stack.Decays:
                        Rows.Add(new Row { Token = placed.Token, Label = stack.Element.Label, Remaining = stack.LifetimeRemaining });
                        break;
                    case Situation situation when situation.StateIdentifier == StateEnum.Ongoing:
                        Rows.Add(new Row { Token = placed.Token, Label = situation.Verb.Label, Remaining = situation.TimeRemaining });
                        break;
                    default:
                        continue;
                }
                Rows[Rows.Count - 1].Pauses = PauseWatch.Pauses(placed.Token.Payload);
            }
            Rows.Sort((a, b) => a.Remaining.CompareTo(b.Remaining));
            _more = Mathf.Max(0, Rows.Count - MaxRows);
            if (_more > 0)
                Rows.RemoveRange(MaxRows, _more);
        }

        public static void Draw(Rect screen, bool interactive)
        {
            if (!IsOpen)
                return;
            Refresh();
            if (_timeStyle == null)
            {
                _labelStyle = new GUIStyle(Styles.Item) { wordWrap = false, clipping = TextClipping.Clip };
                _timeStyle = new GUIStyle(Styles.Item) { alignment = TextAnchor.MiddleRight, wordWrap = false };
                _markStyle = new GUIStyle(Styles.Hint) { alignment = TextAnchor.MiddleRight };
            }

            float rowH = Styles.Item.fixedHeight;
            int lines = Mathf.Max(1, Rows.Count) + (_more > 0 ? 1 : 0);
            _rect.height = Pad + HeaderH + lines * rowH + Pad;
            if (_rect.x < 0f)
                _rect.x = screen.width - Width - 24f;
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, screen.width - Width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, screen.height - HeaderH));

            bool wasEnabled = GUI.enabled;
            GUI.enabled = interactive;
            if (interactive)
                Drag();

            GUI.Box(_rect, GUIContent.none, Styles.Panel);
            float x = _rect.x, y = _rect.y + Pad;
            GUI.Label(new Rect(x + Pad, y, Width - Pad * 2f - 70f, HeaderH - 4f), "Timers", Styles.Title);
            if (GUI.Button(new Rect(x + Width - Pad - 64f, y, 64f, 26f), "Close", Styles.Button))
                IsOpen = false;
            y += HeaderH;
            GUI.Box(new Rect(x + Pad, y - 4f, Width - Pad * 2f, 1f), GUIContent.none, Styles.Rule);

            if (Rows.Count == 0)
                GUI.Label(new Rect(x + Pad, y, Width - Pad * 2f, rowH), "Nothing is counting down", Styles.Hint);
            foreach (var row in Rows)
            {
                if (GUI.Button(new Rect(x + 4f, y, Width - 8f, rowH), GUIContent.none, Styles.Item))
                    Ring(row.Token);
                float labelW = Width - Pad * 2f - TimeW - (row.Pauses ? MarkW : 0f);
                GUI.Label(new Rect(x + 4f, y, labelW, rowH), row.Label, _labelStyle);
                if (row.Pauses)
                    GUI.Label(new Rect(x + Width - Pad - TimeW - MarkW, y, MarkW, rowH), "pauses", _markStyle);
                GUI.Label(new Rect(x + Width - Pad - TimeW, y, TimeW, rowH),
                    row.Remaining.ToString("0.0", CultureInfo.InvariantCulture) + "s", _timeStyle);
                y += rowH;
            }
            if (_more > 0)
                GUI.Label(new Rect(x + Pad, y, Width - Pad * 2f, rowH), "and " + _more + " more", Styles.Hint);
            GUI.enabled = wasEnabled;
        }

        // Drag by the title bar.
        private static void Drag()
        {
            var e = Event.current;
            var header = new Rect(_rect.x, _rect.y, Width - Pad - 70f, HeaderH + Pad);
            if (e.type == EventType.MouseDown && e.button == 0 && header.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragOffset = _rect.position - e.mousePosition;
                e.Use();
            }
            else if (_dragging && e.type == EventType.MouseDrag)
            {
                _rect.position = e.mousePosition + _dragOffset;
                e.Use();
            }
            else if (_dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                _dragging = false;
            }
        }

        private static void Ring(Token token)
        {
            _ringUntil = Time.unscaledTime + RingSeconds;
            TokenRing.Clear(RingGroup);
            TokenRing.Show(token, RingGroup, () => Time.unscaledTime < _ringUntil);
        }
    }
}
