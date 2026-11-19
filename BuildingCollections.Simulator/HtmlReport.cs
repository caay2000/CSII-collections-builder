using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class HtmlReport
    {
        static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        static string Label(float overlap) => overlap <= FootprintMask.StrictOverlap ? "Off" : $"{overlap:0.#} m";

        static string Key(float overlap) => overlap <= FootprintMask.StrictOverlap ? "off" : F(overlap).Replace('.', '_');

        public static string Render(IReadOnlyList<Run> runs, IReadOnlyList<float> overlaps, string version, int seed)
        {
            var sb = new StringBuilder();
            sb.Append(Head);
            sb.Append(SelectionCss(overlaps));
            foreach (var o in overlaps)
            {
                sb.Append($"<input type=\"radio\" class=\"ov\" name=\"ov\" id=\"ov-{Key(o)}\" value=\"{Key(o)}\"{(Key(o) == "0_5" ? " checked" : string.Empty)}>");
            }

            sb.Append($"<header class=\"top\"><div><h1>Block simulator</h1><p class=\"sub\">Collections Builder {Enc(version)} · seed {seed} · same <code>Core</code> code as the mod · dummy buildings 1×1 to 3×3 and corner buildings 1×2 / 2×2</p></div>");
            sb.Append("<div class=\"segwrap\"><span class=\"seglabel\">Overlap per joint</span><div class=\"seg\">");
            foreach (var o in overlaps)
            {
                sb.Append($"<label for=\"ov-{Key(o)}\" data-k=\"{Key(o)}\">{Label(o)}</label>");
            }

            sb.Append("</div></div></header>");
            sb.Append(SolvedCount(runs, overlaps));
            sb.Append(Legend);
            sb.Append(Summary(runs, overlaps));
            foreach (var group in runs.GroupBy(r => r.Scenario.Group))
            {
                sb.Append($"<h2>{Enc(group.Key)}</h2><div class=\"cards\">");
                foreach (var scenario in group.GroupBy(r => r.Scenario.Name))
                {
                    string solved = string.Join(" ", scenario.Where(Solved).Select(r => "ok-" + Key(r.Overlap)));
                    sb.Append($"<article class=\"card {solved}\"><h3>{Enc(scenario.Key)}<span class=\"badge\">solved</span></h3>");
                    sb.Append(Compare(scenario.ToList()));
                    foreach (var run in scenario)
                    {
                        sb.Append($"<div class=\"view\" data-k=\"{Key(run.Overlap)}\">{Svg(run)}{Stats(run)}</div>");
                    }

                    sb.Append("</article>");
                }

                sb.Append("</div>");
            }

            sb.Append(Script);
            return sb.ToString();
        }

        static string Enc(string s) => WebUtility.HtmlEncode(s);

        static string Summary(IReadOnlyList<Run> runs, IReadOnlyList<float> overlaps)
        {
            var sb = new StringBuilder("<section class=\"summary\"><div class=\"scroll\"><table><thead><tr><th>Escenario</th>");
            foreach (var o in overlaps)
            {
                sb.Append($"<th data-k=\"{Key(o)}\">{Label(o)}</th>");
            }

            sb.Append("</tr></thead><tbody>");
            foreach (var scenario in runs.GroupBy(r => r.Scenario.Name))
            {
                sb.Append($"<tr><td class=\"name {string.Join(" ", scenario.Where(Solved).Select(r => "ok-" + Key(r.Overlap)))}\">{Enc(scenario.Key)}</td>");
                foreach (var o in overlaps)
                {
                    var r = scenario.First(x => x.Overlap == o);
                    sb.Append($"<td data-k=\"{Key(o)}\" class=\"{Level(r)}\"><b>{r.FilledShare:P0}</b><span>{F(r.GapMeters)} m uncovered · max {F(r.MaxGap)}</span>{CornerSummary(r)}</td>");
                }

                sb.Append("</tr>");
            }

            sb.Append("</tbody></table></div></section>");
            return sb.ToString();
        }

        static bool Solved(Run r) =>
            r.Result.FrontCells.Count > 0 && r.MaxGap < 0.75 && r.CornersOk == r.Corners.Count;

        static string SolvedCount(IReadOnlyList<Run> runs, IReadOnlyList<float> overlaps)
        {
            int total = runs.Select(r => r.Scenario.Name).Distinct().Count();
            var sb = new StringBuilder("<p class=\"solved\">");
            foreach (var o in overlaps)
            {
                int n = runs.Count(r => r.Overlap == o && Solved(r));
                sb.Append($"<span data-k=\"{Key(o)}\"><i class=\"sw okbox\"></i><b>{n} of {total}</b> scenarios solved with {Label(o)} overlap: every corner has a corner building and no sidewalk gap of 0.75 m or more (not counting slivers ≤ 1.5 m from spread leftovers or the unavoidable wedges of non-square corners).</span>");
            }

            return sb.Append("</p>").ToString();
        }

        static string Compare(List<Run> runs)
        {
            var sb = new StringBuilder("<div class=\"cmp\">");
            foreach (var r in runs)
            {
                string corners = r.Corners.Count == 0 ? string.Empty : $"<i>esq {r.CornersOk}/{r.Corners.Count}</i>";
                sb.Append($"<label for=\"ov-{Key(r.Overlap)}\" data-k=\"{Key(r.Overlap)}\" class=\"{Level(r)}\"><em>{Label(r.Overlap)}</em><b>{r.Result.FrontFilled}/{r.Result.FrontCells.Count}</b><i>{F(r.GapMeters)} m uncovered</i>{corners}</label>");
            }

            return sb.Append("</div>").ToString();
        }

        static string SelectionCss(IReadOnlyList<float> overlaps)
        {
            var sb = new StringBuilder("<style>");
            foreach (var o in overlaps)
            {
                string k = Key(o);
                sb.Append($"body:has(#ov-{k}:checked) .view[data-k=\"{k}\"] {{ display: block; }}");
                sb.Append($"body:has(#ov-{k}:checked) .solved span[data-k=\"{k}\"] {{ display: inline-flex; }}");
                sb.Append($"body:has(#ov-{k}:checked) .card.ok-{k} {{ background: var(--okbg); border-color: var(--ok); }}");
                sb.Append($"body:has(#ov-{k}:checked) .card.ok-{k} .badge {{ display: inline-block; }}");
                sb.Append($"body:has(#ov-{k}:checked) td.name.ok-{k} {{ background: var(--okbg); box-shadow: inset 3px 0 var(--ok); font-weight: 600; }}");
                sb.Append($"body:has(#ov-{k}:checked) .seg label[data-k=\"{k}\"] {{ background: var(--accent); color: var(--bg); }}");
                sb.Append($"body:has(#ov-{k}:checked) .cmp label[data-k=\"{k}\"] {{ border-color: var(--accent); box-shadow: inset 0 0 0 1px var(--accent); background: var(--sel); }}");
                sb.Append($"body:has(#ov-{k}:checked) th[data-k=\"{k}\"], body:has(#ov-{k}:checked) td[data-k=\"{k}\"] {{ background: var(--sel); }}");
            }

            return sb.Append("</style>").ToString();
        }

        static string Level(Run r)
        {
            int gap = r.MaxGap < 0.75 ? 0 : r.MaxGap < 4 ? 1 : 2;
            int corner = r.Corners.Any(c => c.State == CornerState.Empty) ? 2 : r.Corners.Any(c => c.State == CornerState.Front) ? 1 : 0;
            return new[] { "ok", "warn", "bad" }[Math.Max(gap, corner)];
        }

        static string CornerSummary(Run r) =>
            r.Corners.Count == 0 ? string.Empty : $"<span> · corners {r.CornersOk}/{r.Corners.Count}</span>";

        static string CornerStats(Run r)
        {
            if (r.Corners.Count == 0)
            {
                return string.Empty;
            }

            var bad = r.Corners.Where(c => c.State != CornerState.Corner)
                .Select(c => $"{F(c.Angle)}° {(c.State == CornerState.Front ? "regular building" : "empty")}").ToList();
            return $" · corners with a corner building <b>{r.CornersOk}/{r.Corners.Count}</b>{(bad.Count > 0 ? $" (failing: {string.Join(", ", bad)})" : string.Empty)}";
        }

        static string LogBlock(Run r) =>
            $"<details class=\"log\"><summary>Planner log</summary><pre>{Enc(string.Join("\n", r.Result.Log))}</pre></details>";

        static string Stats(Run r) =>
            $"<p class=\"stats\"><b>{r.Result.FrontFilled}/{r.Result.FrontCells.Count}</b> street cells ({r.FilledShare:P0}) · <b>{r.Result.Lots.Count}</b> buildings · " +
            $"<b>{r.RealGaps.Count()}</b> gaps, {F(r.GapMeters)} m (max {F(r.MaxGap)} m){(r.Slivers.Any() ? $" · {r.Slivers.Count()} slivers ≤ 1.5 m" : string.Empty)}{(r.WedgeMeters > 0 ? $" · unavoidable wedges {F(r.WedgeMeters)} m" : string.Empty)}{CornerStats(r)}</p>{LogBlock(r)}";

        static readonly Dictionary<string, string> InlineStyles = new Dictionary<string, string>
        {
            ["road"] = "fill:none;stroke:#999;stroke-width:7",
            ["ground"] = "fill:#eee",
            ["cell"] = "fill:none;stroke:#bbb;stroke-width:0.35",
            ["cell off"] = "fill:none;stroke:#ddd;stroke-width:0.35",
            ["lot"] = "fill:#6a9fd8;fill-opacity:0.6;stroke:#36c;stroke-width:0.4",
            ["lot corner"] = "fill:#e8a33a;fill-opacity:0.7;stroke:#b70;stroke-width:0.4",
            ["face"] = "fill:none;stroke:#222;stroke-width:1.3",
            ["face corner"] = "fill:none;stroke:#222;stroke-width:1.8",
            ["gap"] = "stroke:#d22;stroke-width:2.2",
            ["gap sliver"] = "stroke:#e90;stroke-width:1.2",
            ["gap wedge"] = "stroke:#a0f;stroke-width:1.6",
            ["cmark ok"] = "fill:#2a2",
            ["cmark warn"] = "fill:#e90",
            ["cmark bad"] = "fill:#d22",
        };

        public static string StandaloneSvg(Run r)
        {
            var svg = Svg(r).Replace("role=\"img\"", "xmlns=\"http://www.w3.org/2000/svg\" width=\"1400\" height=\"1000\" role=\"img\"");
            return Regex.Replace(svg, "class=\"([^\"]+)\"", m => InlineStyles.TryGetValue(m.Groups[1].Value, out var style) ? $"style=\"{style}\"" : m.Value);
        }

        static string Svg(Run r)
        {
            double minX = r.Outline.Min(p => p.X) - 10, maxX = r.Outline.Max(p => p.X) + 10;
            double minY = r.Outline.Min(p => p.Y) - 10, maxY = r.Outline.Max(p => p.Y) + 10;
            var sb = new StringBuilder();
            sb.Append($"<svg viewBox=\"{F(minX)} {F(-maxY)} {F(maxX - minX)} {F(maxY - minY)}\" role=\"img\" aria-label=\"{Enc(r.Scenario.Name)}, overlap {Label(r.Overlap)}\">");
            sb.Append($"<polygon class=\"road\" points=\"{Points(r.Outline)}\"/>");
            sb.Append($"<polygon class=\"ground\" points=\"{Points(r.Outline)}\"/>");
            foreach (var st in r.Strips)
            {
                var frame = CellFrame.FromStrip(st);
                for (int x = 0; x < st.Width; x++)
                {
                    for (int y = 0; y < Math.Min(3, st.Depth); y++)
                    {
                        sb.Append($"<polygon class=\"{(st.Grid.Free[x, y] ? "cell" : "cell off")}\" points=\"{Points(frame.Cell(x, y, 0.4).P)}\"/>");
                    }
                }
            }

            foreach (var lot in r.Result.Lots)
            {
                var q = lot.Footprint(0);
                sb.Append($"<polygon class=\"{(lot.Corner ? "lot corner" : "lot")}\" points=\"{Points(q.P)}\"><title>{Enc(lot.Building.PrefabName)}{(lot.Shift != 0 ? $" · corrido {lot.Shift:0.00} m" : string.Empty)}</title></polygon>");
                sb.Append($"<polyline class=\"{(lot.Corner ? "face corner" : "face")}\" points=\"{Points(Facade(lot, q))}\"/>");
            }

            foreach (var c in r.Corners)
            {
                string state = c.State == CornerState.Corner ? "ok" : c.State == CornerState.Front ? "warn" : "bad";
                string text = c.State == CornerState.Corner ? "corner building" : c.State == CornerState.Front ? "regular building, no corner building" : "empty";
                sb.Append($"<circle class=\"cmark {state}\" cx=\"{F(c.At.X)}\" cy=\"{F(-c.At.Y)}\" r=\"2.6\"><title>{F(c.Angle)}° corner: {text}</title></circle>");
            }

            foreach (var g in r.Gaps)
            {
                sb.Append($"<line class=\"{(g.Wedge ? "gap wedge" : g.Sliver ? "gap sliver" : "gap")}\" x1=\"{F(g.From.X)}\" y1=\"{F(-g.From.Y)}\" x2=\"{F(g.To.X)}\" y2=\"{F(-g.To.Y)}\"><title>{(g.Wedge ? "unavoidable corner wedge" : g.Sliver ? "sliver" : "gap")} of {F(g.Length)} m</title></line>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        static IEnumerable<Pt> Facade(PlannedLot lot, Quad q)
        {
            bool left = lot.Corner && (lot.Building.Corner & CornerKind.Left) != 0;
            bool right = lot.Corner && (lot.Building.Corner & CornerKind.Right) != 0;
            var pts = new List<Pt>();
            if (left)
            {
                pts.Add(q.P[3]);
            }

            pts.Add(q.P[0]);
            pts.Add(q.P[1]);
            if (right)
            {
                pts.Add(q.P[2]);
            }

            return pts.Select(p => Inset(p, q, 1.8));
        }

        static Pt Inset(Pt p, Quad q, double k)
        {
            var c = new Pt(q.P.Average(v => v.X), q.P.Average(v => v.Y));
            var d = c - p;
            double len = Math.Sqrt(Pt.Dot(d, d));
            return len < 1e-6 ? p : p + d * (k / len);
        }

        static string Points(IEnumerable<Pt> pts) => string.Join(" ", pts.Select(p => $"{F(p.X)},{F(-p.Y)}"));

        const string Legend =
            "<ul class=\"legend\"><li><i class=\"sw lot\"></i>edificio</li><li><i class=\"sw corner\"></i>esquinero</li>" +
            "<li><i class=\"sw cell\"></i>strip cell</li><li><i class=\"sw gapsw\"></i>sidewalk gap (≥ 0.5 m)</li><li><i class=\"sw sliversw\"></i>sliver ≤ 1.5 m (spread leftover)</li><li><i class=\"sw wedgesw\"></i>unavoidable wedge at a non-square corner</li>" +
            "<li><i class=\"sw roadsw\"></i>street</li><li><i class=\"sw facesw\"></i>facade (L-shaped towards both streets on corner buildings)</li>" +
            "<li><i class=\"sw dot ok\"></i>corner with a corner building</li><li><i class=\"sw dot warn\"></i>corner with a regular building</li><li><i class=\"sw dot bad\"></i>empty corner</li></ul>";

        const string Head = @"<title>Block simulator</title>
<meta name=""claude:viewport-fit-sides"" content=""cover"">
<link rel=""preconnect"" href=""https://fonts.googleapis.com"">
<link rel=""stylesheet"" href=""https://fonts.googleapis.com/css2?family=Barlow:wght@400;600;700&family=IBM+Plex+Mono:wght@400;600&display=swap"">
<style>
:root {
  --bg: #eef1ec; --panel: #ffffff; --fg: #1d2421; --muted: #5b6660; --line: #d5dbd4;
  --road: #4b5551; --ground: #dfe8d6; --cell: #9fb39a; --lot: #3d7ab8; --corner: #d9822b; --gap: #d23b3b; --face: #ffffff;
  --ok: #2f8a4c; --sel: #e3eef7; --okbg: #e4f3e8; --warn: #b7791f; --bad: #c0392b; --accent: #2f6f9f;
  --sans: ""Barlow"", ""Segoe UI"", system-ui, sans-serif; --mono: ""IBM Plex Mono"", ui-monospace, monospace;
}
@media (prefers-color-scheme: dark) { :root:not([data-theme=""light""]) {
  --bg: #141917; --panel: #1c2320; --fg: #e6ece8; --muted: #9aa6a0; --line: #2d3733;
  --road: #59635e; --ground: #24302a; --cell: #4c5f52; --lot: #5a9ad6; --corner: #e89a4a; --gap: #ff6b6b; --face: #ffffff;
  --ok: #5cc27e; --sel: #1f3342; --okbg: #1c3325; --warn: #e0a84a; --bad: #ff7b6e; --accent: #7cb8e6; color-scheme: dark } }
:root[data-theme=""dark""] {
  --bg: #141917; --panel: #1c2320; --fg: #e6ece8; --muted: #9aa6a0; --line: #2d3733;
  --road: #59635e; --ground: #24302a; --cell: #4c5f52; --lot: #5a9ad6; --corner: #e89a4a; --gap: #ff6b6b; --face: #ffffff;
  --ok: #5cc27e; --sel: #1f3342; --okbg: #1c3325; --warn: #e0a84a; --bad: #ff7b6e; --accent: #7cb8e6; color-scheme: dark }
body { background: var(--bg); color: var(--fg); font-family: var(--sans); font-size: 15px; line-height: 1.45;
  padding-block: 20px 40px; padding-left: max(16px, env(safe-area-inset-left, 0px)); padding-right: max(16px, env(safe-area-inset-right, 0px)); }
.ov { position: fixed; top: 0; left: 0; width: 1px; height: 1px; opacity: 0; pointer-events: none; }
.top { position: sticky; top: 0; z-index: 10; background: var(--bg); padding: 10px 0; border-bottom: 1px solid var(--line); display: flex; flex-wrap: wrap; gap: 12px 24px; align-items: end; justify-content: space-between; max-width: 1400px; margin: 0 auto 12px; }
h1 { font-size: 28px; margin: 0; letter-spacing: 0.2px; text-wrap: balance; }
.sub { margin: 4px 0 0; color: var(--muted); max-width: 70ch; }
code { font-family: var(--mono); font-size: 13px; }
h2 { max-width: 1400px; margin: 28px auto 10px; font-size: 13px; text-transform: uppercase; letter-spacing: 1.2px; color: var(--muted); }
.seg { display: inline-flex; border: 1px solid var(--line); border-radius: 6px; overflow: hidden; background: var(--panel); }
.segwrap { display: flex; align-items: center; gap: 10px; }
.seglabel { font-size: 13px; color: var(--muted); }
.seg label { font: 600 14px var(--mono); padding: 8px 14px; color: var(--fg); cursor: pointer; user-select: none; }
.seg label + label { border-left: 1px solid var(--line); }
body:has(.ov:focus-visible) .seg { outline: 2px solid var(--accent); outline-offset: 2px; }
.cmp { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 6px; margin: 0 0 8px; }
.cmp label { border: 1px solid var(--line); border-radius: 6px; padding: 5px 7px; cursor: pointer; display: flex; flex-direction: column; font-size: 11px; color: var(--muted); min-width: 0; }
.cmp label.ok { border-top: 3px solid var(--ok); } .cmp label.warn { border-top: 3px solid var(--warn); } .cmp label.bad { border-top: 3px solid var(--bad); }
.cmp em { font-style: normal; font: 600 12px var(--mono); color: var(--fg); }
.cmp b { font: 600 13px var(--mono); color: var(--fg); font-variant-numeric: tabular-nums; }
.cmp i { font-style: normal; font-family: var(--mono); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.legend { list-style: none; display: flex; flex-wrap: wrap; gap: 6px 18px; padding: 0; margin: 0 auto 16px; max-width: 1400px; color: var(--muted); font-size: 13px; }
.legend li { display: flex; align-items: center; gap: 6px; }
.sw { width: 14px; height: 14px; border-radius: 2px; display: inline-block; }
.sw.lot { background: var(--lot); } .sw.corner { background: var(--corner); } .sw.cell { border: 1.5px solid var(--cell); }
.sw.gapsw { height: 4px; background: var(--gap); } .sw.roadsw { background: var(--road); }
.summary { max-width: 1400px; margin: 0 auto; background: var(--panel); border: 1px solid var(--line); border-radius: 8px; }
.scroll { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-size: 13px; }
th, td { padding: 7px 10px; text-align: left; border-bottom: 1px solid var(--line); vertical-align: top; }
th { font-weight: 600; color: var(--muted); white-space: nowrap; }
td b { font: 600 14px var(--mono); display: block; font-variant-numeric: tabular-nums; }
td span { color: var(--muted); font-family: var(--mono); font-size: 12px; white-space: nowrap; }
td.ok { box-shadow: inset 3px 0 var(--ok); } td.warn { box-shadow: inset 3px 0 var(--warn); } td.bad { box-shadow: inset 3px 0 var(--bad); }
.cards { max-width: 1400px; margin: 0 auto; display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 420px), 1fr)); gap: 14px; }
.card { background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 12px; min-width: 0; }
.badge { display: none; margin-left: 8px; padding: 1px 8px; border-radius: 999px; background: var(--ok); color: var(--panel); font-size: 12px; font-weight: 600; vertical-align: 2px; }
.solved { max-width: 1400px; margin: 0 auto 10px; font-size: 13px; color: var(--muted); }
.solved span { display: none; align-items: center; gap: 6px; }
.solved b { color: var(--fg); }
.sw.okbox { background: var(--okbg); border: 1.5px solid var(--ok); }
.card h3 { margin: 0 0 8px; font-size: 16px; }
.view { display: none; }
svg { width: 100%; height: auto; max-height: 420px; display: block; }
.road { fill: none; stroke: var(--road); stroke-width: 7; stroke-linejoin: round; }
.ground { fill: var(--ground); stroke: none; }
.cell { fill: none; stroke: var(--cell); stroke-width: 0.35; }
.cell.off { stroke-dasharray: 1 1; opacity: 0.6; }
.lot { fill: var(--lot); fill-opacity: 0.6; stroke: var(--lot); stroke-width: 0.4; }
.lot.corner { fill: var(--corner); stroke: var(--corner); }
.gap { stroke: var(--gap); stroke-width: 2.2; stroke-linecap: round; }
.gap.sliver { stroke: var(--warn); stroke-width: 1.2; }
.sw.sliversw { height: 2px; background: var(--warn); }
.gap.wedge { stroke: var(--muted); stroke-dasharray: 1.5 1.5; stroke-linecap: butt; }
.sw.wedgesw { height: 0; border-top: 3px dashed var(--muted); border-radius: 0; }
.face { fill: none; stroke: var(--face); stroke-width: 1.3; stroke-linecap: round; stroke-linejoin: round; }
.face.corner { stroke-width: 1.8; }
.cmark { stroke: var(--panel); stroke-width: 0.8; }
.cmark.ok { fill: var(--ok); } .cmark.warn { fill: var(--warn); } .cmark.bad { fill: var(--bad); }
.sw.facesw { height: 4px; background: var(--face); box-shadow: 0 0 0 1px var(--lot); } .sw.dot { border-radius: 50%; } .sw.dot.ok { background: var(--ok); } .sw.dot.warn { background: var(--warn); } .sw.dot.bad { background: var(--bad); }
.log { margin-top: 6px; font-size: 12px; color: var(--muted); }
.log summary { cursor: pointer; }
.log pre { font-family: var(--mono); font-size: 11px; white-space: pre-wrap; word-break: break-word; max-height: 260px; overflow: auto; background: var(--bg); padding: 8px; border-radius: 6px; }
.stats { margin: 8px 0 0; font-size: 13px; color: var(--muted); }
.stats b { color: var(--fg); font-family: var(--mono); font-variant-numeric: tabular-nums; }
</style>";

        const string Script = @"<script>
(function () {
  var radios = Array.prototype.slice.call(document.querySelectorAll('input.ov'));
  function pick(k) {
    radios.forEach(function (r) { r.checked = r.value === k; });
  }
  var anchor = null, anchorTop = 0;
  document.addEventListener('pointerdown', function (ev) {
    var label = ev.target.closest && ev.target.closest('label[for^=""ov-""]');
    if (!label) { return; }
    anchor = label.closest('.card') || label;
    anchorTop = anchor.getBoundingClientRect().top;
  }, true);
  radios.forEach(function (r) {
    r.addEventListener('change', function () {
      try { localStorage.setItem('sim-overlap', r.value); } catch (e) {}
      if (anchor) {
        var a = anchor, t = anchorTop;
        anchor = null;
        requestAnimationFrame(function () { window.scrollBy(0, a.getBoundingClientRect().top - t); });
      }
    });
  });
  var wanted = location.hash.replace('#', '');
  if (!wanted) { try { wanted = localStorage.getItem('sim-overlap'); } catch (e) {} }
  if (radios.some(function (r) { return r.value === wanted; })) { pick(wanted); }
})();
</script>";
    }
}
