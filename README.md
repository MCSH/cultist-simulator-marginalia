**NOTE**: This repository is entirely AI generated. This note is the only human authored line. This is a basic mod for myself, because I got tired of messy notes, and I put next to no effort in creating it.

# Marginalia

A [Cultist Simulator](https://store.steampowered.com/app/718670/Cultist_Simulator/) DLL mod that adds a hidden context menu to cards, verbs, aspects and the board. You can attach a persistent note to any card, verb or aspect, write labels directly on the table, and draw rulers (card-sized grids and lines) to lay cards out by hand. It can also show the community wiki's article for any card, aspect or verb in an in-game window. And it can pause the game the moment a verb finishes, or just before a card decays.

It also has a recipe explorer: a floating panel listing every recipe from the game's own data, showing what each needs and gives, and what makes and uses each card and aspect, with favourites, hidden recipes and a filter for the recipes you've discovered. And a board list, for finding, fetching, editing and tidying away everything written or drawn on the table, including anything that has ended up off it.

This is an unofficial fan mod. It isn't affiliated with Weather Factory or Fandom. It has only been tried on Linux (Steam), with game version 2026.1.g.2.

![The board with rulers: grids around the stats and lines under the lore, followers and verbs, plus the board menu](docs/rulers.jpg)

| A note on a verb | Writing a note | The wiki, in game |
| --- | --- | --- |
| ![Context menu on the Study verb, with its note previewed](docs/menu.png) | ![The note editor on a card](docs/note-editor.png) | ![The Study wiki article in the in-game window](docs/wiki.png) |

## Use

- **Middle-click** a card, a verb, an aspect icon, an open verb, card or aspect window, or the table to open the menu. **Alt + right-click** does the same.
- Or rest the pointer on a card or verb for a moment: a small round **…** button appears in its top-left corner. Click it for the menu.
- Or **double-click** an aspect icon.
- Pick **Write note…** or **Edit note…**. **Enter** starts a new line. Press **Ctrl+Enter** to save the note (the button is labelled *Keep*) or **Esc** to discard it. **Erase note** deletes it.
- If a note exists, the menu shows a preview of it.
- **Open wiki** shows the item's [Cultist Simulator Wiki](https://cultistsimulator.fandom.com/) article in an in-game window, so it doesn't depend on Steam or a browser. The mod uses the wiki's API to find the best-matching title, fetch the article, and convert it to the game's text format. Links between articles work inside the window. Use **Back** (or the mouse back button) to return, and **Close**, **Esc** or a click outside to dismiss it. **Browser** opens the same article in the Steam overlay, or your system browser if the overlay is unavailable. Fandom's bare `?action=render` view keeps that fast. Images aren't shown, and very long articles are cut short with a pointer to the browser.

Pausing:

| A verb has finished | A card is about to decay |
| --- | --- |
| ![Paused the moment Explore finished, with a gold ring around the verb](docs/pause.jpg) | ![Paused with 0.1 s left on Mme Bechet, with a gold ring around the card](docs/pause-card.png) |

- On a card that decays, pick **Pause before it decays…**. It applies to every card of that kind.
- On a verb, pick **Pause at every step…** to pause each time its timer runs out, including when its recipe leads straight into another.
- Both open a slider for how long before the timer runs out to pause, from **0 s** (right after it ends or decays) up to **185 s**. The steps are fine near zero and get coarser further out: tenths of a second at first, then whole seconds, then 25 s near the top. It opens on the current setting, or on 0.1 s for a card and 0 s for a verb. **Enter** keeps it. Pick the item again to change it, or pick **Stop pausing**.
- **Pause at the end** on a verb pauses right after a recipe that nothing follows, so the results are waiting. It has no slider: whether another recipe follows is only settled once one ends. Pick **Stop pausing at the end** to turn it off. A verb pauses either at every step or at the end, not both.
- If a timer starts with less time than the slider's setting, the game pauses as soon as it appears.
- On any card, pick **Pause when one appears** to pause every time a new card of that kind turns up: on the table, in a verb or along the bottom of the screen, including a stack growing (another *Funds*). The new card gets the ring. **Stop pausing when one appears** turns it off. It doesn't fire for cards already there when a game loads.
- The pause lands on the exact moment at any game speed. Everything else on the table has spent exactly as much time as the verb or card, no more.
- A gold ring pulses around whatever caused the pause until you unpause. If it's a card inside a closed verb window, the ring goes around the verb.
- **Step time 0.1 s forward** on the board menu pauses the game if needed, then moves it on by a tenth of a second. The menu stays open, so you can click it repeatedly. It still stops at any of the marks above.

Finding things and keeping an eye on timers:

| The timers panel | The board menu |
| --- | --- |
| ![The timers panel: running verbs and decaying cards, soonest first, with the ones that will pause marked](docs/timers.png) | ![The board menu with Find on the table, Hide timers and Step time 0.1 s forward](docs/board-menu.png) |

- **Find on the table…** on the board menu opens a search box. As you type, a gold ring goes around every card and verb whose name contains what you typed. A card inside a closed verb rings the verb. **Enter** closes the box and leaves the rings for a few seconds; **Esc** clears them.
- **Show timers** on the board menu opens a small panel listing every running verb and decaying card, soonest first, with the time left. Rows that will pause the game say *pauses*. Click a row to ring that card or verb. Drag the panel by its title; **Close** or **Hide timers** puts it away. It shows the 14 soonest. It remembers where it was and whether it was open, even after restarting the game.

The recipe explorer:

- **Recipe explorer…** on the board menu opens a floating panel listing every recipe in the game, read from the game's own data (so DLCs and content mods are included, not the wiki). Drag it by its title bar and resize it from its bottom-right corner; the board stays in play around it. **Hide recipe explorer**, or **×**, puts it away. It remembers its position, its size and whether it was open, even after restarting the game, and it opens again on the page you left it on during a session.
- Each row starts with its verb's icon and the recipe's name, then the cards and aspects it needs → what it gives, as their icons with a small badge for the amount: `5+` at least five, `0` none allowed, `+1` made, `−1` used up. Hover an icon for its name, e.g. *Lantern 5+*. Something with no icon (a deck, a recipe) shows as text.
- A recipe's needs are totals over every card in the verb, so where it can, the explorer shows them as the cards you put in: the main card (say, a skill), then each slot that card opens, framed, with what goes in it. *Exercise the Mind: A Brighter Illumination* reads as Profound Scholarship, then one frame holding Lore and Forge (a Forge lore in *Something More*), then **4×** Erudition (one in each *A Lesson Learnt*). Hover a frame for its slot; a recipe's own page names them. What the shown cards already carry is taken off, so *A Deeper Grace* asks for a Lore with just **Heart 1+ more**, since its four lessons give Heart 8. Needs that could come from any card stay as plain totals.
- Type in the search box to narrow it down. Every word must match a name, card or aspect, so `lantern edge` finds Reason's Glory. **startable**, beside the search box, hides recipes that only follow another one; click it (it then reads **all**) to show every recipe.
- **discovered**, also beside the search box, shows only recipes you've finished in a game; click it (it then reads **all**) for every recipe. It's on to begin with, on every page. A recipe counts once it completes: the game keeps a history of the recipes each character has done, and the mod adds whatever turns up there to its own list, so loading a save brings in what it had already done. The list is kept across saves and legacies.
- Click a card or aspect to open its page, like JEI in Minecraft: **Made by** (recipes, deck draws, decay and xtriggers), **Used by** (requirements, slots, what consumes it), **Via aspects** (recipes that want one of its aspects), and, for an aspect, **Cards** that have it. Click a recipe's name for its full text and what comes **Before** and **After** it. Click a verb's icon to list its recipes.
- Star a recipe, card or aspect to make it a favourite: hover its row for the star (it stays, filled, once it's a favourite), or click the star beside a page's name. The **★** button in the title bar opens **Favourites**, with your starred **Recipes** and **Cards**; the **discovered** toggle doesn't hide them there.
- Hover a recipe's row for **hide** to leave it out of every list. The bar along the bottom says how many recipes on the page are hidden; **show hidden** lists them again, faded, each with **unhide**. Hidden is its own flag, apart from favourites and discovered; favourites still list a hidden recipe.
- Hover a row for its **wiki** and **note** buttons; a card, aspect or recipe page has them in its title bar too. **wiki** opens the wiki article; **note** writes a note on the recipe, card or aspect, shown under it in the list. **←** (or the mouse back button over the panel) goes back.
- The mouse wheel scrolls the panel while the pointer is over it. While you type in the search box the game's hotkeys are off; **Esc** then closes the panel, and clicking the board hands the keyboard back. Otherwise Esc and the hotkeys work as usual.
- On a card or aspect, **Recipes with this…** opens its page; on a verb, **Recipes for this verb…** lists its recipes.
- With [The Roost Machine](https://steamcommunity.com/sharedfiles/filedetails/?id=2625527332) loaded, effects and xtriggers are read from where Roost keeps them.

On the board:

- **Alt + right-click** empty table, then pick **Write on the board…**. The text appears where you clicked, under the cards, and moves with the table when you pan and zoom.
- **Alt + right-click** a label to **Edit**, make it **Larger** or **Smaller** (the menu stays open so you can click repeatedly), or **Rub out** (delete) it.
- **Middle-drag** or **Alt + left-drag** a label to move it.

The board list:

- **Board list…** on the board menu opens a floating panel listing everything written or drawn on the table: board labels, grids and lines. Tabs show them all or one kind at a time, and the search box finds labels by their text. Hover a row and that label or ruler lights up on the table.
- Each row has **go** (the camera goes to it), **here** (moves it to the middle of the view), **edit** (a label's text, a ruler's size) and **×** (delete). Click a row's text to go to it.
- Tick rows (or **select all**) to **bring here** or **delete** several at once; delete asks once more before it goes ahead.
- Something that has ended up off the table, where neither the camera nor the mouse can reach it, says **off the table** and gets its own tab: bring it back with **here**, or delete it. Dragging a label or ruler now stops at the table's edge, so nothing more goes astray.
- Like the explorer, drag it by its title, resize it by its corner, and it remembers where it was and whether it was open.

Rulers, for laying cards out by hand:

- **Alt + right-click** the table and pick **Add grid…**, **Add line (H)…** or **Add line (V)…**. Sliders set the grid's width and height in cards, or the line's length in card widths (H) or card heights (V). The ruler updates as you drag the sliders. **Enter** keeps it and **Esc** cancels.
- A grid is a frame split into card-sized cells by dashed lines. A line has a tick at every card width or height.
- Rulers snap so their cells line up with where the game snaps cards, using the grid snap setting in the game's options.
- **Alt + right-click** a ruler to **Resize** or **Remove** it. Everything else on the board menu works inside a grid too. **Middle-drag** or **Alt + left-drag** a ruler to move it.
- Rulers never move or sort cards. They are only guides drawn beneath the cards and labels.

While Alt is held, the game doesn't see your clicks, so Alt + right-click never sends a card to a verb or toggles a window. A middle-click that opens the menu doesn't reach the game either. Game hotkeys are off while the menu, editor or wiki window is open.

Notes are global: they are keyed by element, verb or recipe id and shared across all saves. Cards and aspects are both elements, so a note on the *Lantern* aspect is the same note wherever Lantern appears. Board labels and rulers are also global: they appear in every game.

Pausing choices are global too, keyed by verb or element id like notes.

Notes are stored in `marginalia.json`, board labels in `marginalia_board.json`, rulers in `marginalia_rulers.json`, pausing choices in `marginalia_pauses.json`, discovered recipes in `marginalia_discovered.json`, favourites in `marginalia_favourites.json` and hidden recipes in `marginalia_hidden.json`, all in the game's save folder:

| OS | Save folder |
| --- | --- |
| Linux | `~/.config/unity3d/Weather Factory/Cultist Simulator/` |
| Windows | `%USERPROFILE%\AppData\LocalLow\Weather Factory\Cultist Simulator\` |
| macOS | `~/Library/Application Support/Weather Factory/Cultist Simulator/` |

On Steam, the mod also keeps a copy of all seven files in the game's Steam Cloud space, as one small file (`marginalia_cloud.json`), so they follow you between computers like your saves do. When the game starts, each file is compared with its cloud copy and the newer one wins. If you change notes on two computers without the game syncing in between, the later change is kept. Discovered recipes are the exception: both computers' lists are combined, whichever is newer, since a recipe once discovered stays discovered. Without Steam, or with Steam Cloud turned off, everything stays local.

Where the timers panel, the recipe explorer and the board list sit, their size, and whether they're open are kept in `marginalia_local.json` in the same folder. That file belongs to the computer it's on (another screen wants another layout), so it is never copied to Steam Cloud.

If one of these files can't be read, for example after a hand edit with a typo, the mod leaves it untouched and doesn't save changes to it until it's fixed. The game's log says which file it was.

The wiki window needs an internet connection. Notes and labels don't.

Settings go in `config.ini` in the save folder, one per line. For example:

```
marginalia_modifier=ctrl
marginalia_button=forward
marginalia_handle=off
```

- `marginalia_modifier` is the key held for right-click and left-drag: `alt` (the default), `ctrl` or `shift`.
- `marginalia_button` is the mouse button that opens the menu on its own: `middle` (the default), `forward`, `back` or `none`.
- `marginalia_handle=off` hides the **…** button on hovered cards and verbs.

Some Linux window managers grab Alt + right-drag. If the menu never appears, try `ctrl`.

## Build and install

The easiest way to get it is to subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3829272802), along with [GHIRBI](https://steamcommunity.com/sharedfiles/filedetails/?id=2901287611). To build it yourself instead:

No prebuilt DLL is checked in, so you need to build it. This requires the [.NET SDK](https://dotnet.microsoft.com/download). The project targets net472 and compiles against the game's own assemblies, so the game must be installed.

On Linux, `install.sh` builds the mod and copies it into the local mods folder:

```sh
./install.sh
GAME_DIR=/path/to/Cultist\ Simulator MODS_DIR=/path/to/mods ./install.sh   # non-default locations
```

Elsewhere, build it yourself and copy the files:

```sh
dotnet build src/Marginalia.csproj -c Release -p:GameDir="/path/to/Cultist Simulator"
```

Then put these files in `<save folder>/mods/Marginalia/`:

```
Marginalia/
├── synopsis.json        (from mod/)
├── cover.png            (from mod/)
└── dll/
    └── Marginalia.dll   (from src/bin/Release/)
```

Finally, enable **Marginalia** in the game's Mods menu and restart the game, because DLLs load only at startup. You also need the workshop mod [GHIRBI](https://steamcommunity.com/sharedfiles/filedetails/?id=2901287611) enabled, because the game requires it before it will run any DLL mod.

## Adding menu items

Each menu entry is a `ContextMenuItem` registered with `ContextMenuRegistry`. See `NoteMenuItems` in `src/ContextMenuRegistry.cs` for an example:

```csharp
ContextMenuRegistry.Register(new ContextMenuItem
{
    Id = "example.copy-id",
    Order = 20,
    Label = t => "Copy id",
    IsVisible = t => t.Kind == TargetKind.Element,
    Execute = t => GUIUtility.systemCopyBuffer = t.Id
});
```

## Layout

| File | Purpose |
| --- | --- |
| `src/Marginalia.cs` | Entry point (`Marginalia.Initialise`) that the game's mod loader calls |
| `src/MarginaliaController.cs` | Input shield, hotkey suppression, IMGUI menu and editor, label dragging |
| `src/TargetResolver.cs` | Works out which card, verb, aspect, board label or board spot is under the pointer |
| `src/MenuTarget.cs` | The thing a menu was opened on |
| `src/ContextMenuRegistry.cs` | Menu item registry and the note items |
| `src/NoteStore.cs` | Reads and writes `marginalia.json` |
| `src/BoardLabels.cs` | Board labels: storage, tabletop TextMeshPro views, and their menu items |
| `src/Rulers.cs` | Rulers (grids and lines): storage, snapping, drawing, and their menu items |
| `src/JsonFile.cs` | Atomic JSON writes |
| `src/Wiki.cs` | Wiki API client: title lookup, article fetch and cache, browser fallback, and the *Open wiki* menu item |
| `src/WikiHtml.cs` | Converts article HTML into TextMeshPro rich text with clickable links |
| `src/WikiWindow.cs` | The in-game wiki reader (uGUI on the shield canvas) |
| `src/UiKit.cs` | Shared font, nine-sliced panels and layout helpers for the uGUI parts |
| `src/Styles.cs` | IMGUI look |
| `src/RecipeIndex.cs` | Reads every recipe, decay and xtrigger from the compendium, and indexes what makes and uses each card and aspect |
| `src/RecipeExplorer.cs` | The recipe explorer: a floating, resizable panel with card, aspect and verb icons, and its menu items |
| `src/RoostCompat.cs` | Reads the recipe effects and xtriggers that The Roost Machine moves into its own store |
| `src/Discovery.cs` | Keeps the recipes the player has finished, read from the game's own history of recipes done (`marginalia_discovered.json`) |
| `src/HiddenRecipes.cs` | The recipes hidden from the recipe explorer's lists (`marginalia_hidden.json`) |
| `src/Favourites.cs` | The recipes, cards and aspects starred in the recipe explorer (`marginalia_favourites.json`) |
| `src/BoardList.cs` | The board list: every label, grid and line on the table, to find, fetch, edit or delete |
| `src/Panels.cs` | Remembers where the floating panels sit (in `marginalia_local.json`, kept off Steam Cloud), and steadies dragging past the window's edge |
| `mod/synopsis.json` | Mod metadata |
| `mod/cover.png` | 100×100 mod cover, generated by `tools/make_cover.py` |
| `install.sh` | Builds the mod and installs it locally (Linux paths by default) |
