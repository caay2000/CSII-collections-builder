using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public sealed class Scenario
    {
        public string Group;
        public string Name;
        public List<List<Pt>> Sides;
        public List<bool> Frontage;
        public List<Quad> Existing = new List<Quad>();
    }

    public static class Scenarios
    {
        public static IEnumerable<Scenario> All() =>
            Rectangles().Concat(Diagonals()).Concat(RoundedCorners()).Concat(Curves()).Concat(CornerScenarios.All()).Concat(GameBlocks.All());

        static IEnumerable<Scenario> Rectangles()
        {
            foreach (var (w, h) in new (double, double)[] { (80, 88), (96, 64), (160, 88), (123, 76), (101, 67), (31, 76), (35.8, 35.8), (35.8, 88.6), (67.3, 52.1), (99.9, 35.8) })
            {
                yield return new Scenario { Group = "Rectangles", Name = $"{w} × {h} m", Sides = Polygon(new Pt(0, 0), new Pt(w, 0), new Pt(w, h), new Pt(0, h)) };
            }
        }

        static IEnumerable<Scenario> Diagonals()
        {
            const double w = 160, h = 200, y0 = 20;
            foreach (var deg in new[] { 10, 25, 40, 55 })
            {
                double t = Math.Tan(deg * Math.PI / 180);
                double yRight = y0 + w * t;
                var pts = yRight <= h
                    ? new[] { new Pt(0, 0), new Pt(w, 0), new Pt(w, yRight), new Pt(0, y0) }
                    : new[] { new Pt(0, 0), new Pt(w, 0), new Pt(w, h), new Pt((h - y0) / t, h), new Pt(0, y0) };
                double corner = yRight <= h ? 90 - deg : 180 - deg;
                yield return new Scenario { Group = "Diagonal street", Name = $"diagonal at {deg}° ({corner:F0}° corner)", Sides = Polygon(pts) };
            }
        }

        static IEnumerable<Scenario> RoundedCorners()
        {
            const double w = 140, h = 110;
            foreach (var r in new[] { 15, 40, 80 })
            {
                var arc = Arc(new Pt(w - r, h - r), r, 0, 90, 24);
                yield return new Scenario
                {
                    Group = "Curved corner",
                    Name = $"rounded corner R {r} m",
                    Sides = new List<List<Pt>>
                    {
                        Line(new Pt(0, 0), new Pt(w, 0)), Line(new Pt(w, 0), new Pt(w, h - r)), arc,
                        Line(new Pt(w - r, h), new Pt(0, h)), Line(new Pt(0, h), new Pt(0, 0)),
                    },
                };
            }
        }

        static IEnumerable<Scenario> Curves()
        {
            yield return Sector("curve towards the block R 105 m, 62° (Cobblestone-like)", 105, 62, 30);
            yield return Sector("curve towards the block R 60 m, 90°", 60, 90, 20);
            foreach (var (amp, wave, label) in new[] { (10.0, 200.0, "gentle S"), (25.0, 200.0, "marked S"), (15.0, 100.0, "wavy"), (28.0, 90.0, "tight S") })
            {
                const double w = 200, h = 90;
                var top = new List<Pt>();
                for (int i = 0; i <= 80; i++)
                {
                    double x = w - w * i / 80;
                    top.Add(new Pt(x, h + amp * Math.Sin(2 * Math.PI * x / wave)));
                }

                yield return new Scenario
                {
                    Group = "Curves",
                    Name = $"{label} (amplitude {amp} m, wavelength {wave} m)",
                    Sides = new List<List<Pt>>
                    {
                        Line(new Pt(0, 0), new Pt(w, 0)), Line(new Pt(w, 0), top[0]), top, Line(top[top.Count - 1], new Pt(0, 0)),
                    },
                };
            }
        }

        static Scenario Sector(string name, double radius, double sweepDeg, double inner)
        {
            double half = sweepDeg / 2;
            var arc = Arc(new Pt(0, 0), radius, -half, half, 40);
            var innerEnd = Polar(inner, half);
            var innerStart = Polar(inner, -half);
            return new Scenario
            {
                Group = "Curves",
                Name = name,
                Sides = new List<List<Pt>> { arc, Line(arc[arc.Count - 1], innerEnd), Line(innerEnd, innerStart), Line(innerStart, arc[0]) },
            };
        }

        internal static Pt Polar(double r, double deg) => new Pt(r * Math.Cos(deg * Math.PI / 180), r * Math.Sin(deg * Math.PI / 180));

        internal static List<Pt> Arc(Pt center, double r, double fromDeg, double toDeg, int n) =>
            Enumerable.Range(0, n + 1).Select(i => center + Polar(r, fromDeg + (toDeg - fromDeg) * i / n)).ToList();

        internal static List<Pt> Line(Pt a, Pt b) => new List<Pt> { a, b };

        internal static List<List<Pt>> Polygon(params Pt[] pts) =>
            pts.Select((p, i) => Line(p, pts[(i + 1) % pts.Length])).ToList();
    }
}
