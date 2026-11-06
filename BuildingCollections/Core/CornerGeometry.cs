using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    internal static class CornerGeometry
    {
        public const double AcuteDepth = 16;

        internal static double AcuteSetback(Pt dIn, Pt dOut)
        {
            double turn = Math.Atan2(Pt.Cross(dIn, dOut), Pt.Dot(dIn, dOut));
            double alpha = Math.PI - turn;
            if (alpha >= 85 * Math.PI / 180 || alpha <= 20 * Math.PI / 180)
            {
                return 0;
            }

            return Math.Min(2 * CellFrame.Size, AcuteDepth / Math.Tan(alpha));
        }

        internal static Pt ExtendToCorner(Pt from, Pt end, Pt c, Pt d)
        {
            var t = end - from;
            var u = d - c;
            double lt = Math.Sqrt(Pt.Dot(t, t)), lu = Math.Sqrt(Pt.Dot(u, u));
            if (lt < 1e-6 || lu < 1e-6)
            {
                return end;
            }

            t = new Pt(t.X / lt, t.Y / lt);
            u = new Pt(u.X / lu, u.Y / lu);
            double den = Pt.Cross(t, u);
            if (Math.Abs(den) < 0.2)
            {
                return end;
            }

            double s = Pt.Cross(c - end, u) / den;
            return s >= 0 && s <= 1.5 ? new Pt(end.X + t.X * s, end.Y + t.Y * s) : end;
        }
    }
}
