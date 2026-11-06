using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
internal sealed class Side
    {
        public List<Pt> Points = new List<Pt>();
        public bool StartCorner;
        public bool EndCorner;
        public bool StartRounded;
        public bool EndRounded;
        public bool Frontage = true;
    }

    internal static class SideMerger
    {
        public static List<Side> Merge(IReadOnlyList<List<Pt>> sides, IReadOnlyList<bool> frontage, double mergeAngleDeg, double cornerAngleDeg)
        {
            var valid = new List<List<Pt>>();
            var front = new List<bool>();
            for (int i = 0; i < sides.Count; i++)
            {
                var s = sides[i];
                if (s.Count >= 2 && Length(s) > 0.5)
                {
                    valid.Add(s);
                    front.Add(frontage == null || (i < frontage.Count && frontage[i]));
                }
            }

            int n = valid.Count;
            var result = new List<Side>();
            if (n == 0)
            {
                return result;
            }

            var turn = new double[n];
            var cut = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                turn[i] = Angle(EndDir(valid[i]), StartDir(valid[j]));
                cut[i] = Math.Abs(turn[i]) >= mergeAngleDeg || front[i] != front[j];
            }

            bool Corner(int i) => front[i] && front[(i + 1) % n] && IsCorner(turn[i], cornerAngleDeg);

            int start = 0;
            for (int i = 0; i < n; i++)
            {
                if (cut[i])
                {
                    start = (i + 1) % n;
                    break;
                }
            }

            Side cur = null;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                int prev = (i - 1 + n) % n;
                if (cur == null)
                {
                    cur = new Side { StartCorner = Corner(prev), Frontage = front[i] };
                }

                foreach (var p in valid[i])
                {
                    if (cur.Points.Count == 0 || Dist(cur.Points[cur.Points.Count - 1], p) > 0.01)
                    {
                        cur.Points.Add(p);
                    }
                }

                if (cut[i] || k == n - 1)
                {
                    cur.EndCorner = Corner(i);
                    if (cur.Points.Count >= 2)
                    {
                        result.Add(cur);
                    }

                    cur = null;
                }
            }

            return result;
        }

        public const double TightBendTurn = 60;
        public const double TightBendLength = 40;
        const double BendVertexTurn = 2;
        const double BendLeadLength = 2 * CellFrame.Size;
        const double BendLeadTurn = 3;
        const double BendLeadVertexTurn = 1;

        static bool StraightLead(List<Pt> pts, int at, int step)
        {
            double total = 0, walked = 0;
            for (int i = at; i - 1 >= 0 && i + 1 < pts.Count && walked < BendLeadLength; i += step)
            {
                double turn = Math.Abs(Angle(pts[i] - pts[i - 1], pts[i + 1] - pts[i]));
                if (turn > (i == at ? BendVertexTurn : BendLeadVertexTurn))
                {
                    return false;
                }

                total += turn;
                walked += Dist(pts[i], pts[i + step]);
            }

            return total <= BendLeadTurn;
        }

        public static List<Side> SplitTightBends(List<Side> merged, IList<string> notes)
        {
            var result = new List<Side>();
            foreach (var side in merged)
            {
                var rest = side;
                while (rest.Frontage && FindTightBend(rest.Points) is (int from, int to) bend)
                {
                    var head = new Side { Points = rest.Points.Take(bend.from + 1).ToList(), StartCorner = rest.StartCorner, StartRounded = rest.StartRounded, EndCorner = true, EndRounded = true, Frontage = true };
                    var tail = new Side { Points = rest.Points.Skip(bend.to).ToList(), StartCorner = true, StartRounded = true, EndCorner = rest.EndCorner, EndRounded = rest.EndRounded, Frontage = true };
                    var a = rest.Points[bend.from];
                    var b = rest.Points[bend.to];
                    notes?.Add($"tight bend from ({a.X:F1},{a.Y:F1}) to ({b.X:F1},{b.Y:F1}) treated as a corner.");
                    result.Add(head);
                    rest = tail;
                }

                result.Add(rest);
            }

            return result;
        }

        static (int from, int to)? FindTightBend(List<Pt> pts)
        {
            int i = 1;
            while (i < pts.Count - 1)
            {
                if (Angle(pts[i] - pts[i - 1], pts[i + 1] - pts[i]) < BendVertexTurn)
                {
                    i++;
                    continue;
                }

                int j = i;
                double total = 0;
                while (j < pts.Count - 1)
                {
                    double turn = Angle(pts[j] - pts[j - 1], pts[j + 1] - pts[j]);
                    if (turn < BendVertexTurn)
                    {
                        break;
                    }

                    total += turn;
                    j++;
                }

                int from = i, to = j - 1;
                double length = Length(pts.Skip(from).Take(to - from + 1).ToList());
                if (total >= TightBendTurn && total < 170 && length <= TightBendLength
                    && Length(pts.Take(from + 1).ToList()) >= CellFrame.Size && Length(pts.Skip(to).ToList()) >= CellFrame.Size
                    && StraightLead(pts, from - 1, -1) && StraightLead(pts, to + 1, 1))
                {
                    return (from, to);
                }

                i = j;
            }

            return null;
        }

        static bool IsCorner(double turn, double cornerAngleDeg) => turn >= cornerAngleDeg && turn < 170;
    }
}
