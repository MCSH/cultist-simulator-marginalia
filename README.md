**NOTE**: This repository is entirely AI generated. This note is the only human authored line. This is a basic mod for myself, because I got tired of messy notes, and I put next to no effort in creating it.

# Marginalia

A [Cultist Simulator](https://store.steampowered.com/app/718670/Cultist_Simulator/) DLL mod that adds a hidden context menu to cards, verbs, aspects and the board. You can attach a persistent note to any card, verb or aspect, and write labels directly on the table. It can also show the community wiki's article for any card, aspect or verb in an in-game window.

This is an unofficial fan mod. It isn't affiliated with Weather Factory or Fandom. It has only been tried on Linux (Steam), with game version 2026.1.g.2.

## Use

- Hold **Alt** and **right-click** a card, a verb, an aspect icon, or an open verb, card or aspect window.
- Pick **Write note…** or **Edit note…**. Press **Ctrl+Enter** to save the note (the button is labelled *Keep*) or **Esc** to discard it. **Erase note** deletes it.
- If a note exists, the menu shows a preview of it.
- **Open wiki** shows the item's [Cultist Simulator Wiki](https://cultistsimulator.fandom.com/) article in an in-game window, so it doesn't depend on Steam or a browser. The mod uses the wiki's API to find the best-matching title, fetch the article, and convert it to the game's text format. Links between articles work inside the window. Use **Back** (or the mouse back button) to return, and **Close**, **Esc** or a click outside to dismiss it. **Browser** opens the same article in the Steam overlay, or your system browser if the overlay is unavailable. Fandom's bare `?action=render` view keeps that fast. Images aren't shown, and very long articles are cut short with a pointer to the browser.

On the board:

- **Alt + right-click** empty table, then pick **Write on the board…**. The text appears where you clicked, under the cards, and moves with the table when you pan and zoom.
- **Alt + right-click** a label to **Edit**, make it **Larger** or **Smaller** (the menu stays open so you can click repeatedly), or **Rub out** (delete) it.
- **Alt + left-drag** a label to move it.

Rulers, for laying cards out by hand:

- **Alt + right-click** the table and pick **Add grid…**, **Add line (H)…** or **Add line (V)…**. Sliders set the grid's width and height in cards, or the line's length in card widths (H) or card heights (V). The ruler updates as you drag the sliders. **Enter** keeps it and **Esc** cancels.
- A grid is a frame split into card-sized cells by dashed lines. A line has a tick at every card width or height.
- Rulers snap so their cells line up with where the game snaps cards, using the grid snap setting in the game's options.
- **Alt + right-click** a ruler to **Resize** or **Remove** it. Everything else on the board menu works inside a grid too. **Alt + left-drag** a ruler to move it.
- Rulers never move or sort cards. They are only guides drawn beneath the cards and labels.

While Alt is held, the game doesn't see your clicks, so Alt + right-click never sends a card to a verb or toggles a window. Game hotkeys are off while the menu, editor or wiki window is open.

Notes are global: they are keyed by element or verb id and shared across all saves. Cards and aspects are both elements, so a note on the *Lantern* aspect is the same note wherever Lantern appears. Board labels and rulers are also global: they appear in every game.

Notes are stored in `marginalia.json`, board labels in `marginalia_board.json` and rulers in `marginalia_rulers.json`, all in the game's save folder:

| OS | Save folder |
| --- | --- |
| Linux | `~/.config/unity3d/Weather Factory/Cultist Simulator/` |
| Windows | `%USERPROFILE%\AppData\LocalLow\Weather Factory\Cultist Simulator\` |
| macOS | `~/Library/Application Support/Weather Factory/Cultist Simulator/` |

The wiki window needs an internet connection. Notes and labels don't.

To use a different modifier, add one of these lines to `config.ini` in the save folder:

```
marginalia_modifier=ctrl
marginalia_modifier=shift
```

Some Linux window managers grab Alt + right-drag. If the menu never appears, try `ctrl`.

## Build and install

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
| `mod/synopsis.json` | Mod metadata |
| `mod/cover.png` | 100×100 mod cover, generated by `tools/make_cover.py` |
| `install.sh` | Builds the mod and installs it locally (Linux paths by default) |
