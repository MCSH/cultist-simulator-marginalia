using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using SecretHistories.Abstract;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;

namespace MarginaliaMod
{
    // Pauses the game a chosen number of seconds before a verb's timer runs out or a
    // kind of card decays (or right after, at zero), or whenever a new card of a kind
    // appears. Like notes, the choices are global and keyed by verb or element id,
    // kept in marginalia_pauses.json.
    //
    // Timing: the Heart spends game time in beats, every 0.1s of real time times the
    // game speed, so up to 0.6s at once. Before each beat we work out whether it would
    // carry a watched timer past its mark. If so, we beat by exactly enough to land on
    // the mark ourselves and pause, so the Heart's own beat then spends nothing.
    public static class PauseWatch
    {
        public sealed class Entry
        {
            public string Label;

            // Verbs only: pause right after a recipe that nothing follows, rather than at
            // every step. Whether one follows is only settled once a recipe is over, so
            // this has no lead.
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public bool AtEnd;

            // Seconds before the timer runs out; zero pauses right after.
            [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
            public float Lead;
        }

        public const float StepSeconds = 0.1f;

        // Timers count whole hundredths and round the time spent down, so ask for half
        // a hundredth more than we mean.
        private const float HalfTick = 0.005f;

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo SinceLastBeat = typeof(Heart).GetField("timeSincePreviousHeartBeat", Private);
        private static readonly FieldInfo BeatInterval = typeof(Heart).GetField("minimumHeartBeatInterval", Private);
        private static readonly MethodInfo SpeedMultiplier = typeof(Heart).GetMethod("GetTimerMultiplierForSpeed", Private);

        private static bool CanTime => SinceLastBeat != null && BeatInterval != null && SpeedMultiplier != null;

        private static Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        // What we last saw of each watched timer. Once we've paused at its mark it sits at
        // (or past) the mark, which mustn't pause again; when it's reset (the next recipe,
        // or a looping one) it has more left than last time and counts again.
        private sealed class Watch
        {
            public float LastSeen;
            public bool Paused;
        }

        private static readonly Dictionary<ITokenPayload, Watch> Watches = new Dictionary<ITokenPayload, Watch>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_pauses.json");

        public static void Load()
        {
            if (!CanTime)
                NoonUtility.LogWarning("Marginalia: can't read the game's heartbeat, so pausing on verbs and cards is off.");
            _entries = JsonFile.Read(FilePath, new Dictionary<string, Entry>());
        }

        private const string AppearsPrefix = "appears:";

        private static string Key(TargetKind kind, string id) => (kind == TargetKind.Verb ? "verb:" : "element:") + id;

        public static Entry Get(MenuTarget target) =>
            _entries.TryGetValue(Key(target.Kind, target.Id), out var entry) ? entry : null;

        public static Entry GetAppears(MenuTarget target) =>
            _entries.TryGetValue(AppearsPrefix + target.Id, out var entry) ? entry : null;

        public static void SetAppears(MenuTarget target)
        {
            _entries[AppearsPrefix + target.Id] = new Entry { Label = target.Label };
            JsonFile.Write(FilePath, _entries);
        }

        public static void RemoveAppears(MenuTarget target)
        {
            if (_entries.Remove(AppearsPrefix + target.Id))
                JsonFile.Write(FilePath, _entries);
        }

        public static bool CanDecay(string elementId) =>
            Watchman.Get<Compendium>()?.GetEntityById<Element>(elementId)?.Decays == true;

        public static void Set(MenuTarget target, bool atEnd, float lead)
        {
            _entries[Key(target.Kind, target.Id)] = new Entry { Label = target.Label, AtEnd = atEnd, Lead = lead };
            JsonFile.Write(FilePath, _entries);
        }

        public static void Remove(MenuTarget target)
        {
            if (_entries.Remove(Key(target.Kind, target.Id)))
                JsonFile.Write(FilePath, _entries);
        }

        // Pauses if need be, then moves the game on by a little, still stopping at any
        // watched mark on the way.
        public static void Step()
        {
            if (!Watchman.Exists<Heart>())
                return;
            var heart = Watchman.Get<Heart>();
            if (heart == null || heart.Metapaused || FucineRoot.Get() == null)
                return;
            if (!heart.IsPaused())
                Watchman.Get<LocalNexus>().PauseGame(true);

            float left = StepSeconds;
            for (int pass = 0; pass < 16; pass++)
            {
                float spent = SpendToNextMark(heart, left, out bool stop);
                if (stop)
                    return;
                if (spent <= 0f)
                    break;
                left -= spent;
            }
            if (left > HalfTick)
            {
                heart.Beat(left + HalfTick, 0f);
                Table.Invalidate();
            }
        }

        // Called before every Heart beat could happen: from FixedUpdate, and from
        // LateUpdate to catch speed changes made since the last FixedUpdate.
        public static void Check()
        {
            if (_entries.Count == 0 || !CanTime || !Watchman.Exists<Heart>())
                return;
            var heart = Watchman.Get<Heart>();
            if (heart == null || FucineRoot.Get() == null)
                return;

            // A verb watched only at its end may roll into another recipe, in which case we
            // carry on and look again at what's left of the beat.
            for (int pass = 0; pass < 16; pass++)
            {
                if (heart.Metapaused || heart.IsPaused())
                    return;
                float speed = (float)SpeedMultiplier.Invoke(heart, new object[] { heart.GetEffectiveGameSpeed() });
                float beat = UpcomingBeat(heart, speed);
                if (beat <= 0f)
                    return;

                float spent = SpendToNextMark(heart, beat, out bool stop);
                if (stop)
                {
                    Watchman.Get<LocalNexus>().PauseGame(true);
                    return;
                }
                if (spent <= 0f)
                    return;

                // Nothing to pause for after all: take the time we just spent off the
                // Heart's next beat, so the table doesn't get it twice.
                SinceLastBeat.SetValue(heart, (float)SinceLastBeat.GetValue(heart) - spent / speed);
            }
        }

        // Game seconds the Heart will spend at its next FixedUpdate: it adds a fixed step
        // to its counter and beats once that reaches the interval. Whether its FixedUpdate
        // runs before or after ours, it's the next thing to touch the counter.
        private static float UpcomingBeat(Heart heart, float speed)
        {
            float since = (float)SinceLastBeat.GetValue(heart) + Time.fixedDeltaTime;
            if (since < (float)BeatInterval.GetValue(heart))
                return 0f;
            return since * speed;
        }

        // A watched timer due within the time we're about to spend.
        private sealed class Mark
        {
            public float Seconds;
            public Token Token;
            public ITokenPayload Payload;
            public bool OnlyIfEnded;
        }

        private static readonly List<Mark> Marks = new List<Mark>();
        private static readonly HashSet<ITokenPayload> Seen = new HashSet<ITokenPayload>();

        // If a watched timer reaches its mark within the next budget seconds, spends time up
        // to it and returns how much; stop says whether to pause there. A timer that is
        // already past its mark (it started with less time than the lead) stops at once.
        private static float SpendToNextMark(Heart heart, float budget, out bool stop)
        {
            stop = false;
            Marks.Clear();
            Seen.Clear();
            foreach (var placed in Table.Tokens())
                Consider(placed.Token, placed.Rate, budget);
            foreach (var gone in Watches.Keys.Where(p => !Seen.Contains(p)).ToList())
                Watches.Remove(gone);
            if (Marks.Count == 0)
                return 0f;

            float soonest = Marks.Min(m => m.Seconds);
            var reached = Marks.Where(m => m.Seconds <= soonest + 0.001f).ToList();
            if (soonest > 0f)
            {
                heart.Beat(soonest + HalfTick, 0f);
                Table.Invalidate();
            }

            foreach (var mark in reached)
            {
                if (mark.OnlyIfEnded && !HasEnded((Situation)mark.Payload))
                    continue;
                if (!stop)
                    TokenRing.Clear(RingGroup);
                stop = true;
                TokenRing.Show(mark.Token, RingGroup, IsPaused);
                Watches[mark.Payload] = new Watch { LastSeen = Remaining(mark.Payload), Paused = true };
                NoonUtility.Log("Marginalia: paused for " + mark.Payload.Id);
            }
            return soonest;
        }

        private static bool HasEnded(Situation situation) =>
            situation.Token == null || situation.Token.Defunct || situation.StateIdentifier != StateEnum.Ongoing;

        // rate is how fast the token's timer runs, as the Heart spends time on it.
        private static void Consider(Token token, float rate, float budget)
        {
            var payload = token.Payload;
            Seen.Add(payload);
            if (rate <= 0f || token.Defunct)
                return;
            float toMark = ToMark(payload, out bool onlyIfEnded);
            if (float.IsNaN(toMark))
                return;
            var watch = Observe(payload);
            if (toMark <= HalfTick)
            {
                if (!watch.Paused)
                    Marks.Add(new Mark { Seconds = 0f, Token = token, Payload = payload });
            }
            else if (toMark / rate <= budget)
            {
                Marks.Add(new Mark { Seconds = toMark / rate, Token = token, Payload = payload, OnlyIfEnded = onlyIfEnded });
            }
        }

        // Whether this card or verb is one we'd pause for as its timer runs down.
        public static bool Pauses(ITokenPayload payload) => !float.IsNaN(ToMark(payload, out _));

        private const string RingGroup = "pause";

        private static bool IsPaused()
        {
            var heart = Watchman.Exists<Heart>() ? Watchman.Get<Heart>() : null;
            return heart != null && heart.IsPaused();
        }

        // ---- Pausing when cards appear ------------------------------------------------

        // A new game or a loaded save fills the table over a moment, which isn't its cards
        // appearing, so counts only start to matter this long after.
        private const float SettleSeconds = 3f;

        private static FucineRoot _countedRoot;
        private static float _settledAt;
        private static Dictionary<string, int> _counts = new Dictionary<string, int>();
        private static Dictionary<string, HashSet<Token>> _holders = new Dictionary<string, HashSet<Token>>();

        // Called every frame. Counts each watched kind of card wherever the player can see
        // or reach it (the table, verbs, the hand), and pauses when a count goes up: a new
        // card, or a stack growing. Cards appear within a beat, so this is as soon as it
        // can be.
        public static void CheckAppearances()
        {
            if (!_entries.Keys.Any(k => k.StartsWith(AppearsPrefix)) || !Watchman.Exists<Heart>())
                return;
            var heart = Watchman.Get<Heart>();
            var root = FucineRoot.Get();
            if (heart == null || root == null)
                return;
            if (root != _countedRoot)
            {
                _countedRoot = root;
                _settledAt = Time.unscaledTime + SettleSeconds;
            }

            var counts = new Dictionary<string, int>();
            var holders = new Dictionary<string, HashSet<Token>>();
            foreach (var placed in Table.Tokens())
            {
                if (placed.Hidden || !(placed.Token.Payload is ElementStack stack) ||
                    !_entries.ContainsKey(AppearsPrefix + stack.Element.Id))
                    continue;
                string id = stack.Element.Id;
                counts[id] = (counts.TryGetValue(id, out int count) ? count : 0) + stack.Quantity;
                if (!holders.TryGetValue(id, out var tokens))
                    holders[id] = tokens = new HashSet<Token>();
                tokens.Add(placed.Token);
            }

            var arrived = new List<Token>();
            if (Time.unscaledTime >= _settledAt)
            {
                foreach (var pair in _entries.Where(e => e.Key.StartsWith(AppearsPrefix)))
                {
                    string id = pair.Key.Substring(AppearsPrefix.Length);
                    int now = counts.TryGetValue(id, out int n) ? n : 0;
                    int before = _counts.TryGetValue(id, out int b) ? b : 0;
                    if (now <= before)
                        continue;
                    // Ring the new cards; if a stack just grew, ring every stack of them.
                    var all = holders[id];
                    var fresh = _holders.TryGetValue(id, out var old) ? all.Where(t => !old.Contains(t)).ToList() : all.ToList();
                    arrived.AddRange(fresh.Count > 0 ? fresh : all);
                    NoonUtility.Log("Marginalia: paused as a new " + (pair.Value.Label ?? id) + " appeared");
                }
            }
            _counts = counts;
            _holders = holders;

            if (arrived.Count == 0)
                return;
            if (!heart.IsPaused())
                Watchman.Get<LocalNexus>().PauseGame(true);
            TokenRing.Clear(RingGroup);
            TokenRing.Show(arrived, RingGroup, IsPaused);
        }

        private static Watch Observe(ITokenPayload payload)
        {
            float remaining = Remaining(payload);
            if (!Watches.TryGetValue(payload, out var watch))
                Watches[payload] = watch = new Watch();
            else if (remaining > watch.LastSeen + HalfTick)
                watch.Paused = false;
            watch.LastSeen = remaining;
            return watch;
        }

        private static float Remaining(ITokenPayload payload)
        {
            switch (payload)
            {
                case ElementStack stack: return stack.LifetimeRemaining;
                case Situation situation: return situation.TimeRemaining;
                default: return 0f;
            }
        }

        // Game seconds until a watched card or verb reaches its mark, or NaN if unwatched.
        private static float ToMark(ITokenPayload payload, out bool onlyIfEnded)
        {
            onlyIfEnded = false;
            switch (payload)
            {
                case ElementStack stack when stack.Decays &&
                                             _entries.TryGetValue(Key(TargetKind.Element, stack.Element.Id), out var card):
                    return stack.LifetimeRemaining - card.Lead;
                case Situation situation when situation.StateIdentifier == StateEnum.Ongoing &&
                                              _entries.TryGetValue(Key(TargetKind.Verb, situation.Verb.Id), out var verb):
                    onlyIfEnded = verb.AtEnd;
                    return situation.TimeRemaining - (verb.AtEnd ? 0f : verb.Lead);
                default:
                    return float.NaN;
            }
        }
    }

    public static class PauseMenuItems
    {
        // Slider stops: fine near zero, coarser further out.
        private static readonly float[] Leads = BuildLeads();

        private static float[] BuildLeads()
        {
            var leads = new List<float> { 0f };
            for (int i = 1; i <= 10; i++)
                leads.Add(i / 10f);
            for (float s = 1.25f; s <= 3f; s += 0.25f)
                leads.Add(s);
            for (float s = 3.5f; s <= 10f; s += 0.5f)
                leads.Add(s);
            for (int s = 11; s <= 30; s++)
                leads.Add(s);
            for (int s = 35; s <= 60; s += 5)
                leads.Add(s);
            for (int s = 70; s <= 110; s += 10)
                leads.Add(s);
            for (int s = 135; s <= 185; s += 25)
                leads.Add(s);
            return leads.ToArray();
        }

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "pause.card",
                Order = 20,
                Label = t => PauseWatch.Get(t) is PauseWatch.Entry e
                    ? (e.Lead > 0f ? "Pausing " + Seconds(e.Lead) + " before it decays…" : "Pausing right after it decays…")
                    : "Pause before it decays…",
                IsVisible = t => t.Kind == TargetKind.Element && (PauseWatch.Get(t) != null || PauseWatch.CanDecay(t.Id)),
                Execute = OpenSlider
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "pause.step",
                Order = 20,
                Label = t => PauseWatch.Get(t) is PauseWatch.Entry e && !e.AtEnd
                    ? "Pausing at every step" + (e.Lead > 0f ? ", " + Seconds(e.Lead) + " before" : "") + "…"
                    : "Pause at every step…",
                IsVisible = t => t.Kind == TargetKind.Verb,
                Execute = OpenSlider
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "pause.end",
                Order = 22,
                Label = t => PauseWatch.Get(t)?.AtEnd == true ? "Stop pausing at the end" : "Pause at the end",
                IsVisible = t => t.Kind == TargetKind.Verb,
                Execute = t =>
                {
                    if (PauseWatch.Get(t)?.AtEnd == true)
                        PauseWatch.Remove(t);
                    else
                        PauseWatch.Set(t, true, 0f);
                }
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "pause.remove",
                Order = 21,
                Label = t => t.Kind == TargetKind.Verb ? "Stop pausing at every step" : "Stop pausing before it decays",
                IsVisible = t => PauseWatch.Get(t) is PauseWatch.Entry e && !e.AtEnd,
                Execute = PauseWatch.Remove
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "pause.appears",
                Order = 23,
                Label = t => PauseWatch.GetAppears(t) != null ? "Stop pausing when one appears" : "Pause when one appears",
                IsVisible = t => t.Kind == TargetKind.Element,
                Execute = t =>
                {
                    if (PauseWatch.GetAppears(t) != null)
                        PauseWatch.RemoveAppears(t);
                    else
                        PauseWatch.SetAppears(t);
                }
            });

            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "pause.step-time",
                Order = 40,
                Label = _ => "Step time " + Seconds(PauseWatch.StepSeconds) + " forward",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = _ => PauseWatch.Step(),
                KeepOpen = true
            });
        }

        // How long before a card decays, or before each of a verb's recipes ends. Opens on
        // the current setting, or for a new one on 0.1 s for cards and 0 s for verbs.
        private static void OpenSlider(MenuTarget target)
        {
            var current = PauseWatch.Get(target);
            bool card = target.Kind == TargetKind.Element;
            bool editing = current != null && !current.AtEnd;
            float lead = editing ? current.Lead : card ? 0.1f : 0f;
            var field = new SizerField("Before", NearestStop(lead), 0, Leads.Length - 1) { Format = i => Seconds(Leads[i]) };

            MarginaliaController.Instance.OpenSizer(
                "Pause " + target.Label + (card ? "" : " at every step"),
                (card ? "Before it decays" : "Before each recipe ends") + " · 0 s is right after",
                new List<SizerField> { field },
                () => { },
                () => PauseWatch.Set(target, false, Leads[field.Value]),
                () => { },
                editing ? () => PauseWatch.Remove(target) : (Action)null);
        }

        private static int NearestStop(float lead)
        {
            int best = 0;
            for (int i = 1; i < Leads.Length; i++)
            {
                if (Math.Abs(Leads[i] - lead) < Math.Abs(Leads[best] - lead))
                    best = i;
            }
            return best;
        }

        private static string Seconds(float seconds) => seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }
}
