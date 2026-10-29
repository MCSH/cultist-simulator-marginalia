using System.Collections.Generic;
using System.Reflection;
using SecretHistories.Entities;
using SecretHistories.Entities.NullEntities;
using SecretHistories.UI;
using SecretHistories.UI.Aspects;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MarginaliaMod
{
    // Works out what's under the pointer: a card or verb token, an aspect icon, or an
    // open verb / card / aspect details window.
    public static class TargetResolver
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly FieldInfo SituationWindowPayload = typeof(SituationWindow).GetField("_payload", Instance);
        private static readonly FieldInfo TokenDetailsElement = typeof(TokenDetailsWindow).GetField("_element", Instance);
        private static readonly FieldInfo AspectDetailsElement = typeof(AspectDetailsWindow).GetField("element", Instance);

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        public static NoteTarget At(Vector2 screenPosition, Transform ignoreRoot)
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
            }
            return null;
        }

        private static NoteTarget Resolve(GameObject go)
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

        private static NoteTarget ForElement(Element element)
        {
            if (element == null || element is NullElement || string.IsNullOrEmpty(element.Id))
                return null;
            return new NoteTarget(TargetKind.Element, element.Id, element.Label);
        }

        private static NoteTarget ForVerb(Verb verb)
        {
            if (verb == null || verb is NullVerb || string.IsNullOrEmpty(verb.Id))
                return null;
            return new NoteTarget(TargetKind.Verb, verb.Id, verb.Label);
        }
    }
}
