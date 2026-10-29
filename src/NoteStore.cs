using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
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
            try
            {
                if (!File.Exists(FilePath))
                    return;
                _notes = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(FilePath))
                         ?? new Dictionary<string, Entry>();
            }
            catch (Exception ex)
            {
                // Keep the unreadable file around rather than overwriting it on next save.
                NoonUtility.LogWarning("Marginalia: could not read " + FilePath + ": " + ex.Message);
                TryBackup();
                _notes = new Dictionary<string, Entry>();
            }
        }

        public static string Get(NoteTarget target)
        {
            return _notes.TryGetValue(target.Key, out var entry) ? entry.Text : null;
        }

        public static bool Has(NoteTarget target)
        {
            return !string.IsNullOrEmpty(Get(target));
        }

        public static void Set(NoteTarget target, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                _notes.Remove(target.Key);
            else
                _notes[target.Key] = new Entry { Label = target.Label, Text = text, Updated = DateTime.UtcNow };
            Save();
        }

        public static void Delete(NoteTarget target)
        {
            if (_notes.Remove(target.Key))
                Save();
        }

        private static void Save()
        {
            try
            {
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_notes, Formatting.Indented));
                if (File.Exists(FilePath))
                    File.Replace(tmp, FilePath, null);
                else
                    File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                NoonUtility.LogWarning("Marginalia: could not write " + FilePath + ": " + ex.Message);
            }
        }

        private static void TryBackup()
        {
            try
            {
                File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch (Exception)
            {
                // Nothing more we can do.
            }
        }
    }
}
