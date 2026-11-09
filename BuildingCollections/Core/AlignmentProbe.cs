using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildingCollections.Core
{
    internal sealed class AlignmentProbe
    {
        const int Seeds = 2;
        const double MinGain = 2;

        static readonly IReadOnlyList<BuildingInfo> Plain = new[]
        {
            Lot(1, 2), Lot(2, 2), Lot(3, 2), Lot(2, 3), Lot(3, 3),
        };

        static readonly IReadOnlyList<BuildingInfo> Corners = new[]
        {
            Lot(1, 2, CornerKind.Front | CornerKind.Left), Lot(1, 2, CornerKind.Front | CornerKind.Right),
            Lot(2, 2, CornerKind.Front | CornerKind.Left), Lot(2, 2, CornerKind.Front | CornerKind.Right),
        };

        static readonly IReadOnlyList<BuildingInfo> CornersWithSmall = Corners
            .Concat(new[] { Lot(1, 1, CornerKind.Front | CornerKind.Left), Lot(1, 1, CornerKind.Front | CornerKind.Right) }).ToList();

        static readonly IReadOnlyList<IReadOnlyList<BuildingInfo>> CornerCatalogs = new[] { Corners, CornersWithSmall };

        readonly IReadOnlyList<List<Pt>> sides;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<Quad> existing;
        readonly float overlap;

        public AlignmentProbe(IReadOnlyList<List<Pt>> sides, IReadOnlyList<Pt> outline, IReadOnlyList<Quad> existing, double curveOverlap)
        {
            this.sides = sides;
            this.outline = outline;
            this.existing = existing ?? new List<Quad>();
            overlap = curveOverlap > FootprintMask.StrictOverlap ? (float)curveOverlap : FootprintMask.StrictOverlap;
        }

        public int Best(List<List<LotStrip>> candidates)
        {
            var current = CornerCatalogs.Select(c => Measure(candidates[0], c)).ToList();
            var seen = new HashSet<string> { Signature(candidates[0]) };
            int best = 0;
            double bestMeters = current.Sum(o => o.Meters) - MinGain * Seeds * CornerCatalogs.Count;
            for (int i = 1; i < candidates.Count; i++)
            {
                if (!seen.Add(Signature(candidates[i])))
                {
                    continue;
                }

                double meters = 0;
                bool acceptable = true;
                for (int c = 0; c < CornerCatalogs.Count && acceptable; c++)
                {
                    var outcome = Measure(candidates[i], CornerCatalogs[c]);
                    acceptable = outcome.NoWorseThan(current[c]);
                    meters += outcome.Meters;
                }

                if (acceptable && meters < bestMeters)
                {
                    best = i;
                    bestMeters = meters;
                }
            }

            return best;
        }

        static string Signature(List<LotStrip> strips) => string.Join("|", strips.Select(s =>
        {
            var free = new StringBuilder();
            for (int x = 0; x < s.Grid.Width; x++)
            {
                for (int y = 0; y < s.Grid.Depth; y++)
                {
                    free.Append(s.Grid.Free[x, y] ? '1' : '0');
                }
            }

            return $"{s.SideIndex}:{s.ChunkIndex}:{s.P0.X:F3},{s.P0.Y:F3}:{s.Width}:{s.Grid.LeftCorner}{s.Grid.RightCorner}:{free}";
        }));

        Outcome Measure(List<LotStrip> strips, IReadOnlyList<BuildingInfo> corners)
        {
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
            }).ToList();
            var total = new Outcome();
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var result = PreviewPlanner.Plan(new PreviewRequest
                {
                    Blocks = blocks, Existing = existing, Outline = outline, Plain = Plain, Corners = corners, Overlap = overlap, Seed = seed,
                });
                total.Meters += SidewalkGaps.Find(strips, result.Lots).Where(g => g.Length > EndGapCloser.MaxSpreadSliver).Sum(g => g.Length);
                total.BetweenOwn += FrontCoverage.GapsBetweenOwn(result.Lots, existing, blocks);
                total.EmptyCells += result.FrontCells.Count - result.FrontFilled;
                total.MissedCorners += CornerCheck.Evaluate(sides, result.Lots).Count(c => c.State != CornerState.Corner);
            }

            return total;
        }

        sealed class Outcome
        {
            public double Meters;
            public int BetweenOwn;
            public int EmptyCells;
            public int MissedCorners;

            public bool NoWorseThan(Outcome other) =>
                MissedCorners <= other.MissedCorners && EmptyCells <= other.EmptyCells && BetweenOwn <= other.BetweenOwn
                && Meters <= other.Meters;
        }

        static BuildingInfo Lot(int width, int depth, CornerKind corner = CornerKind.Front) =>
            new BuildingInfo { PrefabName = $"probe {width}x{depth} {corner}", LotWidth = width, LotDepth = depth, Corner = corner };
    }
}
