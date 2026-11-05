using System;
using System.Collections.Generic;

namespace BuildingCollections.Core
{
    /// <summary>Punto 2D en el plano del suelo (x, z del juego).</summary>
    public readonly struct Pt
    {
        public readonly double X;
        public readonly double Y;

        public Pt(double x, double y) { X = x; Y = y; }

        public static Pt operator -(Pt a, Pt b) => new Pt(a.X - b.X, a.Y - b.Y);

        public static double Cross(Pt a, Pt b) => a.X * b.Y - a.Y * b.X;

        public static double Dot(Pt a, Pt b) => a.X * b.X + a.Y * b.Y;
    }

    /// <summary>Cuadrilátero convexo (el contorno de un bloque de zona), con los vértices en orden.</summary>
    public sealed class Quad
    {
        public readonly Pt[] P;
        public readonly double MinX, MinY, MaxX, MaxY;

        public Quad(Pt a, Pt b, Pt c, Pt d)
        {
            P = new[] { a, b, c, d };
            MinX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
            MaxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
            MinY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
            MaxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        }

        /// <summary>Punto dentro (o en el borde), sea cual sea el sentido de los vértices.</summary>
        public bool Contains(Pt q)
        {
            bool pos = false, neg = false;
            for (int i = 0; i < 4; i++)
            {
                double c = Pt.Cross(P[(i + 1) % 4] - P[i], q - P[i]);
                pos |= c > 1e-9;
                neg |= c < -1e-9;
            }

            return !(pos && neg);
        }

        /// <summary>Distancia de un punto al cuadrilátero (0 si está dentro).</summary>
        public double DistanceTo(Pt q)
        {
            if (Contains(q))
            {
                return 0;
            }

            double best = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                best = Math.Min(best, PointSegment(q, P[i], P[(i + 1) % 4]));
            }

            return best;
        }

        /// <summary>
        /// Se tocan o se solapan (Distance &lt;= 0), descartando antes por la caja: casi todos los pares están lejos y
        /// así no se calculan sus 16 distancias entre lados.
        /// </summary>
        public static bool Touch(Quad a, Quad b) =>
            a.MaxX >= b.MinX && b.MaxX >= a.MinX && a.MaxY >= b.MinY && b.MaxY >= a.MinY && Distance(a, b) <= 0;

        /// <summary>Distancia mínima entre dos cuadriláteros (0 si se tocan o se solapan).</summary>
        public static double Distance(Quad a, Quad b)
        {
            for (int i = 0; i < 4; i++)
            {
                if (a.Contains(b.P[i]) || b.Contains(a.P[i]))
                {
                    return 0;
                }
            }

            double best = double.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    best = Math.Min(best, SegmentDistance(a.P[i], a.P[(i + 1) % 4], b.P[j], b.P[(j + 1) % 4]));
                }
            }

            return best;
        }

        static double SegmentDistance(Pt a, Pt b, Pt c, Pt d)
        {
            if (Intersects(a, b, c, d))
            {
                return 0;
            }

            return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
        }

        static bool Intersects(Pt a, Pt b, Pt c, Pt d)
        {
            double d1 = Pt.Cross(b - a, c - a), d2 = Pt.Cross(b - a, d - a);
            double d3 = Pt.Cross(d - c, a - c), d4 = Pt.Cross(d - c, b - c);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        static double PointSegment(Pt p, Pt a, Pt b)
        {
            var ab = b - a;
            double len = Pt.Dot(ab, ab);
            double t = len <= 0 ? 0 : Math.Max(0, Math.Min(1, Pt.Dot(p - a, ab) / len));
            var proj = new Pt(a.X + ab.X * t, a.Y + ab.Y * t);
            var diff = p - proj;
            return Math.Sqrt(Pt.Dot(diff, diff));
        }
    }

    public static class Polygon
    {
        /// <summary>Área (m², sin signo) de un polígono simple.</summary>
        public static double Area(IReadOnlyList<Pt> poly)
        {
            double a = 0;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                a += poly[j].X * poly[i].Y - poly[i].X * poly[j].Y;
            }

            return System.Math.Abs(a) / 2;
        }

        /// <summary>Punto dentro de un polígono simple (convexo o cóncavo), por paridad de cruces.</summary>
        public static bool Contains(IReadOnlyList<Pt> poly, Pt q)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }

    public static class BlockGrouping
    {
        /// <summary>
        /// Agrupa bloques por sus celdas visibles: dos bloques son de la misma manzana si alguna celda de uno tiene el
        /// centro a menos de <paramref name="maxCenterDistance"/> metros de alguna del otro (celdas contiguas: 8 m). Una
        /// calle en medio, aunque sea diagonal, los separa más; las curvas, con huecos en cuña entre bloques, no.
        /// </summary>
        public static List<int> FloodByCells(IReadOnlyList<Pt[]> cells, int start, double maxCenterDistance, int maxBlocks = 256)
        {
            var result = new List<int>();
            if (start < 0 || start >= cells.Count)
            {
                return result;
            }

            var box = new (double minX, double minY, double maxX, double maxY)[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                foreach (var c in cells[i])
                {
                    x0 = Math.Min(x0, c.X); y0 = Math.Min(y0, c.Y); x1 = Math.Max(x1, c.X); y1 = Math.Max(y1, c.Y);
                }

                box[i] = (x0, y0, x1, y1);
            }

            double d2 = maxCenterDistance * maxCenterDistance;
            var seen = new bool[cells.Count];
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0 && result.Count < maxBlocks)
            {
                int i = queue.Dequeue();
                result.Add(i);
                for (int j = 0; j < cells.Count; j++)
                {
                    if (seen[j] || cells[j].Length == 0)
                    {
                        continue;
                    }

                    if (box[j].minX > box[i].maxX + maxCenterDistance || box[j].maxX < box[i].minX - maxCenterDistance ||
                        box[j].minY > box[i].maxY + maxCenterDistance || box[j].maxY < box[i].minY - maxCenterDistance)
                    {
                        continue;
                    }

                    if (AnyClose(cells[i], cells[j], d2))
                    {
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            return result;
        }

        static bool AnyClose(Pt[] a, Pt[] b, double d2)
        {
            foreach (var p in a)
            {
                foreach (var q in b)
                {
                    var d = p - q;
                    if (Pt.Dot(d, d) <= d2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Índices de los bloques conectados con el de partida: dos bloques están conectados si sus contornos están a
        /// menos de <paramref name="tolerance"/> metros. Los bloques del otro lado de una calle quedan fuera porque la
        /// calle los separa más que la tolerancia.
        /// </summary>
        public static List<int> Flood(IReadOnlyList<Quad> quads, int start, double tolerance, int maxBlocks = 256)
        {
            var result = new List<int>();
            if (start < 0 || start >= quads.Count)
            {
                return result;
            }

            var seen = new bool[quads.Count];
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0 && result.Count < maxBlocks)
            {
                int i = queue.Dequeue();
                result.Add(i);
                var a = quads[i];
                for (int j = 0; j < quads.Count; j++)
                {
                    if (seen[j])
                    {
                        continue;
                    }

                    var b = quads[j];
                    if (b.MinX > a.MaxX + tolerance || b.MaxX < a.MinX - tolerance || b.MinY > a.MaxY + tolerance || b.MaxY < a.MinY - tolerance)
                    {
                        continue;
                    }

                    if (Quad.Distance(a, b) <= tolerance)
                    {
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            return result;
        }
    }
}
