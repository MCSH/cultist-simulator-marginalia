using System.Collections.Generic;
using System.Reflection;
using SecretHistories.Entities;
using SecretHistories.Entities.NullEntities;
using SecretHistories.Spheres;
using SecretHistories.UI;
using SecretHistories.UI.Aspects;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MarginaliaMod
{
    // Works out what's under the pointer: a card or verb token, an aspect icon, an
    // open verb / card / aspect details window, or the bare table (and its labels).
    public static class TargetResolver
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo SituationWindowPayload = typeof(SituationWindow).GetField("_payload", Instance);
        private static readonly FieldInfo TokenDetailsElement = typeof(TokenDetailsWindow).GetField("_element", Instance);
        private static readonly FieldInfo AspectDetailsElement = typeof(AspectDetailsWindow).GetField("element", Instance);

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        public static MenuTarget At(Vector2 screenPosition, Transform ignoreRoot)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return null;

            Hits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPosition }, Hits);

            foreach (var hit in Hits)
            {
                if (hit.gameObject == null || hit.gameObject.transform.IsChildOf(ignoreRoot))
                    continue;
                var target = Resolve(hit.gameObject);
                if (target != null)
                    return target;
                // Nothing in front of the table claimed the click. Labels aren't raycast
                // targets (cards must drop through them), so hit-test them by hand.
                if (IsBoard(hit.gameObject))
                    return BoardTarget(screenPosition);
            }

            if (Hits.Count > 0)
                NoonUtility.Log("Marginalia: nothing to annotate under pointer; top hit was " + Path(Hits[0].gameObject));
            return null;
        }

        private static bool IsBoard(GameObject go)
        {
            return go.GetComponentInParent<TabletopBackground>() != null ||
                   go.GetComponentInParent<CameraDragRect>() != null ||
                   go.GetComponentInParent<TabletopSphere>() != null;
        }

        private static MenuTarget BoardTarget(Vector2 screenPosition)
        {
            var label = BoardLabels.LabelAt(screenPosition);
            if (label != null)
                return MenuTarget.ForBoardLabel(label);
            return BoardLabels.ScreenToBoard(screenPosition, out var position) ? MenuTarget.ForBoard(position) : null;
        }

        private static string Path(GameObject go)
        {
            var path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }

        private static MenuTarget Resolve(GameObject go)
        {
            // Aspect icons sit inside tokens and windows, so check them first.
            var frame = go.GetComponentInParent<ElementFrame>();
            if (frame != null && !string.IsNullOrEmpty(frame.ElementId))
                return ForElement(Watchman.Get<Compendium>().GetEntityById<Element>(frame.ElementId));

            var token = go.GetComponentInParent<Token>();
            if (token != null && !token.Defunct)
            {
                switch (token.Payload)
                {
                    case ElementStack stack:
                        return ForElement(stack.Element);
                    case Situation situation:
                        return ForVerb(situation.Verb);
                }
            }

            var situationWindow = go.GetComponentInParent<SituationWindow>();
            if (situationWindow != null && SituationWindowPayload?.GetValue(situationWindow) is Situation windowSituation)
                return ForVerb(windowSituation.Verb);

            var tokenDetails = go.GetComponentInParent<TokenDetailsWindow>();
            if (tokenDetails != null)
                return ForElement(TokenDetailsElement?.GetValue(tokenDetails) as Element);

            var aspectDetails = go.GetComponentInParent<AspectDetailsWindow>();
            if (aspectDetails != null)
                return ForElement(AspectDetailsElement?.GetValue(aspectDetails) as Element);

            return null;
        }

        private static MenuTarget ForElement(Element element)
        {
            if (element == null || element is NullElement || string.IsNullOrEmpty(element.Id))
                return null;
            return MenuTarget.ForElement(element.Id, element.Label);
        }

        private static MenuTarget ForVerb(Verb verb)
        {
            if (verb == null || verb is NullVerb || string.IsNullOrEmpty(verb.Id))
                return null;
            return MenuTarget.ForVerb(verb.Id, verb.Label);
        }
    }
}
