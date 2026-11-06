using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    internal sealed class EndChain
    {
        const double TouchInset = -0.3;

        public readonly List<PlannedLot> Lots;
        public readonly int Own;
        public readonly bool CornerOnly;

        EndChain(List<PlannedLot> lots, int own, bool cornerOnly)
        {
            Lots = lots;
            Own = own;
            CornerOnly = cornerOnly;
        }

        public PlannedLot End => Lots[0];

        public List<PlannedLot> Moving => CornerOnly ? new List<PlannedLot>() : Lots;

        public static EndChain Build(PlanBlock block, IReadOnlyList<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, int dir)
        {
            var mine = lots.Where(p => p.Owner == block).OrderBy(p => p.MinX).ToList();
            var chain = new List<PlannedLot>();
            bool reachedStripEnd = true;
            for (int i = dir > 0 ? mine.Count - 1 : 0; i >= 0 && i < mine.Count; i -= dir)
            {
                var p = mine[i];
                if (p.Corner || p.Shift != 0 || (chain.Count > 0 && !Adjacent(p, chain[chain.Count - 1], dir)))
                {
                    reachedStripEnd = false;
                    break;
                }

                chain.Add(p);
            }

            bool cornerOnly = chain.Count == 0 && mine.Count == 1 && mine[0].Corner && mine[0].Shift == 0;
            if (cornerOnly)
            {
                chain.Add(mine[0]);
            }

            if (chain.Count == 0)
            {
                return null;
            }

            int own = chain.Count;
            if (!cornerOnly && reachedStripEnd)
            {
                ExtendAcrossStrips(block, chain, lots, blocks, dir);
            }

            return new EndChain(chain, own, cornerOnly);
        }

        static bool Adjacent(PlannedLot p, PlannedLot last, int dir) => dir > 0 ? p.MaxX == last.MinX : p.MinX == last.MaxX;

        static void ExtendAcrossStrips(PlanBlock block, List<PlannedLot> chain, IReadOnlyList<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, int dir)
        {
            var strips = blocks.Where(b => b.Strip != null).Select(b => b.Strip).ToList();
            var cur = block;
            while (cur.Strip != null)
            {
                var nextStrip = LotStrip.Next(cur.Strip, strips, -dir);
                if (nextStrip == null)
                {
                    return;
                }

                var next = blocks.First(o => o.Strip == nextStrip);
                var theirs = lots.Where(p => p.Owner == next).OrderBy(p => p.MinX).ToList();
                int added = 0;
                for (int i = dir > 0 ? theirs.Count - 1 : 0; i >= 0 && i < theirs.Count; i -= dir)
                {
                    var p = theirs[i];
                    var last = chain[chain.Count - 1];
                    bool touching = added == 0
                        ? Quad.Touch(p.Footprint(TouchInset), last.Footprint(TouchInset))
                        : Adjacent(p, last, dir);
                    if (p.Corner || p.Shift != 0 || !touching || chain.Contains(p))
                    {
                        return;
                    }

                    chain.Add(p);
                    added++;
                }

                if (added == 0)
                {
                    return;
                }

                cur = next;
            }
        }
    }
}
