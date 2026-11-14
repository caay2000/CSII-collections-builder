using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class RoadEdge
    {
        public int Start;
        public int End;
        public List<Pt> Points = new List<Pt>();
    }

    public struct TurnOption
    {
        public int Edge;
        public bool Forward;
        public Pt Out;
        public Pt Far;
        public double Turn;
    }

    public sealed class TurnStep
    {
        public int Edge;
        public bool Forward;
        public int Node;
        public Pt In;
        public List<TurnOption> Options;
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

                var options = Options(edges, byNode, edge, forward, out _, out _);
                if (options.Count == 0)
                {
                    return null;
                }

                var best = options.OrderByDescending(o => o.Turn).First();
                int nextEdge = best.Edge;
                bool nextForward = best.Forward;

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

        public static List<TurnStep> Explain(IReadOnlyList<RoadEdge> edges, IReadOnlyList<(int edge, bool forward)> path)
        {
            var byNode = Adjacency(edges);
            return path.Select(st =>
            {
                var options = Options(edges, byNode, st.edge, st.forward, out int node, out var inDir);
                return new TurnStep { Edge = st.edge, Forward = st.forward, Node = node, In = inDir, Options = options };
            }).ToList();
        }

        const double NearReverse = 170 * Math.PI / 180;
        const double FarDistance = 30;
        const double Tied = 10 * Math.PI / 180;

        static List<TurnOption> Options(IReadOnlyList<RoadEdge> edges, Dictionary<int, List<int>> byNode, int edge, bool forward, out int node, out Pt inDir)
        {
            var pts = edges[edge].Points;
            int n = pts.Count;
            node = forward ? edges[edge].End : edges[edge].Start;
            inDir = forward ? pts[n - 1] - pts[n - 2] : pts[0] - pts[1];
            var options = new List<TurnOption>();
            if (!byNode.TryGetValue(node, out var around))
            {
                return options;
            }

            var arrival = new Arrival(edges[edge], forward, inDir);
            foreach (int k in around)
            {
                if (edges[k].Start == node)
                {
                    options.Add(Option(edges, k, true, edge, forward, arrival));
                }

                if (edges[k].End == node)
                {
                    options.Add(Option(edges, k, false, edge, forward, arrival));
                }
            }

            SeparateTied(options);
            return options;
        }

        static void SeparateTied(List<TurnOption> options)
        {
            var ranked = Enumerable.Range(0, options.Count).Where(i => options[i].Turn > -Math.PI - 1).OrderBy(i => options[i].Turn).ToList();
            int start = 0;
            for (int i = 1; i <= ranked.Count; i++)
            {
                if (i < ranked.Count && options[ranked[i]].Turn - options[ranked[i - 1]].Turn < Tied)
                {
                    continue;
                }

                if (i - start > 1)
                {
                    Reorder(options, ranked.GetRange(start, i - start));
                }

                start = i;
            }
        }

        static void Reorder(List<TurnOption> options, List<int> group)
        {
            var heading = group.Aggregate(new Pt(0, 0), (sum, i) => sum + Unit(options[i].Out));
            var turns = group.Select(i => options[i].Turn).OrderBy(t => t).ToList();
            var byLeft = group.OrderBy(i => Pt.Cross(heading, options[i].Far)).ToList();
            for (int r = 0; r < byLeft.Count; r++)
            {
                var o = options[byLeft[r]];
                o.Turn = turns[r];
                options[byLeft[r]] = o;
            }
        }

        static Pt Unit(Pt d)
        {
            double len = Math.Sqrt(Pt.Dot(d, d));
            return len > 0 ? new Pt(d.X / len, d.Y / len) : d;
        }

        sealed class Arrival
        {
            public readonly Pt At;
            public readonly Pt In;
            public readonly Pt FarBack;

            public Arrival(RoadEdge edge, bool forward, Pt inDir)
            {
                var path = Leaving(edge, !forward);
                At = path[0];
                In = inDir;
                FarBack = PointAlong(path, FarDistance) - At;
            }
        }

        static TurnOption Option(IReadOnlyList<RoadEdge> edges, int k, bool fwd, int edge, bool forward, Arrival arrival)
        {
            var path = Leaving(edges[k], fwd);
            var outDir = path[1] - path[0];
            var far = PointAlong(path, FarDistance);
            if (k == edge && fwd != forward)
            {
                return new TurnOption { Edge = k, Forward = fwd, Out = outDir, Far = far, Turn = -10 };
            }

            double turn = Angle(arrival.In, outDir);
            if (Math.Abs(turn) > NearReverse)
            {
                turn = Angle(new Pt(-arrival.FarBack.X, -arrival.FarBack.Y), far - arrival.At);
            }

            return new TurnOption { Edge = k, Forward = fwd, Out = outDir, Far = far, Turn = turn };
        }

        static double Angle(Pt from, Pt to) => Math.Atan2(Pt.Cross(from, to), Pt.Dot(from, to));

        static List<Pt> Leaving(RoadEdge edge, bool fromStart)
        {
            var pts = new List<Pt>(edge.Points);
            if (!fromStart)
            {
                pts.Reverse();
            }

            return pts;
        }

        static Pt PointAlong(List<Pt> path, double distance)
        {
            double left = distance;
            for (int i = 1; i < path.Count; i++)
            {
                var d = path[i] - path[i - 1];
                double len = Math.Sqrt(Pt.Dot(d, d));
                if (len >= left && len > 0)
                {
                    double t = left / len;
                    return new Pt(path[i - 1].X + d.X * t, path[i - 1].Y + d.Y * t);
                }

                left -= len;
            }

            return path[path.Count - 1];
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
