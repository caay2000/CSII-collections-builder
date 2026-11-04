#!/usr/bin/env bash
# Compila la UI al directorio del mod y fuerza la recarga en caliente del juego (--uiDeveloperMode):
# el juego solo recarga la página si cambia un .js/.css/.html (no .mjs), y webpack no reescribe ficheros iguales.
set -euo pipefail
cd "$(dirname "$0")"
npx webpack
OUT="${BC_OUT:-/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II/Mods/BuildingCollections}"
sleep 2
touch "$OUT/BuildingCollections.css"
echo "UI desplegada en $OUT"
