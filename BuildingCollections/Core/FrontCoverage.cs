using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public static class FrontCoverage
    {
        public static int CountFilled(IEnumerable<PlannedLot> lots, IReadOnlyList<Pt> frontCells)
        {
            var quads = lots.Select(p => p.Footprint(0.5)).ToList();
            return frontCells.Count(c => quads.Any(q => q.Contains(c)));
        }

        public static IEnumerable<string> Uncovered(IEnumerable<PlannedLot> lots, IEnumerable<Quad> existing, IEnumerable<PlanBlock> blocks)
        {
            var owned = lots.Select(p => (q: p.Footprint(0), strip: p.Owner?.Strip)).Concat(existing.Select(q => (q, strip: (LotStrip)null))).ToList();
            var quads = owned.Select(o => o.q).ToList();
            foreach (var st in blocks.Where(b => b.Strip != null).Select(b => b.Strip))
            {
                var runs = UncoveredRuns(quads, st, u => Neighbour(owned, st, u));
                yield return runs.Count == 0
                    ? $"  sidewalk side {st.SideIndex} segment {st.ChunkIndex + 1}/{st.ChunkCount} ({st.ChunkLength:F1} m): fully covered."
                    : $"  sidewalk side {st.SideIndex} segment {st.ChunkIndex + 1}/{st.ChunkCount} ({st.ChunkLength:F1} m): UNCOVERED {string.Join("; ", runs)}";
            }
        }

        static string Neighbour(List<(Quad q, LotStrip strip)> owned, LotStrip st, double u)
        {
            var at = AlongSidewalk(st, u, st.Inward + 1.5);
            var hit = owned.Where(o => Covered(new[] { o.q }, at)).ToList();
            if (hit.Count == 0)
            {
                return "nothing";
            }

            return hit.Any(o => SameSide(o.strip, st)) ? "this side" : hit.Any(o => o.strip != null) ? "another side" : "existing";
        }

        static bool SameSide(LotStrip a, LotStrip b) => a != null && a.BlockIndex == b.BlockIndex && a.SideIndex == b.SideIndex;

        static List<string> UncoveredRuns(List<Quad> quads, LotStrip st, Func<double, string> neighbour)
        {
            const double step = 0.25;
            double depth = st.Inward + 1.5;
            var runs = new List<string>();
            double runStart = -1;
            for (double u = 0; u <= st.ChunkLength + 1e-6; u += step)
            {
                bool covered = Covered(quads, AlongSidewalk(st, u, depth));
                if (!covered && runStart < 0)
                {
                    runStart = u;
                }

                if (runStart >= 0 && (covered || u + step > st.ChunkLength + 1e-6))
                {
                    double end = covered ? u : st.ChunkLength;
                    if (end - runStart >= 0.5)
                    {
                        string where = runStart < 0.01 ? "at the start" : end > st.ChunkLength - 0.01 ? "at the end" : "in the middle";
                        runs.Add($"{runStart:F1}-{end:F1} m ({end - runStart:F1} m, {where}){Surroundings(quads, st, depth, runStart, end, neighbour)}");
                    }

                    runStart = -1;
                }
            }

            return runs;
        }

        static Pt AlongSidewalk(LotStrip st, double u, double depth) => st.ChunkStart + st.T * u + st.N * depth;

        public static bool Covered(IEnumerable<Quad> quads, Pt q) =>
            quads.Any(o => q.X >= o.MinX && q.X <= o.MaxX && q.Y >= o.MinY && q.Y <= o.MaxY && Polygon.Contains(o.P, q));

        public const string BetweenOwn = "between two buildings of this side";

        static string Surroundings(List<Quad> quads, LotStrip st, double depth, double a, double b, Func<double, string> neighbour)
        {
            bool atStart = a < 0.01, atEnd = b > st.ChunkLength - 0.01;
            if (!atStart && !atEnd)
            {
                string before = neighbour(a - 0.25), after = neighbour(b);
                return before == "this side" && after == "this side"
                    ? $" [{BetweenOwn}]"
                    : $" [between two buildings: {before} before, {after} after]";
            }

            string Probe(double from, int dir)
            {
                for (double d = 0.25; d <= 12; d += 0.25)
                {
                    if (Covered(quads, AlongSidewalk(st, from + dir * d, depth)))
                    {
                        return $"{d:F2} m";
                    }
                }

                return "nothing within 12 m";
            }

            return $" [{(atStart ? $"before the start: {Probe(0, -1)}" : "touching before")}, {(atEnd ? $"after the end: {Probe(st.ChunkLength, +1)}" : "touching after")}]";
        }
    }
}
