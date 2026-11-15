using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public static class CornerWedges
    {
        const double WedgeDepth = 16;
        const double WedgeSlack = 1.5;
        const double WedgeFactor = 1.25;
        const double WedgeReach = 3;

        public static bool IsWedge(Pt from, Pt to, double length, IEnumerable<CornerResult> corners)
        {
            double allowed = 0;
            foreach (var c in corners.Where(c => Math.Abs(c.Angle - 90) > 5 && c.Angle < 175))
            {
                if (Dist(from, c.At) <= WedgeReach || Dist(to, c.At) <= WedgeReach)
                {
                    allowed += WedgeFactor * WedgeDepth / Math.Abs(Math.Tan(c.Angle * Math.PI / 180)) + WedgeSlack;
                }
            }

            return allowed > 0 && length <= allowed;
        }

        public static double GapsBetweenBuildings(IReadOnlyList<LotStrip> strips, IReadOnlyList<PlannedLot> lots, IReadOnlyList<List<Pt>> sides)
        {
            var corners = sides != null && sides.Count >= 3 ? CornerCheck.Evaluate(sides, lots) : new List<CornerResult>();
            return SidewalkGaps.Find(strips, lots)
                .Where(g => g.Length > EndGapCloser.MaxSpreadSliver && !IsWedge(g.From, g.To, g.Length, corners))
                .Sum(g => g.Length);
        }

        static double Dist(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));
    }
}
