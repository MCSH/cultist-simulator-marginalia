using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SecretHistories.UI;

namespace MarginaliaMod
{
    internal static class JsonFile
    {
        // Files that exist but couldn't be read. They're left exactly as they are, and
        // never written over, so whatever is in them can still be fixed by hand.
        private static readonly HashSet<string> Unreadable = new HashSet<string>();

        // The file's contents, or fallback if it's missing or unreadable.
        public static T Read<T>(string path, T fallback) where T : class
        {
            try
            {
                if (!File.Exists(path))
                    return fallback;
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? fallback;
            }
            catch (Exception ex)
            {
                Unreadable.Add(path);
                NoonUtility.LogWarning("Marginalia: could not read " + path + ", so changes to it won't be saved this session: " + ex.Message);
                return fallback;
            }
        }

        public static void Write(string path, object value)
        {
            var json = JsonConvert.SerializeObject(value, Formatting.Indented);
            if (WriteText(path, json))
                SteamCloud.Push(path, json);
        }

        // Write to a temp file first so a crash mid-write can't truncate the real one.
        public static bool WriteText(string path, string json)
        {
            if (Unreadable.Contains(path))
                return false;
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                NoonUtility.LogWarning("Marginalia: could not write " + path + ": " + ex.Message);
                return false;
            }
        }
    }
}
