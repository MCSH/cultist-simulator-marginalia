using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using SecretHistories;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Fucine;
using SecretHistories.UI;

namespace MarginaliaMod
{
    // Everything the recipe explorer shows, read from the game's own compendium (so
    // mods' content is included): every recipe, plus the ways cards change by
    // themselves (decaying, and xtriggers). For each element it keeps what makes it,
    // what uses it, and, for aspects, which cards have it.
    internal sealed class RecipeIndex
    {
        public enum RowKind { Recipe, Change, Card }

        public enum ChipKind { Need, Lack, Give, Take, Slot, Aspect, Plain }

        public sealed class Chip
        {
            public string Text;
            public ChipKind Kind;
            public string ElementId;   // clicking goes to this element, if set
            public string RecipeId;    // or to this recipe
        }

        // One line in the explorer: a recipe, a card changing by itself, or a card.
        public sealed class Row
        {
            public RowKind Kind;
            public string Id;          // recipe id, or the card's element id
            public string Title;
            public string Tag;         // verb label, "decays", "reaction" or "card"
            public string VerbId;
            public bool Startable;     // a player can start it from an empty verb
            public Recipe Recipe;
            public readonly List<Chip> Needs = new List<Chip>();
            public readonly List<Chip> Gives = new List<Chip>();
            public readonly List<Chip> Then = new List<Chip>();
            public string Search;
        }

        // A row as listed on an element's or recipe's page, with why it's there.
        public sealed class Entry
        {
            public Row Row;
            public string Reason;
        }

        public readonly List<Row> Recipes = new List<Row>();
        public readonly Dictionary<string, Row> RecipesById = new Dictionary<string, Row>();
        public readonly Dictionary<string, List<Entry>> MadeBy = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, List<Entry>> UsedBy = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, List<Entry>> ViaAspects = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, List<Entry>> HeldBy = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, List<Entry>> LeadsTo = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, List<Entry>> ComesAfter = new Dictionary<string, List<Entry>>();
        public readonly Dictionary<string, Row> Cards = new Dictionary<string, Row>();

        private readonly Compendium _compendium;
        private readonly Dictionary<string, Element> _elements = new Dictionary<string, Element>();
        private readonly Dictionary<string, DeckSpec> _decks = new Dictionary<string, DeckSpec>();
        private readonly Dictionary<string, string> _verbLabels = new Dictionary<string, string>();

        // An xtrigger: what a card turns into, spawns or gains when a catalyst is present.
        private sealed class Morph
        {
            public string Catalyst;
            public string Id;
            public string Effect;      // Transform, Spawn, Mutate, SetMutation or DeckDraw
            public string Level;
            public int Chance;
        }

        // Catalyst aspect id -> the cards it changes, and how.
        private readonly Dictionary<string, List<(Element Card, Morph Morph)>> _reactions =
            new Dictionary<string, List<(Element, Morph)>>();

        private static readonly Regex Brackets = new Regex(@"\s*\[[^\]]*\]");

        private static RecipeIndex _current;
        private static int _recipeCount;

        // Built on first use, and again if the compendium has changed since.
        public static RecipeIndex Current
        {
            get
            {
                var compendium = Watchman.Get<Compendium>();
                if (compendium == null)
                    return null;
                int count = compendium.GetEntitiesAsList<Recipe>().Count;
                if (_current == null || _current._compendium != compendium || _recipeCount != count)
                {
                    var started = DateTime.UtcNow;
                    _current = new RecipeIndex(compendium);
                    _recipeCount = count;
                    NoonUtility.Log("Marginalia: indexed " + _current.Recipes.Count + " recipes in " +
                                    (int)(DateTime.UtcNow - started).TotalMilliseconds + " ms.");
                }
                return _current;
            }
        }

        private RecipeIndex(Compendium compendium)
        {
            _compendium = compendium;
            foreach (var element in compendium.GetEntitiesAsList<Element>())
                _elements[element.Id] = element;
            foreach (var deck in compendium.GetEntitiesAsList<DeckSpec>())
                _decks[deck.Id] = deck;
            foreach (var verb in compendium.GetEntitiesAsList<Verb>())
                _verbLabels[verb.Id] = Wiki.CleanName(verb.Label);
            foreach (var element in _elements.Values)
                IndexReactions(element);

            foreach (var recipe in compendium.GetEntitiesAsList<Recipe>())
            {
                if (recipe.IsPlatonic() || recipe.HintOnly)
                    continue;
                if (recipe.InternalDeck != null && !string.IsNullOrEmpty(recipe.InternalDeck.Id) && !_decks.ContainsKey(recipe.InternalDeck.Id))
                    _decks[recipe.InternalDeck.Id] = recipe.InternalDeck;
                var row = BuildRecipe(recipe);
                Recipes.Add(row);
                RecipesById[row.Id] = row;
            }
            foreach (var row in Recipes)
                LinkRecipes(row);
            foreach (var element in _elements.Values)
                IndexCard(element);

            Recipes.Sort(ByTitle);
            foreach (var list in MadeBy.Values.Concat(UsedBy.Values).Concat(ViaAspects.Values).Concat(LeadsTo.Values).Concat(ComesAfter.Values))
                list.Sort((a, b) => b.Row.Startable.CompareTo(a.Row.Startable) != 0
                    ? b.Row.Startable.CompareTo(a.Row.Startable)
                    : ByTitle(a.Row, b.Row));
            foreach (var list in HeldBy.Values)
                list.Sort((a, b) => ByTitle(a.Row, b.Row));
        }

        private static int ByTitle(Row a, Row b)
        {
            int byTitle = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            return byTitle != 0 ? byTitle : string.CompareOrdinal(a.Id, b.Id);
        }

        // ---- Lookups

        public Element Element(string id) =>
            id != null && _elements.TryGetValue(id, out var element) ? element : null;

        public bool IsElement(string id) => id != null && _elements.ContainsKey(id);

        public string ElementLabel(string id)
        {
            var label = Element(id) != null ? Wiki.CleanName(Element(id).Label) : null;
            return string.IsNullOrEmpty(label) || label == "." ? id : label;
        }

        public string VerbLabel(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "any verb";
            return _verbLabels.TryGetValue(id, out var label) && !string.IsNullOrEmpty(label) && label != "." ? label : id;
        }

        public static string RecipeLabel(Recipe recipe)
        {
            var label = Wiki.CleanName(recipe.Label);
            return string.IsNullOrEmpty(label) || label == "." ? recipe.Id : label;
        }

        // "Reason's Glory [Minor]" -> "Reason's Glory", which is what the wiki calls it.
        public static string WikiName(string label)
        {
            var name = Brackets.Replace(label ?? "", "").Trim();
            return name.TrimEnd('?', '.', '…', ' ', '!');
        }

        public static string Clean(string text)
        {
            text = Wiki.CleanName(text);
            return text == "." ? "" : text;
        }

        // ---- Recipes

        private Row BuildRecipe(Recipe recipe)
        {
            var row = new Row
            {
                Kind = RowKind.Recipe,
                Id = recipe.Id,
                Recipe = recipe,
                Title = RecipeLabel(recipe),
                VerbId = recipe.ActionId,
                Tag = VerbLabel(recipe.ActionId),
                Startable = recipe.Craftable
            };

            AddNeeds(row, recipe.Requirements, "");
            AddNeeds(row, recipe.TableReqs, " on the table");
            AddNeeds(row, recipe.ExtantReqs, " anywhere");

            if (recipe.Slots != null)
                foreach (var slot in recipe.Slots)
                    AddSlot(row, slot);

            if (recipe.Effects != null)
                foreach (var effect in recipe.Effects)
                    AddEffect(row, effect.Key, effect.Value);
            if (recipe.Mutations != null)
                foreach (var mutation in recipe.Mutations)
                    AddMutation(row, mutation.Filter, mutation.Mutate, mutation.Level, mutation.Additive);
            AddGrandEffects(row, RoostCompat.Property(recipe, "GrandEffects"), 0);

            if (recipe.Purge != null)
                foreach (var purge in recipe.Purge)
                {
                    row.Gives.Add(new Chip { Text = "purge " + purge.Value + " " + ElementLabel(purge.Key), Kind = ChipKind.Take, ElementId = purge.Key });
                    Add(UsedBy, purge.Key, row, "purges " + purge.Value);
                }

            if (recipe.DeckEffects != null)
                foreach (var draw in recipe.DeckEffects)
                    AddDraw(row, draw.Key, draw.Value);

            // The recipe's own aspects can set off xtriggers in the cards it holds.
            if (recipe.Aspects != null)
                foreach (var aspect in recipe.Aspects)
                {
                    if (aspect.Value <= 0 || !_reactions.TryGetValue(aspect.Key, out var reactions))
                        continue;
                    foreach (var (card, morph) in reactions)
                        AddReaction(row, card, morph, ElementLabel(card.Id) + " → ", "", true);
                }

            if (!string.IsNullOrEmpty(recipe.Ending))
                row.Gives.Add(new Chip { Text = "ends the game", Kind = ChipKind.Plain });

            row.Search = SearchText(row);
            return row;
        }

        private void AddNeeds(Row row, Dictionary<string, string> needs, string where)
        {
            if (needs == null)
                return;
            foreach (var need in needs)
            {
                string label = ElementLabel(need.Key);
                if (!TryInt(need.Value, out int n))
                {
                    row.Needs.Add(new Chip { Text = label + " (" + need.Value + ")" + where, Kind = ChipKind.Need, ElementId = need.Key });
                    Add(UsedBy, need.Key, row, "needs some" + where);
                }
                else if (n > 0)
                {
                    row.Needs.Add(new Chip { Text = label + " " + n + "+" + where, Kind = ChipKind.Need, ElementId = need.Key });
                    Add(UsedBy, need.Key, row, "needs " + n + "+" + where);
                }
                else if (n < 0)
                {
                    row.Needs.Add(new Chip { Text = (n == -1 ? "no " + label : label + " < " + -n) + where, Kind = ChipKind.Lack, ElementId = need.Key });
                }
            }
        }

        private void AddSlot(Row row, SphereSpec slot)
        {
            var wants = (slot.Required?.Keys ?? Enumerable.Empty<string>())
                .Concat(slot.Essential?.Keys ?? Enumerable.Empty<string>())
                .Distinct().ToList();
            string slotLabel = Clean(slot.Label);
            if (string.IsNullOrEmpty(slotLabel))
                slotLabel = slot.Id;
            string what = wants.Count == 0 ? "anything" : string.Join(" or ", wants.Select(ElementLabel));
            row.Needs.Add(new Chip
            {
                Text = "slot: " + what + (slot.Consumes ? " (consumed)" : ""),
                Kind = ChipKind.Slot,
                ElementId = wants.Count == 1 ? wants[0] : null
            });
            foreach (var id in wants)
                Add(UsedBy, id, row, "fits its slot “" + slotLabel + "”" + (slot.Consumes ? ", consumed" : "") + (slot.Greedy ? ", taken greedily" : ""));
        }

        private void AddEffect(Row row, string id, string value)
        {
            if (string.IsNullOrEmpty(id))
                return;
            bool numeric = TryInt(value, out int n);
            string label = ElementLabel(id);
            if (numeric && n < 0)
            {
                row.Gives.Add(new Chip { Text = "−" + -n + " " + label, Kind = ChipKind.Take, ElementId = id });
                Add(UsedBy, id, row, "consumes " + -n);
            }
            else if (!numeric || n > 0)
            {
                row.Gives.Add(new Chip { Text = (numeric ? "+" + n : "(" + value + ")") + " " + label, Kind = ChipKind.Give, ElementId = id });
                Add(MadeBy, id, row, numeric ? "makes " + n : "makes some");
            }
        }

        private void AddMutation(Row row, string filter, string mutate, string levelText, bool additive)
        {
            if (string.IsNullOrEmpty(mutate))
                return;
            if (filter == "undefined")
                filter = null;
            string on = string.IsNullOrEmpty(filter) ? "each card" : ElementLabel(filter);
            string aspect = ElementLabel(mutate);
            bool numeric = TryInt(levelText, out int level);
            string amount = !numeric ? "(" + levelText + ")" : additive ? (level >= 0 ? "+" + level : "−" + -level) : "set " + level;
            bool removes = numeric && (additive ? level < 0 : level <= 0);
            row.Gives.Add(new Chip { Text = on + ": " + amount + " " + aspect, Kind = removes ? ChipKind.Take : ChipKind.Give, ElementId = mutate });
            if (removes)
                Add(UsedBy, mutate, row, "removes it from " + on);
            else
                Add(MadeBy, mutate, row, "adds it to " + on);
            if (!string.IsNullOrEmpty(filter))
                Add(UsedBy, filter, row, "changes it: " + amount + " " + aspect);
        }

        // Roost's copy of a recipe's effects, which may nest further groups of effects.
        // Its deck draws and aspects are also copied back to the recipe, so they're read there.
        private void AddGrandEffects(Row row, object grand, int depth)
        {
            if (grand == null || depth > 8)
                return;
            var effects = RoostCompat.Dictionary(grand, "Effects");
            if (effects != null)
                foreach (DictionaryEntry effect in effects)
                    AddEffect(row, RoostCompat.Formula(effect.Key), RoostCompat.Formula(effect.Value));
            var mutations = RoostCompat.List(grand, "Mutations");
            if (mutations != null)
                foreach (var mutation in mutations)
                    AddMutation(row,
                        RoostCompat.Formula(RoostCompat.Get(RoostCompat.Get(mutation, "Filter"), "Filter")),
                        RoostCompat.Get(mutation, "Mutate") as string,
                        RoostCompat.Formula(RoostCompat.Get(mutation, "Level")),
                        RoostCompat.Get(mutation, "Additive") is bool additive && additive);
            var decays = RoostCompat.List(grand, "Decays");
            if (decays != null)
                foreach (var decay in decays)
                {
                    var filter = RoostCompat.Formula(RoostCompat.Get(decay, "Filter"));
                    if (string.IsNullOrEmpty(filter) || filter == "undefined")
                        continue;
                    row.Gives.Add(new Chip { Text = "decays " + ElementLabel(filter), Kind = ChipKind.Take, ElementId = filter });
                    Add(UsedBy, filter, row, "makes it decay");
                }
            var furthermore = RoostCompat.List(grand, "Furthermore");
            if (furthermore != null)
                foreach (var more in furthermore)
                    AddGrandEffects(row, more, depth + 1);
        }

        private void AddDraw(Row row, string deckId, string draws)
        {
            if (!_decks.TryGetValue(deckId, out var deck))
            {
                row.Gives.Add(new Chip { Text = "draw from " + deckId, Kind = ChipKind.Give });
                return;
            }
            string deckLabel = Clean(deck.Label);
            if (string.IsNullOrEmpty(deckLabel))
                deckLabel = deck.Id;
            string times = TryInt(draws, out int n) && n > 1 ? " ×" + n : "";
            row.Gives.Add(new Chip { Text = "draw: " + deckLabel + times, Kind = ChipKind.Give });
            var cards = (deck.Spec ?? new List<string>()).ToList();
            if (!string.IsNullOrEmpty(deck.DefaultCard))
                cards.Add(deck.DefaultCard);
            foreach (var card in cards.Distinct())
                if (IsElement(card))
                    Add(MadeBy, card, row, "may be drawn from “" + deckLabel + "”");
        }

        // Links between recipes: what can follow this one, and what it can follow.
        private void LinkRecipes(Row row)
        {
            void Link(List<LinkedRecipeDetails> links, string how)
            {
                if (links == null)
                    return;
                foreach (var link in links)
                {
                    if (link == null || string.IsNullOrEmpty(link.Id))
                        continue;
                    // The id may be a wildcard: "dream.*" leads to every dream recipe.
                    var targets = link.Id.Contains("*")
                        ? Recipes.Where(r => NoonExtensions.WildcardMatchId(r.Id, link.Id)).ToList()
                        : RecipesById.TryGetValue(link.Id, out var one) ? new List<Row> { one } : new List<Row>();
                    string chance = link.Chance > 0 && link.Chance < 100 ? " " + link.Chance + "%" : "";
                    foreach (var target in targets)
                    {
                        if (target == row)
                            continue;
                        Add(LeadsTo, row.Id, target, how + chance);
                        Add(ComesAfter, target.Id, row, how + chance);
                    }
                    if (targets.Count == 1)
                        row.Then.Add(new Chip { Text = how + ": " + targets[0].Title + chance, Kind = ChipKind.Plain, RecipeId = targets[0].Id });
                    else if (targets.Count > 1)
                        row.Then.Add(new Chip { Text = how + ": " + targets.Count + " recipes" + chance, Kind = ChipKind.Plain, RecipeId = row.Id });
                }
            }

            Link(row.Recipe.Alt, "instead");
            Link(row.Recipe.Linked, "then");
            Link(row.Recipe.Inductions, "sets off");
        }

        // ---- Cards: decay, xtriggers, aspects

        private void IndexReactions(Element element)
        {
            foreach (var morph in XTriggersOf(element))
            {
                if (!_reactions.TryGetValue(morph.Catalyst, out var list))
                    _reactions[morph.Catalyst] = list = new List<(Element, Morph)>();
                list.Add((element, morph));
            }
        }

        // A card's xtriggers, from the game or, if it took them over, from Roost.
        private static List<Morph> XTriggersOf(Element element)
        {
            var morphs = new List<Morph>();
            if (element.XTriggers != null)
                foreach (var trigger in element.XTriggers)
                    foreach (var morph in trigger.Value ?? new List<MorphDetails>())
                        if (morph != null && !string.IsNullOrEmpty(morph.Id))
                            morphs.Add(new Morph
                            {
                                Catalyst = trigger.Key,
                                Id = morph.Id,
                                Effect = morph.MorphEffect.ToString(),
                                Level = morph.Level.ToString(CultureInfo.InvariantCulture),
                                Chance = morph.Chance
                            });

            if (RoostCompat.Property(element, "xtriggers") is IDictionary roost)
                foreach (DictionaryEntry trigger in roost)
                {
                    if (!(trigger.Key is string catalyst) || !(trigger.Value is IList list))
                        continue;
                    foreach (var morph in list)
                    {
                        var id = (morph as IEntityWithId)?.Id;
                        if (string.IsNullOrEmpty(id))
                            continue;
                        morphs.Add(new Morph
                        {
                            Catalyst = catalyst,
                            Id = id,
                            Effect = RoostCompat.Get(morph, "MorphEffect")?.ToString(),
                            Level = RoostCompat.Formula(RoostCompat.Get(morph, "Level")),
                            Chance = TryInt(RoostCompat.Formula(RoostCompat.Get(morph, "Chance")), out int chance) ? chance : 100
                        });
                    }
                }
            return morphs;
        }

        // Where a card decays to; Roost allows a list.
        private static IEnumerable<string> DecaysTo(Element element)
        {
            var targets = new List<string>();
            if (!string.IsNullOrEmpty(element.DecayTo))
                targets.Add(element.DecayTo);
            if (RoostCompat.Property(element, "DecayTo") is IEnumerable<string> roost)
                targets.AddRange(roost.Where(t => !string.IsNullOrEmpty(t)));
            return targets.Distinct();
        }

        private void IndexCard(Element element)
        {
            string label = ElementLabel(element.Id);

            void Becomes(string into, string tag, string verb)
            {
                if (string.IsNullOrEmpty(into))
                    return;
                var row = new Row { Kind = RowKind.Change, Id = element.Id, Title = label + " " + verb, Tag = tag };
                row.Needs.Add(new Chip { Text = label, Kind = ChipKind.Need, ElementId = element.Id });
                row.Gives.Add(new Chip { Text = ElementLabel(into), Kind = ChipKind.Give, ElementId = into });
                row.Search = SearchText(row);
                Add(MadeBy, into, row, label + " " + verb + " into it");
                Add(UsedBy, element.Id, row, "it " + verb + " into " + ElementLabel(into));
            }

            foreach (var into in DecaysTo(element))
                Becomes(into, "decays", element.Lifetime > 0 ? "decays (" + element.Lifetime.ToString("0", CultureInfo.InvariantCulture) + " s)" : "decays");
            Becomes(element.BurnTo, "burns", "burns");
            Becomes(element.DrownTo, "drowns", "drowns");

            foreach (var morph in XTriggersOf(element))
            {
                string catalyst = ElementLabel(morph.Catalyst);
                var row = new Row { Kind = RowKind.Change, Id = element.Id, Title = label + " with " + catalyst, Tag = "reaction" };
                row.Needs.Add(new Chip { Text = label, Kind = ChipKind.Need, ElementId = element.Id });
                row.Needs.Add(new Chip { Text = catalyst, Kind = ChipKind.Need, ElementId = morph.Catalyst });
                if (!AddReaction(row, element, morph, "", " with " + catalyst, true))
                    continue;
                Add(UsedBy, morph.Catalyst, row, "sets off " + label);
                row.Search = SearchText(row);
            }

            if (element.Induces != null)
                foreach (var induce in element.Induces)
                    if (induce != null && RecipesById.TryGetValue(induce.Id ?? "", out var induced))
                        Add(UsedBy, element.Id, induced, "it can set this off" + (induce.Chance > 0 && induce.Chance < 100 ? " (" + induce.Chance + "%)" : ""));

            // An aspect's page lists the cards that have it.
            if (element.IsAspect)
                return;
            var card = new Row { Kind = RowKind.Card, Id = element.Id, Title = label, Tag = "card" };
            if (element.Aspects != null)
                foreach (var aspect in element.Aspects.Where(a => a.Value != 0).OrderByDescending(a => a.Value))
                {
                    card.Needs.Add(new Chip { Text = ElementLabel(aspect.Key) + " " + aspect.Value, Kind = ChipKind.Aspect, ElementId = aspect.Key });
                    if (aspect.Value > 0)
                        Add(HeldBy, aspect.Key, card, ElementLabel(aspect.Key) + " " + aspect.Value);
                }
            card.Search = SearchText(card);
            Cards[element.Id] = card;
        }

        // A card changing because of a catalyst: by the recipe's aspects (how is empty and
        // the recipe also uses the card), or on its own row (how names the catalyst).
        // False if it's a kind of change that makes nothing, e.g. Roost's Destroy.
        private bool AddReaction(Row row, Element card, Morph morph, string prefix, string how, bool usesCard)
        {
            string from = ElementLabel(card.Id) + how;
            string target = ElementLabel(morph.Id);
            string chance = morph.Chance > 0 && morph.Chance < 100 ? " " + morph.Chance + "%" : "";
            string level = TryInt(morph.Level, out int n) ? Math.Max(1, n).ToString(CultureInfo.InvariantCulture) : "(" + morph.Level + ")";
            string made, used;
            switch (morph.Effect)
            {
                case "Mutate":
                case "SetMutation":
                    row.Gives.Add(new Chip { Text = prefix + (morph.Effect == "Mutate" ? "+" : "set ") + morph.Level + " " + target + chance, Kind = ChipKind.Give, ElementId = morph.Id });
                    made = "adds it to " + from;
                    used = "adds " + target + " to it";
                    break;
                case "Spawn":
                    row.Gives.Add(new Chip { Text = prefix + "+" + level + " " + target + chance, Kind = ChipKind.Give, ElementId = morph.Id });
                    made = "spawned by " + from;
                    used = "it spawns " + target;
                    break;
                case "DeckDraw":
                    AddDraw(row, morph.Id, morph.Level);
                    if (usesCard)
                        Add(UsedBy, card.Id, row, "it draws from a deck" + chance);
                    return true;
                case "Transform":
                case null:
                    row.Gives.Add(new Chip { Text = prefix + target + chance, Kind = ChipKind.Give, ElementId = morph.Id });
                    made = "turns " + from + " into it";
                    used = "turns it into " + target;
                    break;
                default:
                    return false;
            }
            Add(MadeBy, morph.Id, row, made + chance);
            if (usesCard)
                Add(UsedBy, card.Id, row, used + chance);
            return true;
        }

        // ---- Element pages

        // Recipes that ask for one of a card's aspects rather than the card itself.
        public List<Entry> ViaAspectsOf(string elementId)
        {
            if (ViaAspects.TryGetValue(elementId, out var cached))
                return cached;
            var list = new List<Entry>();
            var element = Element(elementId);
            if (element?.Aspects != null && !element.IsAspect)
            {
                var direct = new HashSet<Row>((UsedBy.TryGetValue(elementId, out var used) ? used : new List<Entry>()).Select(e => e.Row));
                var seen = new Dictionary<Row, Entry>();
                foreach (var aspect in element.Aspects.Where(a => a.Value > 0))
                {
                    if (!UsedBy.TryGetValue(aspect.Key, out var users))
                        continue;
                    foreach (var user in users)
                    {
                        if (user.Row.Kind != RowKind.Recipe || direct.Contains(user.Row))
                            continue;
                        string reason = ElementLabel(aspect.Key) + " " + aspect.Value + ": " + user.Reason;
                        if (seen.TryGetValue(user.Row, out var entry))
                            entry.Reason += "; " + reason;
                        else
                            list.Add(seen[user.Row] = new Entry { Row = user.Row, Reason = reason });
                    }
                }
                list.Sort((a, b) => b.Row.Startable.CompareTo(a.Row.Startable) != 0
                    ? b.Row.Startable.CompareTo(a.Row.Startable)
                    : ByTitle(a.Row, b.Row));
            }
            ViaAspects[elementId] = list;
            return list;
        }

        public static List<Entry> Get(Dictionary<string, List<Entry>> map, string id) =>
            id != null && map.TryGetValue(id, out var list) ? list : new List<Entry>();

        // One entry per row and element; a second reason joins the first.
        private static void Add(Dictionary<string, List<Entry>> map, string id, Row row, string reason)
        {
            if (string.IsNullOrEmpty(id))
                return;
            if (!map.TryGetValue(id, out var list))
                map[id] = list = new List<Entry>();
            var existing = list.Count > 0 && list[list.Count - 1].Row == row ? list[list.Count - 1] : null;
            if (existing == null)
                list.Add(new Entry { Row = row, Reason = reason });
            else if (!existing.Reason.Contains(reason))
                existing.Reason += ", " + reason;
        }

        private string SearchText(Row row)
        {
            var parts = new List<string> { row.Title, row.Id, row.Tag, row.VerbId };
            foreach (var chip in row.Needs.Concat(row.Gives))
            {
                parts.Add(chip.Text);
                parts.Add(chip.ElementId);
            }
            return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();
        }

        public static bool Matches(Entry entry, string[] terms)
        {
            foreach (var term in terms)
                if (entry.Row.Search.IndexOf(term, StringComparison.Ordinal) < 0 &&
                    (entry.Reason == null || entry.Reason.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0))
                    return false;
            return true;
        }

        private static bool TryInt(string value, out int n) =>
            int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
    }
}
