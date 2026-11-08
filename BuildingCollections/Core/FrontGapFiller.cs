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
        const double FacadeDepth = 0.25;
        const double Reach = CellFrame.Size * MaxWidth + MaxShift;

        readonly List<PlannedLot> lots;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> thinned;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<BuildingInfo> plain;
        readonly float overlap;
        readonly Random rng;
        readonly List<string> log;
        List<PlannedLot> near = new List<PlannedLot>();
        List<Quad> nearExisting = new List<Quad>();
        List<Pt> nearThinned = new List<Pt>();

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

            var area = Bounds.Of(block).Grow(Reach);
            near = lots.Where(p => area.Overlaps(p.Footprint(0))).ToList();
            nearExisting = existing.Where(area.Overlaps).ToList();
            nearThinned = thinned.Where(area.Contains).ToList();
            int added = 0;
            for (int x = 0; x < block.Grid.Width; x++)
            {
                int minX = block.AreaX + x;
                if (Covered(block, minX) || !InsideOutline(block, minX) || !(TryInsert(block, minX) || TryInsertOverlappingBacks(block, minX)))
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
            return near.Any(p => p.Footprint(0).Contains(center)) || nearExisting.Any(q => q.Contains(center));
        }

        bool InsideOutline(PlanBlock block, int minX) => Polygon.Contains(outline, block.Frame.CellCenter(minX, block.AreaY));

        List<List<BuildingInfo>> BySize(IEnumerable<BuildingInfo> buildings) =>
            buildings.GroupBy(b => (b.LotWidth, b.LotDepth)).Select(g => g.ToList()).ToList();

        BuildingInfo AnyOf(List<BuildingInfo> sameSize) => sameSize[rng.Next(sameSize.Count)];

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
            var others = near.Select(p => p.Footprint(0)).ToList();
            var ahead = new LotFitTest(nearExisting, nearThinned, outline, others, +1);
            var behind = new LotFitTest(nearExisting, nearThinned, outline, others, -1);
            var probe = new PlannedLot { Owner = block };
            var sizes = BySize(plain.Where(b => b.LotWidth <= MaxWidth && b.LotDepth <= block.Grid.Depth))
                .OrderByDescending(g => g[0].LotWidth).ThenByDescending(g => g[0].LotDepth).ToList();
            foreach (var sameSize in sizes)
            {
                var box = new LotBox(minX, block.AreaY, minX + sameSize[0].LotWidth, block.AreaY + sameSize[0].LotDepth);
                foreach (float shift in Shifts())
                {
                    if (!ahead.Fits(probe, box, shift, overlap + StrictOverlap, null) || !behind.Fits(probe, box, shift, overlap + StrictOverlap, null))
                    {
                        continue;
                    }

                    Add(block, AnyOf(sameSize), box, shift, string.Empty);
                    return true;
                }
            }

            return false;
        }

        bool TryInsertOverlappingBacks(PlanBlock block, int minX)
        {
            if (overlap <= StrictOverlap)
            {
                return false;
            }

            var others = near.Select(p => p.Footprint(0)).Concat(nearExisting).ToList();
            var probe = new PlannedLot { Owner = block };
            var sizes = BySize(plain.Where(b => b.LotWidth == 1 && b.LotDepth <= block.Grid.Depth))
                .OrderBy(g => g[0].LotDepth).ToList();
            foreach (int wanted in new[] { 2, 1 })
            {
                foreach (var sameSize in sizes)
                {
                    var box = new LotBox(minX, block.AreaY, minX + 1, block.AreaY + sameSize[0].LotDepth);
                    foreach (float shift in Shifts())
                    {
                        if (!FacadeClear(probe, box, shift, others) || TouchedSides(probe, box, shift, others) < wanted || !Clear(probe, box, shift))
                        {
                            continue;
                        }

                        Add(block, AnyOf(sameSize), box, shift, ", backs overlapping");
                        return true;
                    }
                }
            }

            return false;
        }

        bool FacadeClear(PlannedLot probe, LotBox box, float shift, List<Quad> others)
        {
            var q = probe.Footprint(box, shift, 0);
            var ax = probe.Owner.Frame.AxisX * (overlap + StrictOverlap);
            var ay = probe.Owner.Frame.AxisY * FacadeDepth;
            var left = q.P[0] + ax;
            var right = q.P[1] - ax;
            var facade = new Quad(left, right, right + ay, left + ay);
            return !others.Any(o => Quad.Touch(o, facade));
        }

        int TouchedSides(PlannedLot probe, LotBox box, float shift, List<Quad> others)
        {
            var q = probe.Footprint(box, shift, 0);
            var ax = probe.Owner.Frame.AxisX;
            var ay = probe.Owner.Frame.AxisY * FacadeDepth;
            bool Reaches(Pt edge, Pt outward, double reach)
            {
                var far = edge + outward * reach;
                var strip = new Quad(edge, far, far + ay, edge + ay);
                return others.Any(o => Quad.Touch(o, strip));
            }

            int SideScore(Pt edge, Pt outward) => !Reaches(edge, outward, CellFrame.Size) || Reaches(edge, outward, StrictOverlap) ? 1 : 0;
            return SideScore(q.P[0], ax * -1) + SideScore(q.P[1], ax);
        }

        bool Clear(PlannedLot probe, LotBox box, float shift)
        {
            var q = probe.Footprint(box, shift, StrictOverlap);
            var loose = probe.Footprint(box, shift, overlap + StrictOverlap);
            return !nearExisting.Any(e => Quad.Touch(e, q))
                && !near.Any(p => !SameSide(p.Owner, probe.Owner) && Quad.Touch(p.Footprint(p.MinX, p.MinY, p.MaxX, p.MinY + 1, p.Shift, 0), loose))
                && !nearThinned.Any(q.Contains)
                && q.P.Select((c, i) => (c, n: q.P[(i + 1) % q.P.Length])).All(e => Polygon.Contains(outline, e.c) && Polygon.Contains(outline, (e.c + e.n) * 0.5));
        }

        static bool SameSide(PlanBlock a, PlanBlock b) =>
            a.Strip != null && b.Strip != null && a.Strip.BlockIndex == b.Strip.BlockIndex && a.Strip.SideIndex == b.Strip.SideIndex;

        void Add(PlanBlock block, BuildingInfo b, LotBox box, float shift, string note)
        {
            var lot = new PlannedLot { Building = b, Owner = block, MinX = box.MinX, MinY = box.MinY, MaxX = box.MaxX, MaxY = box.MaxY, Shift = shift };
            lots.Add(lot);
            near.Add(lot);
            log.Add($"  {block.Label}: uncovered cell {box.MinX - block.AreaX} filled with {b.PrefabName} ({b.LotWidth}x{b.LotDepth}), shifted {shift:F1} m{note}.");
        }
    }
}
