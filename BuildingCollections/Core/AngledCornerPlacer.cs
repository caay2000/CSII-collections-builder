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
        public const double MaxMismatch = 10;
        public const double Overhang = 0;
        public const double SetbackSpread = 4;
        public const double SpreadStep = 1;
        const double LotSlack = 0.3;
        const double CellSlack = 0.5;
        const double DirectionReach = 16;
        const double MaxStraightError = 1.0;
        const double SetbackSlack = 12;
        const double SetbackStep = 0.5;
        const double CloseEnough = 4;
        const double WingReach = 24;
        const double CurveReach = 48;

        readonly IReadOnlyList<List<Pt>> sides;
        readonly IReadOnlyList<bool> frontage;
        readonly IReadOnlyList<Pt> outline;
        readonly List<Quad> taken;
        readonly IReadOnlyList<BuildingInfo> angled;
        readonly Random rng;

        public List<PlannedLot> Placed { get; } = new List<PlannedLot>();

        public List<IReadOnlyList<PlannedLot>> Choices { get; } = new List<IReadOnlyList<PlannedLot>>();

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
                if (Math.Abs(interior - 90) <= AngleTolerance || Math.Abs(interior - 180) <= AngleTolerance)
                {
                    continue;
                }

                if (Curved(Reversed(arriving)) || Curved(leaving))
                {
                    Log.Add(string.Format(CultureInfo.InvariantCulture, "  angled corner at ({0:F1},{1:F1}), {2:F0}°: one of its sides is curved; left to the regular corner buildings.", corner.X, corner.Y, interior));
                    continue;
                }

                var near = angled.Where(b => Mismatch(b, interior) <= MaxMismatch).ToList();
                if (near.Count == 0)
                {
                    double closest = angled.Min(b => Mismatch(b, interior));
                    Log.Add(string.Format(CultureInfo.InvariantCulture, "  angled corner at ({0:F1},{1:F1}), {2:F0}°: no angled building within {3:F0}° (closest {4:F0}° off).", corner.X, corner.Y, interior, MaxMismatch, closest));
                    continue;
                }

                var options = near
                    .Select(b => (fit: Fit(b, corner, arriving, leaving), mismatch: Mismatch(b, interior)))
                    .Where(o => o.fit.lot != null)
                    .ToList();
                if (options.Count == 0)
                {
                    Log.Add(string.Format(CultureInfo.InvariantCulture, "  angled corner at ({0:F1},{1:F1}), {2:F0}°: none of the {3} angled buildings within {4:F0}° fits.", corner.X, corner.Y, interior, near.Count, MaxMismatch));
                    continue;
                }

                double leastMismatch = options.Min(o => o.mismatch);
                var closer = options.Where(o => o.mismatch <= leastMismatch + AngleTolerance).ToList();
                double best = closer.Min(o => o.fit.setback);
                var close = closer.Where(o => o.fit.setback <= best + CloseEnough).ToList();
                var pick = close[rng.Next(close.Count)];
                var choice = Alternatives(pick.fit.lot.Building, corner, arriving, leaving, pick.fit.setback, pick.fit.lot);
                Placed.Add(pick.fit.lot);
                Choices.Add(choice);
                taken.AddRange(choice.SelectMany(l => l.Shapes()));
                Log.Add(string.Format(CultureInfo.InvariantCulture, "  angled corner at ({0:F1},{1:F1}), {2:F0}°: {3} ({4}°, {5:F0}° off), set back {6:F1} m from the corner, {9:F1} m over the sidewalk; {7} of {8} angled buildings fit.",
                    corner.X, corner.Y, interior, pick.fit.lot.Building.PrefabName, pick.fit.lot.Building.CornerAngle, pick.mismatch, pick.fit.setback, options.Count, angled.Count, OverSidewalk(pick.fit.lot)));
            }
        }

        static double Mismatch(BuildingInfo building, double interior) => Math.Abs(building.CornerAngle - interior);

        bool IsStreet(int side) => frontage == null || side >= frontage.Count || frontage[side];

        (PlannedLot lot, double setback) Fit(BuildingInfo building, Pt corner, List<Pt> arriving, List<Pt> leaving)
        {
            var placing = Placing(building, corner, arriving, leaving);
            if (placing == null)
            {
                return (null, 0);
            }

            for (double s = 0; s <= placing.MaxSetback + 1e-6; s += SetbackStep)
            {
                var lot = placing.At(s);
                if (lot == null && placing.Beyond(s))
                {
                    break;
                }

                if (lot != null && Fits(lot))
                {
                    return (lot, s);
                }
            }

            return (null, 0);
        }

        List<PlannedLot> Alternatives(BuildingInfo building, Pt corner, List<Pt> arriving, List<Pt> leaving, double first, PlannedLot firstLot)
        {
            var found = new List<PlannedLot> { firstLot };
            var placing = Placing(building, corner, arriving, leaving);
            for (double s = first + SpreadStep; s <= Math.Min(first + SetbackSpread, placing.MaxSetback) + 1e-6 && !placing.Beyond(s); s += SpreadStep)
            {
                var lot = placing.At(s);
                if (lot != null && Fits(lot))
                {
                    found.Add(lot);
                }
            }

            return found;
        }

        Placement Placing(BuildingInfo building, Pt corner, List<Pt> arriving, List<Pt> leaving)
        {
            double half = building.LotWidth * CellFrame.Size / 2;
            double beyondPlus = building.Footprint.Max(r => r.MaxX) - half, beyondMinus = -half - building.Footprint.Min(r => r.MinX);
            bool wingAtPlusX = beyondPlus >= beyondMinus;
            var front = wingAtPlusX ? leaving : Reversed(arriving);
            var other = wingAtPlusX ? Reversed(arriving) : leaving;
            double wingReach = Math.Min(WingReach, Length(other));
            if (!Straight(other, corner, Unit(PointAlong(other, wingReach) - corner), wingReach))
            {
                return null;
            }

            return new Placement(building, corner, front, wingAtPlusX, Math.Max(beyondPlus, beyondMinus) + SetbackSlack);
        }

        sealed class Placement
        {
            readonly BuildingInfo building;
            readonly Pt corner;
            readonly List<Pt> front;
            readonly bool wingAtPlusX;
            readonly double width;

            public double MaxSetback { get; }

            public Placement(BuildingInfo building, Pt corner, List<Pt> front, bool wingAtPlusX, double maxSetback)
            {
                this.building = building;
                this.corner = corner;
                this.front = front;
                this.wingAtPlusX = wingAtPlusX;
                width = building.LotWidth * CellFrame.Size;
                MaxSetback = maxSetback;
            }

            public bool Beyond(double setback) => setback + width > Length(front) + 1e-6;

            public PlannedLot At(double setback)
            {
                double reach = setback + width;
                if (Beyond(setback))
                {
                    return null;
                }

                var along = Unit(PointAlong(front, reach) - corner);
                if (!Straight(front, corner, along, reach))
                {
                    return null;
                }

                var inward = wingAtPlusX ? new Pt(-along.Y, along.X) : new Pt(along.Y, -along.X);
                var axisX = wingAtPlusX ? along * -1 : along;
                var center = corner + along * (setback + width / 2) + inward * (building.LotDepth * CellFrame.Size / 2);
                return Lot(building, center, axisX, inward);
            }
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

        double OverSidewalk(PlannedLot lot) => lot.Wings(CellSlack).SelectMany(c => c.P).Select(Outside).DefaultIfEmpty(0).Max();

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

        static bool Curved(List<Pt> fromCorner)
        {
            double reach = Math.Min(CurveReach, Length(fromCorner));
            var corner = fromCorner[0];
            return !Straight(fromCorner, corner, Unit(PointAlong(fromCorner, reach) - corner), reach);
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
