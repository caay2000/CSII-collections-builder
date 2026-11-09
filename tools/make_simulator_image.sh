#!/bin/bash
set -e
if [ $# -ne 1 ]; then echo "usage: $0 <output jpg>" >&2; exit 2; fi
OUT="$1"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
T="$(mktemp -d)"
trap 'rm -rf "$T"' EXIT
F=/mnt/c/Windows/Fonts/segoeuib.ttf
FL=/mnt/c/Windows/Fonts/segoeui.ttf
SIM_SVG="$T/svg" dotnet run -c Release --project "$ROOT/BuildingCollections.Simulator" -- "$T/sim.html" 7 > /dev/null
mkdir -p "$T/tiles"
i=0
for f in Whitestone_with_Prospect_closed_loop_and_80_corner_ Cobblestone_long_curve_and_short_street marked_S_amplitude_25_m_wavelength_200_m_ \
  long_block_18633_11042_34_existing_in_the_game rounded_corner_R_40_m triangle_60_60_60_ parallelogram_60_120_ \
  side_curved_outwards_sagitta_16_m_ending_at_a_corner Whitestone_big_loop block_17817_10255_12_existing_in_the_game \
  two_curved_sides_in_a_row_sagitta_8_m_with_a_corner_between_them wavy_amplitude_15_m_wavelength_100_m_; do
  i=$((i + 1))
  convert -density 90 -background white "$T/svg/${f}_0.5.svg" -trim +repage -resize 372x262 -gravity center -background white \
    -extent 380x270 -bordercolor "#0b1218" -border 10 "$T/tiles/$(printf %02d $i).png"
done
montage "$T"/tiles/*.png -tile 4x3 -geometry +0+0 -background "#0b1218" "$T/grid.png"
convert -size 1600x100 xc:"#0b1218" -font $F -pointsize 34 -fill white -gravity north -annotate +0+12 "Tested in a simulator before every release" \
  -font $FL -pointsize 22 -fill "#cfe6ff" \
  -annotate +0+60 "86 block shapes (curves, S-bends, triangles, real city blocks) × 24 reshuffles × 4 overlaps: corners and sidewalk coverage must never get worse" \
  "$T/head.png"
convert "$T/head.png" "$T/grid.png" -append -quality 90 "$OUT"
