using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    internal static class CurveSnapper
    {
        internal static List<(int from, int to)> Chunks(List<Pt> pts, double tolerance)
        {
            var result = new List<(int, int)>();
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = i + 1;
                while (j + 1 < pts.Count && MaxDeviation(pts, i, j + 1) <= tolerance)
                {
                    j++;
                }

                result.Add((i, j));
                i = j;
            }

            return result;
        }

        public static (List<Pt> pts, List<(int from, int to)> chunks) SnapChunks(List<Pt> src, double tolerance,
            double maxOverlap = 0, IList<string> notes = null, int sideIndex = -1, double? startWedge = null, double? endWedge = null)
        {
            var plain = Snap(src, tolerance, out double rest, out var startSegs);
            int n = plain.chunks.Count;
            if (n < 2 || rest < MinSpread)
            {
                return plain;
            }

            int s0 = n - 1;
            while (s0 > 0 && plain.chunks[s0 - 1].to == plain.chunks[s0].from)
            {
                s0--;
            }

            int joints = n - 1 - s0;
            if (joints < 1)
            {
                notes?.Add($"side {sideIndex}: {rest:F2} m of curve left over; the last segment is detached, left at the end.");
                return plain;
            }

            var cells = plain.chunks.Skip(s0).Select(c => (int)Math.Floor((Dist(plain.pts[c.from], plain.pts[c.to]) + 0.5) / CellFrame.Size)).ToList();
            if (cells.Any(c => c < 1))
            {
                return plain;
            }

            int fromIdx = plain.chunks[s0].from;
            var sub = new List<Pt> { plain.pts[fromIdx] };
            for (int v = startSegs[s0] + 1; v < src.Count; v++)
            {
                sub.Add(src[v]);
            }

            string run = s0 > 0 ? $" (from segment {s0 + 1}, after a very tight turn)" : string.Empty;
            (List<Pt> pts, List<(int from, int to)> chunks) Splice((List<Pt> pts, List<(int from, int to)> chunks) r) =>
                (plain.pts.Take(fromIdx + 1).Concat(r.pts.Skip(1)).ToList(),
                 plain.chunks.Take(s0).Concat(r.chunks.Select(c => (c.from + fromIdx, c.to + fromIdx))).ToList());

            string why = "no overlap";
            if (maxOverlap > 0)
            {
                var more = new List<int>(cells) { 1 };
                var r = TryOneMoreCell(sub, more, null, maxOverlap, out double overlap, out double left);
                if (r.pts == null)
                {
                    why = $"it would need ~{(CellFrame.Size - rest) / more.Count:F2} m overlap per joint and the maximum is {maxOverlap:F2} m";
                }
                else
                {
                    var held = CrowdedCornerJoints(more, overlap, maxOverlap, s0 == 0 ? startWedge : null, endWedge, out double intrusion);
                    if (held == null)
                    {
                        notes?.Add($"side {sideIndex}: {rest:F2} m of curve left over{run}; one more cell, with {overlap:F2} m overlap at each of its {more.Count - 1} joints ({left:F2} m left).");
                        return Splice(r);
                    }

                    int spared = held.Count(h => h);
                    var h2 = TryOneMoreCell(sub, more, held, maxOverlap, out double overlap2, out double left2b);
                    if (h2.pts != null)
                    {
                        notes?.Add($"side {sideIndex}: {rest:F2} m of curve left over{run}; one more cell, with {overlap2:F2} m overlap at each of its {more.Count - 1 - spared} joints and none at {spared} next to a corner ({left2b:F2} m left).");
                        return Splice(h2);
                    }

                    why = $"with one more cell the other street's corner building would reach {intrusion:F2} m into this one and the maximum is {maxOverlap:F2} m";
                }
            }

            var spread = SpreadRest(sub, cells, rest / joints + 1, out double gapPerJoint, out double left2);
            if (spread.pts == null)
            {
                return plain;
            }

            notes?.Add($"side {sideIndex}: {rest:F2} m of curve left over{run} ({why}); spread over {joints} joints ({gapPerJoint:F2} m each; {left2:F2} m left).");
            return Splice(spread);
        }

        const int SearchSteps = 40;

        const int CornerZoneCells = 2;

        static bool[] CrowdedCornerJoints(List<int> cells, double overlap, double maxOverlap, double? startWedge, double? endWedge, out double intrusion)
        {
            intrusion = 0;
            var held = new bool[cells.Count];
            bool any = false;
            int total = cells.Sum();
            foreach (var (wedge, atEnd) in new[] { (startWedge, false), (endWedge, true) })
            {
                if (wedge == null)
                {
                    continue;
                }

                var zone = new List<int>();
                int before = 0;
                for (int i = 1; i < cells.Count; i++)
                {
                    before += cells[i - 1];
                    if ((atEnd ? total - before : before) <= CornerZoneCells)
                    {
                        zone.Add(i);
                    }
                }

                double reach = overlap * zone.Count + wedge.Value;
                if (zone.Count == 0 || reach <= maxOverlap + 1e-6)
                {
                    continue;
                }

                intrusion = Math.Max(intrusion, reach);
                zone.ForEach(i => held[i] = true);
                any = true;
            }

            return any ? held : null;
        }

        static (List<Pt> pts, List<(int from, int to)> chunks) TryOneMoreCell(List<Pt> src, List<int> cells, bool[] held, double maxOverlap, out double overlap, out double left)
        {
            overlap = 0;
            left = 0;
            if (Place(src, cells, -maxOverlap, out _, held).pts == null)
            {
                return (null, null);
            }

            double lo = 0, hi = maxOverlap;
            for (int it = 0; it < SearchSteps; it++)
            {
                double mid = (lo + hi) / 2;
                if (Place(src, cells, -mid, out _, held).pts != null) hi = mid; else lo = mid;
            }

            overlap = hi;
            return Place(src, cells, -hi, out left, held);
        }

        static (List<Pt> pts, List<(int from, int to)> chunks) SpreadRest(List<Pt> src, List<int> cells, double maxGap, out double gap, out double left)
        {
            gap = 0;
            left = 0;
            if (Place(src, cells, 0, out _).pts == null)
            {
                return (null, null);
            }

            double lo = 0, hi = maxGap;
            for (int it = 0; it < SearchSteps; it++)
            {
                double mid = (lo + hi) / 2;
                if (Place(src, cells, mid, out _).pts != null) lo = mid; else hi = mid;
            }

            gap = lo;
            return Place(src, cells, lo, out left);
        }

        public const double TightTolerance = 1.5;

        internal const double MinSpread = 0.3;

        static (List<Pt> pts, List<(int from, int to)> chunks) Place(List<Pt> src, IReadOnlyList<int> cells, double step, out double leftover, bool[] held = null)
        {
            leftover = 0;
            var pts = new List<Pt> { src[0] };
            var chunks = new List<(int, int)>();
            int seg = 0;
            var cur = src[0];
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    MoveAlong(src, ref seg, ref cur, held != null && held[i] ? 0 : step);
                    if (Dist(pts[pts.Count - 1], cur) > 1e-6)
                    {
                        pts.Add(cur);
                    }
                }

                int from = pts.Count - 1;
                if (!PointAtChord(src, seg, cur, cells[i] * CellFrame.Size, out int qs, out var q))
                {
                    return (null, null);
                }

                for (int v = seg + 1; v <= qs; v++)
                {
                    pts.Add(src[v]);
                }

                if (Dist(pts[pts.Count - 1], q) > 1e-6)
                {
                    pts.Add(q);
                }

                chunks.Add((from, pts.Count - 1));
                seg = qs;
                cur = q;
            }

            leftover = Dist(cur, src[seg + 1]);
            for (int v = seg + 1; v + 1 < src.Count; v++)
            {
                leftover += Dist(src[v], src[v + 1]);
            }

            return (pts, chunks);
        }

        static (List<Pt> pts, List<(int from, int to)> chunks) Snap(List<Pt> src, double tolerance, out double rest,
            out List<int> startSegs)
        {
            startSegs = new List<int>();
            var pts = new List<Pt> { src[0] };
            var chunks = new List<(int, int)>();
            int seg = 0;
            var cur = src[0];
            while (seg < src.Count - 1)
            {
                if (!Longest(src, seg, cur, tolerance, out int bestSeg, out var best))
                {
                    if (chunks.Count > 0 && Dist(cur, src[src.Count - 1]) < CellFrame.Size)
                    {
                        break;
                    }

                    if (!PointAtChord(src, seg, cur, CellFrame.Size, out bestSeg, out best) || Deviation(src, cur, seg, best, bestSeg) > TightTolerance)
                    {
                        seg++;
                        cur = src[seg];
                        pts.Add(cur);
                        continue;
                    }
                }

                if (Dist(pts[pts.Count - 1], cur) > 1e-6)
                {
                    pts.Add(cur);
                }

                int from = pts.Count - 1;
                for (int v = seg + 1; v <= bestSeg; v++)
                {
                    pts.Add(src[v]);
                }

                if (Dist(pts[pts.Count - 1], best) > 1e-6)
                {
                    pts.Add(best);
                }

                chunks.Add((from, pts.Count - 1));
                startSegs.Add(seg);
                seg = bestSeg;
                cur = best;
            }

            rest = Dist(cur, src[src.Count - 1]);

            var end = src[src.Count - 1];
            if (Dist(cur, end) > 1e-6)
            {
                if (chunks.Count > 0)
                {
                    var (lf, _) = chunks[chunks.Count - 1];
                    var tail = new List<Pt>(pts.GetRange(lf, pts.Count - lf)) { end };
                    if (MaxDeviation(tail, 0, tail.Count - 1) <= tolerance)
                    {
                        pts.Add(end);
                        chunks[chunks.Count - 1] = (lf, pts.Count - 1);
                        return (pts, chunks);
                    }

                    var a0 = pts[lf];
                    var t = Unit(pts[pts.Count - 1] - a0);
                    double along = Pt.Dot(end - a0, t), off = Math.Abs(Pt.Cross(t, end - a0));
                    if (off <= EndTolerance && along > Dist(a0, pts[pts.Count - 1]) + 1e-6)
                    {
                        pts.Add(new Pt(a0.X + t.X * along, a0.Y + t.Y * along));
                        chunks[chunks.Count - 1] = (lf, pts.Count - 1);
                        return (pts, chunks);
                    }
                }

                pts.Add(end);
            }

            return (pts, chunks);
        }

        internal const double CurveTolerance = 0.5;

        const double EndTolerance = 2.0;

        static void MoveAlong(List<Pt> src, ref int seg, ref Pt cur, double dist)
        {
            if (dist > 0)
            {
                while (true)
                {
                    var b = src[seg + 1];
                    double left = Dist(cur, b);
                    if (dist <= left || seg + 2 >= src.Count)
                    {
                        double f = left < 1e-9 ? 0 : Math.Min(1, dist / left);
                        cur = new Pt(cur.X + (b.X - cur.X) * f, cur.Y + (b.Y - cur.Y) * f);
                        return;
                    }

                    dist -= left;
                    seg++;
                    cur = b;
                }
            }

            dist = -dist;
            while (true)
            {
                var a = src[seg];
                double back = Dist(cur, a);
                if (dist <= back || seg == 0)
                {
                    double f = back < 1e-9 ? 0 : Math.Min(1, dist / back);
                    cur = new Pt(cur.X + (a.X - cur.X) * f, cur.Y + (a.Y - cur.Y) * f);
                    return;
                }

                dist -= back;
                seg--;
                cur = a;
            }
        }

        static bool Longest(List<Pt> src, int seg, Pt cur, double tolerance, out int bestSeg, out Pt best)
        {
            bestSeg = -1;
            best = cur;
            for (int k = 1; ; k++)
            {
                if (!PointAtChord(src, seg, cur, k * CellFrame.Size, out int qs, out var q) || Deviation(src, cur, seg, q, qs) > tolerance)
                {
                    break;
                }

                bestSeg = qs;
                best = q;
            }

            return bestSeg >= 0;
        }

        static bool PointAtChord(List<Pt> src, int seg, Pt cur, double dist, out int qs, out Pt q)
        {
            for (int i = seg; i < src.Count - 1; i++)
            {
                var a = i == seg ? cur : src[i];
                var b = src[i + 1];
                if (Dist(cur, b) < dist)
                {
                    continue;
                }

                var d = b - a;
                double dd = Pt.Dot(d, d);
                if (dd < 1e-12)
                {
                    continue;
                }

                var f = a - cur;
                double bq = 2 * Pt.Dot(f, d), c = Pt.Dot(f, f) - dist * dist;
                double disc = Math.Max(0, bq * bq - 4 * dd * c);
                double t = (-bq + Math.Sqrt(disc)) / (2 * dd);
                t = Math.Min(1, Math.Max(0, t));
                qs = i;
                q = new Pt(a.X + d.X * t, a.Y + d.Y * t);
                return true;
            }

            qs = -1;
            q = cur;
            return false;
        }

        static double Deviation(List<Pt> src, Pt cur, int seg, Pt q, int qs)
        {
            var d = q - cur;
            double len = Math.Sqrt(Pt.Dot(d, d));
            if (len < 1e-6)
            {
                return 0;
            }

            double worst = 0;
            for (int v = seg + 1; v <= qs; v++)
            {
                worst = Math.Max(worst, Math.Abs(Pt.Cross(d, src[v] - cur)) / len);
            }

            return worst;
        }

        static double MaxDeviation(List<Pt> pts, int i, int j)
        {
            var a = pts[i];
            var d = pts[j] - a;
            double len = Math.Sqrt(Pt.Dot(d, d));
            if (len < 1e-6)
            {
                return 0;
            }

            double worst = 0;
            for (int k = i + 1; k < j; k++)
            {
                worst = Math.Max(worst, Math.Abs(Pt.Cross(d, pts[k] - a)) / len);
            }

            return worst;
        }

        internal static List<Pt> Trim(List<Pt> src, double fromStart, double fromEnd)
        {
            var pts = fromStart > 0 ? CutStart(src, fromStart) : new List<Pt>(src);
            if (fromEnd > 0)
            {
                pts.Reverse();
                pts = CutStart(pts, fromEnd);
                pts.Reverse();
            }

            return pts;
        }

        static List<Pt> CutStart(List<Pt> src, double dist)
        {
            for (int i = 1; i < src.Count; i++)
            {
                double seg = Dist(src[i - 1], src[i]);
                if (dist < seg)
                {
                    var cut = src[i - 1] + (src[i] - src[i - 1]) * (dist / seg);
                    var result = new List<Pt> { cut };
                    result.AddRange(src.Skip(i));
                    return result;
                }

                dist -= seg;
            }

            return new List<Pt> { src[src.Count - 1] };
        }
    }
}
