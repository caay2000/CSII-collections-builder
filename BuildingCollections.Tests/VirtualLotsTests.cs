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
        public void Gentle_curve_spreads_the_turn_over_many_short_strips()
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= 40; i++)
            {
                double ang = System.Math.PI / 2 * i / 40; // cuarto de círculo de radio 200
                arc.Add(new Pt(200 * System.Math.Cos(ang), 200 * System.Math.Sin(ang)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 200, 0, 0), L(0, 0, 200, 0) };
            var strips = CurveStrips(sides);
            Assert.True(strips.Count >= 8);
            Assert.True(strips.Average(s => s.Width) >= 2, "tramos de la curva demasiado cortos");
            AssertSmallTurns(strips, 10);
        }

        [Fact]
        public void Cobblestone_like_curve_has_small_turns_and_neighbours_may_overlap_at_the_back()
        {
            // Como Cobblestone: unos 110 m de radio, girando hacia la manzana. Con la tolerancia de 2 m salían tramos
            // de 40 m girando 21°; ahora deben girar poco y poder pegarse (cuña <= 3 m).
            var arc = new List<Pt>();
            for (int i = 0; i <= 60; i++)
            {
                double ang = System.Math.PI / 2 * i / 60;
                arc.Add(new Pt(110 * System.Math.Cos(ang), 110 * System.Math.Sin(ang)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 110, 0, 0), L(0, 0, 110, 0) };
            var strips = CurveStrips(sides);
            Assert.True(strips.Count >= 6);
            AssertSmallTurns(strips, 10.6);
            for (int i = 0; i + 1 < strips.Count; i++)
            {
                Assert.True(VirtualLots.BacksMayOverlap(strips[i], strips[i + 1]), $"juntas {i}-{i + 1}");
            }
        }

        [Fact]
        public void Curve_last_strip_reaches_the_corner()
        {
            // Cuarto de círculo de 110 m de radio (172,8 m): 21 celdas y ~4,8 m de sobrante que no es recto con la
            // tolerancia de las curvas. Antes se perdía y el esquinero quedaba ~4 m antes de la esquina.
            var arc = new List<Pt>();
            for (int i = 0; i <= 60; i++)
            {
                double ang = System.Math.PI / 2 * i / 60;
                arc.Add(new Pt(110 * System.Math.Cos(ang), 110 * System.Math.Sin(ang)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 110, 0, 0), L(0, 0, 110, 0) };
            var strips = CurveStrips(sides);
            var last = strips[strips.Count - 1];
            var end = LotStrip.Add(last.P0, last.T, last.Width * 8, last.N, 0);
            double gap = System.Math.Sqrt(Pt.Dot(end - new Pt(0, 110), end - new Pt(0, 110)));
            Assert.True(gap < 1.0, $"la última celda acaba a {gap:F2} m de la esquina");
            Assert.True(last.Grid.RightCorner);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        public void Curve_leftover_is_spread_over_the_joints_not_left_as_one_hole(double overlap)
        {
            // Como Cobblestone: la curva no mide un múltiplo de 8 m (sobran ~4,8 m). Antes el sobrante quedaba
            // entero en una junta (un hueco de ~4 m en medio de la curva). Con solape se mete una celda más y cada
            // junta solapa como mucho "overlap"; sin él, el sobrante queda en rendijas pequeñas.
            var arc = new List<Pt>();
            for (int i = 0; i <= 60; i++)
            {
                double ang = System.Math.PI / 2 * i / 60;
                arc.Add(new Pt(110 * System.Math.Cos(ang), 110 * System.Math.Sin(ang)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 110, 0, 0), L(0, 0, 110, 0) };
            var plain = CurveStrips(sides, 0);
            var strips = CurveStrips(sides, overlap);
            for (int i = 0; i + 1 < strips.Count; i++)
            {
                var end = LotStrip.Add(strips[i].P0, strips[i].T, strips[i].Width * 8, strips[i].N, 0);
                var start = strips[i + 1].P0;
                double d = System.Math.Sqrt(Pt.Dot(start - end, start - end));
                Assert.True(d <= (overlap > 0 ? overlap : 1.0) + 0.05, $"junta {i}: {d:F2} m entre celdas");
            }

            var first = strips[0];
            var last = strips[strips.Count - 1];
            var lastEnd = LotStrip.Add(last.P0, last.T, last.Width * 8, last.N, 0);
            Assert.True(Dist(first.P0, new Pt(110, 0)) < 1.0, $"la primera celda empieza a {Dist(first.P0, new Pt(110, 0)):F2} m de la esquina");
            Assert.True(Dist(lastEnd, new Pt(0, 110)) < 1.0, $"la última celda acaba a {Dist(lastEnd, new Pt(0, 110)):F2} m de la esquina");
            if (overlap > 0)
            {
                Assert.Equal(plain.Sum(s => s.Width) + 1, strips.Sum(s => s.Width));
            }
        }

        static double Dist(Pt a, Pt b) => System.Math.Sqrt(Pt.Dot(a - b, a - b));

        /// <summary>Los tramos del lado curvo (el que más tramos tiene; tras unir lados puede no ser el 0).</summary>
        static List<LotStrip> CurveStrips(List<List<Pt>> sides, double overlap = 0) =>
            VirtualLots.Build(sides, new Quad[0], curveOverlap: overlap).GroupBy(s => s.SideIndex).OrderByDescending(g => g.Count()).First()
                .OrderBy(s => s.ChunkIndex).ToList();

        static void AssertSmallTurns(List<LotStrip> strips, double maxDeg)
        {
            for (int i = 0; i + 1 < strips.Count; i++)
            {
                double turn = System.Math.Atan2(Pt.Cross(strips[i].T, strips[i + 1].T), Pt.Dot(strips[i].T, strips[i + 1].T)) * 180 / System.Math.PI;
                Assert.True(System.Math.Abs(turn) <= maxDeg, $"junta {i}: gira {turn:F1}°");
            }
        }

        static LotStrip S(int side, int chunk, double deg)
        {
            double a = deg * System.Math.PI / 180;
            return new LotStrip { SideIndex = side, ChunkIndex = chunk, T = new Pt(System.Math.Cos(a), System.Math.Sin(a)) };
        }

        [Fact]
        public void Backs_may_overlap_only_between_contiguous_chunks_with_a_small_wedge()
        {
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 8)));    // 16·tan 8° = 2,2 m
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 2, 8), S(0, 1, 0)));    // en cualquier orden
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 21)));  // 16·tan 21° = 6,1 m: demasiado
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 3, 5)));   // no contiguos
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(1, 2, 5)));   // otro lado
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, -35)));  // giro hacia fuera: no se cruzan
            Assert.False(VirtualLots.BacksMayOverlap(null, S(0, 2, 5)));
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
        public void One_rounded_corner_next_to_a_sharp_one_does_not_throw()
        {
            // La esquina (0,0) es exacta y la (32,0) redondeada: el lado de abajo se alarga solo por el final. Antes eso
            // tocaba la lista de tramos compartida y el lado siguiente leía un punto que no existe (clic sin selección).
            var sides = new List<List<Pt>>
            {
                L(0, 0, 31.5, 0), L(32, 0.5, 32, 40), L(32, 40, 0, 40), L(0, 40, 0, 0),
            };
            for (int rot = 0; rot < 4; rot++)
            {
                var r = sides.Skip(rot).Concat(sides.Take(rot)).ToList();
                var strips = VirtualLots.Build(r, new Quad[0]);
                Assert.Equal(4, strips.Count);
                Assert.Equal(4, strips.Single(s => s.T.X > 0.9).Width);
                Assert.Equal(5, strips.Single(s => s.T.Y > 0.9).Width);
            }
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

        static List<Pt> Arc(double r, double fromDeg, double toDeg, int n)
        {
            var pts = new List<Pt>();
            for (int i = 0; i <= n; i++)
            {
                double a = (fromDeg + (toDeg - fromDeg) * i / n) * System.Math.PI / 180;
                pts.Add(new Pt(r * System.Math.Cos(a), r * System.Math.Sin(a)));
            }
            return pts;
        }

        [Fact]
        public void Curve_chunks_are_whole_cells_and_evenly_spaced_when_bending_away()
        {
            // Recorrida en sentido horario: gira a la derecha, la manzana (a la izquierda) queda por fuera de la curva.
            var (pts, chunks) = VirtualLots.SnapChunks(Arc(200, 90, 30, 60), 2.0);
            Assert.True(chunks.Count >= 3);
            for (int c = 0; c < chunks.Count; c++)
            {
                double len = System.Math.Sqrt(Pt.Dot(pts[chunks[c].to] - pts[chunks[c].from], pts[chunks[c].to] - pts[chunks[c].from]));
                if (c < chunks.Count - 1)
                {
                    Assert.True(System.Math.Abs(len - 8 * System.Math.Round(len / 8)) < 0.01, $"tramo {c}: {len}");
                }
            }

            AssertEvenJoints(pts, chunks);
        }

        /// <summary>Sin solape, el sobrante de la curva se reparte igual entre las juntas (en total, menos de una celda).</summary>
        static void AssertEvenJoints(List<Pt> pts, List<(int from, int to)> chunks)
        {
            var gaps = new List<double>();
            for (int c = 0; c + 1 < chunks.Count; c++)
            {
                var a = pts[chunks[c].to];
                var b = pts[chunks[c + 1].from];
                gaps.Add(System.Math.Sqrt(Pt.Dot(b - a, b - a)));
            }

            Assert.True(gaps.Sum() < 8, $"rendijas {string.Join(", ", gaps.Select(g => g.ToString("F2")))}");
            Assert.True(gaps.Max() - gaps.Min() < 0.05, $"rendijas desiguales {string.Join(", ", gaps.Select(g => g.ToString("F2")))}");
        }

        [Fact]
        public void Curve_chunks_are_evenly_spaced_at_the_front_when_bending_into_the_block()
        {
            // Antihorario: gira a la izquierda, hacia la manzana. Los tramos quedan pegados por delante salvo el
            // sobrante de la curva, repartido igual entre las juntas.
            var (pts, chunks) = VirtualLots.SnapChunks(Arc(200, 30, 90, 60), 2.0);
            Assert.True(chunks.Count >= 3);
            AssertEvenJoints(pts, chunks);
        }
    }
}
