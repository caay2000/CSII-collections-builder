using System;
using System.Collections.Generic;
using System.Linq;
namespace BuildingCollections.Core
{
    public enum CornerState
    {
        Corner,
        Front,
        Empty,
    }

    public sealed class CornerResult
    {
        public Pt At;
        public double Angle;
        public CornerState State;
        public double Distance;
    }

    public static class CornerCheck
    {
        const double MinTurn = 30;
        const double RoundedTurn = 60;
        const double RoundedMaxLength = 50;

        public static List<CornerResult> Evaluate(IReadOnlyList<List<Pt>> sides, IReadOnlyList<PlannedLot> lots)
        {
            var results = new List<CornerResult>();
            for (int i = 0; i < sides.Count; i++)
            {
                var a = sides[i];
                var b = sides[(i + 1) % sides.Count];
                var dIn = a[a.Count - 1] - a[a.Count - 2];
                var dOut = b[1] - b[0];
                double turn = Math.Atan2(Pt.Cross(dIn, dOut), Pt.Dot(dIn, dOut)) * 180 / Math.PI;
                if (Math.Abs(turn) < MinTurn)
                {
                    continue;
                }

                results.Add(Judge(b[0], 180 - turn, lots));
            }

            foreach (var side in sides.Where(s => s.Count > 2))
            {
                double total = TotalTurn(side);
                if (Math.Abs(total) >= RoundedTurn && Length(side) <= RoundedMaxLength)
                {
                    double radius = Length(side) / (Math.Abs(total) * Math.PI / 180);
                    results.Add(Judge(PointAt(side, Length(side) / 2), 180 - total, lots, radius));
                }
            }

            return results;
        }

        static double Reach(double angle)
        {
            double a = angle * Math.PI / 180;
            double setback = angle < 85 && angle > 20 ? Math.Min(16, 16 / Math.Tan(a)) : 0;
            return 10 + setback * 1.5;
        }

        static CornerResult Judge(Pt at, double angle, IReadOnlyList<PlannedLot> lots, double radius = 0)
        {
            double reach = Reach(angle) + radius;
            var near = lots.Select(l => (lot: l, dist: Distance(at, l.Footprint(0)))).Where(x => x.dist <= reach).ToList();
            var corner = near.Where(x => x.lot.Corner).OrderBy(x => x.dist).FirstOrDefault();
            var closest = near.OrderBy(x => x.dist).FirstOrDefault();
            return new CornerResult
            {
                At = at,
                Angle = angle,
                State = corner.lot != null ? CornerState.Corner : closest.lot != null ? CornerState.Front : CornerState.Empty,
                Distance = corner.lot != null ? corner.dist : closest.lot != null ? closest.dist : reach,
            };
        }

        static double TotalTurn(List<Pt> side)
        {
            double total = 0;
            for (int i = 1; i + 1 < side.Count; i++)
            {
                var d1 = side[i] - side[i - 1];
                var d2 = side[i + 1] - side[i];
                total += Math.Atan2(Pt.Cross(d1, d2), Pt.Dot(d1, d2));
            }

            return total * 180 / Math.PI;
        }

        static double Length(List<Pt> side)
        {
            double len = 0;
            for (int i = 1; i < side.Count; i++)
            {
                var d = side[i] - side[i - 1];
                len += Math.Sqrt(Pt.Dot(d, d));
            }

            return len;
        }

        static Pt PointAt(List<Pt> side, double at)
        {
            for (int i = 1; i < side.Count; i++)
            {
                var d = side[i] - side[i - 1];
                double len = Math.Sqrt(Pt.Dot(d, d));
                if (at <= len)
                {
                    return side[i - 1] + d * (at / len);
                }

                at -= len;
            }

            return side[side.Count - 1];
        }

        static double Distance(Pt p, Quad q)
        {
            if (q.Contains(p))
            {
                return 0;
            }

            double best = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                best = Math.Min(best, Segment(p, q.P[i], q.P[(i + 1) % 4]));
            }

            return best;
        }

        static double Segment(Pt p, Pt a, Pt b)
        {
            var d = b - a;
            double len2 = Pt.Dot(d, d);
            double t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, Pt.Dot(p - a, d) / len2));
            var e = p - (a + d * t);
            return Math.Sqrt(Pt.Dot(e, e));
        }
    }
}
