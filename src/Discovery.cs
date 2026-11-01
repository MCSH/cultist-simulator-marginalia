using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.UI;
using UnityEngine;

namespace MarginaliaMod
{
    // Recipes the player has finished. The game itself keeps count: when a recipe completes,
    // it's added to the character's history of executions (saved with the game, so a loaded
    // save brings what it had already done). That history is read now and then, and every
    // recipe ever seen in it is kept here, across saves and legacies, in
    // marginalia_discovered.json, which follows the notes through Steam Cloud.
    //
    // The history holds what ran in the end, though: a recipe that an alternative outcome
    // replaces as it finishes (as "An Early Night" often is) is recorded only as that outcome.
    // So each verb is watched too, for the recipe it started, which counts once the verb has
    // finished running something (a halted verb runs nothing, and doesn't count).
    public static class Discovery
    {
        private const float LookEverySeconds = 1f;

        private static HashSet<string> _recipes = new HashSet<string>();

        // Each verb at the last look: the recipe it started, and the last one it ran.
        private static readonly Dictionary<Situation, (Recipe Started, Recipe Ran)> Verbs =
            new Dictionary<Situation, (Recipe, Recipe)>();
        private static float _nextLook;
        private static bool _readFailed;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_discovered.json");

        // Goes up whenever a recipe is added, so views know to refresh.
        public static int Version { get; private set; }

        public static int Count => _recipes.Count;

        public static void Load()
        {
            _recipes = new HashSet<string>(JsonFile.Read(FilePath, new List<string>()));
            Version++;
        }

        // After Steam Cloud brings a newer copy from another computer: a recipe once discovered
        // stays discovered, so keep what each side knows rather than the newer side only.
        public static void Merge()
        {
            var known = _recipes;
            Load();
            if (known.All(_recipes.Contains))
                return;
            _recipes.UnionWith(known);
            Save();
        }

        public static bool Has(string recipeId) => recipeId != null && _recipes.Contains(recipeId);

        // A verb that has run something since the last look finished the recipe it had started
        // then, and, if it's now done, the one it shows (unless something new has begun).
        private static bool WatchVerbs()
        {
            var situations = Watchman.Get<HornedAxe>()?.GetRegisteredSituations();
            if (situations == null)
                return false;
            bool added = false;
            foreach (var situation in situations)
            {
                var now = (Started: situation.FallbackRecipe, Ran: situation.LastExecutedRecipe);
                if (Verbs.TryGetValue(situation, out var before) && now.Ran != null && now.Ran != before.Ran)
                {
                    added |= Add(before.Started);
                    if (situation.StateIdentifier == StateEnum.Complete)
                        added |= Add(now.Started);
                }
                Verbs[situation] = now;
            }
            // Forget verbs that have gone (or a game left behind).
            foreach (var gone in Verbs.Keys.Where(v => !situations.Contains(v)).ToList())
                Verbs.Remove(gone);
            return added;
        }

        private static bool Add(Recipe recipe) =>
            recipe != null && !(recipe is NullRecipe) && !string.IsNullOrEmpty(recipe.Id) && _recipes.Add(recipe.Id);

        private static void Save() =>
            JsonFile.Write(FilePath, _recipes.OrderBy(id => id, StringComparer.Ordinal).ToList());

        // Called every frame; looks at the character's history once a second.
        public static void Tick()
        {
            if (Time.unscaledTime < _nextLook)
                return;
            _nextLook = Time.unscaledTime + LookEverySeconds;

            Dictionary<string, int> history;
            bool added;
            try
            {
                history = Watchman.Get<Stable>()?.Protag()?.RecipeExecutions;
                added = WatchVerbs();
            }
            catch (Exception ex)
            {
                // Not there yet (the main menu, say); say so once.
                if (!_readFailed)
                    NoonUtility.Log("Marginalia: couldn't read the recipes done so far: " + ex.Message);
                _readFailed = true;
                return;
            }
            if (history != null)
                foreach (var pair in history)
                    if (pair.Value > 0)
                        added |= _recipes.Add(pair.Key);
            if (!added)
                return;
            Version++;
            Save();
        }
    }
}
