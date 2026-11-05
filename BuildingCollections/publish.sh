#!/usr/bin/env bash
# Publica el mod en Paradox Mods con el ModPublisher del Modding Toolchain (el que instala el juego).
#   ./publish.sh Publish     primera vez: crea el mod (apunta el ModId que sale en el XML)
#   ./publish.sh NewVersion  sube una versión nueva (ModVersion y ChangeLog del XML)
#   ./publish.sh Update      solo actualiza la ficha (descripción, imágenes, visibilidad...)
# Inicia sesión solo con la cuenta de Paradox del juego. El contenido se compila limpio en una carpeta temporal de
# Windows (no se sube lo que haya en la carpeta Mods), junto con una copia del XML y sus imágenes.
set -euo pipefail
cmd="${1:-}"
case "$cmd" in Publish|NewVersion|Update) ;; *) echo "Uso: $0 Publish|NewVersion|Update" >&2; exit 2 ;; esac

here="$(cd "$(dirname "$0")" && pwd)"
publisher="/mnt/c/Program Files (x86)/Steam/steamapps/common/Cities Skylines II/Cities2_Data/Content/Game/.ModdingToolchain/ModPublisher/ModPublisher.exe"
stage="/mnt/c/Users/caay2/AppData/Local/Temp/BuildingCollections-publish"
[ -f "$publisher" ] || { echo "No encuentro ModPublisher.exe (instala el Modding Toolchain desde el juego)." >&2; exit 1; }

# Versión: la del csproj tiene que coincidir con ModVersion del XML.
csv=$(grep -oP '(?<=<Version>)[^<]+' "$here/BuildingCollections.csproj")
xmlv=$(grep -oP '(?<=<ModVersion Value=")[^"]+' "$here/Properties/PublishConfiguration.xml")
[ "$csv" = "$xmlv" ] || { echo "Versión distinta: csproj $csv, PublishConfiguration $xmlv." >&2; exit 1; }
if [ "$cmd" != Publish ] && grep -q '<ModId Value="" />' "$here/Properties/PublishConfiguration.xml"; then
    echo "Falta el ModId en Properties/PublishConfiguration.xml (sale al hacer Publish la primera vez)." >&2; exit 1
fi

rm -rf "$stage"
mkdir -p "$stage/content" "$stage/Properties"
if [ "$cmd" != Update ]; then
    dotnet build "$here" -c Release -p:Deploy=true -p:LocalModsPath="$stage/content"
    rm -f "$stage/content/"*.pdb
    BC_OUT="$stage/content" bash "$here/UI/deploy-ui.sh"
    echo "Contenido:"; ls -R "$stage/content"
fi
cp "$here/Properties/PublishConfiguration.xml" "$stage/"
cp -r "$here/Properties/Thumbnail.png" "$here/Properties/Screenshots" "$stage/Properties/"

cd "$stage"
# ModPublisher es de .NET 6; con solo .NET 8 instalado se le deja usar el 8.
DOTNET_ROLL_FORWARD=Major WSLENV=DOTNET_ROLL_FORWARD \
    "$publisher" "$cmd" "$(wslpath -w "$stage/PublishConfiguration.xml")" -c "$(wslpath -w "$stage/content")" -v
