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
cp src/bin/Release/Marginalia.dll "$DEST/dll/"
echo "Installed to $DEST"
