using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class OpenCornerPairs
    {
        public const double MinAngle = 95;
        public const double MaxAngle = 150;
        const double MaxCornerGap = 16;
        const double MaxCurbOverhang = 3;
        const double SharedCorner = 0.5;
        const double MinStraight = 16;
        const double MinStraightBesideCurve = 48;
        const double InsideInset = 0.1;
        const double MinGain = 1;
        const double SideOnTolerance = 0.05;
        const double CrossTolerance = 0.5;
        const double JointMargin = 0.01;
        const double ClosedGap = 0.5;
        const int MaxFillers = 3;
        const int MaxMovedNeighbours = 3;
        const double ScopeMargin = 16;

        readonly List<PlannedLot> lots;
        readonly IReadOnlyList<PlanBlock> blocks;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<BuildingInfo> catalog;
        readonly IReadOnlyList<CornerTwin> twins;
        readonly IReadOnlyList<Pt> frontCells;
        readonly float overlap;
        readonly IList<string> log;
        readonly Random random;
        readonly HashSet<PlannedLot> paired = new HashSet<PlannedLot>();
        List<PlannedLot> scope;
        List<Quad> nearby;
        List<Pt> cells;

        OpenCornerPairs(List<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> catalog, IReadOnlyList<BuildingInfo> corners, IReadOnlyList<Pt> frontCells, float overlap, int seed, IList<string> log)
        {
            this.lots = lots;
            twins = CornerTwins.Find(corners);
            this.blocks = blocks.Where(b => b.Strip != null).ToList();
            this.existing = existing;
            this.outline = outline;
            this.catalog = catalog;
            this.frontCells = frontCells;
            this.overlap = overlap;
            this.log = log;
            random = new Random(seed ^ 0x51ed270b);
        }

        public static int Apply(List<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> catalog, IReadOnlyList<BuildingInfo> corners, IReadOnlyList<Pt> frontCells, float overlap, int seed, IList<string> log) =>
            new OpenCornerPairs(lots, blocks, existing, outline, catalog, corners, frontCells, overlap, seed, log).Run();

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

            Focus(first, second);
            var strips = new[] { first.Strip, second.Strip };
            double before = Uncovered(strips, scope);
            var gapsBefore = Gaps(scope, first, second);
            int filledBefore = FrontCoverage.CountFilled(scope, cells);
            var best = Shapes(first)
                .Select(shape => Candidate(shape, first, second, corner, strips, gapsBefore, filledBefore))
                .Where(c => c != null && (c.A.FrontAlong != 0 ? c.Uncovered <= before + SideOnTolerance : c.Uncovered <= before - MinGain || c.Filled > filledBefore))
                .OrderBy(c => Math.Round(c.Uncovered, 1))
                .ThenByDescending(c => c.A.Width)
                .ThenByDescending(c => c.A.Depth)
                .FirstOrDefault();
            if (best == null)
            {
                log.Add($"  open corner {angle:F0}° (sides {first.Strip.SideIndex}/{second.Strip.SideIndex}): no model fits on both streets without crossing its copy and covers more, kept as planned.");
                return false;
            }

            lots.RemoveAll(best.Displaced.Contains);
            lots.Add(best.A);
            lots.Add(best.B);
            lots.AddRange(best.Fillers);
            paired.Add(best.A);
            paired.Add(best.B);
            string models = best.A.FrontAlong == 0 ? $"{best.A.Building}" : $"{best.A.Building} and {best.B.Building} side-on";
            log.Add($"  open corner {angle:F0}° (sides {first.Strip.SideIndex}/{second.Strip.SideIndex}): {models} on both streets, {before:F1} -> {best.Uncovered:F1} m uncovered, {filledBefore} -> {best.Filled} street cells filled, {SharedShare(best.A, best.B) * 100:F0}% of each lot shared.");
            return true;
        }

        void Focus(PlanBlock first, PlanBlock second)
        {
            var a = Bounds.Of(first);
            var b = Bounds.Of(second);
            var area = new Bounds(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY)).Grow(ScopeMargin);
            scope = lots.Where(l => area.Overlaps(l.Footprint(overlap))).ToList();
            nearby = existing.Where(area.Overlaps).ToList();
            cells = frontCells.Where(area.Contains).ToList();
        }

        sealed class Pair
        {
            public PlannedLot A;
            public PlannedLot B;
            public List<PlannedLot> Displaced;
            public List<PlannedLot> Fillers;
            public double Uncovered;
            public int Filled;
        }

        sealed class Shape
        {
            public BuildingInfo First;
            public BuildingInfo Second;
            public bool SideOn;

            public int Along => SideOn ? First.LotDepth : First.LotWidth;

            public int Deep => SideOn ? First.LotWidth : First.LotDepth;
        }

        List<Shape> Shapes(PlanBlock first)
        {
            var straight = catalog
                .GroupBy(model => (model.LotWidth, model.LotDepth, model.Group))
                .OrderBy(shape => shape.Key)
                .Select(PickModel)
                .Select(model => new Shape { First = model, Second = model });
            var firstSide = SideFacingStreet(Pt.Dot(first.Frame.AxisX, first.Strip.T) > 0 ? 1 : -1);
            var sideOn = twins
                .Where(twin => twin.Left.LotWidth < twin.Left.LotDepth)
                .GroupBy(twin => (twin.Left.LotWidth, twin.Left.LotDepth, twin.Left.Group))
                .OrderBy(shape => shape.Key)
                .Select(shape => shape.ToList())
                .Select(shape => shape[random.Next(shape.Count)])
                .Select(twin => new Shape { First = twin.For(firstSide), Second = twin.For(Opposite(firstSide)), SideOn = true });
            return straight.Concat(sideOn).ToList();
        }

        public static CornerKind SideFacingStreet(int frontAlong) => frontAlong > 0 ? CornerKind.Left : CornerKind.Right;

        static CornerKind Opposite(CornerKind side) => side == CornerKind.Left ? CornerKind.Right : CornerKind.Left;

        Pair Candidate(Shape shape, PlanBlock first, PlanBlock second, Pt corner, LotStrip[] strips, (int Own, int Any) gapsBefore, int filledBefore)
        {
            var model = shape.First;
            if (shape.Along * CellFrame.Size > first.Strip.ChunkLength || shape.Along * CellFrame.Size > second.Strip.ChunkLength
                || shape.Deep > first.Grid.Depth || shape.Deep > second.Grid.Depth)
            {
                return null;
            }

            var a = LotAt(first, shape.First, corner, first.Strip.T * -1, shape.SideOn);
            var b = LotAt(second, shape.Second, corner, second.Strip.T, shape.SideOn);
            if (!Fits(a, corner) || !Fits(b, corner) || BackLeavesCopy(a, b, corner) || BackLeavesCopy(b, a, corner))
            {
                return null;
            }

            var displaced = scope.Where(l => Blocks(a, l) || Blocks(b, l)).ToList();
            if (displaced.Any(l => paired.Contains(l) || l.Building.Group != model.Group || (l.Owner != first && l.Owner != second)))
            {
                return null;
            }

            var trial = scope.Where(l => !displaced.Contains(l)).Append(a).Append(b).ToList();
            var fillers = new List<PlannedLot>();
            foreach (var (lot, block, inward) in new[] { (a, first, -1), (b, second, 1) })
            {
                var (moved, placed) = CloseRow(lot, block, inward, trial);
                trial.RemoveAll(moved.Contains);
                trial.AddRange(placed);
                displaced.AddRange(moved);
                fillers.AddRange(placed);
            }

            int filled = FrontCoverage.CountFilled(trial, cells);
            var gaps = Gaps(trial, first, second);
            if (gaps.Own > gapsBefore.Own || gaps.Any > gapsBefore.Any || filled < filledBefore)
            {
                return null;
            }

            return new Pair { A = a, B = b, Displaced = displaced, Fillers = fillers, Uncovered = Uncovered(strips, trial), Filled = filled };
        }

        BuildingInfo PickModel(IEnumerable<BuildingInfo> shape)
        {
            var models = shape.ToList();
            return models[random.Next(models.Count)];
        }

        (List<PlannedLot> Moved, List<PlannedLot> Fillers) CloseRow(PlannedLot lot, PlanBlock block, int inward, List<PlannedLot> planned)
        {
            var strip = block.Strip;
            var (from, to) = Span(lot, strip);
            double inner = inward > 0 ? to : from;
            var neighbours = planned
                .Where(l => l != lot && l.Owner == block)
                .Select(l => (lot: l, span: Span(l, strip)))
                .Select(n => (n.lot, gap: inward > 0 ? n.span.From - inner : inner - n.span.To))
                .Where(n => n.gap > -overlap)
                .OrderBy(n => n.gap)
                .ToList();
            var moved = new List<PlannedLot>();
            for (int i = 0; i < neighbours.Count; i++)
            {
                if (neighbours[i].gap <= ClosedGap)
                {
                    return (moved, new List<PlannedLot>());
                }

                var kept = planned.Where(l => !moved.Contains(l)).ToList();
                var fillers = Fillers(lot, block, inner, inward, neighbours[i].gap, kept);
                if (fillers.Count > 0)
                {
                    return (moved, fillers);
                }

                var next = neighbours[i].lot;
                if (moved.Count == MaxMovedNeighbours || next.Corner || paired.Contains(next) || next.Building.Group != lot.Building.Group)
                {
                    break;
                }

                moved.Add(next);
            }

            return (new List<PlannedLot>(), new List<PlannedLot>());
        }

        List<PlannedLot> Fillers(PlannedLot lot, PlanBlock block, double inner, int inward, double gap, List<PlannedLot> planned)
        {
            var options = catalog
                .Where(m => m.LotDepth <= block.Grid.Depth && m.Group == lot.Building.Group)
                .GroupBy(m => (m.LotWidth, m.LotDepth))
                .OrderBy(shape => shape.Key)
                .Select(PickModel)
                .OrderByDescending(m => m.LotDepth)
                .ToList();
            foreach (var (widths, excess) in WidthRows(options.Select(m => m.LotWidth).Distinct().ToList(), gap))
            {
                var placed = Row(widths, options, block, inner, inward, Math.Max(0, excess) / (widths.Count + 1), planned);
                if (placed != null)
                {
                    return placed;
                }
            }

            return new List<PlannedLot>();
        }

        IEnumerable<(List<int> Widths, double Excess)> WidthRows(List<int> widths, double gap)
        {
            IEnumerable<List<int>> rows = widths.Select(w => new List<int> { w }).ToList();
            var all = rows.ToList();
            for (int length = 2; length <= MaxFillers; length++)
            {
                rows = rows.SelectMany(row => widths.Select(w => row.Append(w).ToList())).ToList();
                all.AddRange(rows);
            }

            return all
                .Select(row => (row, excess: row.Sum() * CellFrame.Size - gap))
                .Where(o => o.excess >= -ClosedGap && o.excess <= (overlap - JointMargin) * (o.row.Count + 1))
                .OrderBy(o => o.row.Count)
                .ThenBy(o => Math.Abs(o.excess));
        }

        List<PlannedLot> Row(List<int> widths, List<BuildingInfo> options, PlanBlock block, double inner, int inward, double joint, List<PlannedLot> planned)
        {
            var strip = block.Strip;
            var direction = strip.T * inward;
            var placed = new List<PlannedLot>();
            double cursor = inner - inward * joint;
            foreach (int width in widths)
            {
                var start = strip.ChunkStart + strip.T * cursor;
                var lot = options
                    .Where(m => m.LotWidth == width)
                    .Select(m => LotAt(block, m, start, direction))
                    .FirstOrDefault(l => Fits(l) && !planned.Any(o => Quad.Touch(o.Footprint(0), l.Footprint(overlap))));
                if (lot == null)
                {
                    return null;
                }

                lot.Corner = false;
                placed.Add(lot);
                cursor += inward * (width * CellFrame.Size - joint);
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
            if (nearby.Any(q => Quad.Touch(q, strict)))
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

        static PlannedLot LotAt(PlanBlock block, BuildingInfo model, Pt corner, Pt inward, bool sideOn = false)
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

        static double Uncovered(IEnumerable<LotStrip> strips, IEnumerable<PlannedLot> planned) =>
            SidewalkGaps.Find(strips, planned).Sum(g => g.Length);

        (int Own, int Any) Gaps(List<PlannedLot> planned, PlanBlock first, PlanBlock second)
        {
            var pair = new[] { first, second };
            return (FrontCoverage.GapsBetweenOwn(planned, nearby, pair), FrontCoverage.GapsBetweenBuildings(planned, nearby, pair));
        }

        static bool BackLeavesCopy(PlannedLot lot, PlannedLot copy, Pt corner)
        {
            var p = lot.Footprint(0).P;
            int front = Enumerable.Range(0, 4).OrderBy(i => Distance(p[i], corner)).First();
            var along = lot.Owner.Frame.AxisX;
            var back = new[] { p[(front + 1) % 4], p[(front + 3) % 4] }.OrderBy(v => Math.Abs(Pt.Dot(v - p[front], along))).First();
            return !Polygon.Contains(copy.Footprint(-CrossTolerance).P, back);
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
