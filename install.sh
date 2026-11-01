#!/usr/bin/env bash
# Build Marginalia and copy it into Cultist Simulator's local mods folder.
#   GAME_DIR  - game install (default: Steam library)
#   MODS_DIR  - local mods folder (default: Unity persistentDataPath on Linux)
set -euo pipefail
cd "$(dirname "$0")"

GAME_DIR="${GAME_DIR:-$HOME/.steam/steam/steamapps/common/Cultist Simulator}"
MODS_DIR="${MODS_DIR:-$HOME/.config/unity3d/Weather Factory/Cultist Simulator/mods}"
DEST="$MODS_DIR/Marginalia"

dotnet build src/Marginalia.csproj -c Release -p:GameDir="$GAME_DIR" -nologo -v quiet

mkdir -p "$DEST/dll"
# serapeum_catalogue_number.txt holds the Workshop id, so the in-game Upload updates the existing item.
cp mod/synopsis.json mod/cover.png mod/serapeum_catalogue_number.txt "$DEST/"
# Replaced, not overwritten: a running game reads its DLL lazily, and writing over it in
# place scrambles the game's copy (BadImageFormatException: Method has zero rva). Renamed
# into place, the running game keeps the old one and the next start loads the new one.
cp src/bin/Release/Marginalia.dll "$DEST/dll/Marginalia.dll.new"
mv -f "$DEST/dll/Marginalia.dll.new" "$DEST/dll/Marginalia.dll"
echo "Installed to $DEST"
