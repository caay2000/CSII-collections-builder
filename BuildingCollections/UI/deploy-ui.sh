#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
npx webpack
OUT="${BC_OUT:-/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II/Mods/BuildingCollections}"
sleep 2
touch "$OUT/BuildingCollections.css"
echo "UI deployed to $OUT"
