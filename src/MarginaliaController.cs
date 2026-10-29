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
        private const float SizerWidth = 360f;

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

        // Something on the board being moved with modifier + left-drag.
        private sealed class DragState
        {
            public Vector2 Offset;
            public Func<bool> Alive;
            public Action<Vector2> MoveTo;
            public Action Done;
        }

        private DragState _drag;

        private WikiWindow _wiki;

        private bool IsModal => _menuTarget != null || _editor != null || _sizer != null || _wiki.IsOpen;

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
            Rulers.Tick();

            bool modifierHeld = Modifier(keyboard).isPressed;
            _shield.SetActive(modifierHeld || IsModal || _drag != null);
            var position = mouse.position.ReadValue();

            if (_drag != null)
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

            if (modifierHeld && _editor == null && _sizer == null && mouse.rightButton.wasPressedThisFrame)
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

        // ---- Moving board labels and rulers (modifier + left-drag) ----------------------

        private void TryStartDrag(Vector2 position)
        {
            var target = TargetResolver.At(position, _shield.transform);
            if (target == null || !BoardLabels.ScreenToBoard(position, out var pointer))
                return;
            if (target.Kind == TargetKind.BoardLabel && BoardLabels.Get(target.Id) is BoardLabels.Data label)
            {
                _drag = new DragState
                {
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
                    Offset = Rulers.Corner(ruler) - pointer,
                    Alive = () => Rulers.Get(ruler.Id) != null,
                    MoveTo = p => Rulers.Move(ruler, p),
                    Done = Rulers.Save
                };
            }
        }

        private void ContinueDrag(Mouse mouse, Vector2 position)
        {
            if (!BoardLabels.IsAttached || !_drag.Alive())
            {
                _drag = null;
                return;
            }
            if (BoardLabels.ScreenToBoard(position, out var pointer))
                _drag.MoveTo(pointer + _drag.Offset);
            if (!mouse.leftButton.isPressed)
            {
                _drag.Done();
                _drag = null;
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

        private void FinishSizer(Func<SizerState, Action> pick)
        {
            var sizer = _sizer;
            _sizer = null;
            pick(sizer)?.Invoke();
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
            else if (_sizer != null)
                DrawSizer(screen);
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
            const float labelW = 60f, buttonW = 26f, valueW = 34f;
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
                GUI.Label(new Rect(x + inner - valueW, y, valueW, rowH), value.ToString(), Styles.SizerValue);

                value = Mathf.Clamp(value, field.Min, field.Max);
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
