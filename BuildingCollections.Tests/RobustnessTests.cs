using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class RobustnessTests
    {
        static List<Pt> L(params double[] xy)
        {
            var r = new List<Pt>();
            for (int i = 0; i < xy.Length; i += 2) r.Add(new Pt(xy[i], xy[i + 1]));
            return r;
        }

        static LotStrip S(int block, int chunk, double deg, double startX = 0)
        {
            double a = deg * System.Math.PI / 180;
            return new LotStrip { BlockIndex = block, SideIndex = 0, ChunkIndex = chunk, ChunkStart = new Pt(startX, 0), T = new Pt(System.Math.Cos(a), System.Math.Sin(a)) };
        }

        static BuildingInfo B(string name, int w, int d, CornerKind corner = CornerKind.Front) =>
            new BuildingInfo { PrefabName = name, LotWidth = w, LotDepth = d, Corner = corner };

        static List<List<Pt>> CurvedBlock()
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= 20; i++)
            {
                double a = System.Math.PI * i / 40;
                arc.Add(new Pt(100 * System.Math.Cos(a), 100 * System.Math.Sin(a)));
            }

            return new List<List<Pt>> { arc, L(0, 100, 0, 0), L(0, 0, 100, 0) };
        }

        [Fact]
        public void Backs_never_overlap_between_chunks_of_different_blocks()
        {
            Assert.True(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 8)));
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(1, 2, 8)));
        }

        [Fact]
        public void Backs_never_overlap_between_chunks_that_do_not_touch()
        {
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 8, startX: 5)));
        }

        [Fact]
        public void Backs_never_overlap_past_a_right_angle()
        {
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 100)));
            Assert.False(VirtualLots.BacksMayOverlap(S(0, 1, 0), S(0, 2, 170)));
        }

        [Fact]
        public void Identical_blocks_selected_together_are_not_chained_to_each_other()
        {
            var first = VirtualLots.Build(CurvedBlock(), new Quad[0], blockIndex: 0);
            var second = VirtualLots.Build(CurvedBlock(), new Quad[0], blockIndex: 1);
            Assert.True(first.Count(s => first.Any(o => LotStrip.Chained(s, o))) > 0);
            Assert.DoesNotContain(first, a => second.Any(b => LotStrip.Chained(a, b) || LotStrip.Chained(b, a) || VirtualLots.BacksMayOverlap(a, b)));
        }

        [Fact]
        public void Full_density_keeps_every_building_even_with_a_masked_corner()
        {
            var grid = PlanGrid.AllFree(6, 3, true, true);
            grid.Free[0, 0] = grid.Free[0, 1] = grid.Free[0, 2] = false;
            var corner = new Placement { X = 0, Y = 0, Building = B("cL", 1, 2, CornerKind.Front | CornerKind.Left), IsCorner = true };
            var plain = new[] { B("p1", 1, 2) };
            var plan = GridPlanner.Plan(grid, plain, new BuildingInfo[0], 100, 1, new[] { corner });
            Assert.Contains(corner, plan);
            Assert.Equal(6, plan.Count);
        }

        static List<List<Pt>> CurveIntoSquareCorner()
        {
            var arc = new List<Pt>();
            for (int i = 0; i <= 60; i++)
            {
                double ang = System.Math.PI / 2 * i / 60;
                arc.Add(new Pt(110 * System.Math.Cos(ang), 110 * System.Math.Sin(ang)));
            }

            return new List<List<Pt>> { arc, L(0, 110, 0, 0), L(0, 0, 110, 0) };
        }

        [Fact]
        public void Curve_joints_next_to_a_square_corner_take_no_overlap_when_the_corner_building_would_not_fit()
        {
            var tight = new List<string>();
            var loose = new List<string>();
            VirtualLots.Build(CurveIntoSquareCorner(), new Quad[0], notes: tight, curveOverlap: 0.5);
            VirtualLots.Build(CurveIntoSquareCorner(), new Quad[0], notes: loose, curveOverlap: 1.0);
            Assert.Contains(tight, n => n.Contains("and none at") && n.Contains("next to a corner"));
            Assert.DoesNotContain(loose, n => n.Contains("next to a corner") || n.Contains("other street's corner building"));
            Assert.Contains(loose, n => n.Contains("one more cell"));
        }

        [Fact]
        public void Short_street_between_two_corner_buildings_gets_a_shifted_building()
        {
            var sides = new List<List<Pt>>
            {
                L(17646.1, -10311.8, 17613.3, -10289.9), L(17612.7, -10290.1, 17576.8, -10368.1),
                L(17577.0, -10368.8, 17612.4, -10385.1), L(17613.1, -10384.9, 17646.5, -10312.7),
            };
            const float overlap = 0.5f;
            var strips = VirtualLots.Build(sides, new Quad[0], curveOverlap: overlap);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true, Label = $"strip side {s.SideIndex}",
            }).ToList();
            var r = PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Outline = VirtualLots.Outline(sides),
                Plain = new[] { B("1x1", 1, 1), B("1x2", 1, 2), B("2x2", 2, 2), B("3x2", 3, 2), B("2x3", 2, 3), B("3x3", 3, 3) },
                Corners = new[] { B("cL", 1, 2, CornerKind.Front | CornerKind.Left), B("cR", 1, 2, CornerKind.Front | CornerKind.Right), B("cL2", 2, 2, CornerKind.Front | CornerKind.Left), B("cR2", 2, 2, CornerKind.Front | CornerKind.Right) },
                Overlap = overlap, Seed = 7,
            });
            Assert.Contains(r.Log, l => l.Contains("uncovered cell") && l.Contains("filled with"));
            var quads = r.Lots.Select(l => l.Footprint(overlap + FootprintMask.StrictOverlap)).ToList();
            for (int i = 0; i < quads.Count; i++)
            {
                for (int j = i + 1; j < quads.Count; j++)
                {
                    Assert.False(Quad.Touch(quads[i], quads[j]), $"{r.Lots[i].Building.PrefabName} and {r.Lots[j].Building.PrefabName}");
                }
            }
        }

        [Fact]
        public void Degenerate_quad_only_contains_points_on_its_outline()
        {
            var flat = new Quad(new Pt(0, 0), new Pt(10, 0), new Pt(10, 0), new Pt(0, 0));
            Assert.True(flat.Contains(new Pt(5, 0)));
            Assert.False(flat.Contains(new Pt(5, 5)));
            Assert.False(flat.Contains(new Pt(-3, 0)));
        }

        [Fact]
        public void Request_with_default_lists_plans_nothing_without_throwing()
        {
            var r = PreviewPlanner.Plan(new PreviewRequest { Outline = L(0, 0, 10, 0, 10, 10), Overlap = 0.5f });
            Assert.Empty(r.Lots);
        }

        [Fact]
        public void Catalog_without_narrow_buildings_keeps_the_usual_mix()
        {
            var sides = new List<List<Pt>> { L(0, 0, 101.3, 0), L(101.3, 0, 101.3, 67.7), L(101.3, 67.7, 0, 67.7), L(0, 67.7, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0]);
            var blocks = strips.Select(s => new PlanBlock { Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true, Label = "t" }).ToList();
            var r = PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Outline = VirtualLots.Outline(sides), Plain = new[] { B("a2x2", 2, 2), B("a3x2", 3, 2) },
                Corners = new[] { B("cL", 2, 2, CornerKind.Front | CornerKind.Left), B("cR", 2, 2, CornerKind.Front | CornerKind.Right) },
                Overlap = FootprintMask.StrictOverlap, Seed = 3,
            });
            Assert.DoesNotContain(r.Log, l => l.Contains("one-cell-wide"));
            Assert.NotEmpty(r.Lots);
        }

        [Theory]
        [InlineData(0.05f, "spread")]
        [InlineData(0.5f, "closed")]
        public void End_gaps_are_closed_or_spread_without_overlapping_more_than_allowed(float overlap, string expected)
        {
            var sides = new List<List<Pt>> { L(0, 0, 101.3, 0), L(101.3, 0, 101.3, 67.7), L(101.3, 67.7, 0, 67.7), L(0, 67.7, 0, 0) };
            var strips = VirtualLots.Build(sides, new Quad[0], curveOverlap: overlap);
            var blocks = strips.Select(s => new PlanBlock { Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true, Label = "t" }).ToList();
            var r = PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Outline = VirtualLots.Outline(sides),
                Plain = new[] { B("a1x2", 1, 2), B("a2x2", 2, 2), B("a3x2", 3, 2) },
                Corners = new[] { B("cL", 1, 2, CornerKind.Front | CornerKind.Left), B("cR", 1, 2, CornerKind.Front | CornerKind.Right) },
                Overlap = overlap, Seed = 7,
            });
            Assert.Contains(r.Log, l => l.Contains("gap") && l.Contains(expected));
            var quads = r.Lots.Select(l => l.Footprint(overlap + FootprintMask.StrictOverlap)).ToList();
            for (int i = 0; i < quads.Count; i++)
            {
                for (int j = i + 1; j < quads.Count; j++)
                {
                    Assert.False(Quad.Touch(quads[i], quads[j]), $"{r.Lots[i].Building.PrefabName} and {r.Lots[j].Building.PrefabName}");
                }
            }
        }
    }
}
