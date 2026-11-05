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
        /// de la fachada libre (fila frontal) que se cubre. Sin <paramref name="leaveGaps"/>, el relleno cubre el
        /// máximo de fachada posible con los anchos disponibles. El resultado depende solo de las entradas y de la semilla.
        /// </summary>
        public static List<Placement> Plan(PlanGrid grid, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, int density, int seed,
            bool leaveGaps = false, IReadOnlyList<Placement> preplaced = null)
        {
            var result = new List<Placement>();
            if (grid.Width <= 0 || grid.Depth <= 0 || density <= 0)
            {
                return result;
            }

            density = Math.Min(density, 100);
            var rng = new Random(seed);
            var used = (bool[,])grid.Free.Clone();   // true = celda aún disponible

            // 1) esquinas primero, para que el relleno del medio no las ocupe. Si ya vienen decididas (una primera
            //    pasada que reparte las esquinas entre tiras que se tocan), se respetan.
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

            // 2) relleno del medio. Sin huecos: en cada tramo libre de la fila frontal se elige al azar una de las
            //    combinaciones que cubren el máximo de fachada. Con huecos: de izquierda a derecha, al azar.
            if (leaveGaps)
            {
                FillGreedy(grid, used, plain, rng, result);
            }
            else
            {
                FillMax(grid, used, plain, rng, result);
            }

            // 3) densidad: porcentaje de la fachada a calle (celdas libres de la fila frontal), no de toda la rejilla:
            //    el fondo de la parcela no cuenta. Se quitan al azar edificios (primero los normales).
            var removed = Thin(result.Select(p => (p.Building.LotWidth, p.IsCorner)).ToList(), FrontFree(grid), density, rng);
            return result.Where((p, i) => !removed.Contains(i)).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        }

        /// <summary>
        /// Densidad: qué edificios (índices) quitar para cubrir el porcentaje pedido de las celdas de fachada
        /// ("front"). Objetivo redondeado al más cercano; se quitan al azar, primero los normales, y nunca uno que
        /// deje la fachada más lejos del objetivo de lo que estaba (antes se quitaba uno de 2 con 1 de sobra, y con
        /// el redondeo hacia abajo en cada calle corta un 90 % se quedaba en un 70 %). Se usa sobre toda la
        /// selección a la vez: en calles de 3 o 4 celdas, por separado, no hay forma de acercarse.
        /// </summary>
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
                    // Todos se pasan: el más estrecho, solo si quitándolo se queda más cerca del objetivo.
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

        /// <summary>
        /// Solo las esquinas, sobre la rejilla tal como está. Sirve para una primera pasada en la que todas las tiras
        /// reservan sus esquinas antes de rellenar ninguna: dos tiras que se tocan en una esquina se la disputan, y
        /// así el relleno de una no le quita la esquina a la otra.
        /// </summary>
        public static List<Placement> PlanCorners(PlanGrid grid, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, int seed)
        {
            var result = new List<Placement>();
            if (grid.Width > 0 && grid.Depth > 0)
            {
                Corners(grid, (bool[,])grid.Free.Clone(), plain, corners, new Random(seed), result);
            }

            return result;
        }

        /// <summary>
        /// Como el juego (ZoneSpawnSystem), el acceso del edificio debe coincidir exactamente con las calles de la
        /// parcela: con calle a los dos lados y un edificio "izquierda y derecha" que ocupe todo el ancho, ese; si no,
        /// uno por cada esquina.
        /// </summary>
        static void Corners(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> plain, IReadOnlyList<BuildingInfo> corners, Random rng, List<Placement> result)
        {
            if (grid.LeftCorner && grid.RightCorner && TryBoth(grid, used, corners, rng, result))
            {
                return; // relleno completo con un único edificio de dos esquinas
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

        /// <summary>De izquierda a derecha, un edificio al azar entre los que caben en el tramo (puede dejar huecos).</summary>
        static void FillGreedy(PlanGrid grid, bool[,] used, IReadOnlyList<BuildingInfo> plain, Random rng, List<Placement> result)
        {
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

                var pick = Pick(fitting, rng);
                Occupy(used, x, 0, pick);
                result.Add(new Placement { X = x, Y = 0, Building = pick, IsCorner = false });
                x += pick.LotWidth;
            }
        }

        /// <summary>
        /// En cada tramo libre de la fila frontal, programación dinámica: best[i] = máxima fachada cubierta desde i
        /// hasta el final del tramo. Luego se recorre eligiendo al azar entre las opciones que mantienen el óptimo.
        /// </summary>
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

                // Dentro del tramo los edificios ocupan columnas distintas: lo que cabe se decide con el estado inicial.
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
                        k++; // saltarse esta celda es lo óptimo
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
            // Primero un esquinero cuyo acceso lateral sea exactamente ese lado; si no hay, uno normal (decisión del spec).
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

            var pick = Pick(fitting, rng);
            int px = atRight ? grid.Width - pick.LotWidth : 0;
            Occupy(used, px, 0, pick);
            result.Add(new Placement { X = px, Y = 0, Building = pick, IsCorner = isCorner });
        }

        /// <summary>Calle a izquierda y derecha: un edificio "izquierda y derecha" que llene exactamente el ancho.</summary>
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

        /// <summary>
        /// Uno al azar. Con una mezcla de dos colecciones y opciones de las dos, primero se elige la colección según su
        /// parte (Share) y luego un edificio de ella; así el reparto sigue el porcentaje elegido aunque una colección
        /// tenga muchos más edificios que la otra. Sin mezcla, igual que antes (mismo uso del generador).
        /// </summary>
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
