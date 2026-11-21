using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public readonly struct FenceSegment
    {
        public readonly Pt A;
        public readonly Pt B;

        public FenceSegment(Pt a, Pt b)
        {
            A = a;
            B = b;
        }

        public double Length => Math.Sqrt(Pt.Dot(B - A, B - A));
    }

    public static class ParcelFences
    {
        const double JoinDistance = 2.5;
        const double ParallelTolerance = 0.05;
        const double MinLength = 1;
        const double StreetMargin = 1.5;
        const double NeighbourShare = 0.5;

        const double MaxCurveSine = 0.42;

        enum EdgeKind
        {
            Rear,
            Left,
            Right,
        }

        sealed class Edge
        {
            public readonly Pt A;
            public readonly Pt Dir;
            public readonly double Length;
            public readonly int Lot;
            public readonly bool CornerSide;
            public readonly EdgeKind Kind;

            public Edge(Pt a, Pt b, int lot, bool cornerSide, EdgeKind kind = EdgeKind.Rear)
            {
                A = a;
                Length = new FenceSegment(a, b).Length;
                Dir = Length > 0 ? (b - a) * (1 / Length) : new Pt(1, 0);
                Lot = lot;
                CornerSide = cornerSide;
                Kind = kind;
            }

            public Pt At(double t) => A + Dir * t;

            public Pt B => At(Length);

            public Pt Middle => At(Length / 2);

            public Edge Piece(double from, double to) => new Edge(At(from), At(to), Lot, CornerSide, Kind);
        }

        public static List<FenceSegment> Plan(IReadOnlyList<PlannedLot> lots, IReadOnlyList<Pt> outline = null)
        {
            var edges = JoinCurvedSides(lots.SelectMany(EdgesOf).ToList());
            var candidates = edges
                .Where(e => !IsStreetSide(e, edges) && !NearOutline(e, outline))
                .OrderByDescending(e => e.Length)
                .ThenBy(e => e.Lot)
                .ToList();

            var accepted = new List<Edge>();
            foreach (var edge in candidates)
            {
                accepted.AddRange(Uncovered(edge, accepted));
            }

            return accepted.Select(e => new FenceSegment(e.A, e.B)).ToList();
        }

        static IEnumerable<Edge> EdgesOf(PlannedLot lot, int index)
        {
            var q = lot.Footprint(0);
            yield return new Edge(q.P[1], q.P[2], index, lot.Corner, EdgeKind.Right);
            yield return new Edge(q.P[2], q.P[3], index, false);
            yield return new Edge(q.P[0], q.P[3], index, lot.Corner, EdgeKind.Left);
        }

        static List<Edge> JoinCurvedSides(List<Edge> edges)
        {
            var result = edges.Where(e => e.Kind == EdgeKind.Rear).ToList();
            var lefts = edges.Where(e => e.Kind == EdgeKind.Left).ToList();
            foreach (var right in edges.Where(e => e.Kind == EdgeKind.Right))
            {
                var left = lefts
                    .Where(l => IsCurvedNeighbour(right, l))
                    .OrderBy(l => new FenceSegment(right.A, l.A).Length)
                    .FirstOrDefault();
                if (left == null)
                {
                    result.Add(right);
                    continue;
                }

                lefts.Remove(left);
                double length = Math.Max(right.Length, left.Length);
                result.Add(new Edge((right.A + left.A) * 0.5, (right.At(length) + left.At(length)) * 0.5, right.Lot, false, EdgeKind.Right));
            }

            result.AddRange(lefts);
            return result;
        }

        static bool IsCurvedNeighbour(Edge right, Edge left)
        {
            double sine = Math.Abs(Pt.Cross(right.Dir, left.Dir));
            return left.Lot != right.Lot
                && Pt.Dot(right.Dir, left.Dir) > 0
                && sine > ParallelTolerance
                && sine <= MaxCurveSine
                && new FenceSegment(right.A, left.A).Length <= JoinDistance;
        }

        static bool IsStreetSide(Edge edge, IReadOnlyList<Edge> all)
        {
            if (!edge.CornerSide)
            {
                return false;
            }

            var spans = all.Where(o => o.Lot != edge.Lot).Select(o => Span(edge, o)).Where(s => s.HasValue).Select(s => s.Value);
            return Merge(spans).Sum(s => s.To - s.From) < edge.Length * NeighbourShare;
        }

        static bool NearOutline(Edge edge, IReadOnlyList<Pt> outline)
        {
            if (outline == null || outline.Count < 2)
            {
                return false;
            }

            for (int i = 0; i < outline.Count; i++)
            {
                if (PointSegmentDistance(edge.Middle, outline[i], outline[(i + 1) % outline.Count]) <= StreetMargin)
                {
                    return true;
                }
            }

            return false;
        }

        static double PointSegmentDistance(Pt p, Pt a, Pt b)
        {
            var ab = b - a;
            double squared = Pt.Dot(ab, ab);
            double t = squared > 0 ? Math.Max(0, Math.Min(1, Pt.Dot(p - a, ab) / squared)) : 0;
            return new FenceSegment(p, a + ab * t).Length;
        }

        static IEnumerable<Edge> Uncovered(Edge edge, IReadOnlyList<Edge> accepted)
        {
            double cursor = 0;
            foreach (var span in Merge(accepted.Select(o => Span(edge, o)).Where(s => s.HasValue).Select(s => s.Value)))
            {
                if (span.From - cursor >= MinLength)
                {
                    yield return edge.Piece(cursor, span.From);
                }

                cursor = Math.Max(cursor, span.To);
            }

            if (edge.Length - cursor >= MinLength)
            {
                yield return edge.Piece(cursor, edge.Length);
            }
        }

        static (double From, double To)? Span(Edge edge, Edge other)
        {
            if (Math.Abs(Pt.Cross(edge.Dir, other.Dir)) > ParallelTolerance)
            {
                return null;
            }

            var toA = other.A - edge.A;
            var toB = other.B - edge.A;
            if (Math.Max(Math.Abs(Pt.Cross(edge.Dir, toA)), Math.Abs(Pt.Cross(edge.Dir, toB))) > JoinDistance)
            {
                return null;
            }

            double a = Pt.Dot(edge.Dir, toA), b = Pt.Dot(edge.Dir, toB);
            double from = Math.Max(0, Math.Min(a, b)), to = Math.Min(edge.Length, Math.Max(a, b));
            return to > from ? (from, to) : ((double, double)?)null;
        }

        static List<(double From, double To)> Merge(IEnumerable<(double From, double To)> spans)
        {
            var merged = new List<(double From, double To)>();
            foreach (var span in spans.OrderBy(s => s.From))
            {
                if (merged.Count > 0 && span.From <= merged[merged.Count - 1].To)
                {
                    var last = merged[merged.Count - 1];
                    merged[merged.Count - 1] = (last.From, Math.Max(last.To, span.To));
                }
                else
                {
                    merged.Add(span);
                }
            }

            return merged;
        }
    }
}
