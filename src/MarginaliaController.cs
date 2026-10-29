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
    // Owns the hidden context menu and the note editor.
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

        private NoteTarget _menuTarget;
        private Vector2 _menuScreenPos;
        private List<ContextMenuItem> _menuItems;

        private NoteTarget _editorTarget;
        private string _editorText;
        private bool _editorHadNote;
        private bool _focusEditor;
        private Vector2 _editorScroll;

        private bool IsModal => _menuTarget != null || _editorTarget != null;

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

            var blocker = new GameObject("Blocker");
            blocker.transform.SetParent(_shield.transform, false);
            var rect = blocker.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            // Fully transparent, but still a raycast target.
            blocker.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

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

            bool modifierHeld = Modifier(keyboard).isPressed;
            _shield.SetActive(modifierHeld || IsModal);

            if (modifierHeld && _editorTarget == null && mouse.rightButton.wasPressedThisFrame)
            {
                var position = mouse.position.ReadValue();
                var target = TargetResolver.At(position, _shield.transform);
                if (target != null)
                    OpenMenu(target, position);
                else
                    CloseMenu();
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

        public void OpenMenu(NoteTarget target, Vector2 screenPosition)
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

        public void OpenEditor(NoteTarget target)
        {
            CloseMenu();
            _editorTarget = target;
            _editorText = NoteStore.Get(target) ?? "";
            _editorHadNote = _editorText.Length > 0;
            _editorScroll = Vector2.zero;
            _focusEditor = true;
        }

        private void CloseEditor()
        {
            _editorTarget = null;
            _editorText = null;
            GUIUtility.keyboardControl = 0;
        }

        private void SaveEditor()
        {
            NoteStore.Set(_editorTarget, _editorText);
            CloseEditor();
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

            if (_editorTarget != null)
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
            var subtitle = new GUIContent(_menuTarget.KindLabel + " · " + _menuTarget.Id);
            string note = NoteStore.Get(_menuTarget);
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
                    CloseMenu();
                    item.Execute(target);
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
                    SaveEditor();
                    e.Use();
                    return;
                }
            }

            GUI.Box(screen, GUIContent.none, Styles.Dim);

            const float pad = 16f;
            var rect = new Rect((screen.width - EditorWidth) / 2f, (screen.height - EditorHeight) / 2f, EditorWidth, EditorHeight);
            GUI.Box(rect, GUIContent.none, Styles.Panel);

            GUILayout.BeginArea(new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, rect.height - pad * 2f));
            GUILayout.Label(_editorTarget.Label, Styles.Title);
            GUILayout.Label(_editorTarget.KindLabel + " · " + _editorTarget.Id, Styles.Subtitle);
            GUILayout.Space(10f);

            _editorScroll = GUILayout.BeginScrollView(_editorScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, Styles.Field, GUILayout.ExpandHeight(true));
            GUI.SetNextControlName(NoteControlName);
            _editorText = GUILayout.TextArea(_editorText, Styles.TextArea, GUILayout.ExpandHeight(true));
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
            bool erase = _editorHadNote && GUILayout.Button("Erase", Styles.Button, GUILayout.Width(90f));
            bool cancel = GUILayout.Button("Cancel", Styles.Button, GUILayout.Width(90f));
            bool save = GUILayout.Button("Keep", Styles.Button, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (erase)
            {
                NoteStore.Delete(_editorTarget);
                CloseEditor();
            }
            else if (cancel)
            {
                CloseEditor();
            }
            else if (save)
            {
                SaveEditor();
            }
        }

        private static string Truncate(string text, int max)
        {
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
        }
    }
}
