#!/bin/bash
set -e
if [ $# -ne 2 ]; then echo "usage: $0 <captures folder> <output folder>" >&2; exit 2; fi
IN="$1"; OUT="$2"; T="$OUT/tmp"; mkdir -p "$T"
trap 'rm -rf "$T"' EXIT
F=/mnt/c/Windows/Fonts/segoeuib.ttf
FL=/mnt/c/Windows/Fonts/segoeui.ttf
img(){ echo "$IN/Screenshot 2026-11-09 $1.png"; }
label(){ convert -background "#0f1b26e0" -fill white -font $F -pointsize ${3:-30} label:"$1" -bordercolor "#0f1b26e0" -border 18x10 "$2"; }
put(){ convert "$1" "$2" -geometry +${3}+${4} -composite "$1"; }

convert "$(img 001756)" -gravity center -crop 964x482+0+0 +repage -resize 800x400! "$T/thumb.png"
convert -size 800x400 gradient:"#00000000-#000000b0" "$T/shade.png"
convert "$T/thumb.png" "$T/shade.png" -composite "$T/thumb.png"
convert "$T/thumb.png" -font $F -pointsize 52 -fill white -gravity southwest -annotate +28+52 "Collections Builder" \
  -font $FL -pointsize 24 -fill "#cfe6ff" -annotate +30+22 "Fill whole city blocks from your own building collections" "$OUT/Thumbnail.png"

convert "$(img 002025)" -resize 1600x "$T/a.png"
convert "$(img 001750)" -resize x700 -bordercolor "#0b1218" -border 1 "$T/panel.png"
put "$T/a.png" "$T/panel.png" 20 29
label "Preview a block, reshuffle, build" "$T/l.png" 30; convert "$T/a.png" "$T/l.png" -gravity northeast -geometry +20+20 -composite "$T/a.png"
convert "$T/a.png" -quality 90 "$OUT/preview.jpg"

convert "$(img 001806)" -resize 800x -gravity center -crop 800x459+0+0 +repage "$T/b1.png"
convert "$(img 001756)" -resize 800x -gravity center -crop 800x459+0+0 +repage "$T/b2.png"
label "1. Click a block" "$T/l1.png" 28; put "$T/b1.png" "$T/l1.png" 16 16
label "2. Preview with your collection" "$T/l2.png" 28; put "$T/b2.png" "$T/l2.png" 16 16
convert "$T/b1.png" "$T/b2.png" +append -quality 90 "$OUT/select-and-preview.jpg"

convert "$(img 002047)" -resize 1600x -gravity center -crop 1600x600+0+0 +repage "$T/r1.png"
convert "$(img 002055)" -resize 1600x -gravity center -crop 1600x600+0+0 +repage "$T/r2.png"
convert "$T/r1.png" "$T/r2.png" -background "#0b1218" -splice 0x0 -append "$T/r.png"
label "Same block, two reshuffles: a new random mix every time" "$T/l.png" 30; put "$T/r.png" "$T/l.png" 16 16
convert "$T/r.png" -quality 90 "$OUT/reshuffle.jpg"

convert "$(img 002117)" -resize x900 "$T/t1.png"; convert "$(img 002129)" -resize x900 "$T/t2.png"
convert "$T/t1.png" "$T/t2.png" +append -resize 1600x "$T/t.png"
label "Low density with trees in the leftover space" "$T/l.png" 30; put "$T/t.png" "$T/l.png" 16 16
convert "$T/t.png" -quality 90 "$OUT/trees.jpg"

convert "$(img 001836)" -resize 1600x "$T/p.png"
label "Polygon: select any area along the streets" "$T/l.png" 28; put "$T/p.png" "$T/l.png" 16 16
convert "$T/p.png" -quality 90 "$OUT/polygon.jpg"

convert "$(img 002055)" -resize 1600x900^ -gravity center -extent 1600x900 -blur 0x12 -fill "#0b1218" -colorize 45% "$T/bg.png"
convert "$(img 001639)" -resize x780 "$T/c1.png"; convert "$(img 001716)" -resize x780 "$T/c2.png"
convert "$T/c1.png" "$T/c2.png" -background none +smush 40 "$T/cc.png"
convert "$T/bg.png" "$T/cc.png" -gravity center -geometry +0+30 -composite "$T/c.png"
label "Collections: your own lists of buildings, filtered by size and level" "$T/l.png" 28
convert "$T/c.png" "$T/l.png" -gravity north -geometry +0+18 -composite -quality 90 "$OUT/collections.jpg"
