#!/usr/bin/env python3
import argparse
import csv
import os
import re
import sys
from collections import defaultdict
from datetime import datetime, timedelta

MODS_DIR = "/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II/.cache/Mods/pdx_mods"
USER_ROOT = r"c:\users\caay2\appdata\locallow\colossal order\cities skylines ii"
GAME_ROOT = r"c:\program files (x86)\steam\steamapps\common\cities skylines ii"
TIME = re.compile(r"(\d{1,2}):(\d\d):(\d\d)[.,](\d+)\s*([AaPp]\.?\s?[Mm]\.?)?")
LENGTH = re.compile(r"Length:\s*([\d.,\s\u00a0]+)")


class ModNames:
    def __init__(self):
        self.cache = {}

    def name(self, folder):
        if folder in self.cache:
            return self.cache[folder]
        label = folder
        try:
            dlls = [f for f in os.listdir(os.path.join(MODS_DIR, folder))
                    if f.lower().endswith(".dll") and "_win_x86_64" not in f.lower() and f.lower() != "0harmony.dll"]
            if dlls:
                label = f"{folder} ({os.path.splitext(sorted(dlls)[0])[0]})"
        except OSError:
            pass
        self.cache[folder] = label
        return label


def group_of(path, names):
    low = path.lower()
    if low.startswith(USER_ROOT):
        parts = [p for p in path[len(USER_ROOT):].split("\\") if p]
        lowparts = [p.lower() for p in parts]
        if lowparts[:3] == [".cache", "mods", "pdx_mods"] and len(parts) > 3:
            return "mod " + names.name(parts[3])
        if lowparts[:1] == ["modsdata"] and len(parts) > 1:
            return "modsdata " + parts[1]
        if lowparts[:2] == [".cache", "mods"] and len(parts) > 2:
            return "cache " + parts[2]
        return "userdata " + (parts[0] if parts else "")
    if low.startswith(GAME_ROOT):
        parts = [p for p in path[len(GAME_ROOT):].split("\\") if p]
        return "game " + "/".join(parts[:3 if len(parts) > 3 else max(1, len(parts) - 1)])
    parts = [p for p in path.split("\\") if p]
    return "other " + "\\".join(parts[:3])


def parse_time(text):
    m = TIME.search(text)
    if not m:
        return None
    h, mi, s, frac, ampm = m.groups()
    h = int(h)
    if ampm:
        pm = ampm.lower().startswith("p")
        h = h % 12 + (12 if pm else 0)
    micro = int((frac + "000000")[:6])
    return datetime(2000, 1, 1, h, int(mi), int(s), micro)


def parse_number(text):
    text = (text or "").strip()
    if not text:
        return 0.0
    if "," in text and "." not in text:
        text = text.replace(",", ".")
    try:
        return float(text)
    except ValueError:
        return 0.0


def parse_length(detail):
    m = LENGTH.search(detail or "")
    if not m:
        return 0
    digits = re.sub(r"\D", "", m.group(1))
    return int(digits) if digits else 0


def column(header, *candidates):
    for c in candidates:
        if c in header:
            return header.index(c)
    return -1


class Group:
    def __init__(self):
        self.files = set()
        self.opens = 0
        self.dirs = 0
        self.reads = 0
        self.bytes = 0
        self.seconds = 0.0
        self.first = None
        self.last = None

    def touch(self, t):
        if self.first is None or t < self.first:
            self.first = t
        if self.last is None or t > self.last:
            self.last = t


def mb(n):
    return f"{n / 1048576:,.0f}"


def clock(t, start):
    return f"{t:%H:%M:%S} (+{(t - start).total_seconds() / 60:5.1f} min)"


def main():
    parser = argparse.ArgumentParser(description="Summarise a Process Monitor CSV of a Cities: Skylines II startup.")
    parser.add_argument("csv")
    parser.add_argument("--top", type=int, default=25)
    parser.add_argument("--gap", type=float, default=20, help="report periods longer than this many seconds with no file reads")
    parser.add_argument("--bucket", type=int, default=60, help="timeline bucket in seconds")
    args = parser.parse_args()

    csv.field_size_limit(sys.maxsize)
    names = ModNames()
    groups = defaultdict(Group)
    buckets = defaultdict(lambda: defaultdict(int))
    files = defaultdict(lambda: [0, 0.0])
    read_times = []
    start = end = None
    rows = 0
    day = 0
    previous = None

    with open(args.csv, encoding="utf-8-sig", errors="ignore", newline="") as fh:
        reader = csv.reader(fh)
        header = next(reader)
        c_time = column(header, "Time of Day", "Hora del día")
        c_op = column(header, "Operation", "Operación")
        c_path = column(header, "Path", "Ruta de acceso", "Ruta")
        c_result = column(header, "Result", "Resultado")
        c_detail = column(header, "Detail", "Detalle")
        c_duration = column(header, "Duration", "Duración")
        if min(c_time, c_op, c_path) < 0:
            sys.exit(f"Unexpected CSV header: {header}")
        if c_duration < 0:
            print("Warning: no Duration column, I/O time will be 0 (add it in Options > Select Columns).", file=sys.stderr)

        for row in reader:
            if len(row) <= max(c_time, c_op, c_path):
                continue
            t = parse_time(row[c_time])
            if t is None:
                continue
            if previous is not None and t + timedelta(days=day) < previous - timedelta(hours=1):
                day += 1
            t += timedelta(days=day)
            previous = t
            rows += 1
            start = t if start is None else start
            end = t
            op = row[c_op]
            path = row[c_path]
            result = row[c_result] if c_result >= 0 else "SUCCESS"
            g = groups[group_of(path, names)]
            g.touch(t)
            if op == "CreateFile":
                g.opens += 1
                if result == "SUCCESS":
                    g.files.add(path.lower())
            elif op == "QueryDirectory":
                g.dirs += 1
            elif op == "ReadFile":
                n = parse_length(row[c_detail]) if c_detail >= 0 else 0
                d = parse_number(row[c_duration]) if c_duration >= 0 else 0.0
                g.reads += 1
                g.files.add(path.lower())
                g.bytes += n
                g.seconds += d
                f = files[path]
                f[0] += n
                f[1] += d
                read_times.append(t)
                key = int((t - start).total_seconds() // args.bucket)
                buckets[key][group_of(path, names)] += n

    if start is None:
        sys.exit("No events found.")

    print(f"Events: {rows:,}   from {start:%H:%M:%S} to {end:%H:%M:%S}   ({(end - start).total_seconds() / 60:.1f} min)")
    print(f"Total read: {mb(sum(g.bytes for g in groups.values()))} MB in {sum(g.reads for g in groups.values()):,} reads, "
          f"{sum(g.seconds for g in groups.values()):,.0f} s of I/O (summed, threads overlap)\n")

    print(f"Top {args.top} groups by I/O time")
    print(f"{'group':<58} {'files':>7} {'dirs':>7} {'MB':>8} {'io s':>8}  first -> last")
    for name, g in sorted(groups.items(), key=lambda kv: (kv[1].seconds, kv[1].bytes), reverse=True)[:args.top]:
        print(f"{name[:58]:<58} {len(g.files):>7,} {g.dirs:>7,} {mb(g.bytes):>8} {g.seconds:>8.1f}  "
              f"+{(g.first - start).total_seconds() / 60:.1f} -> +{(g.last - start).total_seconds() / 60:.1f} min")

    if not any(g.opens or g.dirs for g in groups.values()):
        print("Note: no CreateFile or QueryDirectory events in the capture; 'files' counts files read, opens and dir queries are 0.\n", file=sys.stderr)

    print(f"\nTop {args.top} groups by files touched")
    for name, g in sorted(groups.items(), key=lambda kv: len(kv[1].files), reverse=True)[:args.top]:
        print(f"{name[:58]:<58} {len(g.files):>7,} files {g.opens:>9,} opens {g.dirs:>8,} dir queries")

    print(f"\nTop {args.top} files by I/O time")
    for path, (n, d) in sorted(files.items(), key=lambda kv: kv[1][1], reverse=True)[:args.top]:
        print(f"{d:8.1f} s {mb(n):>7} MB  {path}")

    print(f"\nTimeline ({args.bucket} s buckets): MB read and the busiest group")
    last_key = int((end - start).total_seconds() // args.bucket)
    for key in range(last_key + 1):
        b = buckets.get(key, {})
        total = sum(b.values())
        top = max(b.items(), key=lambda kv: kv[1])[0] if b else "-"
        t = start + timedelta(seconds=key * args.bucket)
        print(f"{clock(t, start)} {mb(total):>7} MB  {top}")

    print(f"\nPeriods longer than {args.gap:.0f} s with no file reads (CPU work or waiting)")
    read_times.sort()
    edges = [start] + read_times + [end]
    found = False
    for a, b in zip(edges, edges[1:]):
        if (b - a).total_seconds() >= args.gap:
            found = True
            print(f"{clock(a, start)} -> {b:%H:%M:%S}  {(b - a).total_seconds():6.0f} s")
    if not found:
        print("none")


if __name__ == "__main__":
    main()
