using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class OpenCornerPairs
    {
        public const double MinAngle = 100;
        public const double MaxAngle = 150;
        const double MaxCornerGap = 16;
        const double MaxCurbOverhang = 3;
        const double SharedCorner = 0.5;
        const double MinStraight = 16;
        const double MinStraightBesideCurve = 48;
        const double InsideInset = 0.1;
        const double GapTolerance = 0.05;
        const double JointMargin = 0.01;
        const double ClosedGap = 0.5;
        const int MaxFillers = 2;

        readonly List<PlannedLot> lots;
        readonly IReadOnlyList<PlanBlock> blocks;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<BuildingInfo> catalog;
        readonly IReadOnlyList<Pt> frontCells;
        readonly float overlap;
        readonly IList<string> log;
        readonly HashSet<PlannedLot> paired = new HashSet<PlannedLot>();

        OpenCornerPairs(List<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> catalog, IReadOnlyList<Pt> frontCells, float overlap, IList<string> log)
        {
            this.lots = lots;
            this.blocks = blocks.Where(b => b.Strip != null).ToList();
            this.existing = existing;
            this.outline = outline;
            this.catalog = catalog;
            this.frontCells = frontCells;
            this.overlap = overlap;
            this.log = log;
        }

        public static int Apply(List<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> catalog, IReadOnlyList<Pt> frontCells, float overlap, IList<string> log) =>
            new OpenCornerPairs(lots, blocks, existing, outline, catalog, frontCells, overlap, log).Run();

        int Run()
        {
            int placed = 0;
            foreach (var (first, second, angle) in Corners())
            {
                if (TryPair(first, second, angle))
                {
                    placed++;
                }
            }

            return placed;
        }

        IEnumerable<(PlanBlock First, PlanBlock Second, double Angle)> Corners()
        {
            foreach (var first in blocks.Where(b => b.Strip.ChunkIndex == b.Strip.ChunkCount - 1 && IsStraightEnough(b.Strip)))
            {
                var second = blocks
                    .Where(b => b.Strip.ChunkIndex == 0 && IsStraightEnough(b.Strip) && b.Strip.BlockIndex == first.Strip.BlockIndex && b.Strip.SideIndex != first.Strip.SideIndex)
                    .Select(b => (block: b, gap: Distance(first.Strip.ChunkEnd, b.Strip.ChunkStart)))
                    .Where(c => c.gap <= MaxCornerGap)
                    .OrderBy(c => c.gap)
                    .Select(c => c.block)
                    .FirstOrDefault();
                if (second == null)
                {
                    continue;
                }

                double turn = Math.Atan2(Pt.Cross(first.Strip.T, second.Strip.T), Pt.Dot(first.Strip.T, second.Strip.T)) * 180 / Math.PI;
                double angle = 180 - turn;
                if (turn > 0 && angle >= MinAngle && angle <= MaxAngle)
                {
                    yield return (first, second, angle);
                }
            }
        }

        static bool IsStraightEnough(LotStrip strip) =>
            strip.ChunkLength >= (strip.ChunkCount > 1 ? MinStraightBesideCurve : MinStraight);

        bool TryPair(PlanBlock first, PlanBlock second, double angle)
        {
            if (!TryCorner(first, second, out var corner))
            {
                return false;
            }

            var strips = new[] { first.Strip, second.Strip };
            double before = Uncovered(strips, lots);
            int gapsBefore = FrontCoverage.GapsBetweenOwn(lots, existing, new[] { first, second });
            int filledBefore = FrontCoverage.CountFilled(lots, frontCells);
            var best = catalog
                .Select(model => Candidate(model, first, second, corner, strips, gapsBefore, filledBefore))
                .Where(c => c != null && c.Uncovered <= before + GapTolerance)
                .OrderBy(c => Math.Round(c.Uncovered, 1))
                .ThenByDescending(c => c.Displaced.Count(l => l.Building == c.A.Building))
                .ThenByDescending(c => c.A.Width)
                .ThenBy(c => c.A.Building.PrefabName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (best == null)
            {
                log.Add($"  open corner {angle:F0}° (sides {first.Strip.SideIndex}/{second.Strip.SideIndex}): no model fits on both streets, kept as planned.");
                return false;
            }

            lots.RemoveAll(best.Displaced.Contains);
            lots.Add(best.A);
            lots.Add(best.B);
            lots.AddRange(best.Fillers);
            paired.Add(best.A);
            paired.Add(best.B);
            log.Add($"  open corner {angle:F0}° (sides {first.Strip.SideIndex}/{second.Strip.SideIndex}): {best.A.Building} on both streets, {before:F1} -> {best.Uncovered:F1} m uncovered.");
            return true;
        }

        sealed class Pair
        {
            public PlannedLot A;
            public PlannedLot B;
            public List<PlannedLot> Displaced;
            public List<PlannedLot> Fillers;
            public double Uncovered;
        }

        Pair Candidate(BuildingInfo model, PlanBlock first, PlanBlock second, Pt corner, LotStrip[] strips, int gapsBefore, int filledBefore)
        {
            if (model.LotWidth * CellFrame.Size > first.Strip.ChunkLength || model.LotWidth * CellFrame.Size > second.Strip.ChunkLength
                || model.LotDepth > first.Grid.Depth || model.LotDepth > second.Grid.Depth)
            {
                return null;
            }

            var a = LotAt(first, model, corner, first.Strip.T * -1);
            var b = LotAt(second, model, corner, second.Strip.T);
            if (!Fits(a, corner) || !Fits(b, corner))
            {
                return null;
            }

            var displaced = lots.Where(l => Blocks(a, l) || Blocks(b, l)).ToList();
            if (displaced.Any(l => paired.Contains(l) || (l.Owner != first && l.Owner != second)))
            {
                return null;
            }

            var trial = lots.Where(l => !displaced.Contains(l)).Append(a).Append(b).ToList();
            var fillers = Fillers(a, first, -1, trial);
            trial.AddRange(fillers);
            var secondFillers = Fillers(b, second, 1, trial);
            trial.AddRange(secondFillers);
            fillers.AddRange(secondFillers);
            if (FrontCoverage.GapsBetweenOwn(trial, existing, new[] { first, second }) > gapsBefore
                || FrontCoverage.CountFilled(trial, frontCells) < filledBefore)
            {
                return null;
            }

            return new Pair { A = a, B = b, Displaced = displaced, Fillers = fillers, Uncovered = Uncovered(strips, trial) };
        }

        List<PlannedLot> Fillers(PlannedLot lot, PlanBlock block, int inward, List<PlannedLot> planned)
        {
            var strip = block.Strip;
            var (from, to) = Span(lot, strip);
            double inner = inward > 0 ? to : from;
            var gaps = planned
                .Where(l => l != lot && l.Owner == block)
                .Select(l => Span(l, strip))
                .Select(s => inward > 0 ? s.From - inner : inner - s.To)
                .Where(g => g > -overlap)
                .ToList();
            if (gaps.Count == 0 || gaps.Min() <= ClosedGap)
            {
                return new List<PlannedLot>();
            }

            double gap = gaps.Min();
            var options = catalog.Where(m => m.LotDepth <= block.Grid.Depth).ToList();
            var rows = options.Select(m => new[] { m }).Concat(options.SelectMany(m => options.Select(n => new[] { m, n })))
                .Where(row => row.Length <= MaxFillers)
                .Select(row => (row, excess: row.Sum(m => m.LotWidth) * CellFrame.Size - gap))
                .Where(o => o.excess >= -ClosedGap && o.excess <= (overlap - JointMargin) * (o.row.Length + 1))
                .OrderBy(o => o.row.Length)
                .ThenBy(o => Math.Abs(o.excess));
            foreach (var (row, excess) in rows)
            {
                var placed = Row(row, block, inner, inward, Math.Max(0, excess) / (row.Length + 1));
                if (placed.All(l => Fits(l) && !planned.Any(o => Quad.Touch(o.Footprint(0), l.Footprint(overlap)))))
                {
                    return placed;
                }
            }

            return new List<PlannedLot>();
        }

        static List<PlannedLot> Row(BuildingInfo[] row, PlanBlock block, double inner, int inward, double joint)
        {
            var strip = block.Strip;
            var direction = strip.T * inward;
            var placed = new List<PlannedLot>();
            double cursor = inner - inward * joint;
            foreach (var model in row)
            {
                var lot = LotAt(block, model, strip.ChunkStart + strip.T * cursor, direction);
                lot.Corner = false;
                placed.Add(lot);
                cursor += inward * (model.LotWidth * CellFrame.Size - joint);
            }

            return placed;
        }

        static (double From, double To) Span(PlannedLot lot, LotStrip strip)
        {
            var along = lot.Footprint(0).P.Select(p => Pt.Dot(p - strip.ChunkStart, strip.T)).ToList();
            return (along.Min(), along.Max());
        }

        bool Blocks(PlannedLot pair, PlannedLot other) => Quad.Touch(other.Footprint(0), pair.Footprint(overlap));

        bool Fits(PlannedLot lot, Pt? corner = null)
        {
            var strict = lot.Footprint(FootprintMask.StrictOverlap);
            if (existing.Any(q => Quad.Touch(q, strict)))
            {
                return false;
            }

            return lot.Footprint(InsideInset).P.All(p => Polygon.Contains(outline, p) || (corner.HasValue && Distance(p, corner.Value) <= SharedCorner && NearOutline(p)));
        }

        bool NearOutline(Pt point)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                if (SegmentDistance(point, outline[i], outline[(i + 1) % outline.Count]) <= MaxCurbOverhang)
                {
                    return true;
                }
            }

            return false;
        }

        static double SegmentDistance(Pt p, Pt a, Pt b)
        {
            var d = b - a;
            double length = Pt.Dot(d, d);
            double t = length < 1e-9 ? 0 : Math.Max(0, Math.Min(1, Pt.Dot(p - a, d) / length));
            return Distance(p, a + d * t);
        }

        static PlannedLot LotAt(PlanBlock block, BuildingInfo model, Pt corner, Pt inward)
        {
            var frame = block.Frame;
            double edge = Pt.Dot(corner - (frame.Origin - frame.AxisX * (CellFrame.Size / 2)), frame.AxisX);
            int anchor = (int)Math.Round(edge / CellFrame.Size);
            bool growsForward = Pt.Dot(frame.AxisX, inward) > 0;
            int minX = growsForward ? anchor : anchor - model.LotWidth;
            return new PlannedLot
            {
                Building = model,
                Owner = block,
                MinX = minX,
                MinY = block.AreaY,
                MaxX = minX + model.LotWidth,
                MaxY = block.AreaY + model.LotDepth,
                Corner = true,
                Shift = (float)(edge - anchor * CellFrame.Size),
            };
        }

        static bool TryCorner(PlanBlock first, PlanBlock second, out Pt corner)
        {
            var a = FrontPoint(first.Frame, first.AreaY);
            var b = FrontPoint(second.Frame, second.AreaY);
            double denominator = Pt.Cross(first.Frame.AxisX, second.Frame.AxisX);
            if (Math.Abs(denominator) < 1e-6)
            {
                corner = default;
                return false;
            }

            double along = Pt.Cross(b - a, second.Frame.AxisX) / denominator;
            corner = a + first.Frame.AxisX * along;
            return true;
        }

        static Pt FrontPoint(CellFrame frame, int row) => frame.Origin + frame.AxisY * (CellFrame.Size * (row - 0.5));

        static double Uncovered(IEnumerable<LotStrip> strips, IEnumerable<PlannedLot> planned) =>
            SidewalkGaps.Find(strips, planned).Sum(g => g.Length);

        static double Distance(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));
    }
}
