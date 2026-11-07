using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class Bench
    {
        const double Street = 20;

        public static void Run(int count, int seeds)
        {
            var scenarios = GameBlocks.All().Take(count).ToList();
            var blocks = new List<PlanBlock>();
            var existing = new List<Quad>();
            int columns = (int)Math.Ceiling(Math.Sqrt(scenarios.Count));
            double x = 0, y = 0, rowHeight = 0;
            int column = 0;
            foreach (var s in scenarios)
            {
                var all = s.Sides.SelectMany(p => p).ToList();
                double minX = all.Min(p => p.X), minY = all.Min(p => p.Y);
                double w = all.Max(p => p.X) - minX, h = all.Max(p => p.Y) - minY;
                var d = new Pt(x - minX, y - minY);
                var sides = s.Sides.Select(side => side.Select(p => p + d).ToList()).ToList();
                var moved = s.Existing.Select(q => new Quad(q.P[0] + d, q.P[1] + d, q.P[2] + d, q.P[3] + d)).ToList();
                var strips = VirtualLots.Build(sides, moved, frontage: s.Frontage, curveOverlap: 0.5);
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
            Console.WriteLine($"Bench: {scenarios.Count} game blocks, {blocks.Count} strips, {existing.Count} existing buildings.");
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
    }
}
