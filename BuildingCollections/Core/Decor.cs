using System;
using System.Collections.Generic;

namespace BuildingCollections.Core
{
    /// <summary>
    /// Decoración del espacio sobrante de una manzana (patios, huecos entre edificios): puntos repartidos en una
    /// rejilla con desplazamiento al azar, dentro del contorno, lejos de la acera y de los edificios.
    /// </summary>
    public static class Decor
    {
        /// <param name="outline">Contorno de la manzana (acera).</param>
        /// <param name="blockers">Huellas de edificios (planificados y existentes).</param>
        /// <param name="existing">Árboles que ya hay: no se pone otro encima.</param>
        /// <param name="spacing">Separación media entre puntos (m).</param>
        /// <param name="curbMargin">Distancia mínima a la acera (m).</param>
        /// <param name="clearance">Distancia mínima a un edificio (m).</param>
        public static List<Pt> Scatter(IReadOnlyList<Pt> outline, IReadOnlyList<Quad> blockers, IReadOnlyList<Pt> existing,
            double spacing, double curbMargin, double clearance, int seed, int max = 2000, IReadOnlyList<Pt> clip = null)
        {
            var result = new List<Pt>();
            if (outline.Count < 3 || spacing <= 0.5)
            {
                return result;
            }

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in outline)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }

            // Con el tope de puntos, una rejilla recorrida por filas dejaría vacía la parte de arriba: se abre la
            // separación lo justo para que quepa toda el área.
            spacing = Math.Max(spacing, Math.Sqrt((maxX - minX) * (maxY - minY) / max));
            var rng = new Random(seed);
            double jitter = spacing * 0.35;
            double minGap = spacing * 0.5;
            for (double y = minY + spacing / 2; y < maxY && result.Count < max; y += spacing)
            {
                for (double x = minX + spacing / 2; x < maxX && result.Count < max; x += spacing)
                {
                    var q = new Pt(x + (rng.NextDouble() * 2 - 1) * jitter, y + (rng.NextDouble() * 2 - 1) * jitter);
                    if (!Polygon.Contains(outline, q) || (clip != null && !Polygon.Contains(clip, q)) || DistanceToOutline(outline, q) < curbMargin)
                    {
                        continue;
                    }

                    bool blocked = false;
                    foreach (var b in blockers)
                    {
                        if (q.X >= b.MinX - clearance && q.X <= b.MaxX + clearance && q.Y >= b.MinY - clearance && q.Y <= b.MaxY + clearance &&
                            b.DistanceTo(q) < clearance)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (blocked)
                    {
                        continue;
                    }

                    foreach (var e in existing)
                    {
                        var d = e - q;
                        if (Pt.Dot(d, d) < minGap * minGap)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (!blocked)
                    {
                        result.Add(q);
                    }
                }
            }

            return result;
        }

        /// <summary>Separación de la rejilla para una densidad (10 a 100 %): 5 m al 100 %, más ancha cuanto menor.</summary>
        public static double SpacingFor(int density) => 5.0 / Math.Sqrt(Math.Max(10, Math.Min(100, density)) / 100.0);

        static double DistanceToOutline(IReadOnlyList<Pt> outline, Pt q)
        {
            double best = double.MaxValue;
            for (int i = 0; i < outline.Count; i++)
            {
                best = Math.Min(best, CurbSnap.PointSegment(q, outline[i], outline[(i + 1) % outline.Count], out _));
            }

            return best;
        }
    }

    /// <summary>Punto enganchado a un bordillo: qué bordillo y en qué posición (índice de segmento + fracción).</summary>
    public struct CurbHit
    {
        public int Curb;
        public double Arc;
        public Pt Point;
        public double Distance;
    }

    /// <summary>
    /// Bordillos (borde exterior de cada calle, como polilíneas) para dibujar un polígono que se adapta a las calles:
    /// el cursor se engancha al bordillo cercano y, si dos vértices seguidos están en el mismo, el lado lo sigue.
    /// </summary>
    public static class CurbSnap
    {
        public static bool TrySnap(IReadOnlyList<List<Pt>> curbs, Pt q, double maxDistance, out CurbHit hit)
        {
            hit = new CurbHit { Curb = -1, Distance = double.MaxValue };
            for (int c = 0; c < curbs.Count; c++)
            {
                var pts = curbs[c];
                for (int i = 1; i < pts.Count; i++)
                {
                    double d = PointSegment(q, pts[i - 1], pts[i], out double t);
                    if (d < hit.Distance)
                    {
                        var a = pts[i - 1];
                        var b = pts[i];
                        hit = new CurbHit { Curb = c, Arc = i - 1 + t, Distance = d, Point = new Pt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t) };
                    }
                }
            }

            // Cerca del final de un bordillo (la esquina de la acera) se engancha al extremo exacto, solo si el cursor
            // ya está casi encima del bordillo (si no, el punto saltaría lejos de donde se hizo clic).
            if (hit.Curb >= 0 && hit.Distance < 2)
            {
                var pts = curbs[hit.Curb];
                foreach (var end in new[] { 0, pts.Count - 1 })
                {
                    var d = pts[end] - hit.Point;
                    if (Pt.Dot(d, d) < 4 * 4)
                    {
                        hit.Point = pts[end];
                        hit.Arc = end;
                    }
                }
            }

            return hit.Curb >= 0 && hit.Distance <= maxDistance;
        }

        /// <summary>Puntos del bordillo entre dos posiciones, en el sentido de <paramref name="from"/> a <paramref name="to"/>.</summary>
        public static List<Pt> Path(List<Pt> curb, double from, double to)
        {
            var result = new List<Pt> { At(curb, from) };
            if (to >= from)
            {
                for (int i = (int)Math.Floor(from) + 1; i < to; i++)
                {
                    result.Add(curb[i]);
                }
            }
            else
            {
                for (int i = (int)Math.Ceiling(from) - 1; i > to; i--)
                {
                    result.Add(curb[i]);
                }
            }

            var last = At(curb, to);
            var dd = last - result[result.Count - 1];
            if (Pt.Dot(dd, dd) > 1e-6)
            {
                result.Add(last);
            }

            return result;
        }

        /// <summary>El segmento a-b va pegado a algún bordillo (sus puntos intermedios a menos de <paramref name="tolerance"/>).</summary>
        public static bool Hugs(IReadOnlyList<List<Pt>> curbs, Pt a, Pt b, double tolerance)
        {
            foreach (double f in new[] { 0.25, 0.5, 0.75 })
            {
                var q = new Pt(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f);
                if (!TrySnap(curbs, q, tolerance, out _))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Lados de un polígono dibujado: entre dos vértices enganchados al mismo bordillo el lado lo sigue (y da a la
        /// calle); si no, es recto, y da a la calle solo si va pegado a un bordillo. Se devuelven en sentido
        /// antihorario (la manzana a la izquierda), como espera <see cref="VirtualLots"/>.
        /// </summary>
        public static void Sides(IReadOnlyList<Pt> verts, IReadOnlyList<int> curbOf, IReadOnlyList<double> arcOf, IReadOnlyList<List<Pt>> curbs,
            double hugTolerance, out List<List<Pt>> sides, out List<bool> frontage)
        {
            sides = new List<List<Pt>>();
            frontage = new List<bool>();
            int n = verts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (curbOf[i] >= 0 && curbOf[i] == curbOf[j])
                {
                    sides.Add(Path(curbs[curbOf[i]], arcOf[i], arcOf[j]));
                    frontage.Add(true);
                }
                else
                {
                    sides.Add(new List<Pt> { verts[i], verts[j] });
                    frontage.Add(Hugs(curbs, verts[i], verts[j], hugTolerance));
                }
            }

            if (SignedArea(sides) < 0)
            {
                sides.Reverse();
                frontage.Reverse();
                foreach (var s in sides)
                {
                    s.Reverse();
                }
            }
        }

        static double SignedArea(List<List<Pt>> sides)
        {
            var pts = new List<Pt>();
            foreach (var s in sides)
            {
                pts.AddRange(s);
            }

            double a = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                a += Pt.Cross(pts[i], pts[(i + 1) % pts.Count]);
            }

            return a / 2;
        }

        public static Pt At(List<Pt> curb, double arc)
        {
            int i = Math.Max(0, Math.Min(curb.Count - 1, (int)Math.Floor(arc)));
            if (i >= curb.Count - 1)
            {
                return curb[curb.Count - 1];
            }

            double t = arc - i;
            return new Pt(curb[i].X + (curb[i + 1].X - curb[i].X) * t, curb[i].Y + (curb[i + 1].Y - curb[i].Y) * t);
        }

        public static double PointSegment(Pt p, Pt a, Pt b, out double t)
        {
            var ab = b - a;
            double len = Pt.Dot(ab, ab);
            t = len <= 0 ? 0 : Math.Max(0, Math.Min(1, Pt.Dot(p - a, ab) / len));
            var d = p - new Pt(a.X + ab.X * t, a.Y + ab.Y * t);
            return Math.Sqrt(Pt.Dot(d, d));
        }
    }
}
