using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    /// <summary>
    /// Rejilla de celdas de un bloque. x avanza a lo largo de la calle y y se aleja de ella (y = 0 es la fila de
    /// la calle). Free[x, y] indica si la celda está libre.
    /// </summary>
    public sealed class PlanGrid
    {
        public int Width;
        public int Depth;
        public bool[,] Free;

        /// <summary>El extremo izquierdo (x = 0) de la fila frontal es una esquina.</summary>
        public bool LeftCorner;

        /// <summary>El extremo derecho (x = Width - 1) de la fila frontal es una esquina.</summary>
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
        /// <summary>
        /// Reparte edificios a lo largo de la fila frontal. Las esquinas llevan edificios esquineros (si la lista
        /// tiene alguno que encaje; si no, uno normal) y el resto, normales. La densidad (1 a 100) es el porcentaje
        /// de celdas libres que se cubren. El resultado depende solo de las entradas y de la semilla.
        /// </summary>
        public static List<Placement> Plan(PlanGrid grid, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, int density, int seed)
        {
            var result = new List<Placement>();
            if (grid.Width <= 0 || grid.Depth <= 0 || density <= 0)
            {
                return result;
            }

            density = Math.Min(density, 100);
            var rng = new Random(seed);
            var used = (bool[,])grid.Free.Clone();   // true = celda aún disponible

            // 1) esquinas primero, para que el relleno del medio no las ocupe.
            if (grid.RightCorner)
            {
                TryCorner(grid, used, corners, plain, CornerKind.Right, true, rng, result);
            }

            if (grid.LeftCorner)
            {
                TryCorner(grid, used, corners, plain, CornerKind.Left, false, rng, result);
            }

            // 2) relleno del medio, de izquierda a derecha.
            int x = 0;
            while (x < grid.Width)
            {
                if (!used[x, 0])
                {
                    x++;
                    continue;
                }

                int run = 0;
                while (x + run < grid.Width && used[x + run, 0])
                {
                    run++;
                }

                var fitting = plain.Where(b => b.LotWidth <= run && Fits(used, grid, x, 0, b)).ToList();
                if (fitting.Count == 0)
                {
                    x++;
                    continue;
                }

                var pick = fitting[rng.Next(fitting.Count)];
                Occupy(used, x, 0, pick);
                result.Add(new Placement { X = x, Y = 0, Building = pick, IsCorner = false });
                x += pick.LotWidth;
            }

            // 3) densidad: se quitan al azar edificios (primero los normales) hasta no pasar el objetivo.
            int free = grid.FreeCount();
            int target = (int)Math.Floor(free * density / 100.0);
            int covered = result.Sum(p => p.Cells);
            while (covered > target && result.Count > 0)
            {
                var removable = result.Where(p => !p.IsCorner).ToList();
                if (removable.Count == 0)
                {
                    removable = result;
                }

                var victim = removable[rng.Next(removable.Count)];
                result.Remove(victim);
                covered -= victim.Cells;
            }

            return result.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        }

        static void TryCorner(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> corners, IReadOnlyList<BuildingInfo> plain,
            CornerKind side, bool atRight, Random rng, List<Placement> result)
        {
            // Primero un esquinero que admita ese lado; si no hay, uno normal (decisión del spec).
            var pool = corners.Where(b => (b.Corner & side) != 0).ToList();
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

            var pick = fitting[rng.Next(fitting.Count)];
            int px = atRight ? grid.Width - pick.LotWidth : 0;
            Occupy(used, px, 0, pick);
            result.Add(new Placement { X = px, Y = 0, Building = pick, IsCorner = isCorner });
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
