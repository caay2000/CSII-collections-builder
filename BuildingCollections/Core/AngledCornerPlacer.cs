using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    public sealed class AngledCornerPlacer
    {
        public const double AngleTolerance = 4;
        public const double Overhang = 2.0;
        const double LotSlack = 0.3;
        const double CellSlack = 0.5;
        const double DirectionReach = 16;
        const double MaxStraightError = 1.0;
        const double SetbackSlack = 12;
        const double SetbackStep = 0.5;
        const double CloseEnough = 4;
        const double WingReach = 24;

        readonly IReadOnlyList<List<Pt>> sides;
        readonly IReadOnlyList<bool> frontage;
        readonly IReadOnlyList<Pt> outline;
        readonly List<Quad> taken;
        readonly IReadOnlyList<BuildingInfo> angled;
        readonly Random rng;

        public List<PlannedLot> Placed { get; } = new List<PlannedLot>();

        public List<string> Log { get; } = new List<string>();

        AngledCornerPlacer(IReadOnlyList<List<Pt>> sides, IReadOnlyList<bool> frontage, IReadOnlyList<Pt> outline,
            IEnumerable<Quad> existing, IReadOnlyList<BuildingInfo> angled, int seed)
        {
            this.sides = sides;
            this.frontage = frontage;
            this.outline = outline;
            taken = existing.ToList();
            this.angled = angled;
            rng = new Random(seed ^ 0x2c1b3c6d);
        }

        public static AngledCornerPlacer Place(IReadOnlyList<List<Pt>> sides, IReadOnlyList<bool> frontage, IReadOnlyList<Pt> outline,
            IEnumerable<Quad> existing, IEnumerable<BuildingInfo> buildings, int seed)
        {
            var usable = buildings.Where(b => b.IsAngledCorner && b.Footprint != null && b.Footprint.Count > 0).ToList();
            var placer = new AngledCornerPlacer(sides, frontage, outline, existing ?? Enumerable.Empty<Quad>(), usable, seed);
            if (usable.Count > 0 && sides != null && sides.Count >= 3 && outline != null && outline.Count >= 3)
            {
                placer.Run();
            }

            return placer;
        }

        void Run()
        {
            for (int i = 0; i < sides.Count; i++)
            {
                int next = (i + 1) % sides.Count;
                if (!IsStreet(i) || !IsStreet(next) || sides[i].Count < 2 || sides[next].Count < 2)
                {
                    continue;
                }

                var arriving = sides[i];
                var leaving = sides[next];
                var corner = leaving[0];
                var dIn = Unit(arriving[arriving.Count - 1] - PointBack(arriving, DirectionReach));
                var dOut = Unit(PointAlong(leaving, DirectionReach) - leaving[0]);
                double interior = 180 - Angle(dIn, dOut);
                var options = angled
                    .Where(b => Math.Abs(b.CornerAngle - interior) <= AngleTolerance)
                    .Select(b => Fit(b, corner, arriving, leaving))
                    .Where(o => o.lot != null)
                    .ToList();
                if (options.Count == 0)
                {
                    if (angled.Any(b => Math.Abs(b.CornerAngle - interior) <= AngleTolerance))
                    {
                        Log.Add(string.Format(CultureInfo.InvariantCulture, "  angled corner at ({0:F1},{1:F1}), {2:F0}°: no angled building fits.", corner.X, corner.Y, interior));
                    }

                    continue;
                }

                double best = options.Min(o => o.setback);
                var close = options.Where(o => o.setback <= best + CloseEnough).ToList();
                var pick = close[rng.Next(close.Count)];
                Placed.Add(pick.lot);
                taken.AddRange(pick.lot.Shapes());
                Log.Add(string.Format(CultureInfo.InvariantCulture, "  angled corner at ({0:F1},{1:F1}), {2:F0}°: {3} ({4}°), set back {5:F1} m from the corner; {6} of {7} angled buildings fit.",
                    corner.X, corner.Y, interior, pick.lot.Building.PrefabName, pick.lot.Building.CornerAngle, pick.setback, options.Count, angled.Count));
            }
        }

        bool IsStreet(int side) => frontage == null || side >= frontage.Count || frontage[side];

        (PlannedLot lot, double setback) Fit(BuildingInfo building, Pt corner, List<Pt> arriving, List<Pt> leaving)
        {
            double half = building.LotWidth * CellFrame.Size / 2;
            double beyondPlus = building.Footprint.Max(r => r.MaxX) - half, beyondMinus = -half - building.Footprint.Min(r => r.MinX);
            bool wingAtPlusX = beyondPlus >= beyondMinus;
            double maxSetback = Math.Max(beyondPlus, beyondMinus) + SetbackSlack;
            var front = wingAtPlusX ? leaving : Reversed(arriving);
            var other = wingAtPlusX ? Reversed(arriving) : leaving;
            double wingReach = Math.Min(WingReach, Length(other));
            if (!Straight(other, corner, Unit(PointAlong(other, wingReach) - corner), wingReach))
            {
                return (null, 0);
            }

            double width = building.LotWidth * CellFrame.Size;
            for (double s = 0; s <= maxSetback + 1e-6; s += SetbackStep)
            {
                double reach = s + width;
                if (reach > Length(front) + 1e-6)
                {
                    break;
                }

                var along = Unit(PointAlong(front, reach) - corner);
                if (!Straight(front, corner, along, reach))
                {
                    continue;
                }

                var inward = wingAtPlusX ? new Pt(-along.Y, along.X) : new Pt(along.Y, -along.X);
                var axisX = wingAtPlusX ? along * -1 : along;
                var center = corner + along * (s + width / 2) + inward * (building.LotDepth * CellFrame.Size / 2);
                var lot = Lot(building, center, axisX, inward);
                if (Fits(lot))
                {
                    return (lot, s);
                }
            }

            return (null, 0);
        }

        static PlannedLot Lot(BuildingInfo building, Pt center, Pt axisX, Pt inward)
        {
            var origin = center - axisX * (CellFrame.Size * (building.LotWidth / 2.0 - 0.5)) - inward * (CellFrame.Size * (building.LotDepth / 2.0 - 0.5));
            return new PlannedLot
            {
                Building = building,
                Owner = new PlanBlock { Frame = new CellFrame(origin, axisX, inward), Label = "angled corner" },
                MinX = 0,
                MinY = 0,
                MaxX = building.LotWidth,
                MaxY = building.LotDepth,
                Corner = true,
            };
        }

        bool Fits(PlannedLot lot)
        {
            var lotQuad = lot.Footprint(LotSlack);
            if (lotQuad.P.Any(p => Outside(p) > 0))
            {
                return false;
            }

            var cells = lot.Wings(CellSlack).ToList();
            if (cells.Any(c => c.P.Any(p => Outside(p) > Overhang)))
            {
                return false;
            }

            var shapes = cells.Append(lotQuad).ToList();
            return !taken.Any(t => shapes.Any(s => Quad.Distance(s, t) <= 0));
        }

        double Outside(Pt p)
        {
            if (Polygon.Contains(outline, p))
            {
                return 0;
            }

            double best = double.MaxValue;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                best = Math.Min(best, SegmentDistance(p, outline[j], outline[i]));
            }

            return best;
        }

        static double SegmentDistance(Pt p, Pt a, Pt b)
        {
            var d = b - a;
            double len2 = Pt.Dot(d, d);
            double t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, Pt.Dot(p - a, d) / len2));
            return Dist(p, a + d * t);
        }

        static bool Straight(List<Pt> front, Pt corner, Pt along, double reach)
        {
            double travelled = 0;
            for (int i = 1; i < front.Count && travelled <= reach; i++)
            {
                travelled += Dist(front[i - 1], front[i]);
                var off = front[i] - corner;
                double u = Pt.Dot(off, along);
                if (u > reach + 1e-6)
                {
                    break;
                }

                if (Math.Abs(Pt.Cross(along, off)) > MaxStraightError)
                {
                    return false;
                }
            }

            return true;
        }

        static List<Pt> Reversed(List<Pt> side)
        {
            var copy = new List<Pt>(side);
            copy.Reverse();
            return copy;
        }

        static Pt PointAlong(List<Pt> side, double at)
        {
            for (int i = 1; i < side.Count; i++)
            {
                double len = Dist(side[i - 1], side[i]);
                if (at <= len)
                {
                    return side[i - 1] + (side[i] - side[i - 1]) * (len < 1e-9 ? 0 : at / len);
                }

                at -= len;
            }

            return side[side.Count - 1];
        }

        static Pt PointBack(List<Pt> side, double at) => PointAlong(Reversed(side), at);
    }
}
