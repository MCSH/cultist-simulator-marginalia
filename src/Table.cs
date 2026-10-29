using System.Collections.Generic;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;

namespace MarginaliaMod
{
    // Every token the game holds, found the way the Heart walks them: the root's spheres,
    // then the spheres inside each token, and so on down.
    public static class Table
    {
        public sealed class Placed
        {
            public Token Token;

            // How fast its timer runs: the product of the sphere multipliers above it, as
            // the Heart applies them. Zero means it isn't ticking.
            public float Rate;

            // In a deck, a dormant or hidden pile, or similar: not somewhere the player can
            // see it or reach it.
            public bool Hidden;
        }

        private static readonly List<Placed> Cache = new List<Placed>();
        private static int _cachedFrame = -1;

        // Walked at most once a frame. The game's own state, so read it and let it go.
        public static List<Placed> Tokens()
        {
            if (_cachedFrame == Time.frameCount)
                return Cache;
            Cache.Clear();
            _cachedFrame = Time.frameCount;
            var root = FucineRoot.Get();
            if (root == null)
                return Cache;
            foreach (var sphere in root.Spheres)
            {
                float multiplier = sphere.TokenHeartbeatIntervalMultiplier;
                Walk(sphere, multiplier > -1f ? multiplier : 0f, false, 0);
            }
            return Cache;
        }

        // A change made in the middle of a frame (we just spent some time ourselves).
        public static void Invalidate()
        {
            _cachedFrame = -1;
        }

        private static void Walk(Sphere sphere, float rate, bool hidden, int depth)
        {
            if (sphere == null || depth > 16)
                return;
            hidden = hidden || IsHidden(sphere);
            // The Heart doesn't spend time on a shrouded sphere's tokens.
            if (sphere.Shrouded || rate < 0f)
                rate = 0f;
            foreach (var token in sphere.Tokens)
            {
                Cache.Add(new Placed { Token = token, Rate = rate, Hidden = hidden });
                foreach (var inner in token.Payload.GetSpheres())
                    Walk(inner, rate * inner.TokenHeartbeatIntervalMultiplier, hidden, depth + 1);
            }
        }

        private static bool IsHidden(Sphere sphere)
        {
            if (sphere is AbstractCardPile || sphere is DrydockSphere || sphere is OffstageSphere && sphere.ContentsHidden)
                return true;
            switch (sphere.SphereCategory)
            {
                case SphereCategory.World:
                case SphereCategory.Threshold:
                case SphereCategory.SituationStorage:
                case SphereCategory.Output:
                case SphereCategory.Commitment:
                    return false;
                default:
                    return true;
            }
        }

        public static string LabelOf(Token token)
        {
            switch (token.Payload)
            {
                case ElementStack stack: return stack.Element.Label;
                case Situation situation: return situation.Verb.Label;
                default: return token.Payload.Id;
            }
        }

        // Element id for a card, verb id for a verb.
        public static string IdOf(Token token)
        {
            switch (token.Payload)
            {
                case ElementStack stack: return stack.Element.Id;
                case Situation situation: return situation.Verb.Id;
                default: return null;
            }
        }
    }
}
