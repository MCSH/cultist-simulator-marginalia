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
    // Pauses the game the moment a chosen verb's timer runs out, or just before a chosen
    // kind of card decays. Like notes, the choice is global and keyed by verb or element
    // id, kept in marginalia_pauses.json.
    //
    // Timing: the Heart spends game time in beats, every 0.1s of real time times the
    // game speed, so up to 0.6s at once. Before each beat we work out whether it would
    // carry a watched timer past its mark. If so, we beat by exactly enough to land on
    // the mark ourselves and pause, so the Heart's own beat then spends nothing.
    public static class PauseWatch
    {
        // How much life a card has left when we pause for it.
        private const float CardLead = 0.1f;

        // Timers count whole hundredths and round the time spent down, so ask for half
        // a hundredth more than we mean.
        private const float HalfTick = 0.005f;

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo SinceLastBeat = typeof(Heart).GetField("timeSincePreviousHeartBeat", Private);
        private static readonly FieldInfo BeatInterval = typeof(Heart).GetField("minimumHeartBeatInterval", Private);
        private static readonly MethodInfo SpeedMultiplier = typeof(Heart).GetMethod("GetTimerMultiplierForSpeed", Private);

        private static bool CanTime => SinceLastBeat != null && BeatInterval != null && SpeedMultiplier != null;

        private static HashSet<string> _keys = new HashSet<string>();

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_pauses.json");

        public static void Load()
        {
            if (!CanTime)
                NoonUtility.LogWarning("Marginalia: can't read the game's heartbeat, so pausing on verbs and cards is off.");
            try
            {
                if (File.Exists(FilePath))
                    _keys = new HashSet<string>(JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(FilePath))
                                                ?? new List<string>());
            }
            catch (Exception ex)
            {
                NoonUtility.LogWarning("Marginalia: could not read " + FilePath + ": " + ex.Message);
                JsonFile.Backup(FilePath);
                _keys = new HashSet<string>();
            }
        }

        // A card pauses before it decays. A verb pauses either at every step (each time
        // its timer runs out, even when the recipe leads into another) or only at the end
        // (when no recipe follows). The two verb modes exclude each other.
        public static string CardKey(string id) => "element:" + id;
        public static string StepKey(string id) => "verb:" + id;
        public static string EndKey(string id) => "verb-end:" + id;

        public static bool Has(string key) => _keys.Contains(key);

        public static bool CanDecay(string elementId) =>
            Watchman.Get<Compendium>()?.GetEntityById<Element>(elementId)?.Decays == true;

        public static void Toggle(string key, string exclusiveWith = null)
        {
            if (!_keys.Remove(key))
            {
                _keys.Add(key);
                if (exclusiveWith != null)
                    _keys.Remove(exclusiveWith);
            }
            JsonFile.Write(FilePath, _keys.OrderBy(k => k).ToList());
        }

        // A watched timer that the upcoming beat would carry past its mark.
        private sealed class Mark
        {
            public float Seconds;
            public ITokenPayload Payload;
            public bool OnlyAtEnd;
        }

        private static readonly List<Mark> Marks = new List<Mark>();

        // Called before every Heart beat could happen: from FixedUpdate, and from
        // LateUpdate to catch speed changes made since the last FixedUpdate.
        public static void Check()
        {
            if (_keys.Count == 0 || !CanTime || !Watchman.Exists<Heart>())
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

                Marks.Clear();
                foreach (var sphere in FucineRoot.Get().Spheres)
                {
                    if (sphere.TokenHeartbeatIntervalMultiplier > -1f)
                        Scan(sphere, sphere.TokenHeartbeatIntervalMultiplier, beat);
                }
                if (Marks.Count == 0)
                    return;

                float soonest = Marks.Min(m => m.Seconds);
                var reached = Marks.Where(m => m.Seconds <= soonest + 0.001f).ToList();
                heart.Beat(soonest, 0f);

                var reason = reached.FirstOrDefault(m => !m.OnlyAtEnd || HasEnded((Situation)m.Payload));
                if (reason != null)
                {
                    Watchman.Get<LocalNexus>().PauseGame(true);
                    NoonUtility.Log("Marginalia: paused for " + reason.Payload.Id);
                    return;
                }

                // Nothing to pause for after all: take the time we just spent off the
                // Heart's next beat, so the table doesn't get it twice.
                SinceLastBeat.SetValue(heart, (float)SinceLastBeat.GetValue(heart) - soonest / speed);
            }
        }

        private static bool HasEnded(Situation situation) =>
            situation.Token == null || situation.Token.Defunct || situation.StateIdentifier != StateEnum.Ongoing;

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

        // Walks spheres and tokens the way the Heart spends time on them, with rate the
        // product of the sphere multipliers on the way down.
        private static void Scan(Sphere sphere, float rate, float beat)
        {
            if (rate <= 0f || sphere.Shrouded)
                return;
            foreach (var token in sphere.Tokens)
            {
                // Zero (or less) means it's already at its mark: we paused for it once, so
                // let it go.
                float toMark = ToMark(token.Payload, out bool onlyAtEnd);
                if (toMark > HalfTick && toMark / rate <= beat)
                    Marks.Add(new Mark { Seconds = (toMark + HalfTick) / rate, Payload = token.Payload, OnlyAtEnd = onlyAtEnd });
                foreach (var inner in token.Payload.GetSpheres())
                    Scan(inner, rate * inner.TokenHeartbeatIntervalMultiplier, beat);
            }
        }

        // Game seconds until a watched card or verb reaches its mark, or -1 if unwatched.
        private static float ToMark(ITokenPayload payload, out bool onlyAtEnd)
        {
            onlyAtEnd = false;
            switch (payload)
            {
                case ElementStack stack when stack.Decays && Has(CardKey(stack.Element.Id)):
                    return stack.LifetimeRemaining - CardLead;
                case Situation situation when situation.StateIdentifier == StateEnum.Ongoing:
                    if (Has(StepKey(situation.Verb.Id)))
                        return situation.TimeRemaining;
                    if (!Has(EndKey(situation.Verb.Id)))
                        return -1f;
                    onlyAtEnd = true;
                    return situation.TimeRemaining;
                default:
                    return -1f;
            }
        }
    }

    public static class PauseMenuItems
    {
        public static void Register()
        {
            Add("pause.card", 20, "before it decays", t => t.Kind == TargetKind.Element && (PauseWatch.Has(PauseWatch.CardKey(t.Id)) || PauseWatch.CanDecay(t.Id)),
                PauseWatch.CardKey, null);
            Add("pause.step", 20, "at every step", t => t.Kind == TargetKind.Verb, PauseWatch.StepKey, PauseWatch.EndKey);
            Add("pause.end", 21, "at the end", t => t.Kind == TargetKind.Verb, PauseWatch.EndKey, PauseWatch.StepKey);
        }

        private static void Add(string id, int order, string when, Func<MenuTarget, bool> isVisible,
            Func<string, string> key, Func<string, string> exclusiveWith)
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = id,
                Order = order,
                Label = t => (PauseWatch.Has(key(t.Id)) ? "Stop pausing " : "Pause ") + when,
                IsVisible = isVisible,
                Execute = t => PauseWatch.Toggle(key(t.Id), exclusiveWith?.Invoke(t.Id))
            });
        }
    }
}
