using System.Collections.Generic;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // A small round "..." button that appears in the top-left corner of a card or verb
    // when the pointer rests on it; clicking it opens the menu, no key needed. It's
    // parented to the token, so it moves and zooms with it. It takes the pointer events
    // itself, so pressing or dragging it never picks up the card underneath.
    internal class MenuHandle : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float HoverDelay = 0.4f;
        private const float LingerSeconds = 0.3f;
        private static readonly Color Fill = new Color(0.07f, 0.06f, 0.05f, 0.92f);

        private static MenuHandle _current;
        private static Token _hovered;
        private static float _hoverSince, _lastOver;
        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        private Token _token;

        // Called every frame. buttonsDown hides it unless the press is on the handle itself
        // (so dragging a card hides it, but clicking the handle works).
        public static void Tick(Vector2 pointer, bool allowed, bool buttonsDown)
        {
            if (!allowed)
            {
                Hide();
                _hovered = null;
                return;
            }
            float now = Time.unscaledTime;
            var token = TokenUnder(pointer, out bool onHandle);
            if (onHandle)
            {
                _lastOver = now;
                return;
            }
            if (buttonsDown)
            {
                Hide();
                _hovered = null;
                return;
            }

            if (token != _hovered)
            {
                _hovered = token;
                _hoverSince = now;
            }
            if (_current != null && token == _current._token)
                _lastOver = now;
            if (_current != null && (_current._token == null || _current._token.Defunct || now - _lastOver > LingerSeconds))
                Hide();
            if (_current == null && token != null && now - _hoverSince >= HoverDelay && TargetResolver.ForToken(token) != null)
                Show(token);
        }

        public static void Hide()
        {
            if (_current != null)
                Destroy(_current.gameObject);
            _current = null;
        }

        // The card or verb at the top under the pointer, if that's what is there.
        private static Token TokenUnder(Vector2 pointer, out bool onHandle)
        {
            onHandle = false;
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return null;
            Hits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = pointer }, Hits);
            if (Hits.Count == 0 || Hits[0].gameObject == null)
                return null;
            var top = Hits[0].gameObject;
            var handle = top.GetComponentInParent<MenuHandle>();
            if (handle != null)
            {
                onHandle = true;
                return handle._token;
            }
            var token = top.GetComponentInParent<Token>();
            return token != null && !token.Defunct ? token : null;
        }

        private static void Show(Token token)
        {
            var parent = token.TokenRectTransform != null ? token.TokenRectTransform : (RectTransform)token.transform;
            var size = TokenRing.TokenSize(token, parent);
            float s = Mathf.Clamp(Mathf.Min(size.x, size.y) * 0.3f, 16f, 44f);
            float inset = s * 0.15f;

            var rect = UiKit.Rect("MarginaliaMenuHandle", parent);
            rect.SetAsLastSibling();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-size.x / 2f + s / 2f + inset, size.y / 2f - s / 2f - inset);
            rect.sizeDelta = new Vector2(s, s);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var disc = rect.gameObject.AddComponent<Image>();
            disc.sprite = TokenRing.DiscSprite();
            disc.color = Fill;

            var edge = UiKit.Rect("Edge", rect).Stretch();
            Decorate(edge, TokenRing.RingSprite(), UiKit.Brass);
            for (int i = -1; i <= 1; i++)
            {
                var dot = UiKit.Rect("Dot", rect);
                dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0.5f, 0.5f);
                dot.anchoredPosition = new Vector2(i * s * 0.22f, 0f);
                dot.sizeDelta = new Vector2(s * 0.15f, s * 0.15f);
                Decorate(dot, TokenRing.DiscSprite(), UiKit.Parchment);
            }

            _current = rect.gameObject.AddComponent<MenuHandle>();
            _current._token = token;
            _lastOver = Time.unscaledTime;
        }

        private static void Decorate(RectTransform rect, Sprite sprite, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;
            var target = TargetResolver.ForToken(_token);
            Hide();
            if (target != null)
                MarginaliaController.Instance.OpenMenu(target, eventData.position);
        }

        // Taken so they don't reach the card underneath.
        public void OnPointerDown(PointerEventData eventData) { }
        public void OnPointerUp(PointerEventData eventData) { }
        public void OnBeginDrag(PointerEventData eventData) { }
        public void OnDrag(PointerEventData eventData) { }
        public void OnEndDrag(PointerEventData eventData) { }
    }
}
