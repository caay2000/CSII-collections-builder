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
        public void Clip_polygon_cuts_the_strips()
        {
            // Solo la mitad izquierda de la manzana (x < 50).
            var clip = L(-5, -5, 50, -5, 50, 65, -5, 65);
            var strips = VirtualLots.Build(Rect(), new Quad[0], clip: clip);
            var bottom = strips.Single(s => s.T.X > 0.9);
            Assert.True(bottom.Grid.Free[0, 0]);
            Assert.False(bottom.Grid.Free[7, 0]); // la celda 7 va de 56 a 64 m: fuera
            Assert.All(strips.Where(s => s.T.Y > 0.9), s => Assert.Equal(0, s.Grid.FreeCount())); // el lado derecho queda fuera
        }

        [Fact]
        public void Curve_bulging_into_the_block_keeps_the_front_row()
        {
            // El lado de arriba es una curva que se mete 6 m en la manzana (de y = 60 a y = 54 en el centro).
            var top = new List<Pt>();
            for (int i = 0; i <= 20; i++)
            {
                double x = 100 - i * 5, u = (x - 50) / 50;
                top.Add(new Pt(x, 54 + 6 * u * u));
            }

            var sides = new List<List<Pt>> { L(0, 0, 100, 0), L(100, 0, 100, 60), top, L(0, 60, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            var curved = strips.Where(s => s.T.X < -0.5).ToList();
            Assert.NotEmpty(curved);
            Assert.Contains(curved, s => Enumerable.Range(0, s.Width).Any(x => s.Grid.Free[x, 0]));
        }

        [Fact]
        public void Gentle_curve_gives_long_strips()
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= 40; i++)
            {
                double ang = System.Math.PI / 2 * i / 40; // cuarto de círculo de radio 200
                arc.Add(new Pt(200 * System.Math.Cos(ang), 200 * System.Math.Sin(ang)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 200, 0, 0), L(0, 0, 200, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]).Where(s => s.T.X < 0.99 && s.T.Y < 0.99 && s.T.X > -0.99).ToList();
            Assert.True(strips.Average(s => s.Width) >= 5, "tramos de la curva demasiado cortos");
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

        [Fact]
        public void Rounded_corners_do_not_lose_a_cell()
        {
            // Manzana de 4 x 5 celdas (32 x 40 m) como la da el juego: cada lado acaba 0,5 m antes de la esquina.
            var sides = new List<List<Pt>>
            {
                L(0.5, 0, 31.5, 0), L(32, 0.5, 32, 39.5), L(31.5, 40, 0.5, 40), L(0, 39.5, 0, 0.5),
            };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            Assert.Equal(4, strips.Count);
            var bottom = strips.Single(s => s.T.X > 0.9);
            Assert.Equal(4, bottom.Width);
            Assert.True(bottom.Slack < 0.1);
            Assert.True(bottom.P0.X < 0.1 && bottom.P0.Y > -0.1);
            Assert.Equal(5, strips.Single(s => s.T.Y > 0.9).Width);
            Assert.True(bottom.Grid.Free[0, 0] && bottom.Grid.Free[3, 0]);
        }
    }
}
