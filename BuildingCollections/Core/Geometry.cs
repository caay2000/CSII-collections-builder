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

    public static class BlockGrouping
    {
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
