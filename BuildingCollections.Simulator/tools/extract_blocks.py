import re, json, sys, math

DATA_SIDE = re.compile(r'\s+\[(?:data|datos)\] (?:sidewalk|acera) (\d+) (?:street|calle)=([01]): (.+)$')
DATA_EXISTING = re.compile(r'\s+\[(?:data|datos)\] (?:existing|existente): (.+)$')
DATA_PROPOSED = re.compile(r'\s+\[(?:data|datos)\] (?:proposed|propuesto) (\d+)x(\d+)( corner| esquina)? (.+?): ([-\d.,;]+)$')
OVERLAP = re.compile(r'\s+(?:overlap allowed|solape admitido) ([\d.]+) m')
CLICK = re.compile(r'(?:street block|manzana de calles).*(?:click at|clic en) \((-?[\d.]+), (-?[\d.]+)\)')
SELECTION = ('[selection] outline:', '[selección] contorno:')
PREVIEW = ('[preview] seed', '[vista previa] semilla')


def points(text):
    return [[float(v) for v in p.split(',')] for p in text.split(';') if p]


def legacy_sides(lines, i):
    sides = {}; nofront = {}; chunks = {}
    j = i + 1
    while j < len(lines):
        l = lines[j]
        m = re.match(r'\s+lado (\d+): \d+ puntos, largo [\d.]+ m, de \((-?[\d.]+),(-?[\d.]+)\) a \((-?[\d.]+),(-?[\d.]+)\)', l)
        if m: sides[int(m[1])] = ((float(m[2]), float(m[3])), (float(m[4]), float(m[5])))
        m = re.match(r'\s+lado (\d+): sin calle', l)
        if m: nofront[int(m[1])] = True
        m = re.match(r'\s+tira \d+x\d+ dir=\((-?[\d.]+),(-?[\d.]+)\)', l)
        if m and j + 1 < len(lines):
            d = (float(m[1]), float(m[2]))
            m2 = re.match(r'\s+lado (\d+) tramo (\d+)/\d+, acera de \((-?[\d.]+),(-?[\d.]+)\) largo ([\d.]+) m', lines[j + 1])
            if m2:
                chunks.setdefault(int(m2[1]), {})[int(m2[2])] = ((float(m2[3]), float(m2[4])), d, float(m2[5]))
        if '[INFO]' in l and j > i + 1 and not CLICK.search(l):
            break
        j += 1
    if not sides:
        return None
    n = max(list(sides) + list(nofront)) + 1
    out = []
    for s in range(n):
        if s in nofront or s not in sides:
            out.append({"frontage": False, "points": None}); continue
        a, b = sides[s]
        pts = [a]
        cs = chunks.get(s, {})
        for k in sorted(cs):
            pts.append(cs[k][0])
        if cs:
            st, d, ln = cs[max(cs)]
            pts.append((st[0] + d[0] * ln, st[1] + d[1] * ln))
        pts.append(b)
        clean = [pts[0]]
        for p in pts[1:]:
            if math.dist(p, clean[-1]) > 0.3: clean.append(p)
        changed = True
        while changed and len(clean) > 2:
            changed = False
            for q in range(1, len(clean) - 1):
                d1 = (clean[q][0] - clean[q-1][0], clean[q][1] - clean[q-1][1])
                d2 = (clean[q+1][0] - clean[q][0], clean[q+1][1] - clean[q][1])
                turn = math.degrees(math.atan2(d1[0]*d2[1] - d1[1]*d2[0], d1[0]*d2[0] + d1[1]*d2[1]))
                if abs(turn) > 120:
                    del clean[q]; changed = True; break
        out.append({"frontage": True, "points": clean})
    for s in range(n):
        if out[s]["points"] is None:
            prev = out[(s - 1) % n]["points"]; nxt = out[(s + 1) % n]["points"]
            out[s]["points"] = [prev[-1] if prev else (0, 0), nxt[0] if nxt else (0, 0)]
    return out


def main(log, outdir):
    lines = open(log, encoding='utf-8', errors='replace').read().splitlines()
    blocks = {}
    current = None
    pending = None
    preview = None
    for i, l in enumerate(lines):
        if any(t in l for t in SELECTION):
            pending = {"line": i + 1, "sides": {}, "existing": []}
            preview = None
            continue
        m = DATA_SIDE.match(l)
        if m and pending is not None:
            pending["sides"][int(m[1])] = {"frontage": m[2] == '1', "points": points(m[3])}
            continue
        m = DATA_EXISTING.match(l)
        if m:
            target = preview if preview is not None else pending
            if target is not None:
                target["existing"].append(points(m[1]))
            continue
        m = CLICK.search(l)
        if m and pending is not None:
            key = (m[1], m[2])
            if pending["sides"]:
                sides = [pending["sides"][k] for k in sorted(pending["sides"])]
                exact = True
            else:
                sides = legacy_sides(lines, pending["line"] - 1)
                exact = False
            if sides:
                block = blocks.setdefault(key, {"click": key, "proposals": {}})
                block.update({"sides": sides, "line": pending["line"], "exact": exact, "existing": pending["existing"]})
                current = block
            pending = None
            continue
        if any(t in l for t in PREVIEW) and current is not None:
            preview = {"overlap": None, "existing": [], "lots": []}
            continue
        if preview is not None:
            m = OVERLAP.match(l)
            if m:
                preview["overlap"] = m[1]
                continue
            m = DATA_PROPOSED.match(l)
            if m:
                preview["lots"].append({"w": int(m[1]), "d": int(m[2]), "corner": bool(m[3]), "name": m[4], "quad": points(m[5])})
                continue
            if '[INFO]' in l or '[WARN' in l:
                if preview["lots"] and preview["overlap"]:
                    current["proposals"][preview["overlap"]] = {"lots": preview["lots"], "existing": preview["existing"] or current["existing"]}
                preview = None
    if preview is not None and preview["lots"] and preview["overlap"] and current is not None:
        current["proposals"][preview["overlap"]] = {"lots": preview["lots"], "existing": preview["existing"] or current["existing"]}
    for k, b in sorted(blocks.items(), key=lambda kv: kv[1]["line"]):
        name = f"block_{float(k[0]):.0f}_{float(k[1]):.0f}"
        json.dump(b, open(f"{outdir}/{name}.json", "w"), indent=1)
        print(name, len(b["sides"]), "sides", sum(len(s["points"]) for s in b["sides"]), "points",
              "no street:", sum(not s["frontage"] for s in b["sides"]),
              "exact" if b["exact"] else "rebuilt",
              len(b["existing"]), "existing", "proposals:", ",".join(sorted(b["proposals"])) or "-")


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
