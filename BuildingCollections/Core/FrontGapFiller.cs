using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class FrontGapFiller
    {
        const float StrictOverlap = FootprintMask.StrictOverlap;
        const float ShiftStep = 0.1f;
        const float MaxShift = 4f;
        const int MaxWidth = 2;

        readonly List<PlannedLot> lots;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> thinned;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<BuildingInfo> plain;
        readonly float overlap;
        readonly Random rng;
        readonly List<string> log;

        public FrontGapFiller(List<PlannedLot> lots, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> thinned, IReadOnlyList<Pt> outline,
            IReadOnlyList<BuildingInfo> plain, float overlap, Random rng, List<string> log)
        {
            this.lots = lots;
            this.existing = existing;
            this.thinned = thinned;
            this.outline = outline;
            this.plain = plain;
            this.overlap = overlap;
            this.rng = rng;
            this.log = log;
        }

        public int Fill(PlanBlock block)
        {
            if (!block.IsVirtual || outline == null || block.Grid == null)
            {
                return 0;
            }

            int added = 0;
            for (int x = 0; x < block.Grid.Width; x++)
            {
                int minX = block.AreaX + x;
                if (Covered(block, minX) || !TryInsert(block, minX))
                {
                    continue;
                }

                added++;
            }

            return added;
        }

        bool Covered(PlanBlock block, int minX)
        {
            var center = block.Frame.CellCenter(minX, block.AreaY);
            return lots.Any(p => p.Footprint(0).Contains(center)) || existing.Any(q => q.Contains(center));
        }

        IEnumerable<float> Shifts()
        {
            yield return 0;
            for (float s = ShiftStep; s <= MaxShift + 1e-4f; s += ShiftStep)
            {
                yield return s;
                yield return -s;
            }
        }

        bool TryInsert(PlanBlock block, int minX)
        {
            var others = lots.Select(p => p.Footprint(0)).ToList();
            var ahead = new LotFitTest(existing, thinned, outline, others, +1);
            var behind = new LotFitTest(existing, thinned, outline, others, -1);
            var probe = new PlannedLot { Owner = block };
            var options = plain.Where(b => b.LotWidth <= MaxWidth && b.LotDepth <= block.Grid.Depth)
                .OrderByDescending(b => b.LotWidth).ThenByDescending(b => b.LotDepth).ThenBy(_ => rng.Next()).ToList();
            foreach (var b in options)
            {
                var box = new LotBox(minX, block.AreaY, minX + b.LotWidth, block.AreaY + b.LotDepth);
                foreach (float shift in Shifts())
                {
                    if (!ahead.Fits(probe, box, shift, overlap + StrictOverlap, null) || !behind.Fits(probe, box, shift, overlap + StrictOverlap, null))
                    {
                        continue;
                    }

                    lots.Add(new PlannedLot { Building = b, Owner = block, MinX = box.MinX, MinY = box.MinY, MaxX = box.MaxX, MaxY = box.MaxY, Shift = shift });
                    log.Add($"  {block.Label}: uncovered cell {minX - block.AreaX} filled with {b.PrefabName} ({b.LotWidth}x{b.LotDepth}), shifted {shift:F1} m.");
                    return true;
                }
            }

            return false;
        }
    }
}
