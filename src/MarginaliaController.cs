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
    // Owns the hidden context menu, the text editor, the slider dialog, the wiki
    // window, and dragging board labels and rulers.
    //
    // Input: holding the modifier (Alt by default) raises an invisible full-screen
    // "shield" canvas above the game's UI, so a modifier+right-click never reaches the
    // game's tokens (which would otherwise send cards to verbs or toggle windows).
    // The shield stays up while the menu or editor is open, so the game below is inert.
    // The menu can also be opened mouse-only: the menu button (middle by default),
    // the "..." handle on a hovered card or verb, or a double-click on an aspect icon.
    //
    // Hotkeys: the game ignores its hotkeys while the EventSystem's selected object has
    // an InputField, so while we're modal we keep a dormant InputField selected.
    public class MarginaliaController : MonoBehaviour
    {
        private const string ModifierConfigKey = "marginalia_modifier";
        private const string ButtonConfigKey = "marginalia_button";
        private const string HandleConfigKey = "marginalia_handle";

        // A press of the menu button that moves further than this is a drag, not a click.
        private const float ClickSlop = 8f;
        private const float DoubleClickSeconds = 0.4f;
        private const string NoteControlName = "MarginaliaNote";
        private const float MenuWidth = 300f;
        private const float EditorWidth = 600f;
        private const float EditorHeight = 400f;
        private const float SizerWidth = 360f;

        public static MarginaliaController Instance { get; private set; }

        private GameObject _shield;
        private GameObject _focusSink;
        private ButtonControl _modifier;
        private ButtonControl _menuButton;
        private bool _menuButtonRead;
        private bool? _handleOn;

        // The menu button is down; on release it opens the menu, unless it was a drag.
        private sealed class ClickState
        {
            public Vector2 Start;
            public MenuTarget Target;
            public ButtonControl Button;
        }

        private ClickState _click;

        private UnityEngine.Object _lastAspect;
        private float _lastAspectTime;

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
        private bool _newlineTyped;
        private Vector2 _editorScroll;

        private sealed class SizerState
        {
            public string Title;
            public string Subtitle;
            public List<SizerField> Fields;
            public Action OnChange;
            public Action OnKeep;
            public Action OnCancel;
            public Action OnErase;
        }

        private SizerState _sizer;

        // A one-line text box, e.g. for searching.
        private sealed class PromptState
        {
            public string Title;
            public string Text = "";
            public string Status;
            public Func<string, string> OnChange;
            public Action OnKeep;
            public Action OnCancel;
        }

        private PromptState _prompt;
        private bool _focusPrompt;
        private const string PromptControlName = "MarginaliaPrompt";
        private const float PromptWidth = 380f;

        // Something on the board being moved with modifier + left-drag, or a drag of the
        // menu button.
        private sealed class DragState
        {
            public ButtonControl Button;
            public Vector2 Offset;
            public Func<bool> Alive;
            public Action<Vector2> MoveTo;
            public Action Done;
        }

        private DragState _drag;

        private WikiWindow _wiki;

        private bool IsModal => _menuTarget != null || _editor != null || _sizer != null || _prompt != null || _wiki.IsOpen;

        // IMGUI is laid out for 1080p and scaled to the screen.
        private static float GuiScale => Mathf.Clamp(Screen.height / 1080f, 0.75f, 3f);

        private static Vector2 ToGui(Vector2 screenPosition) =>
            new Vector2(screenPosition.x, Screen.height - screenPosition.y) / GuiScale;

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

        // The mouse button that opens the menu on its own (middle by default), or null.
        private ButtonControl MenuButton(Mouse mouse)
        {
            if (_menuButtonRead)
                return _menuButton;
            var config = Watchman.Get<Config>();
            if (config == null)
                return mouse.middleButton;
            _menuButtonRead = true;
            switch ((config.GetConfigValue(ButtonConfigKey) ?? "middle").Trim().ToLowerInvariant())
            {
                case "none":
                case "off":
                    _menuButton = null;
                    break;
                case "forward":
                    _menuButton = mouse.forwardButton;
                    break;
                case "back":
                    _menuButton = mouse.backButton;
                    break;
                default:
                    _menuButton = mouse.middleButton;
                    break;
            }
            return _menuButton;
        }

        private bool HandleOn()
        {
            if (_handleOn.HasValue)
                return _handleOn.Value;
            var config = Watchman.Get<Config>();
            if (config == null)
                return true;
            var value = (config.GetConfigValue(HandleConfigKey) ?? "on").Trim().ToLowerInvariant();
            _handleOn = value != "off" && value != "none" && value != "false";
            return _handleOn.Value;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            SteamCloud.Tick();
            BoardLabels.Tick();
            Rulers.Tick();

            bool modifierHeld = Modifier(keyboard).isPressed;
            var menuButton = MenuButton(mouse);
            var position = mouse.position.ReadValue();
            // Over the timers panel, clicks are for the panel, not the table beneath.
            bool overPanel = TimersPanel.Contains(ToGui(position));
            _shield.SetActive(modifierHeld || IsModal || _drag != null || _click != null || overPanel);

            bool buttonsDown = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
            MenuHandle.Tick(position, HandleOn() && !modifierHeld && !IsModal && _drag == null && _click == null && !overPanel,
                buttonsDown);

            if (_drag != null)
            {
                ContinueDrag(position);
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

            bool canOpen = _editor == null && _sizer == null && _prompt == null;
            if (_click != null)
            {
                ContinueClick(position);
            }
            else if (canOpen && menuButton != null && menuButton.wasPressedThisFrame)
            {
                StartClick(position, menuButton);
            }
            else if (modifierHeld && canOpen && mouse.rightButton.wasPressedThisFrame)
            {
                var target = TargetResolver.At(position, _shield.transform);
                if (target != null)
                    OpenMenu(target, position);
                else
                    CloseMenu();
            }
            else if (modifierHeld && !IsModal && mouse.leftButton.wasPressedThisFrame)
            {
                TryStartDrag(position, mouse.leftButton);
            }
            else if (!IsModal && mouse.leftButton.wasPressedThisFrame)
            {
                TryAspectDoubleClick(position);
            }
        }

        // ---- The menu button (middle-click by default) ----------------------------------

        // The game treats a middle-click on a card like a left-click, so the shield goes up
        // at once: the release then lands on the shield and never clicks the card.
        private void StartClick(Vector2 position, ButtonControl button)
        {
            var target = TargetResolver.At(position, _shield.transform);
            if (target == null)
            {
                CloseMenu();
                return;
            }
            _click = new ClickState { Start = position, Target = target, Button = button };
            _shield.SetActive(true);
        }

        private void ContinueClick(Vector2 position)
        {
            var click = _click;
            if ((position - click.Start).sqrMagnitude > ClickSlop * ClickSlop)
            {
                // Dragged: labels and rulers move; anything else was never a click.
                _click = null;
                if (click.Target.Kind == TargetKind.BoardLabel || click.Target.Kind == TargetKind.Ruler)
                    TryStartDrag(click.Start, click.Button);
            }
            else if (!click.Button.isPressed)
            {
                _click = null;
                OpenMenu(click.Target, click.Start);
            }
        }

        // ---- Aspects: double-click ----------------------------------------------------

        // Aspect icons have no room for the hover button, so a double-click opens their
        // menu. The game still shows the aspect's details on each click.
        private void TryAspectDoubleClick(Vector2 position)
        {
            var target = TargetResolver.AspectAt(position, _shield.transform, out var frame);
            float now = Time.unscaledTime;
            if (target != null && _lastAspect != null && _lastAspect == frame && now - _lastAspectTime < DoubleClickSeconds)
            {
                _lastAspect = null;
                OpenMenu(target, position);
                return;
            }
            _lastAspect = target != null ? frame : null;
            _lastAspectTime = now;
        }

        // ---- Moving board labels and rulers (modifier + left-drag, or menu-button drag) --

        private void TryStartDrag(Vector2 position, ButtonControl button)
        {
            var target = TargetResolver.At(position, _shield.transform);
            if (target == null || !BoardLabels.ScreenToBoard(position, out var pointer))
                return;
            if (target.Kind == TargetKind.BoardLabel && BoardLabels.Get(target.Id) is BoardLabels.Data label)
            {
                _drag = new DragState
                {
                    Button = button,
                    Offset = new Vector2(label.X, label.Y) - pointer,
                    Alive = () => BoardLabels.Get(label.Id) != null,
                    MoveTo = p => BoardLabels.Move(label, p),
                    Done = BoardLabels.Save
                };
            }
            else if (target.Kind == TargetKind.Ruler && Rulers.Get(target.Id) is Rulers.Data ruler)
            {
                _drag = new DragState
                {
                    Button = button,
                    Offset = Rulers.Corner(ruler) - pointer,
                    Alive = () => Rulers.Get(ruler.Id) != null,
                    MoveTo = p => Rulers.Move(ruler, p),
                    Done = Rulers.Save
                };
            }
        }

        private void ContinueDrag(Vector2 position)
        {
            if (!BoardLabels.IsAttached || !_drag.Alive())
            {
                _drag = null;
                return;
            }
            if (BoardLabels.ScreenToBoard(position, out var pointer))
                _drag.MoveTo(pointer + _drag.Offset);
            if (!_drag.Button.isPressed)
            {
                _drag.Done();
                _drag = null;
            }
        }

        private void FixedUpdate()
        {
            // The Heart may beat between fixed steps, even within one frame.
            Table.Invalidate();
            PauseWatch.Check();
        }

        private void LateUpdate()
        {
            // The game speed may have changed during Update, before the next beat.
            PauseWatch.Check();
            PauseWatch.CheckAppearances();

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

        // A small modal with an integer slider per field. onChange runs whenever a value
        // moves, so the caller can preview it; onErase, if given, adds a Remove button.
        public void OpenSizer(string title, string subtitle, List<SizerField> fields,
            Action onChange, Action onKeep, Action onCancel, Action onErase = null)
        {
            CloseMenu();
            _sizer = new SizerState
            {
                Title = title,
                Subtitle = subtitle,
                Fields = fields,
                OnChange = onChange,
                OnKeep = onKeep,
                OnCancel = onCancel,
                OnErase = onErase
            };
        }

        // A one-line text box at the top of the screen. onChange runs as the text changes
        // and returns a status line to show under it; Enter keeps, Esc cancels.
        public void OpenPrompt(string title, Func<string, string> onChange, Action onKeep, Action onCancel)
        {
            CloseMenu();
            _prompt = new PromptState { Title = title, OnChange = onChange, OnKeep = onKeep, OnCancel = onCancel };
            _prompt.Status = onChange("");
            _focusPrompt = true;
        }

        private void FinishPrompt(Func<PromptState, Action> pick)
        {
            var prompt = _prompt;
            _prompt = null;
            GUIUtility.keyboardControl = 0;
            pick(prompt)?.Invoke();
        }

        private void FinishSizer(Func<SizerState, Action> pick)
        {
            var sizer = _sizer;
            _sizer = null;
            pick(sizer)?.Invoke();
        }

        private void InsertNewline()
        {
            if (GUI.GetNameOfFocusedControl() != NoteControlName)
                return;
            if (GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) is TextEditor text)
            {
                text.Insert('\n');
                _editor.Text = text.text;
            }
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
            if (!IsModal && !TimersPanel.IsOpen)
                return;

            Styles.Ensure();
            GUI.depth = -1000;
            float scale = GuiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var screen = new Rect(0f, 0f, Screen.width / scale, Screen.height / scale);

            // Drawn first, so the menu and dialogs go over it; only usable when nothing
            // else wants the keyboard. The wiki window is uGUI, which IMGUI would cover.
            if (!_wiki.IsOpen)
                TimersPanel.Draw(screen, _editor == null && _sizer == null && _prompt == null);
            if (!IsModal)
                return;

            if (_editor != null)
                DrawEditor(screen);
            else if (_sizer != null)
                DrawSizer(screen);
            else if (_prompt != null)
                DrawPrompt(screen);
            else if (_menuTarget != null)
                DrawMenu(screen, scale);
        }

        private void DrawPrompt(Rect screen)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    FinishPrompt(p => p.OnCancel);
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n')
                {
                    FinishPrompt(p => p.OnKeep);
                    e.Use();
                    return;
                }
            }

            const float pad = 16f, fieldH = 36f;
            float inner = PromptWidth - pad * 2f;
            var title = new GUIContent(_prompt.Title);
            float titleH = Styles.Title.CalcHeight(title, inner);
            float height = pad + titleH + 8f + fieldH + Styles.Hint.fixedHeight + pad;

            // Top centre, like the sizer, so the table stays in view.
            var rect = new Rect((screen.width - PromptWidth) / 2f, 80f, PromptWidth, height);
            GUI.Box(rect, GUIContent.none, Styles.Panel);
            float x = rect.x + pad, y = rect.y + pad;
            GUI.Label(new Rect(x, y, inner, titleH), title, Styles.Title);
            y += titleH + 8f;

            var field = new Rect(x, y, inner, fieldH);
            GUI.Box(field, GUIContent.none, Styles.Field);
            GUI.SetNextControlName(PromptControlName);
            string text = GUI.TextField(field, _prompt.Text, Styles.TextField);
            if (text != _prompt.Text)
            {
                _prompt.Text = text;
                _prompt.Status = _prompt.OnChange(text);
            }
            if (_focusPrompt && e.type == EventType.Repaint)
            {
                GUI.FocusControl(PromptControlName);
                _focusPrompt = false;
            }
            y += fieldH;
            GUI.Label(new Rect(x, y, inner, Styles.Hint.fixedHeight), _prompt.Status + " · Enter · Esc", Styles.Hint);
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

                // Unity doesn't reliably turn Return into a new line in a text area (on some
                // platforms it sends no character for it), so add the line ourselves. Return
                // may come as a key, a '\n' or '\r' character, or a key and then a character.
                bool newlineKey = e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
                bool newlineChar = e.character == '\n' || e.character == '\r';
                if (newlineKey || newlineChar)
                {
                    if (newlineKey || !_newlineTyped)
                        InsertNewline();
                    _newlineTyped = newlineKey && !newlineChar;
                    e.Use();
                }
                else
                {
                    _newlineTyped = false;
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

        // No dimming: the board stays visible so the thing being sized can be watched.
        private void DrawSizer(Rect screen)
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    FinishSizer(s => s.OnCancel);
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    FinishSizer(s => s.OnKeep);
                    e.Use();
                    return;
                }
            }

            const float pad = 16f;
            const float rowH = 34f;
            float inner = SizerWidth - pad * 2f;
            var title = new GUIContent(_sizer.Title);
            var subtitle = new GUIContent(_sizer.Subtitle);
            float titleH = Styles.Title.CalcHeight(title, inner);
            float subtitleH = Styles.Subtitle.CalcHeight(subtitle, inner);
            float height = pad + titleH + subtitleH + 10f + _sizer.Fields.Count * rowH + 10f + Styles.Button.fixedHeight + pad;

            // Top centre, out of the way of the table where the ruler is.
            var rect = new Rect((screen.width - SizerWidth) / 2f, 80f, SizerWidth, height);
            GUI.Box(rect, GUIContent.none, Styles.Panel);

            float x = rect.x + pad;
            float y = rect.y + pad;
            GUI.Label(new Rect(x, y, inner, titleH), title, Styles.Title);
            y += titleH;
            GUI.Label(new Rect(x, y, inner, subtitleH), subtitle, Styles.Subtitle);
            y += subtitleH + 10f;

            bool changed = false;
            const float labelW = 60f, buttonW = 26f;
            float valueW = _sizer.Fields.Any(f => f.Format != null) ? 64f : 34f;
            foreach (var field in _sizer.Fields)
            {
                float cy = y + (rowH - buttonW) / 2f;
                GUI.Label(new Rect(x, y, labelW, rowH), field.Label, Styles.SizerLabel);
                float sx = x + labelW;
                int value = field.Value;
                if (GUI.Button(new Rect(sx, cy, buttonW, buttonW), "-", Styles.Button))
                    value--;
                float sliderX = sx + buttonW + 8f;
                float sliderW = inner - labelW - buttonW * 2f - valueW - 16f;
                GUI.Box(new Rect(sliderX, y + rowH / 2f - 1f, sliderW, 2f), GUIContent.none, Styles.Rule);
                value = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(sliderX, cy, sliderW, buttonW), value, field.Min, field.Max,
                    GUIStyle.none, Styles.Thumb));
                if (GUI.Button(new Rect(sliderX + sliderW + 8f, cy, buttonW, buttonW), "+", Styles.Button))
                    value++;
                value = Mathf.Clamp(value, field.Min, field.Max);
                GUI.Label(new Rect(x + inner - valueW, y, valueW, rowH), field.Format?.Invoke(value) ?? value.ToString(), Styles.SizerValue);

                if (value != field.Value)
                {
                    field.Value = value;
                    changed = true;
                }
                y += rowH;
            }
            if (changed)
                _sizer.OnChange();

            y += 10f;
            float bw = 90f, bh = Styles.Button.fixedHeight;
            float bx = rect.xMax - pad - bw;
            if (GUI.Button(new Rect(bx, y, bw, bh), "Keep", Styles.Button))
            {
                FinishSizer(s => s.OnKeep);
                return;
            }
            bx -= bw + 6f;
            if (GUI.Button(new Rect(bx, y, bw, bh), "Cancel", Styles.Button))
            {
                FinishSizer(s => s.OnCancel);
                return;
            }
            bx -= bw + 6f;
            if (_sizer.OnErase != null && GUI.Button(new Rect(bx, y, bw, bh), "Remove", Styles.Button))
            {
                FinishSizer(s => s.OnErase);
                return;
            }
            if (bx - x > 60f)
                GUI.Label(new Rect(x, y, bx - x - 6f, bh), "Enter · Esc", Styles.Hint);
        }

        private static string Truncate(string text, int max)
        {
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
        }
    }

    // One integer slider in the sizer dialog.
    public sealed class SizerField
    {
        public readonly string Label;
        public readonly int Min;
        public readonly int Max;
        public int Value;

        // Shows the value as something else, e.g. when it indexes a list of stops.
        public Func<int, string> Format;

        public SizerField(string label, int value, int min, int max)
        {
            Label = label;
            Value = value;
            Min = min;
            Max = max;
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
