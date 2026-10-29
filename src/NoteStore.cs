using System;
using System.Collections.Generic;
using System.IO;
using SecretHistories.UI;
using UnityEngine;

namespace MarginaliaMod
{
    // Notes are global (shared across every save and legacy), keyed by entity id, and
    // live next to the save files as marginalia.json.
    public static class NoteStore
    {
        public sealed class Entry
        {
            public string Label;
            public string Text;
            public DateTime Updated;
        }

        private static Dictionary<string, Entry> _notes = new Dictionary<string, Entry>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia.json");

        public static void Load()
        {
            _notes = JsonFile.Read(FilePath, new Dictionary<string, Entry>());
        }

        public static string Get(MenuTarget target)
        {
            return _notes.TryGetValue(target.NoteKey, out var entry) ? entry.Text : null;
        }

        public static bool Has(MenuTarget target)
        {
            return !string.IsNullOrEmpty(Get(target));
        }

        public static void Set(MenuTarget target, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                _notes.Remove(target.NoteKey);
            else
                _notes[target.NoteKey] = new Entry { Label = target.Label, Text = text, Updated = DateTime.UtcNow };
            JsonFile.Write(FilePath, _notes);
        }

        public static void Delete(MenuTarget target)
        {
            if (_notes.Remove(target.NoteKey))
                JsonFile.Write(FilePath, _notes);
        }
    }
}
