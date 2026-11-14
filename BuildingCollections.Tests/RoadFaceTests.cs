using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class RoadFaceTests
    {
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

        [Theory]
        [InlineData(97, 97, 0, 0)]
        [InlineData(103, 97, 100, 0)]
        [InlineData(97, 103, 0, 100)]
        [InlineData(103, 103, 100, 100)]
        public void Click_next_to_a_junction_picks_the_right_block(double x, double y, double x0, double y0)
        {
            var poly = RoadFace.Trace(Grid(), new Pt(x, y));
            Assert.NotNull(poly);
            Assert.Equal((x0, y0, x0 + 100, y0 + 100), Bounds(poly));
        }

        [Fact]
        public void Dead_end_inside_the_block_is_walked_around()
        {
            var edges = Grid();
            edges.Add(Line(4, 99, new Pt(100, 100), new Pt(150, 150)));
            var poly = RoadFace.Trace(edges, new Pt(180, 120));
            Assert.NotNull(poly);
            Assert.Equal((100.0, 100.0, 200.0, 200.0), Bounds(poly));
            Assert.Contains(poly, p => p.X == 150 && p.Y == 150);
        }

        [Fact]
        public void Curved_street_follows_the_curve()
        {
            var edges = Grid();
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

        [Fact]
        public void Explain_shows_the_walk_taking_the_sharpest_turn_at_every_node()
        {
            var edges = Grid();
            RoadFace.Trace(edges, new Pt(30, 40), out var path);
            var turns = RoadFace.Explain(edges, path);

            Assert.Equal(path.Count, turns.Count);
            for (int i = 0; i < turns.Count; i++)
            {
                var best = turns[i].Options.OrderByDescending(o => o.Turn).First();
                var next = path[(i + 1) % path.Count];
                Assert.Equal((next.edge, next.forward), (best.Edge, best.Forward));
            }
        }

        [Fact]
        public void Two_streets_meeting_almost_parallel_close_the_wedge_between_them()
        {
            var south = new RoadEdge { Start = 1, End = 0, Points = { new Pt(-100, -12), new Pt(-60, -6), new Pt(-20, -1), new Pt(-4, 0.01), new Pt(0, 0) } };
            var north = new RoadEdge { Start = 2, End = 0, Points = { new Pt(-100, 12), new Pt(-60, 6), new Pt(-20, 1), new Pt(-4, -0.01), new Pt(0, 0) } };
            var east = Line(0, 3, new Pt(0, 0), new Pt(100, 0));
            var west = Line(1, 2, new Pt(-100, -12), new Pt(-100, 12));

            var poly = RoadFace.Trace(new List<RoadEdge> { south, north, east, west }, new Pt(-70, 0));

            Assert.NotNull(poly);
            Assert.Equal((-100.0, -12.0, 0.0, 12.0), Bounds(poly));
        }

        [Fact]
        public void Two_almost_parallel_exits_take_the_one_on_the_block_side()
        {
            var south = new RoadEdge { Start = 1, End = 0, Points = { new Pt(-100, -12), new Pt(-60, -6), new Pt(-20, -1), new Pt(-4, 0.01), new Pt(0, 0) } };
            var north = new RoadEdge { Start = 2, End = 0, Points = { new Pt(-100, 12), new Pt(-60, 6), new Pt(-20, 1), new Pt(-4, -0.01), new Pt(0, 0) } };
            var edges = new List<RoadEdge>
            {
                south,
                north,
                Line(4, 0, new Pt(0, -60), new Pt(0, 0)),
                Line(4, 5, new Pt(0, -60), new Pt(-100, -60)),
                Line(5, 1, new Pt(-100, -60), new Pt(-100, -12)),
                Line(1, 2, new Pt(-100, -12), new Pt(-100, 12)),
            };

            var poly = RoadFace.Trace(edges, new Pt(-50, -40));

            Assert.NotNull(poly);
            var (minX, minY, maxX, maxY) = Bounds(poly);
            Assert.Equal((-100.0, -60.0, 0.0), (minX, minY, maxX));
            Assert.True(maxY < 1, $"the face reaches the north street (max y {maxY})");
        }
    }
}
