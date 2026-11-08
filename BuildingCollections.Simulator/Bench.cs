using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class Bench
    {
        const double Street = 20;

        public static void Run(int count, int seeds, bool polygon)
        {
            var scenarios = GameBlocks.All().Take(count).ToList();
            if (polygon)
            {
                RunCut(scenarios, seeds);
                return;
            }

            var blocks = new List<PlanBlock>();
            var existing = new List<Quad>();
            int columns = (int)Math.Ceiling(Math.Sqrt(scenarios.Count));
            double x = 0, y = 0, rowHeight = 0;
            int column = 0;
            var stripClock = new Stopwatch();
            foreach (var s in scenarios)
            {
                var all = s.Sides.SelectMany(p => p).ToList();
                double minX = all.Min(p => p.X), minY = all.Min(p => p.Y);
                double w = all.Max(p => p.X) - minX, h = all.Max(p => p.Y) - minY;
                var d = new Pt(x - minX, y - minY);
                var sides = s.Sides.Select(side => side.Select(p => p + d).ToList()).ToList();
                var moved = s.Existing.Select(q => new Quad(q.P[0] + d, q.P[1] + d, q.P[2] + d, q.P[3] + d)).ToList();
                stripClock.Start();
                var strips = VirtualLots.Build(sides, moved, frontage: s.Frontage, curveOverlap: 0.5);
                stripClock.Stop();
                blocks.AddRange(strips.Where(st => st.Grid.FreeCount() > 0).Select(st => new PlanBlock
                {
                    Frame = CellFrame.FromStrip(st), Grid = st.Grid, Strip = st, Priority = st.Width * 100, IsVirtual = true,
                    Label = $"{s.Name} side {st.SideIndex} segment {st.ChunkIndex + 1}",
                }));
                existing.AddRange(moved);
                rowHeight = Math.Max(rowHeight, h);
                x += w + Street;
                if (++column == columns)
                {
                    column = 0;
                    x = 0;
                    y += rowHeight + Street;
                    rowHeight = 0;
                }
            }

            var pts = blocks.Select(b => b.Frame.CellCenter(b.AreaX, b.AreaY)).ToList();
            double ox = -500, oy = -500, ex = pts.Max(p => p.X) + 500, ey = pts.Max(p => p.Y) + 500;
            var outline = new List<Pt> { new Pt(ox, oy), new Pt(ex, oy), new Pt(ex, ey), new Pt(ox, ey) };
            Console.WriteLine($"Bench: {scenarios.Count} game blocks, {blocks.Count} strips, {existing.Count} existing buildings; strips built in {stripClock.ElapsedMilliseconds} ms.");
            var total = new Stopwatch();
            for (int seed = 1; seed <= seeds; seed++)
            {
                total.Restart();
                var result = PreviewPlanner.Plan(new PreviewRequest
                {
                    Blocks = blocks, Existing = existing, Outline = outline, Plain = Simulation.Plain, Corners = Simulation.Corners, Overlap = 0.5f, Seed = seed,
                });
                Console.WriteLine($"  seed {seed}: {total.ElapsedMilliseconds} ms, {result.Lots.Count} buildings, {result.FrontFilled}/{result.FrontCells.Count} cells; {string.Join(", ", result.Timings)}");
            }
        }

        static void RunCut(List<Scenario> scenarios, int seeds)
        {
            var cuts = scenarios.Select(s => (scenario: s, clip: LeftHalf(s))).ToList();
            var stripClock = Stopwatch.StartNew();
            var plans = cuts.Select(c => (c.scenario, c.clip, blocks: VirtualLots.Build(c.scenario.Sides, c.scenario.Existing, frontage: c.scenario.Frontage, clip: c.clip, curveOverlap: 0.5)
                .Where(st => st.Grid.FreeCount() > 0)
                .Select(st => new PlanBlock
                {
                    Frame = CellFrame.FromStrip(st), Grid = st.Grid, Strip = st, Priority = st.Width * 100, IsVirtual = true,
                    Label = $"{c.scenario.Name} side {st.SideIndex} segment {st.ChunkIndex + 1}",
                }).ToList())).ToList();
            stripClock.Stop();
            Console.WriteLine($"Bench: {scenarios.Count} game blocks cut through the middle like a polygon selection, {plans.Sum(p => p.blocks.Count)} strips; strips built in {stripClock.ElapsedMilliseconds} ms.");
            var total = new Stopwatch();
            for (int seed = 1; seed <= seeds; seed++)
            {
                var phases = new Dictionary<string, long>();
                int buildings = 0;
                total.Restart();
                foreach (var (scenario, clip, blocks) in plans)
                {
                    var result = PreviewPlanner.Plan(new PreviewRequest
                    {
                        Blocks = blocks, Existing = scenario.Existing, Outline = clip, Plain = Simulation.Plain, Corners = Simulation.Corners, Overlap = 0.5f, Seed = seed,
                    });
                    buildings += result.Lots.Count;
                    foreach (var t in result.Timings)
                    {
                        var m = Regex.Match(t, @"^(.*?)(?: \(\d+ strips\))? (\d+) ms$");
                        phases[m.Groups[1].Value] = phases.GetValueOrDefault(m.Groups[1].Value) + long.Parse(m.Groups[2].Value);
                    }
                }

                Console.WriteLine($"  seed {seed}: {total.ElapsedMilliseconds} ms, {buildings} buildings; {string.Join(", ", phases.Select(p => $"{p.Key} {p.Value} ms"))}");
            }
        }

        static List<Pt> LeftHalf(Scenario s)
        {
            var all = s.Sides.SelectMany(p => p).ToList();
            double minX = all.Min(p => p.X) - 1, maxX = (all.Min(p => p.X) + all.Max(p => p.X)) / 2;
            double minY = all.Min(p => p.Y) - 1, maxY = all.Max(p => p.Y) + 1;
            return new List<Pt> { new Pt(minX, minY), new Pt(maxX, minY), new Pt(maxX, maxY), new Pt(minX, maxY) };
        }
    }
}
