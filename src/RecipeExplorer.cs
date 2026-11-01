using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Services;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using Entry = MarginaliaMod.RecipeIndex.Entry;
using Row = MarginaliaMod.RecipeIndex.Row;
using RowKind = MarginaliaMod.RecipeIndex.RowKind;
using Chip = MarginaliaMod.RecipeIndex.Chip;
using ChipKind = MarginaliaMod.RecipeIndex.ChipKind;

namespace MarginaliaMod
{
    // A floating panel over the board: a searchable book of every recipe, read from the
    // game's own data. Each card, aspect and recipe has a page: what makes it, what uses
    // it, what comes before and after. Cards, aspects and verbs show as their icons; hover
    // one for its name, click it for its page. Drag the panel by its title bar and resize
    // it by its bottom-right corner; the board stays in play around it.
    public static class RecipeExplorer
    {
        private const float DefaultWidth = 480f;
        private const float MinWidth = 360f;
        private const float MinHeight = 320f;
        private const float Pad = 10f;
        private const float TitleH = 30f;
        private const float IconSize = 30f;
        private const float LeadSize = 24f;
        private const float TextChipH = 22f;
        private const float Gap = 4f;
        private const float GripSize = 16f;
        private const float TipWidth = 300f;
        private const int MaxShown = 200;
        private const int LayoutsPerFrame = 30;        // rows per frame that load icons not seen before
        private const float RowGuess = 72f;            // height of a row not laid out yet
        private const float HeaderShare = 0.4f;        // of the panel, before the header scrolls
        private const string SearchControlName = "MarginaliaRecipeSearch";

        private enum PageKind { All, Element, Recipe, Favourites }

        private sealed class Page
        {
            public PageKind Kind;
            public string Id;          // the element or recipe; for All, a verb to filter by
            public string Query = "";
            public int Tab;
            public Vector2 Scroll;
        }

        private sealed class Tab
        {
            public string Title;
            public List<Entry> Entries;
            public List<Entry> Visible;    // Entries less what the "discovered" toggle hides
            public bool Unfiltered;        // favourites: picked by hand, so always shown
            public int HiddenCount;        // of its recipes, how many the player has hidden
        }

        // Something drawn in a row or the header: an icon (with a badge) or a bit of text.
        private sealed class Item
        {
            public Rect Rect;
            public string Text;
            public Sprite Icon;
            public Texture Image;      // drawn as is, e.g. a star
            public string Badge;
            public ChipKind Kind;
            public bool Faded;
            public string Tip;
            public bool HoverOnly;     // only shown while the pointer is over its row
            public float HoverWidth;   // narrower while its row is hovered, to clear the row's buttons
            public GUIStyle Style;
            public Action OnClick;
        }

        private sealed class Layout
        {
            public readonly List<Item> Items = new List<Item>();
            public float Height;
        }

        private enum Grab { None, Move, Resize }

        private const string LayoutName = "recipes";

        // Open, as remembered between sessions; it only shows while a game is on the table.
        private static bool _open;

        public static bool IsOpen => _open && BoardLabels.IsAttached;

        // The search box has the keyboard, so the game's hotkeys should stay off.
        // Only while it's showing: leaving the table mid-word mustn't keep the hotkeys off.
        public static bool Typing => _typing && IsOpen;

        private static bool _typing;

        // In IMGUI's scaled coordinates. x < 0 until first placed, at the left edge.
        private static Rect _rect = new Rect(-1f, 90f, DefaultWidth, 0f);
        private static Grab _grab;
        private static Vector2 _grabOffset;
        private static PanelPointer _pointer;

        private static RecipeIndex _index;
        private static Page _page;
        private static readonly Stack<Page> History = new Stack<Page>();
        private static bool _startableOnly = true;
        // Only recipes the player has finished (Discovery); other rows (decay, cards) always show.
        private static bool _discoveredOnly = true;
        private static int _discoveryVersion = -1;
        private static int _favouritesVersion = -1;
        private static Texture2D _starOn, _starOff;
        private static Rect _titleStar;    // the star beside the page's name, which isn't for dragging
        private static List<Tab> _tabs;
        private static List<Entry> _shown;
        private static int _matched;
        private static string _shownKey;
        private static readonly Dictionary<Entry, Layout> Layouts = new Dictionary<Entry, Layout>();
        // One entry per row for the lists built here (no reason to show), so rebuilding a list
        // reuses the rows' layouts rather than making them again.
        private static readonly Dictionary<Row, Entry> RowEntries = new Dictionary<Row, Entry>();
        private static int _discoveredHere;    // of the recipes here, how many are discovered
        private static int _missedUndiscovered;   // matches the "discovered" toggle hides
        private static int _missedHidden;         // matches that are hidden recipes
        // Hidden recipes (HiddenRecipes) stay out of the lists unless this is on.
        private static bool _showHidden;
        private static int _hiddenVersion = -1;
        private const float FooterH = 26f;
        private static float _layoutWidth;
        private static Layout _header;
        private static float _headerWidth;
        private static bool _descriptionOpen;
        private static bool _focusSearch;
        private static Action _pending;
        private static string _tip;
        private static Vector2 _headerScroll;
        private static float _headerFullHeight;
        private static bool _headerScrolls;
        private static int _notesVersion = -1;
        private static int _layoutFrame = -1;
        private static int _layoutsLeft;
        private static bool _listeningForContent;

        private static readonly Dictionary<string, Sprite> ElementSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> VerbSprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<Sprite, Rect> SpriteUvs = new Dictionary<Sprite, Rect>();
        private static bool _spriteErrorLogged;

        private static GUIStyle _windowTitle, _subtitle, _description, _rowTitle, _rowTitleFaded, _tag, _reason, _note, _arrow,
            _glyph, _small, _smallOn, _tabOn, _tabOff, _toggle, _count, _placeholder, _searchField, _tipStyle, _badgeBack, _leadFrame,
            _groupFrame, _groupLabel, _groupCount, _footer;
        private static readonly Dictionary<ChipKind, GUIStyle> IconStyles = new Dictionary<ChipKind, GUIStyle>();
        private static readonly Dictionary<ChipKind, GUIStyle> InertIconStyles = new Dictionary<ChipKind, GUIStyle>();
        private static readonly Dictionary<ChipKind, GUIStyle> TextChipStyles = new Dictionary<ChipKind, GUIStyle>();
        private static readonly Dictionary<ChipKind, GUIStyle> InertTextChipStyles = new Dictionary<ChipKind, GUIStyle>();
        private static readonly Dictionary<ChipKind, GUIStyle> BadgeStyles = new Dictionary<ChipKind, GUIStyle>();

        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "recipes.all",
                Order = 37,
                Label = _ => IsOpen ? "Hide recipe explorer" : "Recipe explorer…",
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = _ =>
                {
                    if (IsOpen)
                        Close();
                    else
                        Open(null);
                }
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "recipes.element",
                Order = 55,
                Label = _ => "Recipes with this…",
                IsVisible = t => t.Kind == TargetKind.Element,
                Execute = t => Open(new Page { Kind = PageKind.Element, Id = t.Id })
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "recipes.verb",
                Order = 55,
                Label = _ => "Recipes for this verb…",
                IsVisible = t => t.Kind == TargetKind.Verb,
                Execute = t => Open(new Page { Kind = PageKind.All, Id = t.Id })
            });
        }

        public static bool Contains(Vector2 guiPoint) => IsOpen && _page != null && (_grab != Grab.None || _rect.Contains(guiPoint));

        // Back where it was last time, open or not.
        public static void Restore()
        {
            var saved = PanelLayout.Get(LayoutName);
            if (saved == null)
                return;
            if (saved.X >= 0f)
                _rect = new Rect(saved.X, saved.Y, saved.Width > 0f ? saved.Width : DefaultWidth, Mathf.Max(0f, saved.Height));
            _open = saved.Open;
        }

        private static void SaveLayout() => PanelLayout.Save(LayoutName, _rect, _open);

        // ---- Navigation

        // Opens on the given page, or where it was left (the full list the first time).
        private static void Open(Page page)
        {
            if (!Prepare())
            {
                NoonUtility.LogWarning("Marginalia: the compendium isn't loaded, so there are no recipes to show.");
                return;
            }
            bool wasOpen = _open;
            _open = true;
            // Typing into the box takes the game's hotkeys, so only the full list asks for it.
            _focusSearch = page == null;
            if (page != null)
                Go(page);
            if (!wasOpen)
                SaveLayout();
        }

        // The index and a page to show, the full list to begin with. False until the game
        // has loaded its content.
        private static bool Prepare()
        {
            var index = RecipeIndex.Current;
            if (index == null)
                return false;
            ListenForContent();
            if (index != _index)
            {
                _index = index;
                _page = null;
                History.Clear();
                Layouts.Clear();
                RowEntries.Clear();
                _discoveryVersion = -1;
            }
            if (_page == null)
                Show(new Page { Kind = PageKind.All });
            return true;
        }

        // Reloading content (e.g. after changing mods) replaces its sprites.
        private static void ListenForContent()
        {
            if (_listeningForContent)
                return;
            var concursum = Watchman.Get<Concursum>();
            if (concursum == null)
                return;
            concursum.ContentUpdatedEvent.AddListener(_ =>
            {
                ElementSprites.Clear();
                VerbSprites.Clear();
                SpriteUvs.Clear();
                Relayout();
            });
            _listeningForContent = true;
        }

        private static void Go(Page page)
        {
            if (_page != null && _page.Kind == page.Kind && _page.Id == page.Id)
                return;
            if (_page != null)
                History.Push(_page);
            Show(page);
        }

        private static void GoElement(string id)
        {
            if (_index.IsElement(id))
                Go(new Page { Kind = PageKind.Element, Id = id });
        }

        private static void GoRecipe(string id)
        {
            if (_index.RecipesById.ContainsKey(id))
                Go(new Page { Kind = PageKind.Recipe, Id = id });
        }

        private static void GoVerb(string verbId) => Go(new Page { Kind = PageKind.All, Id = verbId });

        public static void Back()
        {
            if (History.Count > 0)
                Show(History.Pop());
        }

        private static void Show(Page page)
        {
            _page = page;
            _tabs = BuildTabs(page);
            _page.Tab = Mathf.Clamp(_page.Tab, 0, _tabs.Count - 1);
            // Favourites open on a tab that has some, e.g. Cards when only cards are starred.
            if (page.Kind == PageKind.Favourites && _tabs[page.Tab].Entries.Count == 0)
                _page.Tab = Mathf.Max(0, _tabs.FindIndex(t => t.Entries.Count > 0));
            _shownKey = null;
            _header = null;
            _headerScroll = Vector2.zero;
            _descriptionOpen = false;
        }

        // Keeps its place: opening it again from the board menu returns to the same page.
        public static void Close()
        {
            if (!_open)
                return;
            _open = false;
            _grab = Grab.None;
            SaveLayout();
            if (_typing)
                GUIUtility.keyboardControl = 0;
            _typing = false;
        }

        // Notes show on the rows, so they need laying out again after an edit.
        private static void Relayout()
        {
            Layouts.Clear();
            _header = null;
        }

        private static Entry EntryFor(Row row) =>
            RowEntries.TryGetValue(row, out var entry) ? entry : RowEntries[row] = new Entry { Row = row };

        // Recipes the player hid, and ones not yet discovered, are left out (unless asked for).
        private static bool Visible(Entry entry)
        {
            if (entry.Row.Kind != RowKind.Recipe)
                return true;
            if (!_showHidden && HiddenRecipes.Has(entry.Row.Id))
                return false;
            return !_discoveredOnly || Discovery.Has(entry.Row.Id);
        }

        private static List<Tab> BuildTabs(Page page)
        {
            var tabs = MakeTabs(page);
            foreach (var tab in tabs)
            {
                tab.Visible = tab.Unfiltered ? tab.Entries : tab.Entries.Where(Visible).ToList();
                tab.HiddenCount = tab.Entries.Count(e => e.Row.Kind == RowKind.Recipe && HiddenRecipes.Has(e.Row.Id));
            }
            return tabs;
        }

        private static List<Tab> MakeTabs(Page page)
        {
            var tabs = new List<Tab>();
            switch (page.Kind)
            {
                case PageKind.All:
                    tabs.Add(new Tab
                    {
                        Title = "Recipes",
                        Entries = _index.Recipes
                            .Where(r => (!_startableOnly || r.Startable) &&
                                        (page.Id == null || SecretHistories.NoonExtensions.WildcardMatchId(page.Id, r.VerbId ?? "")))
                            .Select(EntryFor)
                            .ToList()
                    });
                    break;
                case PageKind.Element:
                    tabs.Add(new Tab { Title = "Made by", Entries = RecipeIndex.Get(_index.MadeBy, page.Id) });
                    tabs.Add(new Tab { Title = "Used by", Entries = RecipeIndex.Get(_index.UsedBy, page.Id) });
                    var element = _index.Element(page.Id);
                    if (element != null && !element.IsAspect)
                        tabs.Add(new Tab { Title = "Via aspects", Entries = _index.ViaAspectsOf(page.Id) });
                    var holders = RecipeIndex.Get(_index.HeldBy, page.Id);
                    if (holders.Count > 0)
                        tabs.Add(new Tab { Title = "Cards", Entries = holders });
                    break;
                case PageKind.Favourites:
                    tabs.Add(new Tab
                    {
                        Title = "Recipes",
                        Unfiltered = true,
                        Entries = Favourites.Ids("recipe")
                            .Where(id => _index.RecipesById.ContainsKey(id))
                            .Select(id => _index.RecipesById[id])
                            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                            .Select(EntryFor)
                            .ToList()
                    });
                    tabs.Add(new Tab
                    {
                        Title = "Cards",
                        Unfiltered = true,
                        Entries = Favourites.Ids("element")
                            .Select(_index.ElementRow)
                            .Where(r => r != null)
                            .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                            .Select(EntryFor)
                            .ToList()
                    });
                    break;
                case PageKind.Recipe:
                    tabs.Add(new Tab { Title = "Before", Entries = RecipeIndex.Get(_index.ComesAfter, page.Id) });
                    tabs.Add(new Tab { Title = "After", Entries = RecipeIndex.Get(_index.LeadsTo, page.Id) });
                    break;
            }
            return tabs;
        }

        private static void Filter()
        {
            string key = _page.Tab + "\n" + _page.Query;
            if (key == _shownKey)
                return;
            _shownKey = key;
            var terms = _page.Query.ToLowerInvariant().Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var tab = _tabs[_page.Tab];
            var matches = tab.Visible.Where(e => RecipeIndex.Matches(e, terms)).ToList();
            _matched = matches.Count;
            // Nothing found among what's shown: say whether the toggles left some out, and which.
            _missedUndiscovered = _missedHidden = 0;
            if (matches.Count == 0 && tab.Visible.Count < tab.Entries.Count)
                foreach (var missed in tab.Entries.Where(e => !Visible(e) && RecipeIndex.Matches(e, terms)))
                {
                    if (HiddenRecipes.Has(missed.Row.Id))
                        _missedHidden++;
                    else
                        _missedUndiscovered++;
                }
            _shown = matches.Take(MaxShown).ToList();
        }

        private static string PageTitle()
        {
            switch (_page.Kind)
            {
                case PageKind.Element: return _index.ElementLabel(_page.Id);
                case PageKind.Recipe: return _index.RecipesById[_page.Id].Title;
                case PageKind.Favourites: return "Favourites";
                default: return _page.Id == null ? "Recipes" : _index.VerbLabel(_page.Id) + " · recipes";
            }
        }

        private static Sprite PageIcon()
        {
            switch (_page.Kind)
            {
                case PageKind.Element: return ElementSprite(_page.Id);
                case PageKind.Recipe: return VerbSprite(_index.RecipesById[_page.Id].VerbId);
                case PageKind.Favourites: return null;
                default: return VerbSprite(_page.Id);
            }
        }

        // ---- Notes and the wiki

        private static MenuTarget NoteTarget(Row row) =>
            row.Kind == RowKind.Recipe ? MenuTarget.ForRecipe(row.Id, row.Title) : MenuTarget.ForElement(row.Id, _index.ElementLabel(row.Id));

        private static MenuTarget PageTarget() =>
            _page.Kind == PageKind.Recipe ? NoteTarget(_index.RecipesById[_page.Id]) :
            _page.Kind == PageKind.Element ? MenuTarget.ForElement(_page.Id, _index.ElementLabel(_page.Id)) : null;

        private static void EditNote(MenuTarget target)
        {
            MarginaliaController.Instance.OpenEditor(target.Label, target.Subtitle, NoteStore.Get(target),
                text =>
                {
                    NoteStore.Set(target, text);
                    Relayout();
                },
                NoteStore.Has(target)
                    ? () =>
                    {
                        NoteStore.Delete(target);
                        Relayout();
                    }
                    : (Action)null);
        }

        private static void OpenWiki(MenuTarget target)
        {
            var name = target.Kind == TargetKind.Recipe ? RecipeIndex.WikiName(target.Label) : Wiki.CleanName(target.Label);
            MarginaliaController.Instance.OpenWiki(name);
        }

        // ---- Icons

        // A card's art or an aspect's icon, as the game draws it; null if it has none.
        private static Sprite ElementSprite(string id)
        {
            if (id == null)
                return null;
            if (ElementSprites.TryGetValue(id, out var sprite) && Alive(sprite))
                return sprite;
            var element = _index.Element(id);
            ElementSprites[id] = sprite = element != null ? Real(() => ResourcesManager.GetAppropriateSpriteForElement(element)) : null;
            return sprite;
        }

        private static Sprite VerbSprite(string verbId)
        {
            var icon = _index.VerbIcon(verbId);
            if (icon == null)
                return null;
            if (VerbSprites.TryGetValue(icon, out var sprite) && Alive(sprite))
                return sprite;
            VerbSprites[icon] = sprite = Real(() => ResourcesManager.GetSpriteForVerbLarge(icon));
            return sprite;
        }

        // A cached "no icon" is kept; a sprite destroyed since (by a content reload) is not.
        private static bool Alive(Sprite sprite) => ReferenceEquals(sprite, null) || sprite != null;

        // The game stands in a placeholder for missing art; that's no use as an icon.
        private static Sprite Real(Func<Sprite> load)
        {
            try
            {
                var sprite = load();
                return sprite != null && sprite.texture != null && !ResourcesManager.SpriteIsPlaceholder(sprite) ? sprite : null;
            }
            catch (Exception ex)
            {
                if (!_spriteErrorLogged)
                    NoonUtility.Log("Marginalia: couldn't load an icon for the recipe explorer: " + ex.Message);
                _spriteErrorLogged = true;
                return null;
            }
        }

        // Fitted inside the rect, keeping its shape (card art is tall, aspect icons square).
        private static void DrawSprite(Rect rect, Sprite sprite, float alpha)
        {
            var size = sprite.rect.size;
            float aspect = size.y > 0f ? size.x / size.y : 1f;
            var fit = rect;
            if (aspect > rect.width / rect.height)
            {
                fit.height = rect.width / aspect;
                fit.y += (rect.height - fit.height) / 2f;
            }
            else
            {
                fit.width = rect.height * aspect;
                fit.x += (rect.width - fit.width) / 2f;
            }
            var color = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * alpha);
            GUI.DrawTextureWithTexCoords(fit, sprite.texture, UvOf(sprite), true);
            GUI.color = color;
        }

        // Where the sprite's whole rect sits in its texture, which may be an atlas of many
        // sprites; the same region a UI Image draws.
        private static Rect UvOf(Sprite sprite)
        {
            if (SpriteUvs.TryGetValue(sprite, out var uv))
                return uv;
            var outer = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
            uv = new Rect(outer.x, outer.y, outer.z - outer.x, outer.w - outer.y);
            if (uv.width <= 0f || uv.height <= 0f)
                uv = new Rect(0f, 0f, 1f, 1f);
            SpriteUvs[sprite] = uv;
            return uv;
        }

        // ---- Drawing

        public static void Draw(Rect screen, bool interactive)
        {
            // Opened last session: it needs its index and a page the first time it's drawn.
            if (!IsOpen || _page == null && !Prepare())
                return;
            EnsureStyles();
            if (_notesVersion != NoteStore.Version)
            {
                _notesVersion = NoteStore.Version;
                Relayout();
            }
            // A star added or taken away: rows show their stars, and the favourites page its list.
            if (_favouritesVersion != Favourites.Version)
            {
                _favouritesVersion = Favourites.Version;
                Relayout();
                if (_page.Kind == PageKind.Favourites)
                {
                    _tabs = BuildTabs(_page);
                    _shownKey = null;
                }
            }
            // A recipe hidden or shown again: the lists, and its row's button.
            if (_hiddenVersion != HiddenRecipes.Version)
            {
                _hiddenVersion = HiddenRecipes.Version;
                Relayout();
                _tabs = BuildTabs(_page);
                _shownKey = null;
            }
            // A recipe finished while it's open: show it.
            if (_discoveryVersion != Discovery.Version)
            {
                _discoveryVersion = Discovery.Version;
                _discoveredHere = _index.Recipes.Count(r => Discovery.Has(r.Id));
                _tabs = BuildTabs(_page);
                _shownKey = null;
                _header = null;
            }
            Filter();
            _tip = null;
            var e = Event.current;

            // A release outside the game window never reaches IMGUI.
            if (_grab != Grab.None && Mouse.current != null && !Mouse.current.leftButton.isPressed)
                EndGrab();
            Place(screen);

            bool wasEnabled = GUI.enabled;
            GUI.enabled = interactive;
            if (interactive)
                HandleGrab(e, screen);

            GUI.Box(_rect, GUIContent.none, Styles.Panel);
            float x = _rect.x + Pad, inner = _rect.width - Pad * 2f, y = _rect.y + 6f;
            y = DrawTitleBar(x, y, inner);
            y = DrawHeader(x, y, inner);
            y = DrawSearch(x, y, inner);
            y = DrawTabs(x, y, inner);
            GUI.Box(new Rect(x, y - 4f, inner, 1f), GUIContent.none, Styles.Rule);
            DrawList(new Rect(x, y, inner, _rect.yMax - Pad - FooterH - 4f - y));
            DrawFooter(x, _rect.yMax - Pad - FooterH, inner);
            DrawGrip();
            DrawTip(screen);

            if (e.type == EventType.Repaint)
                _typing = interactive && GUI.GetNameOfFocusedControl() == SearchControlName;
            GUI.enabled = wasEnabled;

            var pending = _pending;
            _pending = null;
            if (interactive)
                pending?.Invoke();
        }

        // Clicking the board (or the timers panel) hands the keyboard back to the game. Called
        // before anything else is drawn, since whatever is drawn first may use up the click.
        public static void LetGoOfKeyboard(bool usable)
        {
            var e = Event.current;
            if (IsOpen && usable && Typing && e.type == EventType.MouseDown && !_rect.Contains(e.mousePosition))
                GUIUtility.keyboardControl = 0;
        }

        // First shown at the left edge; always kept whole on screen.
        private static void Place(Rect screen)
        {
            if (_rect.height <= 0f)
                _rect.height = Mathf.Min(720f, screen.height - 160f);
            if (_rect.x < 0f)
                _rect.x = 24f;
            _rect.width = Mathf.Clamp(_rect.width, MinWidth, Mathf.Max(MinWidth, screen.width));
            _rect.height = Mathf.Clamp(_rect.height, MinHeight, Mathf.Max(MinHeight, screen.height));
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, screen.width - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, screen.height - _rect.height));
        }

        // Move by the title bar (away from its buttons), resize by the bottom-right corner.
        private static void HandleGrab(Event e, Rect screen)
        {
            var titleBar = new Rect(_rect.x, _rect.y, _rect.width - Pad - TitleButtonsWidth(), TitleH + 6f);
            var grip = new Rect(_rect.xMax - GripSize, _rect.yMax - GripSize, GripSize, GripSize);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _pointer.Begin(e.mousePosition);
                if (grip.Contains(e.mousePosition))
                {
                    _grab = Grab.Resize;
                    _grabOffset = new Vector2(_rect.xMax, _rect.yMax) - e.mousePosition;
                    e.Use();
                }
                else if (titleBar.Contains(e.mousePosition) && !_titleStar.Contains(e.mousePosition))
                {
                    _grab = Grab.Move;
                    _grabOffset = _rect.position - e.mousePosition;
                    e.Use();
                }
            }
            else if (_grab != Grab.None && e.type == EventType.MouseDrag)
            {
                var to = _pointer.Read(e.mousePosition, screen) + _grabOffset;
                if (_grab == Grab.Move)
                {
                    // Not past the left or top edge: x < 0 means "not placed yet".
                    _rect.position = new Vector2(Mathf.Max(0f, to.x), Mathf.Max(0f, to.y));
                }
                else
                {
                    _rect.width = Mathf.Clamp(to.x - _rect.x, MinWidth, Mathf.Max(MinWidth, screen.width - _rect.x));
                    _rect.height = Mathf.Clamp(to.y - _rect.y, MinHeight, Mathf.Max(MinHeight, screen.height - _rect.y));
                }
                e.Use();
            }
            else if (_grab != Grab.None && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                EndGrab();
            }
        }

        // Done moving or resizing: remember where it ended up.
        private static void EndGrab()
        {
            _grab = Grab.None;
            SaveLayout();
        }

        private static float TitleButtonsWidth() => PageTarget() == null ? 26f * 3f + 4f * 2f : 26f * 3f + 44f * 2f + 4f * 4f;

        // The page's icon and name, which is also the handle to drag it by, and its buttons.
        private static float DrawTitleBar(float x, float y, float inner)
        {
            float bx = x + inner - 26f;
            if (Button(new Rect(bx, y + 3f, 26f, 24f), "×", _glyph, "Close (Esc while typing)"))
                _pending = Close;
            var target = PageTarget();
            if (target != null)
            {
                bool hasNote = NoteStore.Has(target);
                bx -= 44f + 4f;
                if (Button(new Rect(bx, y + 3f, 44f, 24f), "note", hasNote ? _smallOn : _small, hasNote ? "Edit the note" : "Write a note"))
                    _pending = () => EditNote(target);
                bx -= 44f + 4f;
                if (Button(new Rect(bx, y + 3f, 44f, 24f), "wiki", _small, "Open its wiki article"))
                    _pending = () => OpenWiki(target);
            }
            bx -= 26f + 4f;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && History.Count > 0;
            if (Button(new Rect(bx, y + 3f, 26f, 24f), "←", _glyph, "Back (mouse back button)"))
                _pending = Back;
            bx -= 26f + 4f;
            var favourites = new Rect(bx, y + 3f, 26f, 24f);
            GUI.enabled = wasEnabled && _page.Kind != PageKind.Favourites;
            if (Button(favourites, "", _glyph, "Favourites"))
                _pending = () => Go(new Page { Kind = PageKind.Favourites });
            GUI.enabled = wasEnabled;
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(favourites.x + 5f, favourites.y + 4f, 16f, 16f), StarOn, ScaleMode.ScaleToFit, true);

            float tx = x;
            var icon = PageIcon();
            if (icon != null || _page.Kind == PageKind.Favourites)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    if (icon != null)
                        DrawSprite(new Rect(tx, y + 1f, 28f, 28f), icon, 1f);
                    else
                        GUI.DrawTexture(new Rect(tx + 2f, y + 3f, 24f, 24f), StarOn, ScaleMode.ScaleToFit, true);
                }
                tx += 32f;
            }
            // The name, then a star to make the card or recipe a favourite.
            var title = PageTitle();
            float room = bx - tx - 6f - (target != null ? 24f : 0f);
            float titleW = Mathf.Min(_windowTitle.CalcSize(new GUIContent(title)).x, room);
            var titleRect = new Rect(tx, y, titleW, TitleH);
            GUI.Label(titleRect, title, _windowTitle);
            if (titleW < _windowTitle.CalcSize(new GUIContent(title)).x)
                TipIfHovered(titleRect, title);
            _titleStar = Rect.zero;
            if (target != null)
            {
                bool starred = Favourites.Has(target);
                var star = _titleStar = new Rect(titleRect.xMax + 4f, y + 6f, 18f, 18f);
                if (Button(star, "", GUIStyle.none, starred ? "A favourite. Click to take it out of favourites" : "Add to favourites"))
                    _pending = () => Favourites.Toggle(target);
                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(star, starred ? StarOn : StarOff, ScaleMode.ScaleToFit, true);
            }
            return y + TitleH + 2f;
        }

        // Past a share of the panel, the header scrolls on its own, so the list keeps its room.
        private static float DrawHeader(float x, float y, float inner)
        {
            float cap = Mathf.Max(60f, (_rect.height - TitleH) * HeaderShare);
            if (_header == null || !Mathf.Approximately(_headerWidth, inner))
            {
                _header = BuildHeader(inner);
                _headerWidth = inner;
                _headerFullHeight = _header.Height;
                _headerScrolls = false;
            }
            // Too tall for the panel as it is now: lay it out beside a scrollbar, or back again.
            if ((_headerFullHeight > cap) != _headerScrolls)
            {
                _headerScrolls = _headerFullHeight > cap;
                _header = BuildHeader(_headerScrolls ? inner - 16f : inner);
            }
            if (!_headerScrolls)
            {
                DrawItems(_header, x, y, true, true);
                return y + _header.Height + 6f;
            }
            var area = new Rect(x, y, inner, cap);
            bool mouseIn = area.Contains(Event.current.mousePosition);
            // Parts scrolled out of view mustn't take a press meant for what's below.
            bool wasEnabled = GUI.enabled;
            if (Event.current.type == EventType.MouseDown && !mouseIn)
                GUI.enabled = false;
            _headerScroll = GUI.BeginScrollView(area, _headerScroll, new Rect(0f, 0f, inner - 16f, _header.Height), false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            DrawItems(_header, 0f, 0f, true, mouseIn);
            GUI.EndScrollView();
            GUI.enabled = wasEnabled;
            return y + cap + 6f;
        }

        private static Layout BuildHeader(float inner)
        {
            var layout = new Layout();
            float y = 0f;
            string subtitle, description = null;
            List<Chip> chips = new List<Chip>(), gives = null, then = null;
            List<RecipeIndex.NeedGroup> groups = null;
            switch (_page.Kind)
            {
                case PageKind.Element:
                    var element = _index.Element(_page.Id);
                    subtitle = (element != null && element.IsAspect ? "Aspect · " : "Card · ") + _page.Id;
                    if (element != null && element.Lifetime > 0)
                        subtitle += " · lasts " + element.Lifetime.ToString("0") + " s";
                    description = element != null ? RecipeIndex.Clean(element.Description) : null;
                    if (_index.Cards.TryGetValue(_page.Id, out var card))
                        chips = card.Needs;
                    break;
                case PageKind.Recipe:
                    var row = _index.RecipesById[_page.Id];
                    subtitle = row.Tag + (row.Recipe.Warmup > 0 ? " · " + row.Recipe.Warmup + " s" : "") +
                               (row.Startable ? "" : " · follows another recipe") + " · " + row.Id;
                    description = RecipeIndex.Clean(row.Recipe.StartDescription);
                    var outcome = RecipeIndex.Clean(row.Recipe.Description);
                    if (!string.IsNullOrEmpty(outcome))
                        description = (string.IsNullOrEmpty(description) ? "" : description + "\n\n") + "Afterwards: " + outcome;
                    groups = row.Groups;
                    chips = groups != null ? row.Loose : row.Needs;
                    gives = row.Gives;
                    then = row.Then;
                    break;
                case PageKind.Favourites:
                    subtitle = "Recipes, cards and aspects you've starred: star one from its row or its page.";
                    break;
                default:
                    subtitle = _index.Recipes.Count + " recipes from the game's own data, " + _discoveredHere +
                               " discovered. Hover an icon for its name; click it for what makes and uses it.";
                    break;
            }

            float subtitleH = _subtitle.CalcHeight(new GUIContent(subtitle), inner);
            layout.Items.Add(new Item { Rect = new Rect(0f, y, inner, subtitleH), Text = subtitle, Style = _subtitle });
            y += subtitleH;

            if (!string.IsNullOrEmpty(description))
            {
                // A couple of lines unless opened; click to open or close it.
                const int shortLength = 160;
                bool longer = description.Length > shortLength;
                string shown = _descriptionOpen ? Truncate(description, 1500) : Truncate(description, shortLength);
                float h = _description.CalcHeight(new GUIContent(shown), inner);
                y += 4f;
                layout.Items.Add(new Item
                {
                    Rect = new Rect(0f, y, inner, h),
                    Text = shown,
                    Style = _description,
                    Tip = longer ? (_descriptionOpen ? "Click to shorten" : "Click to read it all") : null,
                    OnClick = longer
                        ? () =>
                        {
                            _descriptionOpen = !_descriptionOpen;
                            _header = null;
                        }
                        : (Action)null
                });
                y += h;
            }
            if (chips.Count > 0 || groups?.Count > 0 || gives?.Count > 0 || then?.Count > 0)
                y = Flow(layout, chips, gives, then, inner, y + 6f, groups, true);

            var target = PageTarget();
            var note = target != null ? NoteStore.Get(target) : null;
            if (!string.IsNullOrEmpty(note))
            {
                note = Truncate(note, 400);
                float h = _note.CalcHeight(new GUIContent(note), inner);
                y += 4f;
                layout.Items.Add(new Item { Rect = new Rect(0f, y, inner, h), Text = note, Style = _note });
                y += h;
            }
            layout.Height = y;
            return layout;
        }

        private static float DrawSearch(float x, float y, float inner)
        {
            const float fieldH = 26f;
            // Favourites were picked by hand, so the "discovered" toggle doesn't apply there.
            float discoveredW = _page.Kind == PageKind.Favourites ? 0f : 86f;
            float toggleW = _page.Kind == PageKind.All ? 78f : 0f;
            float togglesW = (discoveredW > 0f ? discoveredW + 6f : 0f) + (toggleW > 0f ? toggleW + 6f : 0f);
            var field = new Rect(x, y, inner - togglesW, fieldH);
            GUI.Box(field, GUIContent.none, Styles.Field);
            GUI.SetNextControlName(SearchControlName);
            string query = GUI.TextField(field, _page.Query, _searchField);
            if (query != _page.Query)
            {
                _page.Query = query;
                _page.Scroll = Vector2.zero;
                Filter();
            }
            if (string.IsNullOrEmpty(_page.Query))
            {
                // Shorter when the panel is narrow, rather than cut off.
                var hint = new Rect(field.x + 8f, field.y, field.width - 70f, fieldH);
                string text = "Search names, cards, aspects…";
                if (_placeholder.CalcSize(new GUIContent(text)).x > hint.width)
                    text = "Search…";
                GUI.Label(hint, text, _placeholder);
            }
            GUI.Label(new Rect(field.xMax - 66f, field.y, 60f, fieldH), _matched.ToString("N0"), _count);
            if (_focusSearch && Event.current.type == EventType.Repaint)
            {
                GUI.FocusControl(SearchControlName);
                _focusSearch = false;
            }

            if (discoveredW > 0f && Button(new Rect(x + inner - discoveredW, y, discoveredW, fieldH), _discoveredOnly ? "discovered" : "all", _toggle,
                    _discoveredOnly
                        ? "Only recipes you've finished in a game (" + _discoveredHere + " so far). Click to also show the ones you haven't."
                        : "Recipes whether or not you've finished them. Click to show just the ones you have."))
                _pending = () =>
                {
                    _discoveredOnly = !_discoveredOnly;
                    _tabs = BuildTabs(_page);
                    _shownKey = null;
                };
            if (toggleW > 0f && Button(new Rect(field.xMax + 6f, y, toggleW, fieldH), _startableOnly ? "startable" : "all", _toggle,
                    _startableOnly
                        ? "Only recipes you can start from an empty verb. Click to show every recipe."
                        : "Every recipe, including ones that only follow another. Click to show just the ones you can start."))
                _pending = () =>
                {
                    _startableOnly = !_startableOnly;
                    _tabs = BuildTabs(_page);
                    _shownKey = null;
                };
            return y + fieldH + 6f;
        }

        private static float DrawTabs(float x, float y, float inner)
        {
            if (_tabs.Count <= 1)
                return y + 4f;
            const float h = 22f;
            float tx = x, ty = y;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var text = _tabs[i].Title + " " + _tabs[i].Visible.Count;
                var style = i == _page.Tab ? _tabOn : _tabOff;
                float w = style.CalcSize(new GUIContent(text)).x + 16f;
                if (tx > x && tx + w > x + inner)
                {
                    tx = x;
                    ty += h + 4f;
                }
                int tab = i;
                if (GUI.Button(new Rect(tx, ty, w, h), text, style) && tab != _page.Tab)
                    _pending = () =>
                    {
                        _page.Tab = tab;
                        _page.Scroll = Vector2.zero;
                        _shownKey = null;
                    };
                tx += w + 4f;
            }
            return ty + h + 8f;
        }

        private static void DrawList(Rect area)
        {
            if (area.height < 20f)
                return;
            float width = area.width - 16f;
            if (!Mathf.Approximately(width, _layoutWidth))
            {
                Layouts.Clear();
                _layoutWidth = width;
            }

            // Rows whose icons aren't loaded yet are laid out a few a frame, the rest guessed at.
            if (_layoutFrame != Time.frameCount)
            {
                _layoutFrame = Time.frameCount;
                _layoutsLeft = LayoutsPerFrame;
            }
            float total = 0f;
            bool pending = false;
            foreach (var entry in _shown)
            {
                var layout = LayoutOf(entry, width);
                pending |= layout == null;
                total += layout?.Height ?? RowGuess;
            }
            bool more = _matched > _shown.Count;
            if (_shown.Count == 0 || more)
                total += 30f;

            bool mouseInList = area.Contains(Event.current.mousePosition);
            // Rows cut off at the list's edges mustn't take a press from outside it.
            bool wasEnabled = GUI.enabled;
            if (Event.current.type == EventType.MouseDown && !mouseInList)
                GUI.enabled = false;
            // While rows are still to come, the guesses mustn't cut the saved scroll short.
            float content = Mathf.Max(total, area.height, pending ? _page.Scroll.y + area.height : 0f);
            _page.Scroll = GUI.BeginScrollView(area, _page.Scroll, new Rect(0f, 0f, width, content), false, true,
                GUIStyle.none, GUI.skin.verticalScrollbar);
            var mouse = Event.current.mousePosition;
            float y = 0f;
            float top = _page.Scroll.y, bottom = _page.Scroll.y + area.height;
            foreach (var entry in _shown)
            {
                var layout = LayoutOf(entry, width);
                if (layout == null)
                {
                    y += RowGuess;
                    continue;
                }
                if (y + layout.Height >= top && y <= bottom)
                {
                    bool hovered = mouseInList && new Rect(0f, y, width, layout.Height).Contains(mouse);
                    DrawItems(layout, 0f, y, hovered, mouseInList);
                    GUI.Box(new Rect(0f, y + layout.Height - 3f, width, 1f), GUIContent.none, Styles.Rule);
                }
                y += layout.Height;
            }
            if (_shown.Count == 0)
                GUI.Label(new Rect(4f, y, width, 30f), EmptyText(), Styles.Hint);
            else if (more)
                GUI.Label(new Rect(4f, y, width, 30f), "and " + (_matched - _shown.Count) + " more: narrow the search to see them", Styles.Hint);
            GUI.EndScrollView();
            GUI.enabled = wasEnabled;
        }

        // Along the bottom: how many recipes here are hidden, and whether to show them.
        private static void DrawFooter(float x, float y, float inner)
        {
            var tab = _tabs[_page.Tab];
            const float buttonW = 96f;
            float right = x + inner - GripSize;
            GUI.Box(new Rect(x, y - 2f, inner, 1f), GUIContent.none, Styles.Rule);
            if (Button(new Rect(right - buttonW, y + 2f, buttonW, FooterH - 4f), _showHidden ? "hide them" : "show hidden", _toggle,
                    _showHidden
                        ? "Hidden recipes are listed, faded. Click to leave them out again."
                        : "Recipes you've hidden are left out of every list. Click to list them, faded, so you can unhide them."))
                _pending = () =>
                {
                    _showHidden = !_showHidden;
                    _tabs = BuildTabs(_page);
                    _shownKey = null;
                };
            string text = tab.HiddenCount == 0
                ? "Nothing hidden here" + (HiddenRecipes.Count > 0 ? " · " + HiddenRecipes.Count + " hidden in all" : "")
                : tab.HiddenCount + " hidden here" + (tab.Unfiltered ? ", listed anyway as favourites" : "") +
                  (HiddenRecipes.Count > tab.HiddenCount ? " · " + HiddenRecipes.Count + " in all" : "");
            GUI.Label(new Rect(x, y, right - buttonW - x - 6f, FooterH), text, _footer);
        }

        private static string EmptyText()
        {
            var tab = _tabs[_page.Tab];
            if (_missedUndiscovered + _missedHidden > 0)
            {
                var also = new List<string>();
                if (_missedUndiscovered > 0)
                    also.Add(_missedUndiscovered + " not yet discovered (click “discovered”)");
                if (_missedHidden > 0)
                    also.Add(_missedHidden + " hidden (click “show hidden” below)");
                return "Nothing listed matches; " + string.Join(" and ", also) + " do.";
            }
            if (tab.Visible.Count > 0)
                return "Nothing matches that search.";
            if (_page.Kind == PageKind.Favourites)
                return _tabs.Any(t => t.Entries.Count > 0)
                    ? "No " + tab.Title.ToLowerInvariant() + " starred."
                    : "Nothing starred yet. Star a recipe, card or aspect from its row or its page.";
            if (tab.Entries.Count == 0)
                return "Nothing here.";
            return tab.HiddenCount > 0 && !_showHidden
                ? "Everything here is hidden or not yet discovered: use “discovered” above or “show hidden” below."
                : "Nothing discovered here yet. Click “discovered” to see them all.";
        }

        private static void DrawItems(Layout layout, float x, float y, bool hovered, bool tips)
        {
            var e = Event.current;
            foreach (var item in layout.Items)
            {
                var r = new Rect(x + item.Rect.x, y + item.Rect.y, item.Rect.width, item.Rect.height);
                if (item.HoverOnly && !hovered)
                {
                    // An invisible stand-in, so the controls after it keep their IMGUI ids
                    // whether or not the row is hovered. The pointer isn't over it, so it
                    // never fires.
                    GUI.Button(r, GUIContent.none, GUIStyle.none);
                    continue;
                }
                if (hovered && item.HoverWidth > 0f && r.width > item.HoverWidth)
                    r.width = item.HoverWidth;
                bool clicked;
                if (item.Icon != null)
                {
                    clicked = item.OnClick != null && GUI.Button(r, GUIContent.none, item.Style);
                    if (item.OnClick == null && item.Style != null)
                        GUI.Box(r, GUIContent.none, item.Style);
                    if (e.type == EventType.Repaint)
                    {
                        DrawSprite(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), item.Icon, item.Faded ? 0.35f : 1f);
                        if (!string.IsNullOrEmpty(item.Badge))
                            DrawBadge(r, item.Badge, item.Kind);
                    }
                }
                else if (item.Image != null)
                {
                    clicked = item.OnClick != null && GUI.Button(r, GUIContent.none, GUIStyle.none);
                    if (e.type == EventType.Repaint)
                        GUI.DrawTexture(r, item.Image, ScaleMode.ScaleToFit, true);
                }
                else if (item.OnClick != null)
                {
                    clicked = GUI.Button(r, item.Text, item.Style);
                }
                else
                {
                    GUI.Label(r, item.Text, item.Style);
                    clicked = false;
                }
                if (clicked)
                    _pending = item.OnClick;
                if (tips && item.Tip != null)
                    TipIfHovered(r, item.Tip);
            }
        }

        // The amount in the icon's bottom-right corner, on a dark patch so it reads over any art.
        private static void DrawBadge(Rect icon, string text, ChipKind kind)
        {
            var style = BadgeStyles[kind];
            var content = new GUIContent(text);
            var size = style.CalcSize(content);
            float w = Mathf.Min(size.x, icon.width);
            var rect = new Rect(icon.xMax - w, icon.yMax - size.y, w, size.y);
            // Repaint only, so drawn straight from the style: GUI.Box would take an IMGUI id
            // that other events don't, and shift every control after it.
            _badgeBack.Draw(rect, false, false, false, false);
            style.Draw(rect, content, false, false, false, false);
        }

        private static void DrawGrip()
        {
            float gx = _rect.xMax - GripSize, gy = _rect.yMax - GripSize;
            foreach (var (dx, dy) in new[] { (11f, 3f), (7f, 7f), (11f, 7f), (3f, 11f), (7f, 11f), (11f, 11f) })
                GUI.Box(new Rect(gx + dx, gy + dy, 2f, 2f), GUIContent.none, Styles.Rule);
        }

        private static bool Button(Rect rect, string text, GUIStyle style, string tip)
        {
            TipIfHovered(rect, tip);
            return GUI.Button(rect, text, style);
        }

        private static void TipIfHovered(Rect rect, string tip)
        {
            if (rect.Contains(Event.current.mousePosition))
                _tip = tip;
        }

        // Beside the pointer, kept on screen.
        private static void DrawTip(Rect screen)
        {
            if (string.IsNullOrEmpty(_tip) || Event.current.type != EventType.Repaint)
                return;
            var content = new GUIContent(_tip);
            float w = Mathf.Min(_tipStyle.CalcSize(content).x, TipWidth);
            float h = _tipStyle.CalcHeight(content, w);
            var mouse = Event.current.mousePosition;
            var rect = new Rect(mouse.x + 14f, mouse.y + 18f, w, h);
            if (rect.xMax > screen.width)
                rect.x = mouse.x - w - 6f;
            if (rect.yMax > screen.height)
                rect.y = mouse.y - h - 6f;
            _tipStyle.Draw(rect, content, false, false, false, false);
        }

        // ---- Laying out rows

        private static Layout LayoutOf(Entry entry, float width)
        {
            if (Layouts.TryGetValue(entry, out var cached))
                return cached;
            // Loading icons is what's slow; a row whose icons are all loaded is laid out at once.
            if (!IconsLoaded(entry.Row))
            {
                if (_layoutsLeft <= 0)
                    return null;
                _layoutsLeft--;
            }
            var layout = new Layout();
            var row = entry.Row;
            float y = 3f, x = 0f;

            // The verb's icon (or the card's art) leads the line; then the name.
            var lead = row.Kind == RowKind.Recipe ? VerbSprite(row.VerbId) : row.Kind == RowKind.Card ? ElementSprite(row.Id) : null;
            Action onLead = row.Kind == RowKind.Recipe ? () => GoVerb(row.VerbId) : row.Kind == RowKind.Card ? () => GoElement(row.Id) : (Action)null;
            if (lead != null)
            {
                layout.Items.Add(new Item
                {
                    Rect = new Rect(0f, y, LeadSize, LeadSize),
                    Icon = lead,
                    Style = _leadFrame,
                    Tip = row.Kind == RowKind.Recipe ? row.Tag + ": every recipe for this verb" : null,
                    OnClick = row.Kind == RowKind.Recipe ? onLead : null
                });
                x = LeadSize + 6f;
            }
            else
            {
                float tagW = _tag.CalcSize(new GUIContent(row.Tag)).x;
                layout.Items.Add(new Item { Rect = new Rect(0f, y + 2f, tagW, 20f), Text = row.Tag, Style = _tag, OnClick = onLead });
                x = tagW + 6f;
            }

            bool isRecipe = row.Kind == RowKind.Recipe;
            bool hidden = isRecipe && HiddenRecipes.Has(row.Id);
            var titleStyle = isRecipe && (!row.Startable || hidden) ? _rowTitleFaded : _rowTitle;
            bool hasButtons = row.Kind != RowKind.Change;
            var rowTarget = hasButtons ? NoteTarget(row) : null;
            bool starred = Favourites.Has(rowTarget);
            float fullW = titleStyle.CalcSize(new GUIContent(row.Title)).x;
            // While hovered, the title stops short of the hide, star, wiki and note buttons; a
            // favourite's star and a hidden recipe's "unhide" always show, so its title always does.
            float hoverW = hasButtons ? Mathf.Max(20f, width - (isRecipe ? 154f : 110f) - x) : 0f;
            float titleW = Mathf.Min(fullW, starred || hidden ? hoverW : width - x);
            layout.Items.Add(new Item
            {
                Rect = new Rect(x, y, titleW, LeadSize),
                HoverWidth = hoverW,
                Text = row.Title,
                Style = titleStyle,
                Tip = fullW > (hasButtons ? hoverW : titleW) ? row.Title
                    : row.Kind == RowKind.Recipe && !row.Startable ? "Follows another recipe; it can't be started by hand" : null,
                OnClick = row.Kind == RowKind.Recipe ? () => GoRecipe(row.Id) : (Action)(() => GoElement(row.Id))
            });

            // Wiki and note float over the end of the line while the row is hovered.
            MenuTarget target = null;
            if (hasButtons)
            {
                target = NoteTarget(row);
                var t = target;
                bool hasNote = NoteStore.Has(t);
                layout.Items.Add(new Item
                {
                    Rect = new Rect(width - 84f, y + 3f, 40f, 18f),
                    Text = "wiki",
                    Style = _small,
                    HoverOnly = true,
                    OnClick = () => OpenWiki(t)
                });
                layout.Items.Add(new Item
                {
                    Rect = new Rect(width - 40f, y + 3f, 40f, 18f),
                    Text = "note",
                    Style = hasNote ? _smallOn : _small,
                    HoverOnly = true,
                    OnClick = () => EditNote(t)
                });
                layout.Items.Add(new Item
                {
                    Rect = new Rect(width - 106f, y + 3f, 18f, 18f),
                    Image = starred ? StarOn : StarOff,
                    HoverOnly = !starred,
                    Tip = starred ? "A favourite. Click to take it out of favourites" : "Add to favourites",
                    OnClick = () => Favourites.Toggle(t)
                });
                if (isRecipe)
                {
                    string id = row.Id;
                    layout.Items.Add(new Item
                    {
                        Rect = new Rect(width - 150f, y + 3f, 40f, 18f),
                        Text = hidden ? "unhide" : "hide",
                        Style = hidden ? _smallOn : _small,
                        HoverOnly = !hidden,
                        Tip = hidden ? "Hidden from the lists. Click to list it again" : "Leave it out of the lists (“show hidden” at the bottom brings it back)",
                        OnClick = () => HiddenRecipes.Toggle(id)
                    });
                }
            }
            y += LeadSize + 2f;

            if (!string.IsNullOrEmpty(entry.Reason))
            {
                float h = _reason.CalcHeight(new GUIContent(entry.Reason), width);
                layout.Items.Add(new Item { Rect = new Rect(0f, y, width, h), Text = entry.Reason, Style = _reason });
                y += h + 2f;
            }

            if (row.Needs.Count > 0 || row.Gives.Count > 0 || row.Then.Count > 0)
                y = Flow(layout, row.Groups != null ? row.Loose : row.Needs, row.Gives, row.Then, width, y, row.Groups);

            var note = target != null ? NoteStore.Get(target) : null;
            if (!string.IsNullOrEmpty(note))
            {
                note = Truncate(note.Replace('\n', ' '), 160);
                float h = _note.CalcHeight(new GUIContent(note), width);
                layout.Items.Add(new Item { Rect = new Rect(0f, y, width, h), Text = note, Style = _note });
                y += h + 2f;
            }

            layout.Height = y + 6f;
            Layouts[entry] = layout;
            return layout;
        }

        private static bool IconsLoaded(Row row)
        {
            if (row.Kind == RowKind.Recipe && _index.VerbIcon(row.VerbId) is string icon && !VerbSprites.ContainsKey(icon))
                return false;
            if (row.Kind == RowKind.Card && !ElementSprites.ContainsKey(row.Id))
                return false;
            var chips = row.Needs.Concat(row.Gives).Concat(row.Then)
                .Concat(row.Groups?.SelectMany(g => g.Chips) ?? Enumerable.Empty<Chip>())
                .Concat(row.Loose ?? Enumerable.Empty<Chip>());
            foreach (var chip in chips)
                if (chip.ElementId != null && !ElementSprites.ContainsKey(chip.ElementId))
                    return false;
            return true;
        }

        // Icons left to right, wrapping: what it needs → what it gives, then what follows.
        // Anything without an icon (a deck, a recipe, a card with no art) is a bit of text.
        // With groups, the needs are the cards that go in the verb: the main card, then each
        // slot it opens framed as one card (hover for the slot), then the loose needs.
        private static float Flow(Layout layout, List<Chip> needs, List<Chip> gives, List<Chip> then, float width, float y,
            List<RecipeIndex.NeedGroup> groups = null, bool labels = false)
        {
            const float framePad = 3f;
            float x = 0f;
            bool placed = false;

            void Place(float w, float h, Item item)
            {
                if (x > 0f && x + w > width)
                {
                    x = 0f;
                    y += IconSize + Gap;
                }
                item.Rect = new Rect(x, y + (IconSize - h) / 2f, w, h);
                layout.Items.Add(item);
                x += w + Gap;
                placed = true;
            }

            Item ChipItem(Chip chip, float maxW)
            {
                Action onClick = chip.ElementId != null && _index.IsElement(chip.ElementId) ? () => GoElement(chip.ElementId)
                    : chip.RecipeId != null ? () => GoRecipe(chip.RecipeId)
                    : (Action)null;
                var icon = ElementSprite(chip.ElementId);
                if (icon != null)
                    return new Item
                    {
                        Rect = new Rect(0f, 0f, IconSize, IconSize),
                        Icon = icon,
                        Badge = chip.Amount,
                        Kind = chip.Kind,
                        Faded = chip.Kind == ChipKind.Lack,
                        Tip = chip.Text,
                        Style = (onClick != null ? IconStyles : InertIconStyles)[chip.Kind],
                        OnClick = onClick
                    };
                var style = (onClick != null ? TextChipStyles : InertTextChipStyles)[chip.Kind];
                float fullW = style.CalcSize(new GUIContent(chip.Text)).x;
                float w = Mathf.Min(fullW, maxW);
                return new Item { Rect = new Rect(0f, 0f, w, TextChipH), Text = chip.Text, Style = style, OnClick = onClick, Tip = w < fullW ? chip.Text : null };
            }

            Item TextItem(string text, GUIStyle style) =>
                new Item { Rect = new Rect(0f, 0f, style.CalcSize(new GUIContent(text)).x, TextChipH), Text = text, Style = style };

            // A frame round some items, kept on one line; drawn first, so it's under them
            // and their tips win over its.
            void PlaceFramed(List<Item> inside, string tip)
            {
                float w = framePad * 2f + inside.Sum(i => i.Rect.width) + Gap * (inside.Count - 1);
                if (x > 0f && x + w > width)
                {
                    x = 0f;
                    y += IconSize + Gap;
                }
                layout.Items.Add(new Item { Rect = new Rect(x, y - 1f, w, IconSize + 2f), Text = "", Style = _groupFrame, Tip = tip });
                float cx = x + framePad;
                foreach (var item in inside)
                {
                    item.Rect = new Rect(cx, y + (IconSize - item.Rect.height) / 2f, item.Rect.width, item.Rect.height);
                    layout.Items.Add(item);
                    cx += item.Rect.width + Gap;
                }
                x += w + Gap;
                placed = true;
            }

            void PlaceChip(Chip chip)
            {
                var item = ChipItem(chip, width);
                Place(item.Rect.width, item.Rect.height, item);
            }

            void PlaceGroup(RecipeIndex.NeedGroup group)
            {
                // The main card on its own needs no frame.
                if (group.Label == null && group.Chips.Count == 1)
                {
                    PlaceChip(group.Chips[0]);
                    return;
                }
                var inside = new List<Item>();
                if (labels && group.Label != null)
                    inside.Add(TextItem(group.Label, _groupLabel));
                if (group.Count > 1)
                    inside.Add(TextItem(group.Count + "×", _groupCount));
                foreach (var chip in group.Chips)
                    inside.Add(ChipItem(chip, width - framePad * 2f));
                PlaceFramed(inside, group.Tip);
            }

            if (groups != null)
                foreach (var group in groups)
                    PlaceGroup(group);
            foreach (var chip in needs)
                PlaceChip(chip);
            if (gives != null && gives.Count > 0)
            {
                if (needs.Count > 0 || groups?.Count > 0)
                    Place(18f, IconSize, new Item { Text = "→", Style = _arrow });
                foreach (var chip in gives)
                    PlaceChip(chip);
            }
            if (then != null && then.Count > 0)
            {
                if (placed)
                {
                    x = 0f;
                    y += IconSize + Gap;
                }
                foreach (var chip in then)
                    PlaceChip(chip);
            }
            return placed ? y + IconSize + Gap : y;
        }

        private static string Truncate(string text, int max)
        {
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max).TrimEnd() + "…";
        }

        // ---- Stars

        // Filled in brass for a favourite, an outline otherwise. Drawn rather than typed: the
        // IMGUI font may have no star.
        private static Texture2D StarOn => _starOn != null ? _starOn : _starOn = MakeStar(true, new Color(0.86f, 0.7f, 0.36f, 1f));
        private static Texture2D StarOff => _starOff != null ? _starOff : _starOff = MakeStar(false, new Color(0.66f, 0.6f, 0.5f, 1f));

        private static Texture2D MakeStar(bool filled, Color color)
        {
            const int size = 32, samples = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int px = 0; px < size; px++)
                for (int py = 0; py < size; py++)
                {
                    int inside = 0;
                    for (int sx = 0; sx < samples; sx++)
                        for (int sy = 0; sy < samples; sy++)
                        {
                            float u = (px + (sx + 0.5f) / samples) / size, v = (py + (sy + 0.5f) / samples) / size;
                            bool on = InStar(u, v, 0.48f, 0.2f) && (filled || !InStar(u, v, 0.3f, 0.12f));
                            if (on)
                                inside++;
                        }
                    texture.SetPixel(px, py, new Color(color.r, color.g, color.b, color.a * inside / (samples * samples)));
                }
            texture.Apply();
            return texture;
        }

        // Whether (u, v) is inside a five-pointed star centred in the unit square, point up.
        private static bool InStar(float u, float v, float outer, float inner)
        {
            const int corners = 10;
            bool inside = false;
            for (int i = 0, j = corners - 1; i < corners; j = i++)
            {
                Vector2 a = StarCorner(i, outer, inner), b = StarCorner(j, outer, inner);
                if ((a.y > v) != (b.y > v) && u < (b.x - a.x) * (v - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        private static Vector2 StarCorner(int i, float outer, float inner)
        {
            float radius = i % 2 == 0 ? outer : inner;
            float angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
            return new Vector2(0.5f + radius * Mathf.Cos(angle), 0.47f + radius * Mathf.Sin(angle));
        }

        // ---- Styles

        private static void EnsureStyles()
        {
            if (_windowTitle != null)
                return;
            var parchment = Styles.Item.normal.textColor;
            var faded = Styles.Subtitle.normal.textColor;
            var brass = new Color(0.72f, 0.58f, 0.32f, 1f);
            var ember = new Color(0.32f, 0.22f, 0.11f, 1f);
            var ink = new Color(0.07f, 0.06f, 0.05f, 1f);
            var green = new Color(0.55f, 0.7f, 0.42f, 1f);
            var red = new Color(0.82f, 0.4f, 0.34f, 1f);

            _windowTitle = new GUIStyle(Styles.Title)
            {
                fontSize = 17,
                wordWrap = false,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };
            _subtitle = new GUIStyle(Styles.Subtitle) { fontSize = 11 };
            _description = new GUIStyle(Styles.Preview) { fontSize = 13, wordWrap = true };
            _description.hover.textColor = Color.white;

            _rowTitle = new GUIStyle(Styles.Item)
            {
                fontSize = 14,
                fontStyle = UnityEngine.FontStyle.Bold,
                fixedHeight = LeadSize,
                padding = new RectOffset(4, 4, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            _rowTitleFaded = new GUIStyle(_rowTitle);
            _rowTitleFaded.normal.textColor = faded;

            _tag = new GUIStyle(Styles.Item)
            {
                fontSize = 11,
                fontStyle = UnityEngine.FontStyle.Bold,
                fixedHeight = 20f,
                padding = new RectOffset(6, 6, 0, 0),
                wordWrap = false,
                border = new RectOffset(1, 1, 1, 1)
            };
            _tag.normal.textColor = brass;
            _tag.normal.background = Styles.Bordered(ink, new Color(brass.r, brass.g, brass.b, 0.45f));
            _leadFrame = new GUIStyle
            {
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Styles.Bordered(ink, new Color(brass.r, brass.g, brass.b, 0.3f)) },
                hover = { background = Styles.Bordered(ember, brass) },
                active = { background = Styles.Bordered(brass, brass) }
            };

            _reason = new GUIStyle(Styles.Hint) { fixedHeight = 0f, wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 12 };
            _note = new GUIStyle(Styles.Preview) { fontSize = 12, wordWrap = true };
            _note.normal.textColor = brass;
            _arrow = new GUIStyle(Styles.Item) { fontSize = 15, fixedHeight = IconSize, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter };
            _arrow.hover.background = null;
            _arrow.normal.textColor = faded;

            _glyph = new GUIStyle(Styles.Button) { fontSize = 16, fixedHeight = 24f };
            _small = new GUIStyle(Styles.Button) { fontSize = 11, fixedHeight = 0f };
            _smallOn = new GUIStyle(_small);
            _smallOn.normal.textColor = brass;
            _smallOn.normal.background = Styles.Bordered(ember, brass);
            _tabOff = new GUIStyle(Styles.Button) { fontSize = 12, fixedHeight = 22f };
            _tabOn = new GUIStyle(_tabOff);
            _tabOn.normal.background = Styles.Bordered(ember, brass);
            _tabOn.normal.textColor = Color.white;
            _toggle = new GUIStyle(Styles.Button) { fontSize = 12, fixedHeight = 26f };
            _count = new GUIStyle(Styles.Hint) { alignment = TextAnchor.MiddleRight, fixedHeight = 0f, fontSize = 11 };
            _footer = new GUIStyle(Styles.Hint) { alignment = TextAnchor.MiddleLeft, fixedHeight = 0f, fontSize = 11, wordWrap = false, clipping = TextClipping.Clip };
            _placeholder = new GUIStyle(Styles.Hint) { fixedHeight = 0f, fontSize = 12, wordWrap = false, clipping = TextClipping.Clip };
            _searchField = new GUIStyle(Styles.TextField) { fontSize = 14, padding = new RectOffset(8, 70, 4, 4) };

            _tipStyle = new GUIStyle(Styles.Item)
            {
                fontSize = 12,
                fixedHeight = 0f,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(7, 7, 4, 4),
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Styles.Bordered(new Color(0.11f, 0.09f, 0.07f, 0.98f), brass), textColor = parchment }
            };
            _badgeBack = new GUIStyle { normal = { background = Styles.Solid(new Color(0f, 0f, 0f, 0.72f)) } };
            // One card in the verb: a slot and what goes in it.
            _groupFrame = new GUIStyle
            {
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Styles.Bordered(new Color(brass.r, brass.g, brass.b, 0.08f), new Color(brass.r, brass.g, brass.b, 0.55f)) }
            };
            _groupLabel = new GUIStyle(Styles.Hint) { fontSize = 11, fixedHeight = 0f, wordWrap = false, alignment = TextAnchor.MiddleLeft };
            _groupCount = new GUIStyle(Styles.Item) { fontSize = 13, fontStyle = UnityEngine.FontStyle.Bold, fixedHeight = 0f, padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter };
            _groupCount.hover.background = null;

            var needEdge = new Color(brass.r, brass.g, brass.b, 0.6f);
            var lackEdge = new Color(faded.r, faded.g, faded.b, 0.35f);
            var giveEdge = new Color(green.r, green.g, green.b, 0.7f);
            var takeEdge = new Color(red.r, red.g, red.b, 0.7f);
            AddChipStyles(ChipKind.Need, new Color(0.16f, 0.12f, 0.08f, 1f), needEdge, parchment, UnityEngine.FontStyle.Normal, parchment);
            AddChipStyles(ChipKind.Aspect, new Color(0.16f, 0.12f, 0.08f, 1f), needEdge, parchment, UnityEngine.FontStyle.Normal, parchment);
            AddChipStyles(ChipKind.Lack, ink, lackEdge, faded, UnityEngine.FontStyle.Italic, red);
            AddChipStyles(ChipKind.Give, new Color(0.12f, 0.17f, 0.11f, 1f), giveEdge, parchment, UnityEngine.FontStyle.Normal, green);
            AddChipStyles(ChipKind.Take, new Color(0.2f, 0.09f, 0.08f, 1f), takeEdge, parchment, UnityEngine.FontStyle.Normal, red);
            AddChipStyles(ChipKind.Slot, ink, needEdge, parchment, UnityEngine.FontStyle.Italic, brass);
            AddChipStyles(ChipKind.Plain, ink, lackEdge, faded, UnityEngine.FontStyle.Normal, faded);
        }

        // A kind of chip as an icon frame, as a bit of text, each clickable or not, and its badge.
        private static void AddChipStyles(ChipKind kind, Color fill, Color edge, Color text, UnityEngine.FontStyle fontStyle, Color badge)
        {
            var icon = new GUIStyle
            {
                border = new RectOffset(1, 1, 1, 1),
                normal = { background = Styles.Bordered(fill, edge) },
                hover = { background = Styles.Bordered(Color.Lerp(fill, edge, 0.45f), edge) },
                active = { background = Styles.Bordered(edge, edge) }
            };
            IconStyles[kind] = icon;
            InertIconStyles[kind] = Inert(icon);

            var chip = new GUIStyle(icon)
            {
                fontSize = 11,
                fontStyle = fontStyle,
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = TextChipH,
                padding = new RectOffset(6, 6, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            chip.normal.textColor = text;
            chip.hover.textColor = Color.white;
            chip.active.textColor = new Color(0.07f, 0.06f, 0.05f, 1f);
            TextChipStyles[kind] = chip;
            InertTextChipStyles[kind] = Inert(chip);

            BadgeStyles[kind] = new GUIStyle
            {
                fontSize = 11,
                fontStyle = UnityEngine.FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(2, 2, 0, 0),
                wordWrap = false,
                clipping = TextClipping.Clip,
                normal = { textColor = badge }
            };
        }

        private static GUIStyle Inert(GUIStyle style)
        {
            var inert = new GUIStyle(style);
            inert.hover = inert.normal;
            inert.active = inert.normal;
            return inert;
        }
    }
}
