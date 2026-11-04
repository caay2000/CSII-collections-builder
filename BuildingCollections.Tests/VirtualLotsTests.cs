using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class VirtualLotsTests
    {
        static List<Pt> L(params double[] xy)
        {
            var r = new List<Pt>();
            for (int i = 0; i < xy.Length; i += 2) r.Add(new Pt(xy[i], xy[i + 1]));
            return r;
        }

        // Manzana de 100 x 60 recorrida en sentido antihorario (la manzana a la izquierda).
        static List<List<Pt>> Rect() => new List<List<Pt>>
        {
            L(0, 0, 100, 0), L(100, 0, 100, 60), L(100, 60, 0, 60), L(0, 60, 0, 0),
        };

        [Fact]
        public void Rectangle_gives_one_strip_per_side_with_corners()
        {
            var strips = VirtualLots.Build(Rect(), new Quad[0]);
            Assert.Equal(4, strips.Count);
            var bottom = strips.Single(s => s.T.X > 0.9);
            Assert.Equal(12, bottom.Width);
            Assert.True(bottom.Grid.LeftCorner);
            Assert.True(bottom.Grid.Free[0, 0]);
            Assert.True(bottom.Grid.Free[5, 5]); // 48 m de fondo caben en 60
            var right = strips.Single(s => s.T.Y > 0.9);
            Assert.Equal(7, right.Width);
            Assert.True(right.Grid.LeftCorner);
        }

        [Fact]
        public void Collinear_pieces_are_merged()
        {
            var sides = new List<List<Pt>> { L(0, 0, 50, 0), L(50, 0, 100, 0), L(100, 0, 100, 60), L(100, 60, 0, 60), L(0, 60, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            Assert.Equal(4, strips.Count);
            Assert.Equal(12, strips.Single(s => s.T.X > 0.9).Width);
        }

        [Fact]
        public void Triangle_cells_stay_inside()
        {
            var sides = new List<List<Pt>> { L(0, 0, 120, 0), L(120, 0, 0, 80), L(0, 80, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            var outline = VirtualLots.Outline(sides);
            foreach (var s in strips)
                for (int x = 0; x < s.Width; x++)
                    for (int y = 0; y < s.Depth; y++)
                        if (s.Grid.Free[x, y]) Assert.True(Polygon.Contains(outline, s.CellCenter(x, y)));
            // La diagonal también lleva parcelas.
            var diag = strips.Single(s => s.T.X < -0.5);
            Assert.True(diag.Grid.Free[3, 0]);
        }

        [Fact]
        public void Obstacles_block_cells()
        {
            var house = new Quad(new Pt(20, 2), new Pt(30, 2), new Pt(30, 12), new Pt(20, 12));
            var bottom = VirtualLots.Build(Rect(), new[] { house }).Single(s => s.T.X > 0.9);
            Assert.False(bottom.Grid.Free[2, 0]);
            Assert.False(bottom.Grid.Free[3, 0]);
            Assert.False(bottom.Grid.Free[2, 3]); // detrás de un obstáculo tampoco
            Assert.True(bottom.Grid.Free[6, 0]);
        }

        [Fact]
        public void Curve_is_split_in_straight_pieces()
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= 20; i++)
            {
                double a = System.Math.PI * i / 40; // cuarto de círculo de radio 100
                arc.Add(new Pt(100 * System.Math.Cos(a), 100 * System.Math.Sin(a)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 100, 0, 0), L(0, 0, 100, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            Assert.True(strips.Count >= 4);
        }
    }
}
