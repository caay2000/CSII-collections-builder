using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public enum PairKind
    {
        Narrow,
        Twins,
        SameModel,
        None,
    }

    public sealed class OpenCornerPairs
    {
        public const double MinAngle = 95;
        public const double MaxAngle = 150;
        const double MaxCornerGap = 16;
        const double MaxCurbOverhang = 3;
        const double SharedCorner = 0.5;
        const double MinStraight = 16;
        const double InsideInset = 0.1;
        const double CrossTolerance = 0.5;
        const int NarrowWidth = 2;
        const double SingleCellMinAngle = 120;
        const double HiddenBackDepth = 6;
        const double SlideMaxAngle = 110;
        const double MaxSlide = 3;

        readonly IReadOnlyList<PlanBlock> blocks;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<BuildingInfo> catalog;
        readonly IReadOnlyList<CornerTwin> twins;
        readonly float overlap;
        readonly IList<string> log;
        readonly int seed;
        Random random;
        readonly List<PlannedLot> reserved = new List<PlannedLot>();
        readonly List<(PlannedLot A, PlannedLot B, double Angle)> pairs = new List<(PlannedLot, PlannedLot, double)>();
        readonly IReadOnlyList<PairKind> wanted;
        readonly List<PairKind> used = new List<PairKind>();

        OpenCornerPairs(IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> catalog, IReadOnlyList<BuildingInfo> corners, IReadOnlyList<PairKind> wanted, float overlap, int seed, IList<string> log)
        {
            this.wanted = wanted;
            twins = CornerTwins.Find(corners);
            this.blocks = blocks.Where(b => b.Strip != null).ToList();
            this.existing = existing;
            this.outline = outline;
            this.catalog = catalog;
            this.overlap = overlap;
            this.log = log;
            this.seed = seed;
        }

        public static (List<PlannedLot> Lots, List<PairKind> Kinds) Reserve(IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> catalog, IReadOnlyList<BuildingInfo> corners, IReadOnlyList<PairKind> wanted, float overlap, int seed, IList<string> log) =>
            new OpenCornerPairs(blocks, existing, outline, catalog, corners, wanted, overlap, seed, log).Run();

        (List<PlannedLot>, List<PairKind>) Run()
        {
            foreach (var (first, second, angle) in Corners())
            {
                var kind = used.Count < wanted.Count ? wanted[used.Count] : PairKind.Narrow;
                random = new Random(seed ^ 0x51ed270b ^ (used.Count * 7919));
                if (kind == PairKind.None)
                {
                    log.Add($"  open corner {angle:F0}° (sides {first.Strip.SideIndex}/{second.Strip.SideIndex}): left to the regular corner buildings, the street is better covered without a pair.");
                }

                used.Add(kind == PairKind.None ? PairKind.None : TryPair(first, second, angle, kind));
            }

            SlideTowardsNeighbourPairs();
            return (reserved, used);
        }

        void SlideTowardsNeighbourPairs()
        {
            foreach (var pair in pairs.Where(p => p.Angle < SlideMaxAngle))
            {
                foreach (var lot in new[] { pair.A, pair.B })
                {
                    var next = pairs
                        .Where(other => other != pair)
                        .SelectMany(other => new[] { other.A, other.B })
                        .Where(other => other.Owner == lot.Owner)
                        .Select(other => (other, gap: Gap(lot, other)))
                        .Where(n => Math.Abs(n.gap) > 1e-3 && Math.Abs(n.gap) <= MaxSlide)
                        .OrderBy(n => Math.Abs(n.gap))
                        .FirstOrDefault();
                    if (next.other == null)
                    {
                        continue;
                    }

                    lot.Shift += (float)next.gap;
                    log.Add($"  open corner {pair.Angle:F0}° pair {lot.Building} (side {lot.Owner.Strip.SideIndex}): slid {Math.Abs(next.gap):F1} m away from its corner to meet the pair at the other end of the street.");
                }
            }
        }

        static double Gap(PlannedLot lot, PlannedLot other)
        {
            double size = CellFrame.Size;
            double start = lot.MinX * size + lot.Shift;
            double end = lot.MaxX * size + lot.Shift;
            double otherStart = other.MinX * size + other.Shift;
            double otherEnd = other.MaxX * size + other.Shift;
            return otherStart >= end ? otherStart - end : otherEnd <= start ? otherEnd - start : 0;
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

        static bool IsStraightEnough(LotStrip strip) => strip.ChunkLength >= MinStraight;

        PairKind TryPair(PlanBlock first, PlanBlock second, double angle, PairKind kind)
        {
            if (!TryCorner(first, second, out var corner))
            {
                return PairKind.None;
            }

            var pair = Shapes(first, kind)
                .Select(shape => (Lots: Candidate(shape, first, second, corner, angle), shape.Kind))
                .FirstOrDefault(p => p.Lots.A != null);
            string sides = $"sides {first.Strip.SideIndex}/{second.Strip.SideIndex}";
            var (a, b) = pair.Lots;
            if (a == null)
            {
                log.Add($"  open corner {angle:F0}° ({sides}): no model fits on both streets without crossing its copy, left to the regular corner buildings.");
                return PairKind.None;
            }

            reserved.Add(a);
            reserved.Add(b);
            pairs.Add((a, b, angle));
            string models = a.FrontAlong == 0 ? $"{a.Building}" : $"{a.Building} and {b.Building} side-on";
            log.Add($"  open corner {angle:F0}° ({sides}): {models} on both streets, {SharedShare(a, b) * 100:F0}% of each lot shared, reserved before the fill{(pair.Kind == PairKind.Narrow ? " (narrow pair)" : string.Empty)}.");
            return pair.Kind;
        }

        sealed class Shape
        {
            public BuildingInfo First;
            public BuildingInfo Second;
            public bool SideOn;
            public PairKind Kind;

            public int Along => SideOn ? First.LotDepth : First.LotWidth;

            public int Deep => SideOn ? First.LotWidth : First.LotDepth;
        }

        IEnumerable<Shape> Shapes(PlanBlock first, PairKind kind)
        {
            var firstSide = SideFacingStreet(Pt.Dot(first.Frame.AxisX, first.Strip.T) > 0 ? 1 : -1);
            var sideOn = Shuffled(twins
                .Where(twin => twin.Left.LotWidth < twin.Left.LotDepth)
                .GroupBy(twin => (twin.Left.LotWidth, twin.Left.LotDepth, twin.Left.Group))
                .OrderBy(shape => shape.Key)
                .Select(shape => shape.ToList())
                .Select(shape => shape[random.Next(shape.Count)])
                .Select(twin => new Shape { First = twin.For(firstSide), Second = twin.For(Opposite(firstSide)), SideOn = true, Kind = PairKind.Twins }));
            var straight = Shuffled(catalog
                .GroupBy(model => (model.LotWidth, model.LotDepth, model.Group))
                .OrderBy(shape => shape.Key)
                .Select(shape => shape.ToList())
                .Select(shape => shape[random.Next(shape.Count)])
                .Select(model => new Shape { First = model, Second = model, Kind = PairKind.SameModel }));
            var narrow = straight
                .Where(shape => shape.First.LotWidth <= NarrowWidth)
                .OrderByDescending(shape => shape.First.LotWidth)
                .Select(shape => new Shape { First = shape.First, Second = shape.Second, Kind = PairKind.Narrow });
            switch (kind)
            {
                case PairKind.Narrow:
                    return narrow.Concat(sideOn).Concat(straight);
                case PairKind.Twins:
                    return sideOn.Concat(straight);
                default:
                    return straight;
            }
        }

        List<Shape> Shuffled(IEnumerable<Shape> shapes) => shapes.Select(s => (s, key: random.Next())).OrderBy(x => x.key).Select(x => x.s).ToList();

        public static CornerKind SideFacingStreet(int frontAlong) => frontAlong > 0 ? CornerKind.Left : CornerKind.Right;

        static CornerKind Opposite(CornerKind side) => side == CornerKind.Left ? CornerKind.Right : CornerKind.Left;

        (PlannedLot A, PlannedLot B) Candidate(Shape shape, PlanBlock first, PlanBlock second, Pt corner, double angle)
        {
            if (shape.Along * CellFrame.Size > first.Strip.ChunkLength || shape.Along * CellFrame.Size > second.Strip.ChunkLength
                || shape.Deep > first.Grid.Depth || shape.Deep > second.Grid.Depth)
            {
                return default;
            }

            var a = LotAt(first, shape.First, corner, first.Strip.T * -1, shape.SideOn);
            var b = LotAt(second, shape.Second, corner, second.Strip.T, shape.SideOn);
            double hiddenDepth = shape.Kind == PairKind.Narrow && shape.First.LotWidth == 1 ? (angle >= SingleCellMinAngle ? HiddenBackDepth : double.NaN) : double.PositiveInfinity;
            if (double.IsNaN(hiddenDepth)
                || !Fits(a, corner) || !Fits(b, corner) || BackLeavesCopy(a, b, corner, hiddenDepth) || BackLeavesCopy(b, a, corner, hiddenDepth) || HitsReserved(a) || HitsReserved(b)
                || TakesAnotherFront(a, first, second) || TakesAnotherFront(b, first, second))
            {
                return default;
            }

            return (a, b);
        }

        bool TakesAnotherFront(PlannedLot lot, PlanBlock first, PlanBlock second)
        {
            var footprint = lot.Footprint(0);
            return blocks
                .Where(b => b != first && b != second && Bounds.Of(b).Overlaps(footprint))
                .Any(b => Enumerable.Range(0, b.Grid.Width)
                    .Where(x => b.Grid.Free[x, 0])
                    .Any(x => Quad.Touch(footprint, b.Frame.Cell(b.AreaX + x, b.AreaY, overlap))));
        }

        bool HitsReserved(PlannedLot lot) => reserved.Any(other => Quad.Touch(other.Footprint(0), lot.Footprint(overlap)));

        bool Fits(PlannedLot lot, Pt corner)
        {
            var strict = lot.Footprint(FootprintMask.StrictOverlap);
            if (existing.Any(q => Quad.Touch(q, strict)))
            {
                return false;
            }

            return lot.Footprint(InsideInset).P.All(p => Polygon.Contains(outline, p) || (Distance(p, corner) <= SharedCorner && NearOutline(p)));
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

        static PlannedLot LotAt(PlanBlock block, BuildingInfo model, Pt corner, Pt inward, bool sideOn)
        {
            var frame = block.Frame;
            double edge = Pt.Dot(corner - (frame.Origin - frame.AxisX * (CellFrame.Size / 2)), frame.AxisX);
            int anchor = (int)Math.Round(edge / CellFrame.Size);
            bool growsForward = Pt.Dot(frame.AxisX, inward) > 0;
            int along = sideOn ? model.LotDepth : model.LotWidth;
            int deep = sideOn ? model.LotWidth : model.LotDepth;
            int minX = growsForward ? anchor : anchor - along;
            return new PlannedLot
            {
                Building = model,
                Owner = block,
                MinX = minX,
                MinY = block.AreaY,
                MaxX = minX + along,
                MaxY = block.AreaY + deep,
                Corner = true,
                Paired = true,
                FrontAlong = sideOn ? (growsForward ? -1 : 1) : 0,
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

        static bool BackLeavesCopy(PlannedLot lot, PlannedLot copy, Pt corner, double hiddenDepth)
        {
            var p = lot.Footprint(0).P;
            int front = Enumerable.Range(0, 4).OrderBy(i => Distance(p[i], corner)).First();
            var along = lot.Owner.Frame.AxisX;
            var back = new[] { p[(front + 1) % 4], p[(front + 3) % 4] }.OrderBy(v => Math.Abs(Pt.Dot(v - p[front], along))).First();
            if (Polygon.Contains(copy.Footprint(-CrossTolerance).P, back))
            {
                return false;
            }

            var frame = copy.Owner.Frame;
            return Pt.Dot(back - FrontPoint(frame, copy.Owner.AreaY), frame.AxisY) < hiddenDepth;
        }

        static double SharedShare(PlannedLot a, PlannedLot b)
        {
            var qa = a.Footprint(0).P;
            var qb = b.Footprint(0).P;
            double smaller = Math.Min(Polygon.Area(qa), Polygon.Area(qb));
            return smaller <= 0 ? 0 : Polygon.Area(Polygon.ConvexIntersection(qa, qb)) / smaller;
        }

        static double Distance(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));
    }
}
