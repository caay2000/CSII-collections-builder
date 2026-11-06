using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class CornerScenarios
    {
        const string Group = "Corners";

        public static IEnumerable<Scenario> All() =>
            Parallelograms().Concat(Trapezoids()).Concat(Triangles()).Concat(CurvedSides()).Concat(SmallRounded());

        static Scenario Make(string name, List<List<Pt>> sides) => new Scenario { Group = Group, Name = name, Sides = sides };

        static IEnumerable<Scenario> Parallelograms()
        {
            const double w = 120, h = 88;
            foreach (var a in new[] { 60, 70, 80, 85 })
            {
                double dx = h / Math.Tan(a * Math.PI / 180);
                yield return Make($"parallelogram {a}° / {180 - a}°",
                    Scenarios.Polygon(new Pt(0, 0), new Pt(w, 0), new Pt(w + dx, h), new Pt(dx, h)));
            }
        }

        static IEnumerable<Scenario> Trapezoids()
        {
            const double w = 120, h = 88;
            foreach (var a in new[] { 70, 80, 100, 110 })
            {
                double dx = h / Math.Tan(a * Math.PI / 180);
                yield return Make($"trapezoid, one end at {a}° / {180 - a}°",
                    Scenarios.Polygon(new Pt(0, 0), new Pt(w, 0), new Pt(w - dx, h), new Pt(0, h)));
            }
        }

        static IEnumerable<Scenario> Triangles()
        {
            const double b = 170;
            foreach (var (a1, a2) in new[] { (60.0, 60.0), (45.0, 75.0) })
            {
                double t1 = Math.Tan(a1 * Math.PI / 180), t2 = Math.Tan(a2 * Math.PI / 180);
                double y = b * t1 * t2 / (t1 + t2);
                yield return Make($"triangle {a1:0}° / {a2:0}° / {180 - a1 - a2:0}°",
                    Scenarios.Polygon(new Pt(0, 0), new Pt(b, 0), new Pt(y / t1, y)));
            }
        }

        static IEnumerable<Scenario> CurvedSides()
        {
            const double w = 120, h = 88;
            foreach (var s in new[] { 3.0, 8.0, 16.0, -3.0, -8.0, -16.0 })
            {
                var top = Bulge(new Pt(w, h), new Pt(0, h), s, 24);
                string dir = s > 0 ? "outwards" : "inwards";
                yield return Make($"side curved {dir} (sagitta {Math.Abs(s):0} m) ending at a corner",
                    new List<List<Pt>> { Scenarios.Line(new Pt(0, 0), new Pt(w, 0)), Scenarios.Line(new Pt(w, 0), new Pt(w, h)), top, Scenarios.Line(new Pt(0, h), new Pt(0, 0)) });
            }

            yield return Make("two curved sides in a row (sagitta 8 m) with a corner between them",
                new List<List<Pt>>
                {
                    Scenarios.Line(new Pt(0, 0), new Pt(w, 0)), Bulge(new Pt(w, 0), new Pt(w, h), 8, 24),
                    Bulge(new Pt(w, h), new Pt(0, h), 8, 24), Scenarios.Line(new Pt(0, h), new Pt(0, 0)),
                });
        }

        static IEnumerable<Scenario> SmallRounded()
        {
            const double w = 120, h = 88;
            foreach (var r in new[] { 8, 10, 20, 25 })
            {
                yield return Make($"small rounded corner R {r} m",
                    new List<List<Pt>>
                    {
                        Scenarios.Line(new Pt(0, 0), new Pt(w, 0)), Scenarios.Line(new Pt(w, 0), new Pt(w, h - r)),
                        Scenarios.Arc(new Pt(w - r, h - r), r, 0, 90, 12),
                        Scenarios.Line(new Pt(w - r, h), new Pt(0, h)), Scenarios.Line(new Pt(0, h), new Pt(0, 0)),
                    });
            }
        }

        static List<Pt> Bulge(Pt a, Pt b, double sagitta, int n)
        {
            var d = b - a;
            double c = Math.Sqrt(Pt.Dot(d, d));
            var u = new Pt(d.X / c, d.Y / c);
            var outward = new Pt(u.Y, -u.X);
            double s = Math.Abs(sagitta);
            double r = (c * c / 4 + s * s) / (2 * s);
            var mid = new Pt((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            return Enumerable.Range(0, n + 1).Select(i =>
            {
                double x = -c / 2 + c * i / n;
                double hgt = (Math.Sqrt(r * r - x * x) - (r - s)) * Math.Sign(sagitta);
                return mid + u * x + outward * hgt;
            }).ToList();
        }
    }
}
