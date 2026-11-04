using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class RoadFaceTests
    {
        // Rejilla de 3x3 nodos a 100 m: cuatro manzanas cuadradas. Nodo (i, j) = j * 3 + i.
        static List<RoadEdge> Grid()
        {
            var edges = new List<RoadEdge>();
            Pt P(int i, int j) => new Pt(i * 100, j * 100);
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (i < 2) edges.Add(Line(j * 3 + i, j * 3 + i + 1, P(i, j), P(i + 1, j)));
                    if (j < 2) edges.Add(Line(j * 3 + i, (j + 1) * 3 + i, P(i, j), P(i, j + 1)));
                }
            }

            return edges;
        }

        static RoadEdge Line(int a, int b, Pt pa, Pt pb, int segments = 4)
        {
            var e = new RoadEdge { Start = a, End = b };
            for (int k = 0; k <= segments; k++)
            {
                double t = k / (double)segments;
                e.Points.Add(new Pt(pa.X + (pb.X - pa.X) * t, pa.Y + (pb.Y - pa.Y) * t));
            }

            return e;
        }

        static (double minX, double minY, double maxX, double maxY) Bounds(List<Pt> p) =>
            (p.Min(q => q.X), p.Min(q => q.Y), p.Max(q => q.X), p.Max(q => q.Y));

        [Theory]
        [InlineData(30, 40, 0, 0)]
        [InlineData(170, 20, 100, 0)]
        [InlineData(140, 160, 100, 100)]
        [InlineData(20, 190, 0, 100)]
        public void Finds_the_block_around_the_click(double x, double y, double x0, double y0)
        {
            var poly = RoadFace.Trace(Grid(), new Pt(x, y));
            Assert.NotNull(poly);
            Assert.Equal((x0, y0, x0 + 100, y0 + 100), Bounds(poly));
            Assert.True(Polygon.Contains(poly, new Pt(x, y)));
        }

        [Fact]
        public void Dead_end_inside_the_block_is_walked_around()
        {
            var edges = Grid();
            edges.Add(Line(4, 99, new Pt(100, 100), new Pt(150, 150))); // calle sin salida dentro de la manzana (1,1)
            var poly = RoadFace.Trace(edges, new Pt(180, 120));
            Assert.NotNull(poly);
            Assert.Equal((100.0, 100.0, 200.0, 200.0), Bounds(poly));
            Assert.Contains(poly, p => p.X == 150 && p.Y == 150);
        }

        [Fact]
        public void Curved_street_follows_the_curve()
        {
            var edges = Grid();
            // El tramo inferior de la manzana (0,0) se sustituye por una curva que baja hasta y = -40.
            edges.RemoveAll(e => e.Start == 0 && e.End == 1);
            var curve = new RoadEdge { Start = 0, End = 1 };
            for (int k = 0; k <= 10; k++)
            {
                double t = k / 10.0;
                curve.Points.Add(new Pt(100 * t, -160 * t * (1 - t)));
            }

            edges.Add(curve);
            var poly = RoadFace.Trace(edges, new Pt(50, -20));
            Assert.NotNull(poly);
            Assert.True(Bounds(poly).minY < -35);
        }

        [Fact]
        public void Open_area_returns_null()
        {
            Assert.Null(RoadFace.Trace(Grid(), new Pt(-50, 50)));
            var single = new List<RoadEdge> { Line(0, 1, new Pt(0, 0), new Pt(100, 0)) };
            Assert.Null(RoadFace.Trace(single, new Pt(50, 10)));
        }
    }
}
