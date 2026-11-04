using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class DecorAndCurbTests
    {
        static List<Pt> Rect(double w, double h) => new List<Pt> { new Pt(0, 0), new Pt(w, 0), new Pt(w, h), new Pt(0, h) };

        [Fact]
        public void Scatter_stays_inside_and_away_from_buildings()
        {
            var outline = Rect(100, 60);
            var house = new Quad(new Pt(0, 0), new Pt(100, 0), new Pt(100, 20), new Pt(0, 20));
            var pts = Decor.Scatter(outline, new[] { house }, new Pt[0], 5, 2, 2, 1);
            Assert.NotEmpty(pts);
            Assert.All(pts, p => Assert.True(Polygon.Contains(outline, p) && p.Y >= 22 - 1e-9 && p.Y <= 58 + 1e-9));
        }

        [Fact]
        public void PickFitting_only_picks_plants_whose_crown_clears_the_buildings()
        {
            var house = new Quad(new Pt(0, 0), new Pt(10, 0), new Pt(10, 10), new Pt(0, 10));
            var radii = new List<double> { 1.0, 5.0 };
            var rng = new System.Random(1);

            // A 3 m del edificio solo cabe el pequeño; a 8 m cualquiera; pegado, ninguno.
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(0, Decor.PickFitting(new Pt(13, 5), new[] { house }, radii, 0.5, rng));
            }

            var far = Enumerable.Range(0, 50).Select(_ => Decor.PickFitting(new Pt(18, 5), new[] { house }, radii, 0.5, rng)).ToList();
            Assert.Contains(0, far);
            Assert.Contains(1, far);
            Assert.Equal(-1, Decor.PickFitting(new Pt(10.5, 5), new[] { house }, radii, 0.5, rng));
        }

        [Fact]
        public void Lower_density_means_fewer_points()
        {
            var outline = Rect(100, 100);
            int n100 = Decor.Scatter(outline, new Quad[0], new Pt[0], Decor.SpacingFor(100), 2, 2, 1).Count;
            int n20 = Decor.Scatter(outline, new Quad[0], new Pt[0], Decor.SpacingFor(20), 2, 2, 1).Count;
            Assert.True(n20 < n100 / 3);
        }

        static List<List<Pt>> Curbs() => new List<List<Pt>>
        {
            Enumerable.Range(0, 11).Select(i => new Pt(i * 10, 0)).ToList(),   // bordillo a lo largo de y = 0
        };

        [Fact]
        public void Snaps_to_curb_and_to_its_end()
        {
            Assert.True(CurbSnap.TrySnap(Curbs(), new Pt(33, 3), 6, out var hit));
            Assert.Equal(33, hit.Point.X, 6);
            Assert.Equal(0, hit.Point.Y, 6);
            Assert.True(CurbSnap.TrySnap(Curbs(), new Pt(98, 1), 6, out var end));
            Assert.Equal(100, end.Point.X, 6);
            Assert.False(CurbSnap.TrySnap(Curbs(), new Pt(50, 20), 6, out _));
        }

        [Fact]
        public void Polygon_side_on_curb_follows_it_and_has_frontage()
        {
            var curbs = Curbs();
            CurbSnap.TrySnap(curbs, new Pt(5, 1), 6, out var a);
            CurbSnap.TrySnap(curbs, new Pt(95, 1), 6, out var b);
            var verts = new List<Pt> { a.Point, b.Point, new Pt(95, 50), new Pt(5, 50) };
            CurbSnap.Sides(verts, new[] { 0, 0, -1, -1 }, new[] { a.Arc, b.Arc, 0, 0 }, curbs, 1.5, out var sides, out var front);
            Assert.Equal(new[] { true, false, false, false }, front);
            Assert.True(sides[0].Count > 2);
            var strips = VirtualLots.Build(sides, new Quad[0], frontage: front);
            Assert.Single(strips);
            Assert.True(strips[0].T.X > 0.9);
            Assert.False(strips[0].Grid.LeftCorner || strips[0].Grid.RightCorner);
        }

        [Fact]
        public void Clockwise_polygon_is_reoriented()
        {
            var curbs = Curbs();
            CurbSnap.TrySnap(curbs, new Pt(95, 1), 6, out var a);
            CurbSnap.TrySnap(curbs, new Pt(5, 1), 6, out var b);
            var verts = new List<Pt> { new Pt(95, 50), a.Point, b.Point, new Pt(5, 50) }; // horario
            CurbSnap.Sides(verts, new[] { -1, 0, 0, -1 }, new[] { 0, a.Arc, b.Arc, 0 }, curbs, 1.5, out var sides, out var front);
            var strips = VirtualLots.Build(sides, new Quad[0], frontage: front);
            Assert.Single(strips);
            Assert.True(strips[0].T.X > 0.9); // con la manzana a la izquierda, el frente va hacia +x
        }
    }
}
