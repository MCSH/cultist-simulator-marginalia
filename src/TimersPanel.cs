using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SecretHistories.Constants;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MarginaliaMod
{
    // A small panel with the game's speed controls over a list of every running verb and
    // decaying card, soonest first. Clicking a row rings that token. It stays open while you
    // play; drag it by its title.
    public static class TimersPanel
    {
        private const float Width = 330f;
        private const float Pad = 10f;
        private const float HeaderH = 34f;
        private const float ControlH = 26f;
        private const float ControlsH = ControlH + 10f;
        private const float ControlGap = 6f;
        private const float TimeW = 80f;
        private const float MarkW = 56f;
        private const int MaxRows = 14;
        private const string RingGroup = "timers";
        private const float RingSeconds = 3f;

        private const string LayoutName = "timers";

        // Open, as remembered between sessions; it only shows while a game is on the table.
        private static bool _open;

        public static bool IsOpen => _open && BoardLabels.IsAttached;

        // In IMGUI's scaled coordinates. x < 0 until first placed, at the right edge.
        private static Rect _rect = new Rect(-1f, 140f, Width, 100f);
        private static bool _dragging;
        private static Vector2 _dragOffset;
        private static PanelPointer _pointer;
        private static float _ringUntil;
        private static GUIStyle _labelStyle, _timeStyle, _markStyle, _controlStyle, _controlOnStyle;

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
                Execute = _ => SetOpen(!_open)
            });
        }

        public static bool Contains(Vector2 guiPoint) => IsOpen && (_dragging || _rect.Contains(guiPoint));

        public static bool Dragging => IsOpen && _dragging;

        // Back where it was last time, open or not.
        public static void Restore()
        {
            var saved = PanelLayout.Get(LayoutName);
            if (saved == null)
                return;
            if (saved.X >= 0f)
                _rect.position = new Vector2(saved.X, saved.Y);
            _open = saved.Open;
        }

        private static void SetOpen(bool open)
        {
            _open = open;
            SaveLayout();
        }

        private static void SaveLayout() => PanelLayout.Save(LayoutName, _rect, _open);

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
                _controlStyle = new GUIStyle(Styles.Button) { fontSize = 13, fixedHeight = 0f };
                _controlOnStyle = new GUIStyle(_controlStyle);
                _controlOnStyle.normal.background = Styles.Bordered(UiKit.Ember, UiKit.Brass);
                _controlOnStyle.normal.textColor = Color.white;
            }

            float rowH = Styles.Item.fixedHeight;
            int lines = Mathf.Max(1, Rows.Count) + (_more > 0 ? 1 : 0);
            _rect.height = Pad + HeaderH + ControlsH + lines * rowH + Pad;
            if (_rect.x < 0f)
                _rect.x = screen.width - Width - 24f;
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, screen.width - Width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, screen.height - HeaderH));

            // A release outside the game window never reaches IMGUI.
            if (_dragging && Mouse.current != null && !Mouse.current.leftButton.isPressed)
                EndDrag();

            bool wasEnabled = GUI.enabled;
            GUI.enabled = interactive;
            if (interactive || _dragging)
                Drag(screen);

            GUI.Box(_rect, GUIContent.none, Styles.Panel);
            float x = _rect.x, y = _rect.y + Pad;
            GUI.Label(new Rect(x + Pad, y, Width - Pad * 2f - 70f, HeaderH - 4f), "Timers", Styles.Title);
            if (GUI.Button(new Rect(x + Width - Pad - 64f, y, 64f, 26f), "Close", Styles.Button))
                SetOpen(false);
            y += HeaderH;
            GUI.Box(new Rect(x + Pad, y - 4f, Width - Pad * 2f, 1f), GUIContent.none, Styles.Rule);

            DrawControls(x + Pad, y + 2f, interactive);
            GUI.enabled = interactive;
            y += ControlsH;
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
            // Invisible stand-ins for the empty rows, so the panel takes the same IMGUI ids every
            // frame. IMGUI ids are counted in order, so a row coming or going would otherwise
            // shift the ids of the recipe explorer drawn after it, and its search box would lose
            // the keyboard mid-word.
            GUI.enabled = false;
            for (int i = Rows.Count; i < MaxRows; i++)
                GUI.Button(new Rect(-10000f, -10000f, 1f, 1f), GUIContent.none, GUIStyle.none);
            GUI.enabled = interactive;
            if (_more > 0)
                GUI.Label(new Rect(x + Pad, y, Width - Pad * 2f, rowH), "and " + _more + " more", Styles.Hint);
            GUI.enabled = wasEnabled;
        }

        // Pause, one tick, x1, x2: all four every time, greyed out if need be, so the IMGUI ids
        // below them stay the same (see the stand-ins above).
        private static void DrawControls(float x, float y, bool interactive)
        {
            var heart = Watchman.Exists<Heart>() ? Watchman.Get<Heart>() : null;
            var nexus = Watchman.Exists<LocalNexus>() ? Watchman.Get<LocalNexus>() : null;
            bool usable = heart != null && nexus != null && !heart.Metapaused && !heart.IsForcePaused() && !nexus.PlayerInputDisabled();
            var speed = heart != null ? heart.GetEffectiveGameSpeed() : GameSpeed.DeferToNextLowestCommand;
            GUI.enabled = interactive && usable;

            const float wide = 84f;
            float narrow = (Width - Pad * 2f - ControlGap * 3f - wide * 2f) / 2f;
            string stepKey = MarginaliaController.Instance.StepKeyName;

            // As the game's own pause key: it goes back to the speed from before the pause.
            if (GUI.Button(new Rect(x, y, wide, ControlH), "Pause", speed == GameSpeed.Paused ? _controlOnStyle : _controlStyle))
            {
                if (speed == GameSpeed.Paused)
                    nexus.UnPauseGame(true);
                else
                    nexus.PauseGame(true);
            }
            x += wide + ControlGap;
            if (GUI.Button(new Rect(x, y, wide, ControlH), stepKey != null ? "Tick  " + stepKey : "Tick", _controlStyle))
                PauseWatch.Step();
            x += wide + ControlGap;
            if (GUI.Button(new Rect(x, y, narrow, ControlH), "x1", speed == GameSpeed.Normal ? _controlOnStyle : _controlStyle))
                SetSpeed(nexus, GameSpeed.Normal);
            x += narrow + ControlGap;
            if (GUI.Button(new Rect(x, y, narrow, ControlH), "x2", speed == GameSpeed.Fast ? _controlOnStyle : _controlStyle))
                SetSpeed(nexus, GameSpeed.Fast);
        }

        // As the game's own speed keys do it.
        private static void SetSpeed(LocalNexus nexus, GameSpeed speed)
        {
            nexus.SpeedControlEvent.Invoke(new SpeedControlEventArgs
            {
                ControlPriorityLevel = SpeedControlEventArgs.DefaultControlPriorityLevel,
                GameSpeed = speed,
                WithSFX = true
            });
        }

        // Drag by the title bar.
        private static void Drag(Rect screen)
        {
            var e = Event.current;
            var header = new Rect(_rect.x, _rect.y, Width - Pad - 70f, HeaderH + Pad);
            if (e.type == EventType.MouseDown && e.button == 0 && header.Contains(e.mousePosition))
            {
                _dragging = true;
                _dragOffset = _rect.position - e.mousePosition;
                _pointer.Begin(e.mousePosition);
                e.Use();
            }
            else if (_dragging && e.type == EventType.MouseDrag)
            {
                // Not past the left edge: x < 0 means "not placed yet".
                var to = _pointer.Read(e.mousePosition, screen) + _dragOffset;
                _rect.position = new Vector2(Mathf.Max(0f, to.x), to.y);
                e.Use();
            }
            else if (_dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                EndDrag();
            }
        }

        // Done moving: remember where it ended up.
        private static void EndDrag()
        {
            _dragging = false;
            SaveLayout();
        }

        private static void Ring(Token token)
        {
            _ringUntil = Time.unscaledTime + RingSeconds;
            TokenRing.Clear(RingGroup);
            TokenRing.Show(token, RingGroup, () => Time.unscaledTime < _ringUntil);
        }
    }
}
