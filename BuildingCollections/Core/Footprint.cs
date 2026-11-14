using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BuildingCollections.Core
{
    public readonly struct FootprintRect
    {
        public readonly double MinX, MinZ, MaxX, MaxZ;

        public FootprintRect(double minX, double minZ, double maxX, double maxZ)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }

        public double Area => (MaxX - MinX) * (MaxZ - MinZ);
    }

    public sealed class Footprint
    {
        public const double DefaultCell = 1.0;

        readonly HashSet<(int X, int Z)> cells = new HashSet<(int, int)>();
        readonly double cell;

        Footprint(double cell)
        {
            this.cell = cell;
        }

        public int CellCount => cells.Count;

        public double Area => cells.Count * cell * cell;

        public static Footprint FromTriangles(IEnumerable<(Pt A, Pt B, Pt C)> triangles, double cell = DefaultCell)
        {
            var footprint = new Footprint(cell);
            foreach (var (a, b, c) in triangles)
            {
                footprint.Add(a, b, c);
            }

            return footprint;
        }

        public List<FootprintRect> Rects()
        {
            var runs = new Dictionary<(int Start, int End), List<int>>();
            foreach (var row in cells.GroupBy(c => c.Z))
            {
                var xs = row.Select(c => c.X).OrderBy(x => x).ToList();
                int start = xs[0], prev = xs[0];
                foreach (var x in xs.Skip(1).Append(int.MaxValue))
                {
                    if (x == prev + 1)
                    {
                        prev = x;
                        continue;
                    }

                    if (!runs.TryGetValue((start, prev), out var zs))
                    {
                        zs = new List<int>();
                        runs[(start, prev)] = zs;
                    }

                    zs.Add(row.Key);
                    start = prev = x;
                }
            }

            var rects = new List<FootprintRect>();
            foreach (var run in runs)
            {
                var zs = run.Value.OrderBy(z => z).ToList();
                int from = zs[0], to = zs[0];
                foreach (var z in zs.Skip(1).Append(int.MaxValue))
                {
                    if (z == to + 1)
                    {
                        to = z;
                        continue;
                    }

                    rects.Add(new FootprintRect(run.Key.Start * cell, from * cell, (run.Key.End + 1) * cell, (to + 1) * cell));
                    from = to = z;
                }
            }

            return rects.OrderBy(r => r.MinZ).ThenBy(r => r.MinX).ToList();
        }

        public static string Encode(IEnumerable<FootprintRect> rects) =>
            string.Join(";", rects.Select(r => string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##},{3:0.##}", r.MinX, r.MinZ, r.MaxX, r.MaxZ)));

        public static List<FootprintRect> Decode(string text) =>
            (text ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray())
                .Select(v => new FootprintRect(v[0], v[1], v[2], v[3]))
                .ToList();

        void Add(Pt a, Pt b, Pt c)
        {
            double area = Pt.Cross(b - a, c - a);
            if (Math.Abs(area) < 1e-6)
            {
                AddSegment(a, b);
                AddSegment(b, c);
                AddSegment(c, a);
                return;
            }

            int x0 = Index(Math.Min(a.X, Math.Min(b.X, c.X))), x1 = Index(Math.Max(a.X, Math.Max(b.X, c.X)));
            int z0 = Index(Math.Min(a.Y, Math.Min(b.Y, c.Y))), z1 = Index(Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (int x = x0; x <= x1; x++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (Inside(a, b, c, new Pt((x + 0.5) * cell, (z + 0.5) * cell)))
                    {
                        cells.Add((x, z));
                    }
                }
            }
        }

        void AddSegment(Pt a, Pt b)
        {
            double length = Math.Sqrt(Pt.Dot(b - a, b - a));
            int steps = Math.Max(1, (int)Math.Ceiling(length / (cell * 0.5)));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                cells.Add((Index(a.X + (b.X - a.X) * t), Index(a.Y + (b.Y - a.Y) * t)));
            }
        }

        int Index(double v) => (int)Math.Floor(v / cell);

        static bool Inside(Pt a, Pt b, Pt c, Pt p)
        {
            double d1 = Pt.Cross(b - a, p - a), d2 = Pt.Cross(c - b, p - b), d3 = Pt.Cross(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
