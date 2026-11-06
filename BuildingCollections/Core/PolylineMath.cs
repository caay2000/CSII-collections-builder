using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    internal static class PolylineMath
    {
        internal static Pt Unit(Pt d)
        {
            double l = Math.Sqrt(Pt.Dot(d, d));
            return l < 1e-9 ? d : new Pt(d.X / l, d.Y / l);
        }

        internal static Pt StartDir(List<Pt> s) => Dir(s[0], s[Math.Min(s.Count - 1, 1)], s);

        internal static Pt EndDir(List<Pt> s) => Dir(s[Math.Max(0, s.Count - 2)], s[s.Count - 1], s);

        static Pt Dir(Pt a, Pt b, List<Pt> s)
        {
            var d = b - a;
            if (Pt.Dot(d, d) < 1e-6)
            {
                d = s[s.Count - 1] - s[0];
            }

            return d;
        }

        internal static double Angle(Pt a, Pt b) => Math.Atan2(Pt.Cross(a, b), Pt.Dot(a, b)) * 180 / Math.PI;

        internal static double Length(List<Pt> s)
        {
            double l = 0;
            for (int i = 1; i < s.Count; i++)
            {
                l += Dist(s[i - 1], s[i]);
            }

            return l;
        }

        internal static double Dist(Pt a, Pt b)
        {
            var d = b - a;
            return Math.Sqrt(Pt.Dot(d, d));
        }
    }
}
