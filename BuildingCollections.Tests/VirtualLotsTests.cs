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
            Assert.True(bottom.Grid.Free[5, 5]);
            var right = strips.Single(s => s.T.Y > 0.9);
            Assert.Equal(7, right.Width);
            Assert.True(right.Grid.LeftCorner);
        }

        [Fact]
        public void Acute_corner_sets_the_corner_cells_back_so_a_two_deep_building_fits()
        {
            double c = System.Math.Cos(80 * System.Math.PI / 180) * 67, sn = System.Math.Sin(80 * System.Math.PI / 180) * 67;
            var sides = new List<List<Pt>> { L(0, 0, 96, 0), L(96, 0, 96 + c, sn), L(96 + c, sn, c, sn), L(c, sn, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            double setback = 16 / System.Math.Tan(80 * System.Math.PI / 180);

            var bottom = strips.Single(st => st.T.X > 0.9);
            Assert.True(bottom.Grid.LeftCorner);
            Assert.InRange(bottom.Offset, setback - 0.05, setback + 0.05);
            Assert.True(bottom.Grid.Free[0, 0] && bottom.Grid.Free[0, 1], "a 2-cell-deep corner building fits");

            var left = strips.Single(st => st.T.Y < -0.9);
            Assert.True(left.Grid.RightCorner);
            double endGap = left.ChunkLength - (left.Offset + left.Width * 8);
            Assert.InRange(endGap, setback - 0.05, setback + 1.0);

            var right = strips.Single(st => st.T.Y > 0.9 && st.T.X > 0);
            Assert.True(right.Offset < 0.05);
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
            Assert.False(bottom.Grid.Free[2, 3]);
            Assert.True(bottom.Grid.Free[6, 0]);
        }

        [Fact]
        public void Clip_polygon_cuts_the_strips()
        {
            var clip = L(-5, -5, 50, -5, 50, 65, -5, 65);
            var strips = VirtualLots.Build(Rect(), new Quad[0], clip: clip);
            var bottom = strips.Single(s => s.T.X > 0.9);
            Assert.True(bottom.Grid.Free[0, 0]);
            Assert.False(bottom.Grid.Free[7, 0]);
            Assert.All(strips.Where(s => s.T.Y > 0.9), s => Assert.Equal(0, s.Grid.FreeCount()));
        }

        [Fact]
        public void Curve_bulging_into_the_block_keeps_the_front_row()
        {
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
                double ang = System.Math.PI / 2 * i / 40;
                arc.Add(new Pt(200 * System.Math.Cos(ang), 200 * System.Math.Sin(ang)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 200, 0, 0), L(0, 0, 200, 0) };
            var strips = CurveStrips(sides);
            Assert.True(strips.Count >= 8);
            Assert.True(strips.Average(s => s.Width) >= 2, "curve segments too short");
            AssertSmallTurns(strips, 10);
        }

        [Fact]
        public void Cobblestone_like_curve_has_small_turns_and_neighbours_may_overlap_at_the_back()
        {
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
            Assert.True(gap < 1.0, $"the last cell ends {gap:F2} m from the corner");
            Assert.True(last.Grid.RightCorner);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void Curve_leftover_is_spread_over_the_joints_not_left_as_one_hole(double overlap)
        {
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
                Assert.True(d <= (overlap > 0 ? overlap : 1.0) + 0.05, $"joint {i}: {d:F2} m between cells");
            }

            var first = strips[0];
            var last = strips[strips.Count - 1];
            var lastEnd = LotStrip.Add(last.P0, last.T, last.Width * 8, last.N, 0);
            Assert.True(Dist(first.P0, new Pt(110, 0)) < 1.0, $"the first cell starts {Dist(first.P0, new Pt(110, 0)):F2} m from the corner");
            Assert.True(Dist(lastEnd, new Pt(0, 110)) < 1.0, $"the last cell ends {Dist(lastEnd, new Pt(0, 110)):F2} m from the corner");
            Assert.Equal(plain.Sum(s => s.Width) + (overlap >= 1 ? 1 : 0), strips.Sum(s => s.Width));
        }

        static double Dist(Pt a, Pt b) => System.Math.Sqrt(Pt.Dot(a - b, a - b));

        static List<LotStrip> CurveStrips(List<List<Pt>> sides, double overlap = 0) =>
            VirtualLots.Build(sides, new Quad[0], curveOverlap: overlap).GroupBy(s => s.SideIndex).OrderByDescending(g => g.Count()).First()
                .OrderBy(s => s.ChunkIndex).ToList();

        static void AssertSmallTurns(List<LotStrip> strips, double maxDeg)
        {
            for (int i = 0; i + 1 < strips.Count; i++)
            {
                double turn = System.Math.Atan2(Pt.Cross(strips[i].T, strips[i + 1].T), Pt.Dot(strips[i].T, strips[i + 1].T)) * 180 / System.Math.PI;
                Assert.True(System.Math.Abs(turn) <= maxDeg, $"joint {i}: turns {turn:F1}°");
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
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 8)));
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 2, 8), S(0, 1, 0)));
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 21)));
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 3, 5)));
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(1, 2, 5)));
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, -35)));
            Assert.False(VirtualLots.BacksMayOverlap(null, S(0, 2, 5)));
        }

        [Fact]
        public void Curve_is_split_in_straight_pieces()
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= 20; i++)
            {
                double a = System.Math.PI * i / 40;
                arc.Add(new Pt(100 * System.Math.Cos(a), 100 * System.Math.Sin(a)));
            }

            var sides = new List<List<Pt>> { arc, L(0, 100, 0, 0), L(0, 0, 100, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            Assert.True(strips.Count >= 4);
        }

        [Fact]
        public void One_rounded_corner_next_to_a_sharp_one_does_not_throw()
        {
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
        public void Tight_turn_gets_one_cell_chunks_instead_of_a_hole()
        {
            var (pts, chunks) = VirtualLots.SnapChunks(Arc(12, 0, 180, 40), VirtualLots.CurveTolerance);
            Assert.True(chunks.Count >= 3, $"{chunks.Count} tramos");
            AssertEvenJoints(pts, chunks);
        }

        [Fact]
        public void Leftover_is_spread_over_the_chunks_after_a_skipped_kink()
        {
            var src = new List<Pt> { new Pt(0, 0), new Pt(37, 0) };
            double h0 = 160 * System.Math.PI / 180, r = 100;
            var p0 = new Pt(37, 0);
            var c = new Pt(p0.X - r * System.Math.Sin(h0), p0.Y + r * System.Math.Cos(h0));
            for (int i = 1; i <= 60; i++)
            {
                double a = h0 + (60.0 * i / 60) * System.Math.PI / 180;
                src.Add(new Pt(c.X + r * System.Math.Sin(a), c.Y - r * System.Math.Cos(a)));
            }

            var (pts, chunks) = VirtualLots.SnapChunks(src, VirtualLots.CurveTolerance);
            Assert.True(Dist(pts[chunks[0].to], pts[chunks[1].from]) > 2, "the kink is skipped");
            var run = chunks.Skip(1).ToList();
            Assert.True(run.Count >= 3, "the arc has several segments");
            var gaps = new List<double>();
            for (int i = 0; i + 1 < run.Count; i++)
            {
                gaps.Add(Dist(pts[run[i].to], pts[run[i + 1].from]));
            }

            Assert.True(gaps.Max() - gaps.Min() < 0.05, $"rendijas desiguales {string.Join(", ", gaps.Select(g => g.ToString("F2")))}");
            Assert.True(Dist(pts[run[run.Count - 1].to], src[src.Count - 1]) < 0.5, "the last segment reaches the end");
        }

        [Fact]
        public void Curve_chunks_are_whole_cells_and_evenly_spaced_when_bending_away()
        {
            var (pts, chunks) = VirtualLots.SnapChunks(Arc(200, 90, 30, 60), 2.0);
            Assert.True(chunks.Count >= 3);
            for (int c = 0; c < chunks.Count; c++)
            {
                double len = System.Math.Sqrt(Pt.Dot(pts[chunks[c].to] - pts[chunks[c].from], pts[chunks[c].to] - pts[chunks[c].from]));
                if (c < chunks.Count - 1)
                {
                    Assert.True(System.Math.Abs(len - 8 * System.Math.Round(len / 8)) < 0.01, $"segment {c}: {len}");
                }
            }

            AssertEvenJoints(pts, chunks);
        }

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
            var (pts, chunks) = VirtualLots.SnapChunks(Arc(200, 30, 90, 60), 2.0);
            Assert.True(chunks.Count >= 3);
            AssertEvenJoints(pts, chunks);
        }

        static List<Pt> InwardCurve(double w, double h, double sagitta, int n)
        {
            double r = (w * w / 4 + sagitta * sagitta) / (2 * sagitta);
            var pts = new List<Pt>();
            for (int i = 0; i <= n; i++)
            {
                double x = w / 2 - w * i / n;
                pts.Add(new Pt(w / 2 + x, h - (System.Math.Sqrt(r * r - x * x) - (r - sagitta))));
            }

            return pts;
        }

        static bool TakesCorner(LotStrip s, bool atEnd)
        {
            int x = atEnd ? s.Width - 1 : 0;
            return (atEnd ? s.Grid.RightCorner : s.Grid.LeftCorner) && s.Grid.Free[x, 0] && s.Grid.Free[x, 1];
        }

        [Fact]
        public void Straight_side_takes_the_corner_where_a_curve_ends()
        {
            var sides = new List<List<Pt>> { L(0, 0, 120, 0), L(120, 0, 120, 88), InwardCurve(120, 88, 3, 24), L(0, 88, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            var right = strips.Single(s => s.T.Y > 0.9);
            var left = strips.Single(s => s.T.Y < -0.9);
            var bottom = strips.Single(s => s.T.X > 0.9);
            Assert.True(TakesCorner(right, true), "top right corner");
            Assert.True(TakesCorner(left, false), "top left corner");
            Assert.True(TakesCorner(bottom, false) || TakesCorner(left, true), "bottom left corner");
            Assert.True(TakesCorner(bottom, true) || TakesCorner(right, false), "bottom right corner");
        }

        static List<List<Pt>> RoundedRect(double w, double h, double r, int n)
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= n; i++)
            {
                double a = System.Math.PI / 2 * i / n;
                arc.Add(new Pt(w - r + r * System.Math.Cos(a), h - r + r * System.Math.Sin(a)));
            }

            return new List<List<Pt>> { L(0, 0, w, 0), L(w, 0, w, h - r), arc, L(w - r, h, 0, h), L(0, h, 0, 0) };
        }

        [Fact]
        public void Small_rounded_corner_is_a_corner_for_both_streets()
        {
            var strips = VirtualLots.Build(RoundedRect(120, 88, 10, 12), new Quad[0]);
            var right = strips.Where(s => s.T.Y > 0.9).OrderBy(s => s.P0.Y).Last();
            var top = strips.Where(s => s.T.X < -0.9).OrderBy(s => s.P0.X).Last();
            Assert.True(right.Grid.RightCorner, "the right street ends at a corner");
            Assert.True(top.Grid.LeftCorner, "the top street starts at a corner");
            Assert.True(TakesCorner(right, true) || TakesCorner(top, false), "one of the two places the corner building");
            Assert.DoesNotContain(strips, s => System.Math.Abs(s.T.X) < 0.9 && System.Math.Abs(s.T.Y) < 0.9);
        }

        [Fact]
        public void Wavy_street_is_not_cut_into_corners()
        {
            var top = new List<Pt>();
            for (int i = 0; i <= 80; i++)
            {
                double x = 200 - 200.0 * i / 80;
                top.Add(new Pt(x, 90 + 15 * System.Math.Sin(2 * System.Math.PI * x / 100)));
            }

            var notes = new List<string>();
            VirtualLots.Build(new List<List<Pt>> { L(0, 0, 200, 0), L(200, 0, 200, top[0].Y), top, L(0, top[80].Y, 0, 0) }, new Quad[0], notes: notes);
            Assert.DoesNotContain(notes, n => n.Contains("tight bend"));
        }

        [Fact]
        public void Backs_of_two_cell_chunks_on_an_80_m_curve_may_overlap()
        {
            double turn = 16.0 / 80;
            var a = new LotStrip { T = new Pt(1, 0), SideIndex = 0, ChunkIndex = 0 };
            var b = new LotStrip { T = new Pt(System.Math.Cos(turn), System.Math.Sin(turn)), SideIndex = 0, ChunkIndex = 1 };
            Assert.True(VirtualLots.BacksMayOverlap(a, b));
            var sharp = new LotStrip { T = new Pt(System.Math.Cos(0.3), System.Math.Sin(0.3)), SideIndex = 0, ChunkIndex = 1 };
            Assert.False(VirtualLots.BacksMayOverlap(a, sharp));
        }
    }
}
