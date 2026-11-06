using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class PreviewPlannerTests
    {
        static List<Pt> L(params double[] xy)
        {
            var r = new List<Pt>();
            for (int i = 0; i < xy.Length; i += 2) r.Add(new Pt(xy[i], xy[i + 1]));
            return r;
        }

        static BuildingInfo B(string name, int w, int d, CornerKind corner = CornerKind.Front) =>
            new BuildingInfo { PrefabName = name, LotWidth = w, LotDepth = d, Corner = corner };

        static readonly List<BuildingInfo> Plain = new List<BuildingInfo> { B("a1x2", 1, 2), B("a2x2", 2, 2), B("a3x2", 3, 2) };

        static readonly List<BuildingInfo> Corners = new List<BuildingInfo>
        {
            B("cL", 1, 2, CornerKind.Front | CornerKind.Left), B("cR", 1, 2, CornerKind.Front | CornerKind.Right),
        };

        static PreviewResult Plan(List<List<Pt>> sides, float overlap, int seed = 7, int density = 100, List<BuildingInfo> plain = null,
            List<BuildingInfo> corners = null)
        {
            var strips = VirtualLots.Build(sides, new Quad[0], curveOverlap: overlap > FootprintMask.StrictOverlap ? overlap : 0);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
                Label = $"strip side {s.SideIndex} segment {s.ChunkIndex + 1}",
            }).ToList();
            return PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Outline = VirtualLots.Outline(sides), Plain = plain ?? Plain, Corners = corners ?? Corners,
                Overlap = overlap, Density = density, Seed = seed,
            });
        }

        static List<List<Pt>> Rect(double w, double h) => new List<List<Pt>> { L(0, 0, w, 0), L(w, 0, w, h), L(w, h, 0, h), L(0, h, 0, 0) };

        [Fact]
        public void Exact_rectangle_fills_every_street_cell()
        {
            var r = Plan(Rect(96, 64), 0.5f);
            Assert.Equal(r.FrontCells.Count, r.FrontFilled);
            Assert.True(r.Lots.Count(l => l.Corner) >= 4);
        }

        [Fact]
        public void Lots_never_overlap_more_than_the_allowed_overlap()
        {
            var r = Plan(Rect(101, 67), 0.5f);
            var quads = r.Lots.Select(l => l.Footprint(0.5 + 0.05)).ToList();
            for (int i = 0; i < quads.Count; i++)
            {
                for (int j = i + 1; j < quads.Count; j++)
                {
                    Assert.False(Quad.Touch(quads[i], quads[j]), $"{r.Lots[i].Building} and {r.Lots[j].Building} overlap more than 0.5 m");
                }
            }
        }

        [Fact]
        public void Same_seed_gives_the_same_plan()
        {
            string Sig(PreviewResult r) => string.Join("|", r.Lots.Select(l => $"{l.Building.PrefabName}@{l.MinX},{l.MinY}+{l.Shift:F2}"));
            Assert.Equal(Sig(Plan(Rect(123, 76), 0.5f, seed: 42)), Sig(Plan(Rect(123, 76), 0.5f, seed: 42)));
        }

        [Fact]
        public void Overlap_closes_more_street_than_no_overlap()
        {
            var strict = Plan(Rect(123, 76), FootprintMask.StrictOverlap);
            var loose = Plan(Rect(123, 76), 1f);
            Assert.True(loose.FrontFilled >= strict.FrontFilled);
            Assert.Contains(loose.Log, l => l.Contains("closed"));
        }

        [Fact]
        public void Density_removes_buildings()
        {
            var full = Plan(Rect(160, 88), 0.5f);
            var half = Plan(Rect(160, 88), 0.5f, density: 50);
            Assert.True(half.Lots.Count < full.Lots.Count);
            Assert.NotEmpty(half.Thinned);
        }

        [Fact]
        public void Leftover_that_cannot_be_closed_is_spread_into_even_slivers()
        {
            var r = Plan(Rect(123, 76), FootprintMask.StrictOverlap);
            Assert.Contains(r.Log, l => l.Contains("spread"));
            Assert.Contains(r.Lots, l => l.Shift != 0 && !l.Corner);
        }

        [Fact]
        public void Deep_buildings_of_one_street_leave_the_front_row_of_the_other_free()
        {
            var plain = new List<BuildingInfo> { B("1x1", 1, 1), B("1x2", 1, 2), B("2x2", 2, 2), B("3x2", 3, 2), B("2x3", 2, 3), B("3x3", 3, 3) };
            var r = Plan(new List<List<Pt>> { L(0, 0, 160, 0), L(160, 0, 160, 48.2), L(160, 48.2, 0, 20), L(0, 20, 0, 0) }, 0.5f, plain: plain);
            var bottom = r.Lots.Where(l => l.Owner.Frame.AxisX.X > 0.99 && l.Owner.Frame.AxisY.Y > 0.99).ToList();
            int bottomCells = bottom.Sum(l => l.Width);
            Assert.True(bottomCells >= 17, $"the bottom street only has {bottomCells} cells with a building");
        }

        [Fact]
        public void Without_one_cell_deep_buildings_no_street_cell_is_left_with_a_single_free_row()
        {
            var plain = new List<BuildingInfo> { B("1x2", 1, 2), B("2x2", 2, 2), B("3x2", 3, 2), B("2x3", 2, 3), B("3x3", 3, 3) };
            var corners = new List<BuildingInfo>
            {
                B("cL1", 1, 1, CornerKind.Front | CornerKind.Left), B("cR1", 1, 1, CornerKind.Front | CornerKind.Right),
                B("cL2", 1, 2, CornerKind.Front | CornerKind.Left), B("cR2", 1, 2, CornerKind.Front | CornerKind.Right),
            };
            for (int seed = 1; seed <= 10; seed++)
            {
                var r = Plan(Rect(96, 64), 2f, seed: seed, plain: plain, corners: corners);
                Assert.True(r.FrontFilled == r.FrontCells.Count, $"seed {seed}: {r.FrontFilled} of {r.FrontCells.Count} street cells filled");
            }
        }

        [Fact]
        public void A_two_wide_one_deep_building_does_not_shrink_the_reserved_front()
        {
            var plain = new List<BuildingInfo> { B("1x2", 1, 2), B("2x1", 2, 1), B("2x2", 2, 2), B("3x2", 3, 2), B("2x3", 2, 3), B("3x3", 3, 3) };
            var corners = new List<BuildingInfo>
            {
                B("cL1", 1, 1, CornerKind.Front | CornerKind.Left), B("cR1", 1, 1, CornerKind.Front | CornerKind.Right),
                B("cL2", 1, 2, CornerKind.Front | CornerKind.Left), B("cR2", 1, 2, CornerKind.Front | CornerKind.Right),
                B("cL22", 2, 2, CornerKind.Front | CornerKind.Left), B("cR22", 2, 2, CornerKind.Front | CornerKind.Right),
            };
            var runs = Enumerable.Range(1, 10).Select(seed => Plan(Rect(99.9, 35.8), 2f, seed: seed, plain: plain, corners: corners)).ToList();
            int filled = runs.Sum(r => r.FrontFilled);
            int cells = runs.Sum(r => r.FrontCells.Count);
            Assert.True(filled >= cells - 10, $"{filled} of {cells} street cells filled over ten seeds");
        }

        [Fact]
        public void Street_with_leftover_and_few_joints_is_filled_with_narrow_buildings()
        {
            var plain = new List<BuildingInfo> { B("1x1", 1, 1), B("1x2", 1, 2), B("2x2", 2, 2), B("3x2", 3, 2), B("2x3", 2, 3), B("3x3", 3, 3) };
            var r = Plan(Rect(61.1, 100), FootprintMask.StrictOverlap, plain: plain);
            Assert.Contains(r.Log, l => l.Contains("one-cell-wide"));
            Assert.Contains(r.Log, l => l.Contains("spread"));
        }
    }
}
