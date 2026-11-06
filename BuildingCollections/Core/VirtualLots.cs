using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    public static class VirtualLots
    {
        public const double Cell = CellFrame.Size;

        public static List<LotStrip> Build(IReadOnlyList<List<Pt>> sides, IReadOnlyList<Quad> obstacles, int maxDepth = 6,
            double mergeAngleDeg = 20, double cornerAngleDeg = 30, double straightTolerance = 2.0, IReadOnlyList<bool> frontage = null,
            IReadOnlyList<Pt> clip = null, IList<string> notes = null, double curveTolerance = CurveTolerance, double curveOverlap = 0, int blockIndex = 0)
        {
            var strips = new List<LotStrip>();
            var outline = Outline(sides);
            if (outline.Count < 3)
            {
                return strips;
            }

            var merged = SideMerger.SplitTightBends(SideMerger.Merge(sides, frontage, mergeAngleDeg, cornerAngleDeg), notes);
            var mergedChunks = merged.Select(m => CurveSnapper.Chunks(m.Points, straightTolerance)).ToList();
            var settings = new StripSettings
            {
                Outline = outline, Obstacles = obstacles, Clip = clip, MaxDepth = maxDepth, BlockIndex = blockIndex,
                Notes = notes, CurveTolerance = curveTolerance, CurveOverlap = curveOverlap,
            };
            var sidePlans = new List<SidePlan>();
            for (int sideIndex = 0; sideIndex < merged.Count; sideIndex++)
            {
                if (!merged[sideIndex].Frontage)
                {
                    notes?.Add($"side {sideIndex}: no street (only closes the outline), length {Length(merged[sideIndex].Points):F1} m.");
                    continue;
                }

                sidePlans.Add(SideStripBuilder.For(merged, mergedChunks, sideIndex, settings).Plan());
            }

            strips.AddRange(AlignmentChooser.Choose(sidePlans, merged.Count, notes));
            return strips;
        }

        public static (List<Pt> pts, List<(int from, int to)> chunks) SnapChunks(List<Pt> src, double tolerance,
            double maxOverlap = 0, IList<string> notes = null, int sideIndex = -1) =>
            CurveSnapper.SnapChunks(src, tolerance, maxOverlap, notes, sideIndex);

        public static List<Pt> Outline(IReadOnlyList<List<Pt>> sides)
        {
            var poly = new List<Pt>();
            foreach (var s in sides)
            {
                foreach (var p in s)
                {
                    if (poly.Count == 0 || Dist(poly[poly.Count - 1], p) > 0.01)
                    {
                        poly.Add(p);
                    }
                }
            }

            if (poly.Count > 1 && Dist(poly[0], poly[poly.Count - 1]) <= 0.01)
            {
                poly.RemoveAt(poly.Count - 1);
            }

            return poly;
        }

        public const double CurveTolerance = CurveSnapper.CurveTolerance;

        public const double MaxBackWedge = 4.0;

        const double WedgeDepth = 16;

        public static bool BacksMayOverlap(LotStrip a, LotStrip b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            var (first, second) = a.ChunkIndex < b.ChunkIndex ? (a, b) : (b, a);
            if (!LotStrip.Chained(first, second))
            {
                return false;
            }

            double turn = Math.Atan2(Pt.Cross(first.T, second.T), Pt.Dot(first.T, second.T));
            return turn <= 0 || (turn < Math.PI / 2 && WedgeDepth * Math.Tan(turn) <= MaxBackWedge + 1e-9);
        }
    }
}
