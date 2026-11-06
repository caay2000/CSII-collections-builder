using System;
using System.Collections.Generic;

namespace BuildingCollections.Core
{
    public sealed class RoadEdge
    {
        public int Start;
        public int End;
        public List<Pt> Points = new List<Pt>();
    }

    public static class RoadFace
    {
        public static List<Pt> Trace(IReadOnlyList<RoadEdge> edges, Pt click, int maxSteps = 400) =>
            Trace(edges, click, out _, maxSteps);

        public static List<Pt> Trace(IReadOnlyList<RoadEdge> edges, Pt click, out List<(int edge, bool forward)> path, int maxSteps = 400,
            Dictionary<int, List<int>> adjacency = null)
        {
            path = null;
            var near = new List<(double d, int edge, bool forward)>();
            for (int e = 0; e < edges.Count; e++)
            {
                var pts = edges[e].Points;
                double best = double.MaxValue, bestSide = 0;
                for (int i = 1; i < pts.Count; i++)
                {
                    double d = SegmentDistance(pts[i - 1], pts[i], click, out double side);
                    if (d < best)
                    {
                        best = d;
                        bestSide = side;
                    }
                }

                if (best < double.MaxValue)
                {
                    near.Add((best, e, bestSide > 0));
                }
            }

            if (near.Count == 0)
            {
                return null;
            }

            near.Sort((a, b) => a.d.CompareTo(b.d));
            double limit = near[0].d + 0.5;
            var byNode = adjacency ?? Adjacency(edges);
            foreach (var c in near)
            {
                if (c.d > limit)
                {
                    break;
                }

                foreach (bool fwd in new[] { c.forward, !c.forward })
                {
                    var steps = new List<(int, bool)>();
                    var poly = Walk(edges, byNode, c.edge, fwd, click, maxSteps, steps);
                    if (poly != null)
                    {
                        path = steps;
                        return poly;
                    }
                }
            }

            return null;
        }

        public static Dictionary<int, List<int>> Adjacency(IReadOnlyList<RoadEdge> edges)
        {
            var byNode = new Dictionary<int, List<int>>();
            for (int e = 0; e < edges.Count; e++)
            {
                if (edges[e].Points.Count < 2)
                {
                    continue;
                }

                Add(byNode, edges[e].Start, e);
                if (edges[e].End != edges[e].Start)
                {
                    Add(byNode, edges[e].End, e);
                }
            }

            return byNode;
        }

        static List<Pt> Walk(IReadOnlyList<RoadEdge> edges, Dictionary<int, List<int>> byNode, int startEdge, bool startForward, Pt click, int maxSteps, List<(int, bool)> steps)
        {
            var poly = new List<Pt>();
            int edge = startEdge;
            bool forward = startForward;
            for (int step = 0; step < maxSteps; step++)
            {
                steps.Add((edge, forward));
                var pts = edges[edge].Points;
                int n = pts.Count;
                for (int i = poly.Count == 0 ? 0 : 1; i < n; i++)
                {
                    poly.Add(forward ? pts[i] : pts[n - 1 - i]);
                }

                int node = forward ? edges[edge].End : edges[edge].Start;
                Pt inDir = forward ? pts[n - 1] - pts[n - 2] : pts[0] - pts[1];

                int nextEdge = -1;
                bool nextForward = true;
                double bestTurn = double.NegativeInfinity;
                if (byNode.TryGetValue(node, out var around))
                {
                    foreach (int k in around)
                    {
                        var kp = edges[k].Points;
                        if (edges[k].Start == node)
                        {
                            Consider(k, true, kp[1] - kp[0]);
                        }

                        if (edges[k].End == node)
                        {
                            Consider(k, false, kp[kp.Count - 2] - kp[kp.Count - 1]);
                        }
                    }
                }

                void Consider(int k, bool fwd, Pt outDir)
                {
                    bool uTurn = k == edge && fwd != forward;
                    double turn = uTurn ? -10 : Math.Atan2(Pt.Cross(inDir, outDir), Pt.Dot(inDir, outDir));
                    if (turn > bestTurn)
                    {
                        bestTurn = turn;
                        nextEdge = k;
                        nextForward = fwd;
                    }
                }

                if (nextEdge < 0)
                {
                    return null;
                }

                if (nextEdge == startEdge && nextForward == startForward)
                {
                    poly.RemoveAt(poly.Count - 1);
                    return poly.Count >= 3 && Polygon.Contains(poly, click) ? poly : null;
                }

                edge = nextEdge;
                forward = nextForward;
            }

            return null;
        }

        static void Add(Dictionary<int, List<int>> map, int node, int edge)
        {
            if (!map.TryGetValue(node, out var list))
            {
                map[node] = list = new List<int>();
            }

            list.Add(edge);
        }

        static double SegmentDistance(Pt a, Pt b, Pt q, out double side)
        {
            var ab = b - a;
            var aq = q - a;
            side = Pt.Cross(ab, aq);
            double len2 = Pt.Dot(ab, ab);
            double t = len2 > 0 ? Math.Max(0, Math.Min(1, Pt.Dot(aq, ab) / len2)) : 0;
            var p = new Pt(a.X + ab.X * t, a.Y + ab.Y * t);
            var d = q - p;
            return Math.Sqrt(Pt.Dot(d, d));
        }
    }
}
