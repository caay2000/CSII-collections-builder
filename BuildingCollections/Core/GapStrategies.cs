using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    internal sealed class EndGap
    {
        public PlanBlock Block;
        public EndChain Chain;
        public float Gap;
        public int Dir;
        public LotFitTest Fits;
        public List<Quad> Beside;
        public float Base;
        public Bounds Area;

        public float Missing => (float)CellFrame.Size - Gap;

        public string Where => Dir > 0 ? "end" : "start";
    }

    internal sealed class GapTools
    {
        public List<PlannedLot> Lots;
        public IReadOnlyList<BuildingInfo> Plain;
        public float Overlap;
        public Random Rng;
        public List<string> Log;
    }

    internal interface IGapStrategy
    {
        bool TryClose(EndGap gap);
    }

    internal sealed class WidenStrategy : IGapStrategy
    {
        const float StrictOverlap = FootprintMask.StrictOverlap;
        const float Tolerance = 0.01f;

        readonly GapTools tools;

        public WidenStrategy(GapTools tools)
        {
            this.tools = tools;
        }

        public bool TryClose(EndGap gap)
        {
            var chain = gap.Chain.Lots;
            int n = gap.Chain.Moving.Count;
            if (tools.Overlap <= StrictOverlap || n == 0)
            {
                return false;
            }

            int dir = gap.Dir;
            float missing = gap.Missing;
            float perJoint = missing / (n + 1);
            if (perJoint > tools.Overlap + Tolerance)
            {
                return false;
            }

            float JointShift(int j) => -dir * missing * (n - j) / (n + 1);
            var stayOk = new bool?[n];
            var moveOk = new bool?[n];
            bool Stays(int j) => stayOk[j] ??= gap.Fits.Fits(chain[j], chain[j].Box, gap.Base + JointShift(j), perJoint + StrictOverlap, null);
            bool Moves(int j) => moveOk[j] ??= gap.Fits.Fits(chain[j], chain[j].Box.Moved(dir), gap.Base + JointShift(j), perJoint + StrictOverlap, null);

            foreach (int k in Enumerable.Range(0, gap.Chain.Own).OrderBy(_ => tools.Rng.Next()))
            {
                bool rest = true;
                for (int j = 0; j < n && rest; j++)
                {
                    rest = j == k || (j < k ? Moves(j) : Stays(j));
                }

                if (rest && TryWidenOne(gap, k, n, perJoint, JointShift))
                {
                    return true;
                }
            }

            return false;
        }

        bool TryWidenOne(EndGap gap, int k, int n, float perJoint, Func<int, float> jointShift)
        {
            var chain = gap.Chain.Lots;
            int dir = gap.Dir;
            var cur = chain[k];
            int w = cur.Width, dep = cur.Depth;
            var options = tools.Plain.Where(b => b.LotWidth == w + 1 && b.LotDepth <= dep)
                .OrderBy(b => b.LotDepth == dep ? 0 : 1).ThenBy(_ => tools.Rng.Next()).ToList();
            foreach (var b in options)
            {
                var box = new LotBox(dir > 0 ? cur.MinX : cur.MinX - 1, cur.MinY, dir > 0 ? cur.MaxX + 1 : cur.MaxX, cur.MinY + b.LotDepth);
                if (!gap.Fits.Fits(cur, box, gap.Base + jointShift(k), perJoint + StrictOverlap, null))
                {
                    continue;
                }

                int own = gap.Chain.Own;
                tools.Log.Add($"  {gap.Gap:F1} m gap at the {gap.Where} closed: {cur.Building.PrefabName} -> {b.PrefabName} ({w}->{w + 1} wide), {n} buildings squeezed{(n > own ? $" ({n - own} from previous segments)" : string.Empty)}, {perJoint:F2} m overlap per joint.");
                for (int j = 0; j < n; j++)
                {
                    var p = chain[j];
                    if (j < k)
                    {
                        p.MinX += dir;
                        p.MaxX += dir;
                    }

                    p.Shift = gap.Base + jointShift(j);
                }

                cur.Building = b;
                cur.MinX = box.MinX;
                cur.MaxX = box.MaxX;
                cur.MaxY = box.MaxY;
                return true;
            }

            return false;
        }
    }

    internal sealed class InsertStrategy : IGapStrategy
    {
        const float StrictOverlap = FootprintMask.StrictOverlap;
        const float Tolerance = 0.01f;
        const int SpareJoints = 2;

        readonly GapTools tools;

        public InsertStrategy(GapTools tools)
        {
            this.tools = tools;
        }

        public bool TryClose(EndGap gap)
        {
            if (tools.Overlap <= StrictOverlap)
            {
                return false;
            }

            var moving = gap.Chain.Moving;
            int least = Math.Max(0, (int)Math.Ceiling(gap.Missing / tools.Overlap) - SpareJoints);
            for (int m = moving.Count; m >= least; m--)
            {
                if (TryInsert(gap, moving.Take(m).ToList()))
                {
                    return true;
                }
            }

            return false;
        }

        bool TryInsert(EndGap gap, List<PlannedLot> moving)
        {
            var block = gap.Block;
            var end = gap.Chain.End;
            int dir = gap.Dir;
            float missing = gap.Missing;
            int count = moving.Count + 1;
            float perJoint = missing / (count + 1);
            if (perJoint > tools.Overlap + Tolerance)
            {
                return false;
            }

            int minX = dir > 0 ? end.MaxX : end.MinX - 1;
            int minY = end.MinY;
            if (minX < block.AreaX || minX + 1 > block.AreaX + block.Grid.Width)
            {
                return false;
            }

            int dep = end.Depth;
            var options = tools.Plain.Where(b => b.LotWidth == 1 && b.LotDepth <= dep && b.LotDepth <= block.Grid.Depth)
                .OrderBy(b => b.LotDepth == dep ? 0 : 1).ThenBy(_ => tools.Rng.Next()).ToList();
            float Shift(int j) => -dir * missing * (count - j) / (count + 1);
            for (int j = 0; j < moving.Count; j++)
            {
                if (!gap.Fits.Fits(moving[j], moving[j].Box, gap.Base + Shift(j + 1), perJoint + StrictOverlap, null))
                {
                    return false;
                }
            }

            var probe = new PlannedLot { Owner = block };
            foreach (var b in options)
            {
                var box = new LotBox(minX, minY, minX + 1, minY + b.LotDepth);
                if (!gap.Fits.Fits(probe, box, gap.Base + Shift(0), perJoint + StrictOverlap, gap.Beside))
                {
                    continue;
                }

                var loose = probe.Footprint(box, gap.Base + Shift(0), tools.Overlap + StrictOverlap);
                if (gap.Beside.Any(q => Quad.Touch(q, loose)))
                {
                    continue;
                }

                for (int j = 0; j < moving.Count; j++)
                {
                    moving[j].Shift = gap.Base + Shift(j + 1);
                }

                tools.Lots.Add(new PlannedLot
                {
                    Building = b,
                    Owner = block,
                    MinX = box.MinX,
                    MinY = box.MinY,
                    MaxX = box.MaxX,
                    MaxY = box.MaxY,
                    Shift = gap.Base + Shift(0),
                });
                tools.Log.Add($"  {gap.Gap:F1} m gap at the {gap.Where} closed: inserted {b.PrefabName} (1x{b.LotDepth}) after {end.Building.PrefabName}, {moving.Count} buildings squeezed, {perJoint:F2} m overlap per joint.");
                return true;
            }

            return false;
        }
    }

    internal sealed class SpreadStrategy : IGapStrategy
    {
        const float StrictOverlap = FootprintMask.StrictOverlap;
        public const float MaxSliver = 1.5f;

        readonly GapTools tools;

        public SpreadStrategy(GapTools tools)
        {
            this.tools = tools;
        }

        public bool TryClose(EndGap gap)
        {
            if (tools.Overlap > StrictOverlap)
            {
                return false;
            }

            var moving = gap.Chain.Moving;
            int least = Math.Max(1, (int)Math.Ceiling(gap.Gap / MaxSliver) - 1);
            for (int m = moving.Count; m >= least; m--)
            {
                if (TrySpread(gap, moving.Take(m).ToList()))
                {
                    return true;
                }
            }

            return false;
        }

        bool TrySpread(EndGap gap, List<PlannedLot> moving)
        {
            int n = moving.Count;
            float sliver = gap.Gap / (n + 1);
            if (n == 0 || sliver > MaxSliver)
            {
                return false;
            }

            float Shift(int j) => gap.Dir * (gap.Gap - sliver * (j + 1));
            for (int j = 0; j < n; j++)
            {
                var p = moving[j];
                if (!gap.Fits.Fits(p, p.Box, Shift(j), StrictOverlap, j == 0 ? gap.Beside : null))
                {
                    return false;
                }
            }

            for (int j = 0; j < n; j++)
            {
                moving[j].Shift = Shift(j);
            }

            tools.Log.Add($"  {gap.Gap:F1} m gap at the {gap.Where} spread: {n} buildings shifted, {sliver:F2} m slivers.");
            return true;
        }
    }
}
