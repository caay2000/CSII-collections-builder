#!/bin/sh
# Compila el arnés y hace capturas de las tres vistas. Uso: harness/run.sh <carpeta de salida>
set -e
cd "$(dirname "$0")/.."
npx webpack --config harness/webpack.harness.js 2>&1 | grep -E "compiled|ERROR" || true
node harness/build.js
CH=$(ls -d ~/.cache/ms-playwright/chromium-*/chrome-linux64/chrome | tail -1)
for t in "" "#builder" "#builder-demo" "#builder-picker" "#builder-mix" "#builder-trees"; do
  n=${t:-collections}; n=${n#\#}
  timeout 60 "$CH" --headless --no-sandbox --disable-gpu --hide-scrollbars --window-size=${W:-1500},${H:-900} --virtual-time-budget=4000 --screenshot="$1/shot_$n.png" "file://$PWD/harness/dist/harness.html$t" >/dev/null 2>&1
done
