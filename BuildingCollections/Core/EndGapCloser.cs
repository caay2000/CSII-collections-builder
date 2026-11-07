using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class EndGapCloser
    {
        const float Step = 0.1f;
        const float CoarseStep = 1f;
        const float CellSize = (float)CellFrame.Size;
        const float StrictOverlap = FootprintMask.StrictOverlap;
        const float MinGap = 0.3f;
        const float BesideMargin = 1f;
        const double JoinSlack = 1;
        const float SlideStep = 0.25f;
        const float MaxSlide = 4f;
        const double InsideInset = 0.1;
        const double Reach = 16;
        public const float MaxSpreadSliver = SpreadStrategy.MaxSliver;

        readonly List<PlannedLot> lots;
        readonly IReadOnlyList<PlanBlock> blocks;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> thinned;
        readonly IReadOnlyList<Pt> outline;
        readonly GapTools tools;
        readonly IReadOnlyList<IGapStrategy> strategies;
        readonly IReadOnlyList<IGapStrategy> closing;

        public EndGapCloser(List<PlannedLot> lots, IReadOnlyList<PlanBlock> blocks, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> thinned,
            IReadOnlyList<Pt> outline, IReadOnlyList<BuildingInfo> plain, float overlap, Random rng, List<string> log)
        {
            this.lots = lots;
            this.blocks = blocks;
            this.existing = existing;
            this.thinned = thinned;
            this.outline = outline;
            tools = new GapTools { Lots = lots, Plain = plain, Overlap = overlap, Rng = rng, Log = log };
            closing = new IGapStrategy[] { new WidenStrategy(tools), new InsertStrategy(tools) };
            strategies = overlap > StrictOverlap ? closing : closing.Append(new SpreadStrategy(tools)).ToList();
        }

        IEnumerable<LotStrip> Strips => blocks.Where(b => b.Strip != null).Select(b => b.Strip);

        public int Close(PlanBlock block)
        {
            if (!block.IsVirtual || outline == null || !lots.Any(p => p.Owner == block))
            {
                return 0;
            }

            int closed = 0;
            var st = block.Strip;
            bool startFirst = st != null && EndSlack(st, -1) > EndSlack(st, +1);
            foreach (int dir in startFirst ? new[] { -1, +1 } : new[] { +1, -1 })
            {
                if (!JoinsNextChunk(block, dir) || HasSlack(block, dir))
                {
                    closed += CloseEnd(block, dir);
                }
            }

            return closed;
        }

        bool HasSlack(PlanBlock block, int dir)
        {
            var st = block.Strip;
            if (st == null)
            {
                return false;
            }

            if (EndSlack(st, dir) >= JoinSlack)
            {
                return true;
            }

            var next = LotStrip.Next(st, Strips, dir);
            return next != null && EndSlack(next, -dir) >= JoinSlack;
        }

        static double EndSlack(LotStrip st, int dir) => dir > 0 ? st.ChunkLength - st.Offset - st.Width * CellSize : st.Offset;

        bool JoinsNextChunk(PlanBlock block, int dir) => block.Strip != null && LotStrip.Next(block.Strip, Strips, dir) != null;

        int CloseEnd(PlanBlock block, int dir)
        {
            var chain = EndChain.Build(block, lots, blocks, dir);
            if (chain == null)
            {
                return 0;
            }

            var area = Area(block, chain);
            var nearExisting = existing.Where(area.Overlaps).ToList();
            var nearThinned = thinned.Where(area.Contains).ToList();
            var otherQuads = lots.Where(p => !chain.Lots.Contains(p)).Select(p => p.Footprint(0)).Where(area.Overlaps).ToList();
            var gap = new EndGap
            {
                Block = block,
                Chain = chain,
                Dir = dir,
                Fits = new LotFitTest(nearExisting, nearThinned, outline, otherQuads, dir),
                Beside = Beside(chain.End, otherQuads, nearExisting, dir),
                Area = area,
            };
            gap.Gap = Measure(gap);
            if (gap.Gap < MinGap || gap.Gap >= CellSize - Step - 1e-3f)
            {
                return 0;
            }

            if (strategies.Any(s => s.TryClose(gap)) || TrySlidingTheFarCorner(gap))
            {
                return 1;
            }

            int n = chain.Moving.Count;
            tools.Log.Add(tools.Overlap > StrictOverlap
                ? $"  {gap.Gap:F1} m gap at the {gap.Where}: neither widening one nor inserting a one-cell building fits with at most {n + 2} joints ({gap.Missing / (n + 2):F2} m per joint, maximum {tools.Overlap:F1} m); left at the {gap.Where} instead of spreading it between buildings."
                : $"  {gap.Gap:F1} m gap at the {gap.Where}: without overlap it can be neither closed nor spread; left as is.");
            return 0;
        }

        bool TrySlidingTheFarCorner(EndGap gap)
        {
            var anchor = FarCorner(gap);
            if (anchor == null)
            {
                return false;
            }

            int dir = gap.Dir;
            float room = gap.Block.Strip == null ? 0 : (float)Math.Min(MaxSlide, EndSlack(gap.Block.Strip, -dir));
            var area = gap.Area.Union(anchor.Footprint(0)).Grow(Reach);
            var nearExisting = existing.Where(area.Overlaps).ToList();
            var nearThinned = thinned.Where(area.Contains).ToList();
            var rest = lots.Where(p => p != anchor && !gap.Chain.Lots.Contains(p)).Select(p => p.Footprint(0)).Where(area.Overlaps).ToList();
            var back = new LotFitTest(nearExisting, nearThinned, outline, rest, -dir);
            var besideAnchor = Beside(anchor, rest, nearExisting, -dir);
            for (float slide = SlideStep; slide <= room + 1e-3f; slide += SlideStep)
            {
                float baseShift = -dir * slide;
                if (!CanSlide(anchor, baseShift, back, besideAnchor))
                {
                    return false;
                }

                var slid = Slid(gap, anchor, rest, nearExisting, nearThinned, baseShift);
                if (slid.Gap >= CellSize - Step - 1e-3f)
                {
                    return false;
                }

                if (closing.Any(s => s.TryClose(slid)))
                {
                    anchor.Shift = baseShift;
                    tools.Log.Add($"  (the corner building {anchor.Building.PrefabName} at the other end slid {slide:F1} m towards its corner to make room)");
                    return true;
                }
            }

            return false;
        }

        PlannedLot FarCorner(EndGap gap)
        {
            var chain = gap.Chain;
            if (tools.Overlap <= StrictOverlap || chain.CornerOnly || chain.Own != chain.Lots.Count)
            {
                return null;
            }

            var far = chain.Lots[chain.Lots.Count - 1];
            return lots.FirstOrDefault(p => p.Owner == gap.Block && p.Corner && p.Shift == 0
                && (gap.Dir > 0 ? p.MaxX == far.MinX : p.MinX == far.MaxX));
        }

        bool CanSlide(PlannedLot anchor, float shift, LotFitTest back, List<Quad> beside) =>
            back.Fits(anchor, anchor.Box, shift, StrictOverlap, beside) && Inside(anchor, shift) && !HitsAFront(anchor, shift);

        EndGap Slid(EndGap gap, PlannedLot anchor, List<Quad> rest, List<Quad> nearExisting, List<Pt> nearThinned, float baseShift)
        {
            var others = rest.Append(anchor.Footprint(anchor.Box, baseShift, 0)).ToList();
            var slid = new EndGap
            {
                Block = gap.Block,
                Chain = gap.Chain,
                Dir = gap.Dir,
                Base = baseShift,
                Fits = new LotFitTest(nearExisting, nearThinned, outline, others, gap.Dir),
                Beside = Beside(gap.Chain.End, others, nearExisting, gap.Dir),
                Area = gap.Area,
            };
            slid.Gap = Measure(slid);
            return slid;
        }

        bool HitsAFront(PlannedLot lot, float shift)
        {
            var q = lot.Footprint(lot.Box, shift, StrictOverlap);
            var area = Bounds.Of(q).Grow(1);
            return lots.Any(p => p != lot && area.Overlaps(p.Footprint(0)) && p.Owner != lot.Owner && Quad.Touch(p.Footprint(p.MinX, p.MinY, p.MaxX, p.MinY + 1, p.Shift, StrictOverlap), q));
        }

        bool Inside(PlannedLot lot, float shift) => lot.Footprint(lot.Box, shift, InsideInset).P.All(c => Polygon.Contains(outline, c));

        static Bounds Area(PlanBlock block, EndChain chain)
        {
            var area = block.Grid == null ? Bounds.Of(chain.End.Footprint(0)) : Bounds.Of(block);
            foreach (var p in chain.Lots)
            {
                area = area.Union(p.Footprint(0));
            }

            return area.Grow(Reach);
        }

        List<Quad> Beside(PlannedLot end, List<Quad> otherQuads, List<Quad> nearExisting, int dir)
        {
            var axis = end.Owner.Frame.AxisX;
            var endQuad = end.Footprint(end.Box, 0, StrictOverlap);
            float front = Proj(end.Footprint(end.Box, 0, 0), axis, dir, max: true);
            return nearExisting.Concat(otherQuads)
                .Where(q => Quad.Touch(q, endQuad) && Proj(q, axis, dir, max: false) < front - BesideMargin).ToList();
        }

        static float Measure(EndGap gap)
        {
            var end = gap.Chain.End;
            bool Free(float g) => gap.Fits.Fits(end, end.Box, gap.Base + gap.Dir * g, StrictOverlap, gap.Beside);
            float measured = 0;
            while (measured + CoarseStep + Step < CellSize && Free(measured + CoarseStep))
            {
                measured += CoarseStep;
            }

            while (measured + Step < CellSize && Free(measured + Step))
            {
                measured += Step;
            }

            return measured;
        }

        static float Proj(Quad q, Pt axis, int sign, bool max)
        {
            float best = max ? float.MinValue : float.MaxValue;
            foreach (var c in q.P)
            {
                float v = (float)(c.X * axis.X + c.Y * axis.Y) * sign;
                best = max ? Math.Max(best, v) : Math.Min(best, v);
            }

            return best;
        }
    }
}
