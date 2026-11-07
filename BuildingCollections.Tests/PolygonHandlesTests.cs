using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class PolygonHandlesTests
    {
        static List<Pt> Square() => new List<Pt> { new Pt(0, 0), new Pt(100, 0), new Pt(100, 100), new Pt(0, 100) };

        static readonly int[] NoCurb = { -1, -1, -1, -1 };
        static readonly double[] NoArc = { 0, 0, 0, 0 };
        static readonly List<List<Pt>> NoCurbs = new List<List<Pt>>();

        [Fact]
        public void Finds_the_nearest_vertex_within_the_radius()
        {
            var h = PolygonHandles.Find(Square(), NoCurb, NoArc, NoCurbs, true, new Pt(98, 3), 6);
            Assert.Equal(HandleKind.Vertex, h.Kind);
            Assert.Equal(1, h.Index);
            Assert.Equal(HandleKind.None, PolygonHandles.Find(Square(), NoCurb, NoArc, NoCurbs, true, new Pt(30, 30), 6).Kind);
        }

        [Fact]
        public void Closed_polygon_offers_edge_midpoints_including_the_closing_edge()
        {
            var h = PolygonHandles.Find(Square(), NoCurb, NoArc, NoCurbs, true, new Pt(2, 51), 6);
            Assert.Equal(HandleKind.Midpoint, h.Kind);
            Assert.Equal(3, h.Index);
            Assert.Equal(0, h.Point.X, 6);
            Assert.Equal(50, h.Point.Y, 6);
            Assert.Equal(-1, h.Curb);
        }

        [Fact]
        public void Open_polygon_offers_only_vertices()
        {
            Assert.Equal(HandleKind.None, PolygonHandles.Find(Square(), NoCurb, NoArc, NoCurbs, false, new Pt(50, 1), 6).Kind);
            Assert.Equal(HandleKind.Vertex, PolygonHandles.Find(Square(), NoCurb, NoArc, NoCurbs, false, new Pt(1, 1), 6).Kind);
        }

        [Fact]
        public void Vertex_wins_over_a_nearby_midpoint()
        {
            var tri = new List<Pt> { new Pt(0, 0), new Pt(8, 0), new Pt(4, 50) };
            var h = PolygonHandles.Find(tri, new[] { -1, -1, -1 }, new double[3], NoCurbs, true, new Pt(4.5, 0), 6);
            Assert.Equal(HandleKind.Vertex, h.Kind);
        }

        [Fact]
        public void Midpoint_of_a_side_on_a_curved_curb_lies_on_the_curb()
        {
            var arc = Enumerable.Range(0, 11).Select(i =>
            {
                double a = System.Math.PI / 2 * i / 10;
                return new Pt(60 * System.Math.Cos(a), 60 * System.Math.Sin(a));
            }).ToList();
            var curbs = new List<List<Pt>> { arc };
            var verts = new List<Pt> { arc[0], arc[10], new Pt(0, 0) };
            var m = PolygonHandles.Midpoint(verts, new[] { 0, 0, -1 }, new double[] { 0, 10, 0 }, curbs, 0);
            Assert.Equal(0, m.Curb);
            Assert.Equal(5, m.Arc, 6);
            var r = System.Math.Sqrt(m.Point.X * m.Point.X + m.Point.Y * m.Point.Y);
            Assert.InRange(r, 59, 60.01);
            Assert.Equal(m.Point.X, m.Point.Y, 6);
        }
    }
}
