using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MarginaliaMod
{
    // Recipes, cards and aspects starred in the recipe explorer. Like notes they're global,
    // keyed the same way ("recipe:<id>", "element:<id>"), and kept in
    // marginalia_favourites.json, which follows the notes through Steam Cloud.
    public static class Favourites
    {
        private static HashSet<string> _keys = new HashSet<string>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_favourites.json");

        // Goes up whenever a favourite is added or removed, so views know to refresh.
        public static int Version { get; private set; }

        public static void Load()
        {
            _keys = new HashSet<string>(JsonFile.Read(FilePath, new List<string>()));
            Version++;
        }

        public static bool Has(MenuTarget target) => target != null && _keys.Contains(target.NoteKey);

        public static void Toggle(MenuTarget target)
        {
            if (!_keys.Remove(target.NoteKey))
                _keys.Add(target.NoteKey);
            Version++;
            JsonFile.Write(FilePath, _keys.OrderBy(key => key, StringComparer.Ordinal).ToList());
        }

        // The ids starred of one kind, e.g. "recipe" or "element".
        public static IEnumerable<string> Ids(string kind)
        {
            string prefix = kind + ":";
            return _keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).Select(key => key.Substring(prefix.Length));
        }
    }
}
