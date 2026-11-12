using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class MixSplit
    {
        public int First { get; private set; }

        public int Second => 1 - First;

        public double FirstShare { get; private set; }

        public static MixSplit Of(IEnumerable<BuildingInfo> buildings)
        {
            var groups = buildings.GroupBy(b => b.Group).ToDictionary(g => g.Key, g => g.First().Share);
            if (!groups.ContainsKey(0) || !groups.ContainsKey(1))
            {
                return null;
            }

            int first = groups[0] >= groups[1] ? 0 : 1;
            return new MixSplit { First = first, FirstShare = groups[first] };
        }

        public static IReadOnlyList<BuildingInfo> InGroup(IReadOnlyList<BuildingInfo> buildings, int group) =>
            buildings.Where(b => b.Group == group).ToList();

        public double MeasuredFirstShare(IEnumerable<PlannedLot> lots, IReadOnlyList<Pt> frontCells)
        {
            var front = FrontCoverage.CellsPerLot(lots, frontCells);
            int all = front.Sum(f => f.cells);
            return all == 0 ? FirstShare : (double)front.Where(f => f.lot.Building.Group == First).Sum(f => f.cells) / all;
        }

        public HashSet<PlannedLot> Surplus(IReadOnlyList<(PlannedLot lot, int cells)> front, int frontTotal, Func<PlanBlock, int> room, Random rng)
        {
            int target = (int)Math.Round(frontTotal * FirstShare, MidpointRounding.AwayFromZero);
            var cells = front.Where(f => f.lot.Building.Group == First).ToDictionary(f => f.lot, f => f.cells);
            var rows = cells.Keys.GroupBy(l => l.Owner).ToDictionary(g => g.Key, g => g.OrderBy(l => l.MinX).ToList());
            int covered = cells.Values.Sum();
            var removed = new HashSet<PlannedLot>();
            foreach (var seed in cells.Keys.OrderBy(_ => rng.Next()).ToList())
            {
                if (removed.Contains(seed))
                {
                    continue;
                }

                var run = Run(rows[seed.Owner], seed, room(seed.Owner), removed, rng);
                int freed = run.Sum(l => cells[l]);
                if (Math.Abs(covered - freed - target) < Math.Abs(covered - target))
                {
                    removed.UnionWith(run);
                    covered -= freed;
                }
            }

            return removed;
        }

        static List<PlannedLot> Run(List<PlannedLot> row, PlannedLot seed, int room, HashSet<PlannedLot> removed, Random rng)
        {
            int lo = row.IndexOf(seed);
            int hi = lo;
            int width = seed.Width;
            while (width < room)
            {
                bool left = lo > 0 && !removed.Contains(row[lo - 1]) && row[lo - 1].MaxX == row[lo].MinX;
                bool right = hi < row.Count - 1 && !removed.Contains(row[hi + 1]) && row[hi + 1].MinX == row[hi].MaxX;
                if (!left && !right)
                {
                    break;
                }

                if (left && (!right || rng.Next(2) == 0))
                {
                    lo--;
                    width += row[lo].Width;
                }
                else
                {
                    hi++;
                    width += row[hi].Width;
                }
            }

            return row.GetRange(lo, hi - lo + 1);
        }
    }
}
