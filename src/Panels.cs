using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SecretHistories.UI;
using UnityEngine;

namespace MarginaliaMod
{
    // Where the floating panels sit, how big they are, and whether they're open. This
    // belongs to this computer (another screen wants another layout), so unlike the mod's
    // other files it isn't copied to Steam Cloud. Kept in marginalia_local.json in the
    // save folder, in IMGUI's 1080p-scaled units.
    internal static class PanelLayout
    {
        public sealed class Panel
        {
            public float X = -1f;      // < 0: never placed, so the panel picks its own spot
            public float Y;
            public float Width;
            public float Height;
            public bool Open;
        }

        private static Dictionary<string, Panel> _panels = new Dictionary<string, Panel>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_local.json");

        public static void Load()
        {
            _panels = JsonFile.Read(FilePath, new Dictionary<string, Panel>());
        }

        // What was saved for the panel, or null.
        public static Panel Get(string name) => _panels.TryGetValue(name, out var panel) ? panel : null;

        public static void Save(string name, Rect rect, bool open)
        {
            _panels[name] = new Panel { X = rect.x, Y = rect.y, Width = rect.width, Height = rect.height, Open = open };
            // Written without the Steam Cloud copy that JsonFile.Write makes.
            JsonFile.WriteText(FilePath, JsonConvert.SerializeObject(_panels, Formatting.Indented));
        }
    }

    // The pointer while a panel is dragged. Past the game window's left or top edge, the
    // pointer can be reported as a huge coordinate, as if it had gone off the right or bottom
    // edge, which sent the panel flying to the other side. A jump from the near half straight
    // past the far edge, or to a place no screen reaches, can only be that, so it's read as
    // just past the near edge until the pointer comes back on screen. Any jump of over half
    // the window in one step goes in the game's log, to tell what the platform really sent.
    internal struct PanelPointer
    {
        private Vector2 _lastOnScreen;
        private Vector2 _last;
        private bool _logged;

        public void Begin(Vector2 pointer)
        {
            _lastOnScreen = pointer;
            _last = pointer;
            _logged = false;
        }

        public Vector2 Read(Vector2 pointer, Rect screen)
        {
            var raw = pointer;
            pointer.x = Axis(pointer.x, ref _lastOnScreen.x, screen.width);
            pointer.y = Axis(pointer.y, ref _lastOnScreen.y, screen.height);
            if (!_logged && (Mathf.Abs(raw.x - _last.x) > screen.width / 2f || Mathf.Abs(raw.y - _last.y) > screen.height / 2f))
            {
                _logged = true;
                NoonUtility.Log("Marginalia: while a panel was dragged, the pointer jumped from " + _last + " to " + raw +
                                " (window " + screen.width + " x " + screen.height + "); read as " + pointer + ".");
            }
            _last = raw;
            return pointer;
        }

        private static float Axis(float value, ref float lastOnScreen, float size)
        {
            if (value >= 0f && value <= size)
            {
                lastOnScreen = value;
                return value;
            }
            bool wrapped = value > size && (lastOnScreen < size / 2f || value > size * 4f);
            return wrapped ? -1f : value;
        }
    }
}
