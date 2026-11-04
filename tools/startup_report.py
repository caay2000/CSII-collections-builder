#!/usr/bin/env python3
"""Informe de arranque de Cities: Skylines II a partir de los logs que el juego ya escribe.

No toca el juego ni los mods: solo lee Logs/*.log. Úsalo después de arrancar, y compara informes de
distintos días o de distintos playsets.

    python3 tools/startup_report.py [carpeta_de_logs] [--gap 30] [--top 10]
"""
import argparse
import glob
import os
import re
from collections import Counter
from datetime import datetime

DEFAULT = "/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II/Logs"
STAMP = re.compile(r"^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d),\d+\]")

# Marcas que el juego escribe en SceneFlow.log / FileSystem.log. Se muestran en orden cronológico.
MARKERS = [
    ("LoadAssetLibraryAsync", "Biblioteca de assets cargada"),
    ("Registered 'ParadoxMods' database", "Base de datos de Paradox Mods registrada"),
    ("Preload Mods at boot", "Inicio de precarga de mods"),
    ("OnActivePlaysetChanged performed", "Playset activado"),
    ("Creating ECS systems", "Creando sistemas ECS (aquí se cargan los mods)"),
    ("Boot completed", "Arranque completado"),
    ("Starting game from ''", "Menú principal disponible"),
    ("All custom assets have been loaded", "ExtraAssetsImporter termina de cargar sus assets"),
    ("Starting game from '", "Empieza a cargar una partida"),
    ("Loading completed", "Carga terminada (menú o partida)"),
]
# Líneas de mantenimiento periódico: no cuentan como actividad al buscar huecos.
NOISE = ("Quota on data source",)


def parse(path):
    """Devuelve [(datetime, línea)] de las líneas con marca de tiempo."""
    rows = []
    with open(path, encoding="utf-8", errors="ignore") as fh:
        for line in fh:
            m = STAMP.match(line)
            if m:
                rows.append((datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S"), line.rstrip()))
    return rows


def fmt(seconds):
    return f"{seconds / 60:5.1f} min" if seconds >= 60 else f"{seconds:5.0f} s  "


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("logs", nargs="?", default=DEFAULT)
    ap.add_argument("--gap", type=int, default=30, help="huecos mínimos (s) a listar")
    ap.add_argument("--top", type=int, default=10)
    a = ap.parse_args()

    files = {os.path.basename(f): parse(f) for f in glob.glob(os.path.join(a.logs, "*.log"))}
    files = {k: v for k, v in files.items() if v}
    scene = files.get("SceneFlow.log", [])
    fs = files.get("FileSystem.log", [])
    if not scene:
        raise SystemExit("No encuentro SceneFlow.log con marcas de tiempo.")

    t0 = min(v[0][0] for v in files.values())
    # Arranque más reciente: último "Boot completed" - se mira desde el último inicio de sesión.
    print(f"Primera marca de la sesión: {t0}\n")

    print("== Hitos (primera aparición de cada marca)")
    everything = sorted(x for rows in files.values() for x in rows)
    for key, label in MARKERS:
        hits = [(t, l) for t, l in everything if key in l]
        if key == "Starting game from '":
            hits = [h for h in hits if "from ''" not in h[1]]
        if not hits:
            print(f"  --------  {label}: no aparece")
            continue
        for t, _ in hits[:4]:
            print(f"  {t:%H:%M:%S}  ({fmt((t - t0).total_seconds())} desde el inicio)  {label}")

    print(f"\n== Mayores huecos (>= {a.gap} s) en FileSystem.log y SceneFlow.log")
    out = []
    for name in ("FileSystem.log", "SceneFlow.log"):
        rows = [x for x in files.get(name, []) if not any(n in x[1] for n in NOISE)]
        for (t1, l1), (t2, l2) in zip(rows, rows[1:]):
            d = (t2 - t1).total_seconds()
            if d >= a.gap:
                out.append((d, name, t1, l1, l2))
    for d, name, t1, l1, l2 in sorted(out, reverse=True)[: a.top]:
        print(f"  {fmt(d)}  {t1:%H:%M:%S}  {name}\n      antes:   {l1[24:150]}\n      después: {l2[24:150]}")

    print("\n== Mods con más actividad en los primeros 60 min (líneas de log y tamaño)")
    limit = t0.replace(microsecond=0)
    rows = []
    for name, v in files.items():
        early = [x for x in v if (x[0] - limit).total_seconds() <= 3600]
        if early:
            rows.append((len(early), name, os.path.getsize(os.path.join(a.logs, name)) // 1024,
                         (early[-1][0] - early[0][0]).total_seconds()))
    for n, name, kb, span in sorted(rows, reverse=True)[: a.top]:
        print(f"  {n:6d} líneas  {kb:6d} KB  activo {fmt(span)}  {name}")

    print("\n== Avisos que se repiten mucho")
    counters = Counter()
    for name in ("FileSystem.log", "SceneFlow.log"):
        for _, l in files.get(name, []):
            for key in ("Duplicate prefab ID", "missing reference", "No asset matching filter", "[ERROR]"):
                if key in l:
                    counters[(name, key)] += 1
    for (name, key), n in counters.most_common():
        print(f"  {n:6d}  {key}  ({name})")


if __name__ == "__main__":
    main()
