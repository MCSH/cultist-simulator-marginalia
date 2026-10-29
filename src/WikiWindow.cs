using System;
using System.Collections.Generic;
using SecretHistories.Infrastructure;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // An in-game reader for the community wiki. Lives on the controller's shield
    // canvas, so while it's open the game underneath can't be clicked; links in the
    // article navigate within the window, with a Back history.
    public class WikiWindow : MonoBehaviour
    {
        private const float Width = 920f;

        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private ScrollRect _scroll;
        private Button _back;

        private readonly Stack<string> _history = new Stack<string>();
        private string _current;   // article title being shown or loaded
        private string _searched;  // in-game name the window was opened for
        private int _request;

        public bool IsOpen => gameObject.activeSelf;

        public static WikiWindow Create(Transform canvas)
        {
            var root = UiKit.Rect("MarginaliaWiki", canvas);
            root.anchorMin = new Vector2(0.5f, 0.05f);
            root.anchorMax = new Vector2(0.5f, 0.95f);
            root.sizeDelta = new Vector2(Width, 0f);
            var panel = root.gameObject.AddComponent<Image>();
            panel.sprite = UiKit.Panel(UiKit.Ink, UiKit.Brass);
            panel.type = Image.Type.Sliced;

            var window = root.gameObject.AddComponent<WikiWindow>();
            window.Build(root);
            root.gameObject.SetActive(false);
            return window;
        }

        private void Build(RectTransform root)
        {
            var title = UiKit.Rect("Title", root);
            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.offsetMin = new Vector2(28f, -74f);
            title.offsetMax = new Vector2(-330f, -20f);
            _title = UiKit.Text(title, 34f, UiKit.Parchment);
            _title.fontStyle = FontStyles.Bold;
            _title.alignment = TextAlignmentOptions.MidlineLeft;
            _title.enableWordWrapping = false;
            _title.overflowMode = TextOverflowModes.Ellipsis;

            var subtitle = UiKit.Rect("Subtitle", root);
            subtitle.anchorMin = new Vector2(0f, 1f);
            subtitle.anchorMax = new Vector2(1f, 1f);
            subtitle.offsetMin = new Vector2(28f, -100f);
            subtitle.offsetMax = new Vector2(-330f, -74f);
            var subtitleText = UiKit.Text(subtitle, 15f, UiKit.Faded);
            subtitleText.fontStyle = FontStyles.Italic;
            subtitleText.text = "from the Cultist Simulator Wiki";

            MakeButton(root, "Close", 0, Close);
            MakeButton(root, "Browser", 1, OpenInBrowser);
            _back = MakeButton(root, "Back", 2, Back);

            var rule = UiKit.Rect("Rule", root);
            rule.anchorMin = new Vector2(0f, 1f);
            rule.anchorMax = new Vector2(1f, 1f);
            rule.offsetMin = new Vector2(28f, -113f);
            rule.offsetMax = new Vector2(-28f, -112f);
            rule.gameObject.AddComponent<Image>().color = new Color(UiKit.Brass.r, UiKit.Brass.g, UiKit.Brass.b, 0.35f);

            BuildScrollView(root);
        }

        private void BuildScrollView(RectTransform root)
        {
            var area = UiKit.Rect("Scroll", root).Stretch(28f, 24f, 16f, 128f);
            _scroll = area.gameObject.AddComponent<ScrollRect>();

            var viewport = UiKit.Rect("Viewport", area).Stretch(0f, 0f, 20f, 0f);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // catches scroll/drag
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = UiKit.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            _body = UiKit.Text(content, 21f, UiKit.Parchment);
            _body.richText = true;
            _body.enableWordWrapping = true;
            _body.lineSpacing = 4f;
            _body.raycastTarget = true; // for link clicks
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            content.gameObject.AddComponent<WikiLinks>().Init(this, _body);

            var bar = UiKit.Rect("Scrollbar", area);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(6f, 0f);
            bar.gameObject.AddComponent<Image>().color = new Color(UiKit.Brass.r, UiKit.Brass.g, UiKit.Brass.b, 0.12f);
            var slidingArea = UiKit.Rect("Sliding Area", bar).Stretch();
            var handle = UiKit.Rect("Handle", slidingArea).Stretch();
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = new Color(UiKit.Brass.r, UiKit.Brass.g, UiKit.Brass.b, 0.75f);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

            _scroll.content = content;
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 45f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        // Buttons sit in a row at the top right, counted from the right edge.
        private static Button MakeButton(RectTransform root, string label, int fromRight, Action onClick)
        {
            const float w = 92f, h = 36f, gap = 8f, margin = 24f;
            var rect = UiKit.Rect(label + "Button", root);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = new Vector2(-(margin + fromRight * (w + gap)), -30f);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiKit.Panel(UiKit.Ember, UiKit.Brass);
            image.type = Image.Type.Sliced;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.55f, 0.55f, 0.55f, 1f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var text = UiKit.Text(UiKit.Rect("Label", rect).Stretch(), 18f, UiKit.Parchment);
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;
            return button;
        }

        // ---- Navigation

        public void OpenFor(string name)
        {
            Show();
            _history.Clear();
            _current = null;
            _searched = name;
            int request = ++_request;
            ShowMessage(name, "Consulting the wiki…");
            StartCoroutine(Wiki.FindTitle(name, (title, error) =>
            {
                if (request != _request)
                    return;
                if (error != null)
                    ShowMessage(name, error);
                else if (title == null)
                    ShowMessage(name, "The wiki has no article for “" + name + "”. Try Browser to search it.");
                else
                    Load(title);
            }));
        }

        public void Navigate(string title)
        {
            if (_current != null && !string.Equals(_current, title, StringComparison.OrdinalIgnoreCase))
                _history.Push(_current);
            Load(title);
        }

        public void Back()
        {
            if (_history.Count > 0)
                Load(_history.Pop());
        }

        private void Load(string title)
        {
            int request = ++_request;
            _current = title;
            ShowMessage(title, "Consulting the wiki…");
            StartCoroutine(Wiki.Fetch(title, (article, error) =>
            {
                if (request != _request)
                    return;
                if (error != null)
                {
                    ShowMessage(title, error);
                    return;
                }
                _current = article.Title;
                Show(article.Title, article.Body);
            }));
        }

        private void OpenInBrowser()
        {
            if (_current != null)
                Wiki.OpenInBrowser(_current);
            else if (_searched != null)
                Wiki.SearchInBrowser(_searched);
        }

        // ---- Display

        private void ShowMessage(string title, string message)
        {
            Show(title, "<i><color=#A89980>" + message + "</color></i>");
        }

        private void Show(string title, string body)
        {
            _title.text = title;
            _body.text = body;
            _back.interactable = _history.Count > 0;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void Show()
        {
            if (!IsOpen)
            {
                gameObject.SetActive(true);
                // Otherwise the scroll wheel also zooms the table underneath.
                Watchman.Get<LocalNexus>()?.DisablePlayerZoom();
            }
            transform.SetAsLastSibling();
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            ++_request;
            StopAllCoroutines();
            gameObject.SetActive(false);
            Watchman.Get<LocalNexus>()?.EnablePlayerZoom();
        }
    }

    // Routes clicks on <link="Title"> spans in the article to the window.
    internal class WikiLinks : MonoBehaviour, IPointerClickHandler
    {
        private WikiWindow _window;
        private TextMeshProUGUI _text;

        public void Init(WikiWindow window, TextMeshProUGUI text)
        {
            _window = window;
            _text = text;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging)
                return;
            int index = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, eventData.pressEventCamera);
            if (index >= 0)
                _window.Navigate(_text.textInfo.linkInfo[index].GetLinkID());
        }
    }
}
