using System;
using System.Linq;
using UnityEngine;

namespace MarginaliaMod
{
    // Rings cards and verbs on the table by name. A card inside a closed verb rings the
    // verb. The rings stay while searching and for a few seconds after.
    public static class Finder
    {
        private const string RingGroup = "find";
        private const float LingerSeconds = 8f;

        private static bool _searching;
        private static float _until;

        private static bool Keep() => _searching || Time.unscaledTime < _until;

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "find.search",
                Order = 35,
                Label = _ => "Find on the table…",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = _ => Search()
            });
        }

        private static void Search()
        {
            _searching = true;
            MarginaliaController.Instance.OpenPrompt("Find on the table", Show,
                () =>
                {
                    _searching = false;
                    _until = Time.unscaledTime + LingerSeconds;
                },
                () =>
                {
                    _searching = false;
                    _until = 0f;
                    TokenRing.Clear(RingGroup);
                });
        }

        // Rings what matches the query and says how many.
        private static string Show(string query)
        {
            TokenRing.Clear(RingGroup);
            query = query.Trim();
            if (query.Length == 0)
                return "Type part of a name";
            var found = Table.Tokens()
                .Where(p => !p.Hidden && Table.IdOf(p.Token) != null &&
                            (Table.LabelOf(p.Token) ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => p.Token)
                .ToList();
            TokenRing.Show(found, RingGroup, Keep);
            return found.Count == 0 ? "Nothing by that name" : found.Count == 1 ? "1 found" : found.Count + " found";
        }
    }
}
