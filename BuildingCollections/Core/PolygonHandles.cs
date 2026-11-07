using System.Collections.Generic;

namespace BuildingCollections.Core
{
    public enum HandleKind
    {
        None,
        Vertex,
        Midpoint,
    }

    public struct PolygonHandle
    {
        public HandleKind Kind;
        public int Index;
        public Pt Point;
        public int Curb;
        public double Arc;

        public static PolygonHandle None => new PolygonHandle { Kind = HandleKind.None, Index = -1, Curb = -1 };
    }

    public static class PolygonHandles
    {
        public static PolygonHandle Find(IReadOnlyList<Pt> verts, IReadOnlyList<int> curbOf, IReadOnlyList<double> arcOf, IReadOnlyList<List<Pt>> curbs,
            bool closed, Pt q, double radius)
        {
            var best = PolygonHandle.None;
            double bestDistance = radius;
            for (int i = 0; i < verts.Count; i++)
            {
                double d = Distance(verts[i], q);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = new PolygonHandle { Kind = HandleKind.Vertex, Index = i, Point = verts[i], Curb = curbOf[i], Arc = arcOf[i] };
                }
            }

            if (best.Kind == HandleKind.Vertex || !closed)
            {
                return best;
            }

            foreach (var m in Midpoints(verts, curbOf, arcOf, curbs))
            {
                double d = Distance(m.Point, q);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = m;
                }
            }

            return best;
        }

        public static List<PolygonHandle> Midpoints(IReadOnlyList<Pt> verts, IReadOnlyList<int> curbOf, IReadOnlyList<double> arcOf, IReadOnlyList<List<Pt>> curbs)
        {
            var result = new List<PolygonHandle>();
            int n = verts.Count;
            if (n < 2)
            {
                return result;
            }

            for (int i = 0; i < n; i++)
            {
                result.Add(Midpoint(verts, curbOf, arcOf, curbs, i));
            }

            return result;
        }

        public static PolygonHandle Midpoint(IReadOnlyList<Pt> verts, IReadOnlyList<int> curbOf, IReadOnlyList<double> arcOf, IReadOnlyList<List<Pt>> curbs, int edge)
        {
            int j = (edge + 1) % verts.Count;
            int curb = curbOf[edge];
            if (curb >= 0 && curb == curbOf[j] && curb < curbs.Count)
            {
                double arc = (arcOf[edge] + arcOf[j]) / 2;
                return new PolygonHandle { Kind = HandleKind.Midpoint, Index = edge, Point = CurbSnap.At(curbs[curb], arc), Curb = curb, Arc = arc };
            }

            var a = verts[edge];
            var b = verts[j];
            return new PolygonHandle { Kind = HandleKind.Midpoint, Index = edge, Point = new Pt((a.X + b.X) / 2, (a.Y + b.Y) / 2), Curb = -1, Arc = 0 };
        }

        static double Distance(Pt a, Pt b)
        {
            var d = a - b;
            return System.Math.Sqrt(Pt.Dot(d, d));
        }
    }
}
