using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Services;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // Owns the hidden context menu, the text editor, the wiki window, and dragging
    // board labels.
    //
    // Input: holding the modifier (Alt by default) raises an invisible full-screen
    // "shield" canvas above the game's UI, so a modifier+right-click never reaches the
    // game's tokens (which would otherwise send cards to verbs or toggle windows).
    // The shield stays up while the menu or editor is open, so the game below is inert.
    //
    // Hotkeys: the game ignores its hotkeys while the EventSystem's selected object has
    // an InputField, so while we're modal we keep a dormant InputField selected.
    public class MarginaliaController : MonoBehaviour
    {
        private const string ModifierConfigKey = "marginalia_modifier";
        private const string NoteControlName = "MarginaliaNote";
        private const float MenuWidth = 300f;
        private const float EditorWidth = 600f;
        private const float EditorHeight = 400f;

        public static MarginaliaController Instance { get; private set; }

        private GameObject _shield;
        private GameObject _focusSink;
        private ButtonControl _modifier;

        private MenuTarget _menuTarget;
        private Vector2 _menuScreenPos;
        private List<ContextMenuItem> _menuItems;

        private sealed class EditorState
        {
            public string Title;
            public string Subtitle;
            public string Text;
            public Action<string> OnKeep;
            public Action OnErase;
        }

        private EditorState _editor;
        private bool _focusEditor;
        private Vector2 _editorScroll;

        private BoardLabels.Data _dragLabel;
        private Vector2 _dragOffset;

        private WikiWindow _wiki;

        private bool IsModal => _menuTarget != null || _editor != null || _wiki.IsOpen;

        private void Awake()
        {
            Instance = this;
            BuildShield();
            BuildFocusSink();
        }

        private void BuildShield()
        {
            _shield = new GameObject("MarginaliaShield");
            _shield.transform.SetParent(transform, false);
            var canvas = _shield.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            _shield.AddComponent<GraphicRaycaster>();
            // uGUI windows on the shield are laid out for 1080p and scale with the screen.
            var scaler = _shield.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(_shield.transform, false);
            var rect = blocker.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // Fully transparent, but still a raycast target.
            blocker.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            // Clicking outside an open window dismisses it.
            var backdrop = blocker.AddComponent<ShieldBlocker>();
            backdrop.CanDismiss = () => _wiki.IsOpen;
            backdrop.Dismiss = () => _wiki.Close();

            _wiki = WikiWindow.Create(_shield.transform);
            _shield.SetActive(false);
        }

        private void BuildFocusSink()
        {
            // Kept inactive: it only needs to *have* an InputField for the game's
            // UIController.IsEditingText() check, never to behave like one.
            _focusSink = new GameObject("MarginaliaFocusSink");
            _focusSink.SetActive(false);
            _focusSink.transform.SetParent(transform, false);
            _focusSink.AddComponent<InputField>().enabled = false;
        }

        private ButtonControl Modifier(Keyboard keyboard)
        {
            if (_modifier != null)
                return _modifier;
            var config = Watchman.Get<Config>();
            if (config == null)
                return keyboard.altKey;
            switch ((config.GetConfigValue(ModifierConfigKey) ?? "alt").Trim().ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    _modifier = keyboard.ctrlKey;
                    break;
                case "shift":
                    _modifier = keyboard.shiftKey;
                    break;
                default:
                    _modifier = keyboard.altKey;
                    break;
            }
            return _modifier;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            BoardLabels.Tick();

            bool modifierHeld = Modifier(keyboard).isPressed;
            _shield.SetActive(modifierHeld || IsModal || _dragLabel != null);
            var position = mouse.position.ReadValue();

            if (_dragLabel != null)
            {
                ContinueDrag(mouse, position);
                return;
            }

            if (_wiki.IsOpen)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    _wiki.Close();
                else if (mouse.backButton.wasPressedThisFrame)
                    _wiki.Back();
                return;
            }

            if (modifierHeld && _editor == null && mouse.rightButton.wasPressedThisFrame)
            {
                var target = TargetResolver.At(position, _shield.transform);
                if (target != null)
                    OpenMenu(target, position);
                else
                    CloseMenu();
            }
            else if (modifierHeld && !IsModal && mouse.leftButton.wasPressedThisFrame)
            {
                TryStartDrag(position);
            }
        }

        // ---- Moving board labels (modifier + left-drag) ---------------------------------

        private void TryStartDrag(Vector2 position)
        {
            var target = TargetResolver.At(position, _shield.transform);
            if (target == null || target.Kind != TargetKind.BoardLabel)
                return;
            var label = BoardLabels.Get(target.Id);
            if (label == null || !BoardLabels.ScreenToBoard(position, out var pointer))
                return;
            _dragLabel = label;
            _dragOffset = new Vector2(label.X, label.Y) - pointer;
        }

        private void ContinueDrag(Mouse mouse, Vector2 position)
        {
            if (!BoardLabels.IsAttached || BoardLabels.Get(_dragLabel.Id) == null)
            {
                _dragLabel = null;
                return;
            }
            if (BoardLabels.ScreenToBoard(position, out var pointer))
                BoardLabels.Move(_dragLabel, pointer + _dragOffset);
            if (!mouse.leftButton.isPressed)
            {
                _dragLabel = null;
                BoardLabels.Save();
            }
        }

        private void LateUpdate()
        {
            // Clicking anywhere makes the EventSystem drop its selection, so re-assert it
            // every frame; keyboard input is processed before the next Update.
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return;
            if (IsModal)
            {
                if (eventSystem.currentSelectedGameObject != _focusSink)
                    eventSystem.SetSelectedGameObject(_focusSink);
            }
            else if (eventSystem.currentSelectedGameObject == _focusSink)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        public void OpenWiki(MenuTarget target)
        {
            CloseMenu();
            _wiki.OpenFor(Wiki.CleanName(target.Label));
        }

        public void OpenMenu(MenuTarget target, Vector2 screenPosition)
        {
            _menuTarget = target;
            _menuScreenPos = screenPosition;
            _menuItems = ContextMenuRegistry.ItemsFor(target).ToList();
        }

        public void CloseMenu()
        {
            _menuTarget = null;
            _menuItems = null;
        }

        // A modal multi-line text editor. onKeep receives the text (possibly empty);
        // onErase, if given, adds an Erase button.
        public void OpenEditor(string title, string subtitle, string text, Action<string> onKeep, Action onErase = null)
        {
            CloseMenu();
            _editor = new EditorState
            {
                Title = title,
                Subtitle = subtitle,
                Text = text ?? "",
                OnKeep = onKeep,
                OnErase = onErase
            };
            _editorScroll = Vector2.zero;
            _focusEditor = true;
        }

        private void CloseEditor()
        {
            _editor = null;
            GUIUtility.keyboardControl = 0;
        }

        private void KeepEditor()
        {
            var editor = _editor;
            CloseEditor();
            editor.OnKeep(editor.Text);
        }

        private void EraseEditor()
        {
            var editor = _editor;
            CloseEditor();
            editor.OnErase();
        }

        // ---- IMGUI ------------------------------------------------------------------

        private void OnGUI()
        {
            if (!IsModal)
                return;

            Styles.Ensure();
            GUI.depth = -1000;
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 3f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var screen = new Rect(0f, 0f, Screen.width / scale, Screen.height / scale);

            if (_editor != null)
                DrawEditor(screen);
            else
                DrawMenu(screen, scale);
        }

        private void DrawMenu(Rect screen, float scale)
        {
            var e = Event.current;
            const float pad = 12f;
            float inner = MenuWidth - pad * 2f;

            var title = new GUIContent(_menuTarget.Label);
            var subtitle = new GUIContent(_menuTarget.Subtitle);
            string note = _menuTarget.CanHaveNote ? NoteStore.Get(_menuTarget) : null;
            var preview = string.IsNullOrEmpty(note) ? null : new GUIContent(Truncate(note, 240));

            float titleH = Styles.Title.CalcHeight(title, inner);
            float subtitleH = Styles.Subtitle.CalcHeight(subtitle, inner);
            float previewH = preview == null ? 0f : Styles.Preview.CalcHeight(preview, inner) + 8f;
            float itemH = Styles.Item.fixedHeight;
            float height = pad + titleH + subtitleH + previewH + 8f + _menuItems.Count * itemH + pad;

            // Open at the pointer, nudged back on screen if it would spill over an edge.
            var pos = new Vector2(_menuScreenPos.x / scale, (Screen.height - _menuScreenPos.y) / scale);
            var rect = new Rect(
                Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, screen.width - MenuWidth)),
                Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, screen.height - height)),
                MenuWidth, height);

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                CloseMenu();
                e.Use();
                return;
            }
            // The right-click that opened the menu also arrives here; swallow right-button
            // events so it can't dismiss the menu or press an item. Alt+right-click
            // elsewhere re-opens via Update instead.
            if (e.isMouse && e.button == 1)
            {
                e.Use();
            }
            else if (e.type == EventType.MouseDown && !rect.Contains(e.mousePosition))
            {
                CloseMenu();
                e.Use();
                return;
            }

            GUI.Box(rect, GUIContent.none, Styles.Panel);
            float y = rect.y + pad;
            GUI.Label(new Rect(rect.x + pad, y, inner, titleH), title, Styles.Title);
            y += titleH;
            GUI.Label(new Rect(rect.x + pad, y, inner, subtitleH), subtitle, Styles.Subtitle);
            y += subtitleH;
            if (preview != null)
            {
                y += 8f;
                GUI.Label(new Rect(rect.x + pad, y, inner, previewH - 8f), preview, Styles.Preview);
                y += previewH - 8f;
            }
            y += 8f;
            GUI.Box(new Rect(rect.x + pad, y - 4f, inner, 1f), GUIContent.none, Styles.Rule);

            foreach (var item in _menuItems)
            {
                if (GUI.Button(new Rect(rect.x + 4f, y, MenuWidth - 8f, itemH), item.Label(_menuTarget), Styles.Item))
                {
                    var target = _menuTarget;
                    if (item.KeepOpen)
                    {
                        item.Execute(target);
                        _menuItems = ContextMenuRegistry.ItemsFor(target).ToList();
                    }
                    else
                    {
                        CloseMenu();
                        item.Execute(target);
                    }
                    return;
                }
                y += itemH;
            }
        }

        private void DrawEditor(Rect screen)
        {
            var e = Event.current;

            // Handle shortcuts before the text area gets a chance to eat them.
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    CloseEditor();
                    e.Use();
                    return;
                }
                bool enter = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n';
                if (enter && (e.control || e.command))
                {
                    KeepEditor();
                    e.Use();
                    return;
                }
            }

            GUI.Box(screen, GUIContent.none, Styles.Dim);

            const float pad = 16f;
            var rect = new Rect((screen.width - EditorWidth) / 2f, (screen.height - EditorHeight) / 2f, EditorWidth, EditorHeight);
            GUI.Box(rect, GUIContent.none, Styles.Panel);

            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f));
            GUILayout.Label(_editor.Title, Styles.Title);
            GUILayout.Label(_editor.Subtitle, Styles.Subtitle);
            GUILayout.Space(10f);

            _editorScroll = GUILayout.BeginScrollView(_editorScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, Styles.Field, GUILayout.ExpandHeight(true));
            GUI.SetNextControlName(NoteControlName);
            _editor.Text = GUILayout.TextArea(_editor.Text, Styles.TextArea, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            if (_focusEditor && e.type == EventType.Repaint)
            {
                GUI.FocusControl(NoteControlName);
                _focusEditor = false;
            }

            GUILayout.Space(10f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ctrl+Enter to keep · Esc to discard", Styles.Hint);
            GUILayout.FlexibleSpace();
            bool erase = _editor.OnErase != null && GUILayout.Button("Erase", Styles.Button, GUILayout.Width(90f));
            bool cancel = GUILayout.Button("Cancel", Styles.Button, GUILayout.Width(90f));
            bool save = GUILayout.Button("Keep", Styles.Button, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (erase)
                EraseEditor();
            else if (cancel)
                CloseEditor();
            else if (save)
                KeepEditor();
        }

        private static string Truncate(string text, int max)
        {
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
        }
    }

    // The shield's full-screen backdrop: a click outside an open window dismisses it.
    // Only clicks that *started* while the window was open count, so the click that
    // opened it (from the IMGUI menu, which uGUI sees as a backdrop click) can't close it.
    internal class ShieldBlocker : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
        public Func<bool> CanDismiss;
        public Action Dismiss;

        private bool _armed;

        public void OnPointerDown(PointerEventData eventData)
        {
            _armed = CanDismiss();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_armed && CanDismiss())
                Dismiss();
            _armed = false;
        }
    }
}
