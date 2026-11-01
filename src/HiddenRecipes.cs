using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MarginaliaMod
{
    // Recipes the player has hidden from the recipe explorer's lists. Separate from favourites
    // and from what's discovered; global like the notes, kept in marginalia_hidden.json, which
    // follows the notes through Steam Cloud.
    public static class HiddenRecipes
    {
        private static HashSet<string> _ids = new HashSet<string>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_hidden.json");

        // Goes up whenever a recipe is hidden or shown again, so views know to refresh.
        public static int Version { get; private set; }

        public static int Count => _ids.Count;

        public static void Load()
        {
            _ids = new HashSet<string>(JsonFile.Read(FilePath, new List<string>()));
            Version++;
        }

        public static bool Has(string recipeId) => recipeId != null && _ids.Contains(recipeId);

        public static void Toggle(string recipeId)
        {
            if (!_ids.Remove(recipeId))
                _ids.Add(recipeId);
            Version++;
            JsonFile.Write(FilePath, _ids.OrderBy(id => id, StringComparer.Ordinal).ToList());
        }
    }
}
