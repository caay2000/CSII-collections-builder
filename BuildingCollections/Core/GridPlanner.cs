using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class PlanGrid
    {
        public int Width;
        public int Depth;
        public bool[,] Free;

        public bool LeftCorner;

        public bool RightCorner;

        public static PlanGrid AllFree(int width, int depth, bool leftCorner, bool rightCorner)
        {
            var free = new bool[width, depth];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < depth; y++)
                {
                    free[x, y] = true;
                }
            }

            return new PlanGrid { Width = width, Depth = depth, Free = free, LeftCorner = leftCorner, RightCorner = rightCorner };
        }

        public int FreeCount()
        {
            int n = 0;
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Depth; y++)
                {
                    if (Free[x, y])
                    {
                        n++;
                    }
                }
            }

            return n;
        }
    }

    public sealed class Placement
    {
        public int X;
        public int Y;
        public BuildingInfo Building;
        public bool IsCorner;
        public int Cells => Building.LotWidth * Building.LotDepth;
    }

    public static class GridPlanner
    {
        public static List<Placement> Plan(PlanGrid grid, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, int density, int seed,
            IReadOnlyList<Placement> preplaced = null)
        {
            var result = new List<Placement>();
            if (grid.Width <= 0 || grid.Depth <= 0 || density <= 0)
            {
                return result;
            }

            density = Math.Min(density, 100);
            var rng = new Random(seed);
            var used = (bool[,])grid.Free.Clone();

            if (preplaced != null)
            {
                foreach (var p in preplaced)
                {
                    Occupy(used, p.X, p.Y, p.Building);
                    result.Add(p);
                }
            }
            else
            {
                Corners(grid, used, plain, corners, rng, result);
            }

            FillMax(grid, used, plain, rng, result);
            if (density >= 100)
            {
                return result.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            }

            var removed = Thin(result.Select(p => (p.Building.LotWidth, p.IsCorner)).ToList(), FrontFree(grid), density, rng);
            return result.Where((p, i) => !removed.Contains(i)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        }

        public static HashSet<int> Thin(IReadOnlyList<(int width, bool corner)> items, int front, int density, Random rng)
        {
            var removed = new HashSet<int>();
            int target = (int)Math.Round(front * Math.Min(Math.Max(density, 0), 100) / 100.0, MidpointRounding.AwayFromZero);
            int covered = items.Sum(i => i.width);
            while (covered > target)
            {
                int excess = covered - target;
                var pool = Enumerable.Range(0, items.Count).Where(i => !removed.Contains(i) && !items[i].corner).ToList();
                if (pool.Count == 0)
                {
                    pool = Enumerable.Range(0, items.Count).Where(i => !removed.Contains(i)).ToList();
                }

                if (pool.Count == 0)
                {
                    break;
                }

                var fit = pool.Where(i => items[i].width <= excess).ToList();
                int victim;
                if (fit.Count > 0)
                {
                    victim = fit[rng.Next(fit.Count)];
                }
                else
                {
                    int w = pool.Min(i => items[i].width);
                    if (w - excess >= excess)
                    {
                        break;
                    }

                    var narrow = pool.Where(i => items[i].width == w).ToList();
                    victim = narrow[rng.Next(narrow.Count)];
                }

                removed.Add(victim);
                covered -= items[victim].width;
            }

            return removed;
        }

        public static List<Placement> PlanCorners(PlanGrid grid, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, int seed)
        {
            var result = new List<Placement>();
            if (grid.Width > 0 && grid.Depth > 0)
            {
                Corners(grid, (bool[,])grid.Free.Clone(), plain, corners, new Random(seed), result);
            }

            return result;
        }

        static void Corners(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, Random rng, List<Placement> result)
        {
            if (grid.LeftCorner && grid.RightCorner && TryBoth(grid, used, corners, rng, result))
            {
                return;
            }

            if (grid.RightCorner)
            {
                TryCorner(grid, used, corners, plain, CornerKind.Right, true, rng, result);
            }

            if (grid.LeftCorner)
            {
                TryCorner(grid, used, corners, plain, CornerKind.Left, false, rng, result);
            }
        }

        static int FrontFree(PlanGrid grid)
        {
            int n = 0;
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.Free[x, 0])
                {
                    n++;
                }
            }

            return n;
        }

        static void FillMax(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> plain, Random rng, List<Placement> result)
        {
            int x0 = 0;
            while (x0 < grid.Width)
            {
                if (!used[x0, 0])
                {
                    x0++;
                    continue;
                }

                int len = 0;
                while (x0 + len < grid.Width && used[x0 + len, 0])
                {
                    len++;
                }

                var options = new List<BuildingInfo>[len];
                for (int i = 0; i < len; i++)
                {
                    options[i] = plain.Where(b => b.LotWidth <= len - i && Fits(used, grid, x0 + i, 0, b)).ToList();
                }

                var best = new int[len + 1];
                for (int i = len - 1; i >= 0; i--)
                {
                    best[i] = best[i + 1];
                    foreach (var b in options[i])
                    {
                        best[i] = Math.Max(best[i], b.LotWidth + best[i + b.LotWidth]);
                    }
                }

                int k = 0;
                while (k < len)
                {
                    var optimal = options[k].Where(b => b.LotWidth + best[k + b.LotWidth] == best[k]).ToList();
                    if (optimal.Count == 0)
                    {
                        k++;
                        continue;
                    }

                    var pick = Pick(optimal, rng);
                    Occupy(used, x0 + k, 0, pick);
                    result.Add(new Placement { X = x0 + k, Y = 0, Building = pick, IsCorner = false });
                    k += pick.LotWidth;
                }

                x0 += len;
            }
        }

        static void TryCorner(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> corners, IReadOnlyList<BuildingInfo> plain,
            CornerKind side, bool atRight, Random rng, List<Placement> result)
        {
            var pool = corners.Where(b => (b.Corner & (CornerKind.Left | CornerKind.Right)) == side).ToList();
            bool isCorner = pool.Count > 0;
            if (!isCorner)
            {
                pool = plain.ToList();
            }

            var fitting = new List<BuildingInfo>();
            foreach (var b in pool)
            {
                int x = atRight ? grid.Width - b.LotWidth : 0;
                if (x >= 0 && Fits(used, grid, x, 0, b))
                {
                    fitting.Add(b);
                }
            }

            if (fitting.Count == 0)
            {
                return;
            }

            var pick = Pick(DeepEnough(fitting, plain), rng);
            int px = atRight ? grid.Width - pick.LotWidth : 0;
            Occupy(used, px, 0, pick);
            result.Add(new Placement { X = px, Y = 0, Building = pick, IsCorner = isCorner });
        }

        static bool TryBoth(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> corners, Random rng, List<Placement> result)
        {
            var both = CornerKind.Left | CornerKind.Right;
            var fitting = corners.Where(b => (b.Corner & both) == both && b.LotWidth == grid.Width && Fits(used, grid, 0, 0, b)).ToList();
            if (fitting.Count == 0)
            {
                return false;
            }

            var pick = Pick(fitting, rng);
            Occupy(used, 0, 0, pick);
            result.Add(new Placement { X = 0, Y = 0, Building = pick, IsCorner = true });
            return true;
        }

        static List<BuildingInfo> DeepEnough(List<BuildingInfo> fitting, IReadOnlyList<BuildingInfo> plain)
        {
            if (plain.Count == 0)
            {
                return fitting;
            }

            int minDepth = plain.Min(b => b.LotDepth);
            var deep = fitting.Where(b => b.LotDepth >= minDepth).ToList();
            return deep.Count > 0 ? deep : fitting;
        }

        internal static BuildingInfo Pick(List<BuildingInfo> options, Random rng)
        {
            int first = 0;
            foreach (var b in options)
            {
                first += b.Group == 0 ? 1 : 0;
            }

            if (first == 0 || first == options.Count)
            {
                return options[rng.Next(options.Count)];
            }

            bool fromFirst = rng.NextDouble() < options.First(b => b.Group == 0).Share;
            var pool = options.Where(b => (b.Group == 0) == fromFirst).ToList();
            return pool[rng.Next(pool.Count)];
        }

        static bool Fits(bool[,] used, PlanGrid grid, int x, int y, BuildingInfo b)
        {
            if (b.LotWidth <= 0 || b.LotDepth <= 0 || x < 0 || y < 0 || x + b.LotWidth > grid.Width || y + b.LotDepth > grid.Depth)
            {
                return false;
            }

            for (int i = 0; i < b.LotWidth; i++)
            {
                for (int j = 0; j < b.LotDepth; j++)
                {
                    if (!used[x + i, y + j])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        static void Occupy(bool[,] used, int x, int y, BuildingInfo b)
        {
            for (int i = 0; i < b.LotWidth; i++)
            {
                for (int j = 0; j < b.LotDepth; j++)
                {
                    used[x + i, y + j] = false;
                }
            }
        }
    }
}
