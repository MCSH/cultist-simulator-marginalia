# Marginalia

A Cultist Simulator DLL mod that adds a hidden context menu to cards, verbs, aspects and the board. You can attach a persistent note to any card, verb or aspect, and write labels directly on the table.

## Use

- Hold **Alt** and **right-click** a card, a verb, an aspect icon, or an open verb, card or aspect window.
- Pick **Write note…** or **Edit note…**. Press **Ctrl+Enter** to save the note (the button is labelled *Keep*) or **Esc** to discard it. **Erase note** deletes it.
- If a note exists, the menu shows a preview of it.
- **Open wiki** opens the item's article on the [Cultist Simulator Wiki](https://cultistsimulator.fandom.com/), in the Steam overlay browser (or your system browser if the overlay is unavailable). The mod asks the wiki's search API for the best-matching title, then opens Fandom's bare article view (`?action=render`), which skips the slow site chrome, scripts and ads. If the lookup fails, it opens the full site's search instead.

On the board:

- **Alt + right-click** empty table, then pick **Write on the board…**. The text appears where you clicked, under the cards, and moves with the table when you pan and zoom.
- **Alt + right-click** a label to **Edit**, make it **Larger** or **Smaller** (the menu stays open so you can click repeatedly), or **Rub out** (delete) it.
- **Alt + left-drag** a label to move it.

While Alt is held, the game doesn't see your clicks, so Alt + right-click never sends a card to a verb or toggles a window. Game hotkeys are off while the menu or editor is open.

Notes are global: they are keyed by element or verb id and shared across all saves. Cards and aspects are both elements, so a note on the *Lantern* aspect is the same note wherever Lantern appears. Notes are stored in `marginalia.json` next to the save files (`~/.config/unity3d/Weather Factory/Cultist Simulator/` on Linux). Board labels are also global (the same labels appear in every game) and are stored in `marginalia_board.json` in the same folder.

To use a different modifier, add one of these lines to `config.ini` in the same folder:

```
marginalia_modifier=ctrl
marginalia_modifier=shift
```

Some Linux window managers grab Alt + right-drag. If the menu never appears, try `ctrl`.

## Build and install

Requires the .NET SDK. The project targets net472 and references the game's own assemblies.

```sh
./install.sh            # builds and copies to the local mods folder
GAME_DIR=/path/to/Cultist\ Simulator ./install.sh
```

Then enable **Marginalia** in the game's Mods menu and restart the game. DLLs load only at startup. The workshop mod *GHIRBI* must also be enabled, because the game requires it for any DLL mod.

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
| `src/JsonFile.cs` | Atomic JSON writes |
| `src/Wiki.cs` | The *Open wiki* item: builds the wiki URL and opens it in the Steam overlay or the system browser |
| `src/Styles.cs` | IMGUI look |
| `mod/synopsis.json` | Mod metadata |
