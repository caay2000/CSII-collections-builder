using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    /// <summary>
    /// Tira de parcelas propias a lo largo de un tramo recto de acera: Width celdas de 8 m a lo largo de la calle
    /// (x = 0 en <see cref="P0"/>, creciendo hacia <see cref="T"/>) y Depth hacia dentro de la manzana (a la izquierda
    /// de T). Grid.Free indica qué celdas caben dentro de la manzana sin pisar edificios existentes.
    /// </summary>
    public sealed class LotStrip
    {
        public Pt P0;
        public Pt T;
        public int Width;
        public int Depth;
        public PlanGrid Grid;

        /// <summary>Normal hacia dentro de la manzana (izquierda de T).</summary>
        public Pt N => new Pt(-T.Y, T.X);

        public Pt CellCenter(int x, int y) => Add(P0, T, x * 8 + 4, N, y * 8 + 4);

        internal static Pt Add(Pt p, Pt a, double ka, Pt b, double kb) => new Pt(p.X + a.X * ka + b.X * kb, p.Y + a.Y * ka + b.Y * kb);
    }

    /// <summary>
    /// Parcelas propias de una manzana, sin depender de las celdas de zona del juego (que faltan en diagonales y
    /// esquinas). Se parte del contorno de la manzana por la acera, como una lista cerrada de lados (cada uno una
    /// polilínea, recorridos con la manzana a la izquierda). Los lados casi rectos se unen; los giros fuertes son
    /// esquinas. Cada lado se parte en tramos rectos y cada tramo da una tira de celdas de 8 m.
    /// </summary>
    public static class VirtualLots
    {
        public const double Cell = 8.0;

        /// <param name="frontage">Por lado: si da a una calle (lleva parcelas). null = todos. Un lado sin calle
        /// (un polígono que corta la manzana) solo cierra el contorno, y no hace esquina con sus vecinos.</param>
        public static List<LotStrip> Build(IReadOnlyList<List<Pt>> sides, IReadOnlyList<Quad> obstacles, int maxDepth = 6,
            double mergeAngleDeg = 20, double cornerAngleDeg = 30, double straightTolerance = 0.75, IReadOnlyList<bool> frontage = null,
            IReadOnlyList<Pt> clip = null)
        {
            var strips = new List<LotStrip>();
            var outline = Outline(sides);
            if (outline.Count < 3)
            {
                return strips;
            }

            foreach (var side in Merge(sides, frontage, mergeAngleDeg, cornerAngleDeg))
            {
                if (!side.Frontage)
                {
                    continue;
                }

                // Tramos de al menos una celda: un tramo corto al principio no debe quitarle la esquina al siguiente.
                var chunks = Chunks(side.Points, straightTolerance)
                    .Where(c => (int)Math.Floor((Dist(side.Points[c.from], side.Points[c.to]) + 0.5) / Cell) >= 1)
                    .ToList();
                for (int k = 0; k < chunks.Count; k++)
                {
                    var a = side.Points[chunks[k].from];
                    var b = side.Points[chunks[k].to];
                    var d = b - a;
                    double len = Math.Sqrt(Pt.Dot(d, d));
                    int width = (int)Math.Floor((len + 0.5) / Cell);

                    var t = new Pt(d.X / len, d.Y / len);
                    bool first = k == 0, last = k == chunks.Count - 1;
                    double slack = Math.Max(0, len - width * Cell);
                    double offset = first && side.StartCorner ? 0 : last && side.EndCorner ? slack : first ? 0 : last ? slack : slack / 2;
                    var strip = new LotStrip { P0 = LotStrip.Add(a, t, offset, t, 0), T = t, Width = width, Depth = maxDepth };
                    var grid = PlanGrid.AllFree(width, maxDepth, false, false);
                    grid.LeftCorner = first && side.StartCorner && offset < 1;
                    grid.RightCorner = last && side.EndCorner && slack - offset < 1;
                    for (int x = 0; x < width; x++)
                    {
                        bool open = true;
                        for (int y = 0; y < maxDepth; y++)
                        {
                            open = open && CellFits(strip, x, y, outline, obstacles) && (clip == null || CellInside(strip, x, y, clip));
                            grid.Free[x, y] = open; // un edificio necesita celdas seguidas desde la acera
                        }
                    }

                    strip.Grid = grid;
                    strips.Add(strip);
                }
            }

            return strips;
        }

        /// <summary>Contorno cerrado de la manzana: los lados uno tras otro.</summary>
        public static List<Pt> Outline(IReadOnlyList<List<Pt>> sides)
        {
            var poly = new List<Pt>();
            foreach (var s in sides)
            {
                foreach (var p in s)
                {
                    if (poly.Count == 0 || Dist(poly[poly.Count - 1], p) > 0.01)
                    {
                        poly.Add(p);
                    }
                }
            }

            if (poly.Count > 1 && Dist(poly[0], poly[poly.Count - 1]) <= 0.01)
            {
                poly.RemoveAt(poly.Count - 1);
            }

            return poly;
        }

        /// <summary>Celda (centro y esquinas metidas 0,5 m) dentro de un polígono de recorte.</summary>
        static bool CellInside(LotStrip s, int x, int y, IReadOnlyList<Pt> clip)
        {
            var c = s.CellCenter(x, y);
            double h = Cell / 2 - 0.5;
            return Polygon.Contains(clip, c) &&
                Polygon.Contains(clip, LotStrip.Add(c, s.T, -h, s.N, -h)) && Polygon.Contains(clip, LotStrip.Add(c, s.T, h, s.N, -h)) &&
                Polygon.Contains(clip, LotStrip.Add(c, s.T, h, s.N, h)) && Polygon.Contains(clip, LotStrip.Add(c, s.T, -h, s.N, h));
        }

        /// <summary>Celda dentro de la manzana (sus esquinas, metidas 0,5 m) y sin pisar ningún obstáculo.</summary>
        static bool CellFits(LotStrip s, int x, int y, List<Pt> outline, IReadOnlyList<Quad> obstacles)
        {
            var c = s.CellCenter(x, y);
            double h = Cell / 2 - 0.5;
            var corners = new[]
            {
                LotStrip.Add(c, s.T, -h, s.N, -h), LotStrip.Add(c, s.T, h, s.N, -h),
                LotStrip.Add(c, s.T, h, s.N, h), LotStrip.Add(c, s.T, -h, s.N, h),
            };
            if (!Polygon.Contains(outline, c))
            {
                return false;
            }

            foreach (var p in corners)
            {
                if (!Polygon.Contains(outline, p))
                {
                    return false;
                }
            }

            if (obstacles.Count > 0)
            {
                var cell = new Quad(corners[0], corners[1], corners[2], corners[3]);
                foreach (var o in obstacles)
                {
                    if (o.MaxX >= cell.MinX && o.MinX <= cell.MaxX && o.MaxY >= cell.MinY && o.MinY <= cell.MaxY && Quad.Distance(o, cell) <= 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        sealed class Side
        {
            public List<Pt> Points = new List<Pt>();
            public bool StartCorner;
            public bool EndCorner;
            public bool Frontage = true;
        }

        /// <summary>
        /// Une los lados seguidos que casi no giran (una calle partida en varios tramos) y marca como esquina cada
        /// giro de más de <paramref name="cornerAngleDeg"/> grados. Una vuelta en U (fin de una calle sin salida) no
        /// es esquina.
        /// </summary>
        static List<Side> Merge(IReadOnlyList<List<Pt>> sides, IReadOnlyList<bool> frontage, double mergeAngleDeg, double cornerAngleDeg)
        {
            var valid = new List<List<Pt>>();
            var front = new List<bool>();
            for (int i = 0; i < sides.Count; i++)
            {
                var s = sides[i];
                if (s.Count >= 2 && Length(s) > 0.5)
                {
                    valid.Add(s);
                    front.Add(frontage == null || (i < frontage.Count && frontage[i]));
                }
            }

            int n = valid.Count;
            var result = new List<Side>();
            if (n == 0)
            {
                return result;
            }

            // Giro (grados, con signo) al pasar del lado i al siguiente, y si ahí se corta (no se une).
            var turn = new double[n];
            var cut = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                turn[i] = Angle(EndDir(valid[i]), StartDir(valid[j]));
                cut[i] = Math.Abs(turn[i]) >= mergeAngleDeg || front[i] != front[j];
            }

            // Esquina: giro convexo entre dos lados con calle.
            bool Corner(int i) => front[i] && front[(i + 1) % n] && IsCorner(turn[i], cornerAngleDeg);

            // Se empieza justo después de un corte, para no partir un lado por el principio de la lista.
            int start = 0;
            for (int i = 0; i < n; i++)
            {
                if (cut[i])
                {
                    start = (i + 1) % n;
                    break;
                }
            }

            Side cur = null;
            for (int k = 0; k < n; k++)
            {
                int i = (start + k) % n;
                int prev = (i - 1 + n) % n;
                if (cur == null)
                {
                    cur = new Side { StartCorner = Corner(prev), Frontage = front[i] };
                }

                foreach (var p in valid[i])
                {
                    if (cur.Points.Count == 0 || Dist(cur.Points[cur.Points.Count - 1], p) > 0.01)
                    {
                        cur.Points.Add(p);
                    }
                }

                if (cut[i] || k == n - 1)
                {
                    cur.EndCorner = Corner(i);
                    if (cur.Points.Count >= 2)
                    {
                        result.Add(cur);
                    }

                    cur = null;
                }
            }

            return result;
        }

        // Solo giros a la izquierda (esquinas convexas de la manzana): en una esquina hacia dentro el edificio no tiene
        // calle al lado.
        static bool IsCorner(double turn, double cornerAngleDeg) => turn >= cornerAngleDeg && turn < 170;

        /// <summary>Tramos rectos: cada uno llega tan lejos como pueda sin que los puntos se separen de la cuerda.</summary>
        static List<(int from, int to)> Chunks(List<Pt> pts, double tolerance)
        {
            var result = new List<(int, int)>();
            int i = 0;
            while (i < pts.Count - 1)
            {
                int j = i + 1;
                while (j + 1 < pts.Count && MaxDeviation(pts, i, j + 1) <= tolerance)
                {
                    j++;
                }

                result.Add((i, j));
                i = j;
            }

            return result;
        }

        static double MaxDeviation(List<Pt> pts, int i, int j)
        {
            var a = pts[i];
            var d = pts[j] - a;
            double len = Math.Sqrt(Pt.Dot(d, d));
            if (len < 1e-6)
            {
                return 0;
            }

            double worst = 0;
            for (int k = i + 1; k < j; k++)
            {
                worst = Math.Max(worst, Math.Abs(Pt.Cross(d, pts[k] - a)) / len);
            }

            return worst;
        }

        static Pt StartDir(List<Pt> s) => Dir(s[0], s[Math.Min(s.Count - 1, 1)], s);

        static Pt EndDir(List<Pt> s) => Dir(s[Math.Max(0, s.Count - 2)], s[s.Count - 1], s);

        static Pt Dir(Pt a, Pt b, List<Pt> s)
        {
            var d = b - a;
            if (Pt.Dot(d, d) < 1e-6)
            {
                d = s[s.Count - 1] - s[0];
            }

            return d;
        }

        static double Angle(Pt a, Pt b) => Math.Atan2(Pt.Cross(a, b), Pt.Dot(a, b)) * 180 / Math.PI;

        static double Length(List<Pt> s)
        {
            double l = 0;
            for (int i = 1; i < s.Count; i++)
            {
                l += Dist(s[i - 1], s[i]);
            }

            return l;
        }

        static double Dist(Pt a, Pt b)
        {
            var d = b - a;
            return Math.Sqrt(Pt.Dot(d, d));
        }
    }
}
