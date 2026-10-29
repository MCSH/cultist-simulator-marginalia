using System;
using System.IO;
using Newtonsoft.Json;
using SecretHistories.UI;

namespace MarginaliaMod
{
    internal static class JsonFile
    {
        // Write to a temp file first so a crash mid-write can't truncate the real one.
        public static void Write(string path, object value)
        {
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(value, Formatting.Indented));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                NoonUtility.LogWarning("Marginalia: could not write " + path + ": " + ex.Message);
            }
        }

        // Keep an unreadable file around rather than overwriting it on the next save.
        public static void Backup(string path)
        {
            try
            {
                File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }
            catch (Exception)
            {
                // Nothing more we can do.
            }
        }
    }
}
