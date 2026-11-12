using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class PreviewPlanner
    {
        readonly PreviewRequest request;
        readonly PreviewResult result = new PreviewResult();
        readonly List<(Quad quad, Quad front, PlanBlock owner)> footprints = new List<(Quad, Quad, PlanBlock)>();
        readonly List<(PlanBlock block, PlanGrid grid)> grids = new List<(PlanBlock, PlanGrid)>();
        readonly List<(Quad front, Quad deep, Pt center, PlanBlock owner)> fronts = new List<(Quad, Quad, Pt, PlanBlock)>();
        readonly Stopwatch clock = new Stopwatch();
        int seedIndex;
        int frontDepth = 1;
        readonly MixSplit mix;
        readonly IReadOnlyList<BuildingInfo> firstPlain;
        readonly IReadOnlyList<BuildingInfo> firstCorners;

        PreviewPlanner(PreviewRequest request)
        {
            this.request = request;
            mix = MixSplit.Of(request.Plain.Concat(request.Corners));
            firstPlain = Own(request.Plain);
            firstCorners = mix == null ? request.Corners : MixSplit.InGroup(request.Corners, mix.First);
        }

        IReadOnlyList<BuildingInfo> Own(IReadOnlyList<BuildingInfo> plain)
        {
            var own = mix == null ? plain : MixSplit.InGroup(plain, mix.First);
            return own.Count > 0 ? own : plain;
        }

        public static PreviewResult Plan(PreviewRequest request) => new PreviewPlanner(request).Run();

        PreviewResult Run()
        {
            clock.Start();
            BuildGrids();
            ReserveFronts();
            Lap("grids");
            var cornerPlans = ReserveCorners();
            Fill(cornerPlans);
            Lap($"corners and fill ({grids.Count} strips)");
            if (mix != null)
            {
                BalanceMix();
                Lap("mix");
            }

            ApplyDensity();
            Lap("density");
            CloseEndGaps();
            Lap("end gaps");
            FillFrontGaps();
            Lap("front gaps");
            MeasureFrontage();
            Lap("frontage");
            Diagnose();
            Lap("diagnostics");
            return result;
        }

        void Lap(string phase)
        {
            result.Timings.Add($"{phase} {clock.ElapsedMilliseconds} ms");
            clock.Restart();
        }

        bool WedgesMayOverlap => request.Overlap > FootprintMask.StrictOverlap;

        const double MaskReach = 4;

        List<Quad> Others(PlanBlock block, bool backToBack = false)
        {
            var area = Bounds.Of(block).Grow(MaskReach);
            return footprints
                .Where(f => f.owner != block && area.Overlaps(f.quad) && !(WedgesMayOverlap && VirtualLots.BacksMayOverlap(f.owner.Strip, block.Strip)))
                .Select(f => backToBack && BackToBack(f.owner, block) ? f.front : f.quad)
                .ToList();
        }

        const double BackToBackCos = -0.5;

        static bool BackToBack(PlanBlock a, PlanBlock b) =>
            a.Strip != null && b.Strip != null && !SameSide(a, b) && Pt.Dot(a.Frame.AxisY, b.Frame.AxisY) < BackToBackCos;

        const int CornerCells = 2;

        void ReserveFronts()
        {
            frontDepth = GridPlanner.ReservedFrontDepth(request.Plain);
            foreach (var (block, grid) in grids)
            {
                var st = block.Strip;
                bool startCorner = st != null && st.ChunkIndex == 0;
                bool endCorner = st != null && st.ChunkIndex == st.ChunkCount - 1;
                int exempt = startCorner && endCorner ? Math.Min(CornerCells, (grid.Width - 1) / 2) : CornerCells;
                for (int x = 0; x < grid.Width; x++)
                {
                    bool nearStart = startCorner && x < exempt;
                    bool nearEnd = endCorner && x >= grid.Width - exempt;
                    if (grid.Free[x, 0] && !nearStart && !nearEnd)
                    {
                        int x0 = block.AreaX + x;
                        fronts.Add((block.Frame.Cell(x0, block.AreaY, 0), block.Frame.Rect(x0, block.AreaY, x0 + 1, block.AreaY + ReservedRows(grid, x, frontDepth)),
                            block.Frame.CellCenter(x0, block.AreaY), block));
                    }
                }
            }
        }

        int ReservedRows(PlanGrid grid, int x, int wanted)
        {
            int rows = 1;
            while (rows < wanted && rows < grid.Depth && grid.Free[x, rows])
            {
                rows++;
            }

            return rows;
        }

        List<Quad> OtherFronts(PlanBlock block, bool backToBack = false)
        {
            var area = Bounds.Of(block).Grow(MaskReach);
            return fronts
                .Where(f => f.owner != block && area.Overlaps(f.deep))
                .Select(f => SameSide(f.owner, block) || (backToBack && BackToBack(f.owner, block)) ? f.front : f.deep)
                .ToList();
        }

        static bool SameSide(PlanBlock a, PlanBlock b) =>
            a.Strip != null && b.Strip != null && a.Strip.BlockIndex == b.Strip.BlockIndex && a.Strip.SideIndex == b.Strip.SideIndex;

        int MaskOthers(PlanBlock block, PlanGrid grid, bool withFronts)
        {
            int masked = 0;
            var strict = Masked(block, grid, false, withFronts);
            bool facing = footprints.Any(f => BackToBack(f.owner, block)) || (withFronts && fronts.Any(f => BackToBack(f.owner, block)));
            var relaxed = facing ? Masked(block, grid, true, withFronts) : strict;
            for (int x = 0; x < grid.Width; x++)
            {
                bool rescue = FreeDepth(strict, x) < frontDepth && FreeDepth(relaxed, x) >= frontDepth;
                for (int y = 0; y < grid.Depth; y++)
                {
                    bool free = rescue ? y < frontDepth && relaxed.Free[x, y] : strict.Free[x, y];
                    if (grid.Free[x, y] && !free)
                    {
                        grid.Free[x, y] = false;
                        masked++;
                    }
                }
            }

            return masked;
        }

        PlanGrid Masked(PlanBlock block, PlanGrid grid, bool backToBack, bool withFronts)
        {
            var g = Clone(grid);
            FootprintMask.Apply(g, block, Others(block, backToBack), request.Overlap);
            if (withFronts)
            {
                FootprintMask.Apply(g, block, OtherFronts(block, backToBack), request.Overlap, 1);
            }

            return g;
        }

        int NextSeed() => request.Seed + 7919 * seedIndex++;

        void BuildGrids()
        {
            var seen = new Dictionary<(long, long), List<Pt>>();
            foreach (var block in request.Blocks.OrderByDescending(b => b.Priority))
            {
                var grid = Clone(block.Grid);
                FootprintMask.Apply(grid, block, request.Existing, FootprintMask.StrictOverlap);
                grids.Add((block, grid));
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.Free[x, 0])
                    {
                        continue;
                    }

                    var pt = block.Frame.CellCenter(block.AreaX + x, block.AreaY);
                    if (!NearSeen(seen, pt))
                    {
                        result.FrontCells.Add(pt);
                        var key = Bucket(pt);
                        if (!seen.TryGetValue(key, out var bucket))
                        {
                            seen[key] = bucket = new List<Pt>();
                        }

                        bucket.Add(pt);
                    }
                }
            }
        }

        const double SameCell = 2;

        static (long, long) Bucket(Pt p) => ((long)Math.Floor(p.X / SameCell), (long)Math.Floor(p.Y / SameCell));

        static bool NearSeen(Dictionary<(long, long), List<Pt>> seen, Pt pt)
        {
            var (bx, by) = Bucket(pt);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (seen.TryGetValue((bx + dx, by + dy), out var bucket) && bucket.Any(o => Pt.Dot(o - pt, o - pt) < SameCell * SameCell))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static PlanGrid Clone(PlanGrid g) => new PlanGrid
        {
            Width = g.Width, Depth = g.Depth, Free = (bool[,])g.Free.Clone(), LeftCorner = g.LeftCorner, RightCorner = g.RightCorner,
        };

        Quad FrontRow(PlanBlock block, Placement p) =>
            block.Frame.Rect(block.AreaX + p.X, block.AreaY + p.Y, block.AreaX + p.X + p.Building.LotWidth, block.AreaY + p.Y + 1);

        Quad Footprint(PlanBlock block, Placement p) =>
            block.Frame.Rect(block.AreaX + p.X, block.AreaY + p.Y, block.AreaX + p.X + p.Building.LotWidth, block.AreaY + p.Y + p.Building.LotDepth);

        Dictionary<PlanBlock, List<Placement>> ReserveCorners()
        {
            var plans = new Dictionary<PlanBlock, List<Placement>>();
            foreach (var (block, grid) in grids)
            {
                MaskOthers(block, grid, withFronts: false);
                var corners = GridPlanner.PlanCorners(grid, firstPlain, firstCorners, NextSeed());
                plans[block] = corners;
                footprints.AddRange(corners.Select(p => (Footprint(block, p), FrontRow(block, p), block)));
            }

            fronts.RemoveAll(f => footprints.Any(fp => fp.owner != f.owner && fp.quad.Contains(f.center)));

            return plans;
        }

        void Fill(Dictionary<PlanBlock, List<Placement>> cornerPlans)
        {
            foreach (var (block, grid) in grids)
            {
                int masked = MaskOthers(block, grid, withFronts: true);
                string depths = FreeDepths(grid);
                var plan = GridPlanner.Plan(grid, PlainFor(block, grid, firstPlain), firstCorners, 100, NextSeed(), cornerPlans[block]);
                Replace(block, plan);
                result.Log.Add($"  {block.Label}: grid {grid.Width}x{grid.Depth}, {grid.FreeCount()} free ({masked} covered by other blocks), corners L={grid.LeftCorner} R={grid.RightCorner}, free depth {depths} -> {Describe(plan)}");
            }
        }

        static string Describe(IEnumerable<Placement> plan) =>
            string.Join("; ", plan.Select(p => $"x{p.X} {p.Building}{(p.IsCorner ? " [corner]" : string.Empty)}"));

        void Replace(PlanBlock block, IEnumerable<Placement> plan)
        {
            footprints.RemoveAll(f => f.owner == block);
            result.Lots.RemoveAll(l => l.Owner == block);
            foreach (var p in plan)
            {
                footprints.Add((Footprint(block, p), FrontRow(block, p), block));
                result.Lots.Add(new PlannedLot
                {
                    Building = p.Building,
                    Owner = block,
                    MinX = block.AreaX + p.X,
                    MinY = block.AreaY + p.Y,
                    MaxX = block.AreaX + p.X + p.Building.LotWidth,
                    MaxY = block.AreaY + p.Y + p.Building.LotDepth,
                    Corner = p.IsCorner,
                });
            }
        }

        static Placement Kept(PlannedLot lot) => new Placement
        {
            X = lot.MinX - lot.Owner.AreaX, Y = lot.MinY - lot.Owner.AreaY, Building = lot.Building, IsCorner = lot.Corner,
        };

        void BalanceMix()
        {
            var front = FrontCoverage.CellsPerLot(result.Lots, result.FrontCells);
            double before = mix.MeasuredFirstShare(result.Lots, result.FrontCells);
            var secondPlain = MixSplit.InGroup(request.Plain, mix.Second);
            var secondCorners = MixSplit.InGroup(request.Corners, mix.Second);
            var depth = grids.ToDictionary(g => g.block, g => Enumerable.Range(0, g.grid.Width).Select(x => FreeDepth(g.grid, x)).DefaultIfEmpty(0).Max());
            int Room(PlanBlock block) => secondPlain.Concat(secondCorners).Where(b => b.LotDepth <= depth[block]).Select(b => b.LotWidth).DefaultIfEmpty(1).Min();
            var surplus = mix.Surplus(front, result.FrontCells.Count, Room, new Random(request.Seed ^ 0x5bd1e995));
            result.Lots.RemoveAll(surplus.Contains);
            result.Log.Add($"  mix: first pass with group {mix.First} only covers {before:P0} of the filled street cells (target {mix.FirstShare:P0}); {surplus.Count} of its buildings make room for group {mix.Second}.");

            foreach (var (block, grid) in grids)
            {
                var kept = result.Lots.Where(l => l.Owner == block).Select(Kept).ToList();
                footprints.RemoveAll(f => f.owner == block);
                MaskOthers(block, grid, withFronts: true);
                var plan = GridPlanner.Refill(grid, PlainFor(block, grid, secondPlain), secondCorners, firstPlain, NextSeed(), kept);
                Replace(block, plan);
                var added = plan.Where(p => !kept.Contains(p)).ToList();
                if (added.Count > 0)
                {
                    result.Log.Add($"  {block.Label}: mix refill -> {Describe(added)}");
                }
            }
        }

        const double TypicalWidth = 2;
        const double MinSlack = 1;

        IReadOnlyList<BuildingInfo> PlainFor(PlanBlock block, PlanGrid grid, IReadOnlyList<BuildingInfo> pool)
        {
            var st = block.Strip;
            if (st == null || st.Slack < MinSlack)
            {
                return pool;
            }

            var narrow = pool.Where(b => b.LotWidth == 1).ToList();
            if (narrow.Count == 0)
            {
                return pool;
            }

            int free = Enumerable.Range(0, grid.Width).Count(x => grid.Free[x, 0]);
            double movable = free / TypicalWidth;
            double needed = request.Overlap > FootprintMask.StrictOverlap
                ? Math.Ceiling((CellFrame.Size - st.Slack) / request.Overlap) - 2
                : Math.Ceiling(st.Slack / EndGapCloser.MaxSpreadSliver) - 1;
            if (needed <= movable || needed > free)
            {
                return pool;
            }

            result.Log.Add($"  {block.Label}: {st.Slack:F1} m left over and {needed:F0} movable buildings needed; using only one-cell-wide buildings.");
            return narrow;
        }

        static string FreeDepths(PlanGrid grid)
        {
            var chars = new char[grid.Width];
            for (int x = 0; x < grid.Width; x++)
            {
                chars[x] = (char)('0' + FreeDepth(grid, x));
            }

            return new string(chars);
        }

        static int FreeDepth(PlanGrid grid, int x)
        {
            int d = 0;
            while (d < grid.Depth && grid.Free[x, d])
            {
                d++;
            }

            return d;
        }

        void ApplyDensity()
        {
            if (request.Density >= 100)
            {
                return;
            }

            var frontCells = result.FrontCells;
            var front = FrontCoverage.CellsPerLot(result.Lots, frontCells);

            var removed = GridPlanner.Thin(front.Select(f => (f.cells, f.lot.Corner)).ToList(), frontCells.Count, request.Density,
                new Random(request.Seed ^ 0x1b873593));
            var gone = new HashSet<PlannedLot>(removed.Select(i => front[i].lot));
            result.Thinned.AddRange(gone.Select(p => p.Footprint(0)).Select(q => new Pt(q.P.Average(v => v.X), q.P.Average(v => v.Y))));
            result.Lots.RemoveAll(gone.Contains);
            result.Log.Add($"  density {request.Density}%: removed {gone.Count} buildings ({removed.Sum(i => front[i].cells)} street cells) of {frontCells.Count} cells.");
        }

        void CloseEndGaps()
        {
            var closer = new EndGapCloser(result.Lots, request.Blocks, request.Existing, result.Thinned, request.Outline,
                request.Plain, request.Overlap, new Random(request.Seed ^ 0x2545f491), result.Log);
            foreach (var (block, _) in grids)
            {
                closer.Close(block);
            }
        }

        void FillFrontGaps()
        {
            if (request.Density < 100)
            {
                return;
            }

            var filler = new FrontGapFiller(result.Lots, request.Existing, result.Thinned, request.Outline, request.Plain, request.Overlap,
                new Random(request.Seed ^ 0x3c6ef372), result.Log);
            foreach (var (block, _) in grids)
            {
                filler.Fill(block);
            }
        }

        void MeasureFrontage()
        {
            result.FrontFilled = FrontCoverage.CountFilled(result.Lots, result.FrontCells);
            result.Log.Add($"  frontage: {result.FrontFilled} of {result.FrontCells.Count} street cells filled.");
            if (mix != null)
            {
                double first = mix.MeasuredFirstShare(result.Lots, result.FrontCells);
                result.FirstGroupShare = mix.First == 0 ? first : 1 - first;
                result.Log.Add($"  mix: group 0 covers {result.FirstGroupShare:P0} of the filled street cells, group 1 {1 - result.FirstGroupShare:P0}.");
            }
        }

        void Diagnose()
        {
            try
            {
                result.Log.AddRange(FrontCoverage.Uncovered(result.Lots, request.Existing, request.Blocks));
            }
            catch (Exception e)
            {
                result.Log.Add($"  sidewalk diagnostics failed: {e.Message}");
            }
        }
    }
}
