using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class MixTests
    {
        static BuildingInfo B(string n, int w, int d, int group, double share, CornerKind corner = CornerKind.Front) =>
            new BuildingInfo { PrefabName = n, LotWidth = w, LotDepth = d, Level = 1, Corner = corner }.InGroup(group, share);

        static (List<BuildingInfo> plain, List<BuildingInfo> corners) WideAndRow(double wideShare)
        {
            var plain = new List<BuildingInfo>
            {
                B("w3x2", 3, 2, 0, wideShare), B("w2x3", 2, 3, 0, wideShare), B("w3x3", 3, 3, 0, wideShare),
                B("r1x2", 1, 2, 1, 1 - wideShare), B("r2x2", 2, 2, 1, 1 - wideShare),
            };
            var corners = new List<BuildingInfo>
            {
                B("rL", 1, 2, 1, 1 - wideShare, CornerKind.Front | CornerKind.Left), B("rR", 1, 2, 1, 1 - wideShare, CornerKind.Front | CornerKind.Right),
            };
            return (plain, corners);
        }

        static PreviewResult Plan(double w, double h, List<BuildingInfo> plain, List<BuildingInfo> corners, int seed)
        {
            var sides = new List<List<Pt>>
            {
                new List<Pt> { new Pt(0, 0), new Pt(w, 0) }, new List<Pt> { new Pt(w, 0), new Pt(w, h) },
                new List<Pt> { new Pt(w, h), new Pt(0, h) }, new List<Pt> { new Pt(0, h), new Pt(0, 0) },
            };
            var strips = VirtualLots.Build(sides, new Quad[0], curveOverlap: 0.5);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true, Label = $"side {s.SideIndex}",
            }).ToList();
            return PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Outline = VirtualLots.Outline(sides), Plain = plain, Corners = corners, Overlap = 0.5f, Seed = seed,
            });
        }

        [Theory]
        [InlineData(0.8)]
        [InlineData(0.5)]
        [InlineData(0.2)]
        public void Street_frontage_follows_the_slider_when_only_one_collection_has_corners_and_narrow_buildings(double share)
        {
            var (plain, corners) = WideAndRow(share);
            var shares = Enumerable.Range(0, 10).Select(seed => Plan(160, 88, plain, corners, seed)).Select(r => r.FirstGroupShare.Value).ToList();
            Assert.InRange(shares.Average(), share - 0.07, share + 0.07);
        }

        [Fact]
        public void Mixing_keeps_every_street_cell_filled()
        {
            var (plain, corners) = WideAndRow(0.8);
            var r = Plan(96, 64, plain, corners, 3);
            Assert.Equal(r.FrontCells.Count, r.FrontFilled);
        }

        [Fact]
        public void The_smaller_collection_is_spread_over_the_block()
        {
            var (plain, corners) = WideAndRow(0.8);
            var owners = Enumerable.Range(0, 5).Select(seed => Plan(160, 88, plain, corners, seed))
                .Select(r => r.Lots.Where(l => l.Building.Group == 1).Select(l => l.Owner).Distinct().Count());
            Assert.True(owners.Average() >= 2);
        }

        [Fact]
        public void Without_a_mix_there_is_no_share()
        {
            var plain = new List<BuildingInfo> { new BuildingInfo { PrefabName = "a", LotWidth = 2, LotDepth = 2 } };
            Assert.Null(Plan(96, 64, plain, new List<BuildingInfo>(), 1).FirstGroupShare);
        }

        [Fact]
        public void Surplus_frees_runs_wide_enough_for_the_other_collection()
        {
            var block = new PlanBlock();
            var lots = Enumerable.Range(0, 10).Select(i => new PlannedLot
            {
                Owner = block, MinX = i, MaxX = i + 1, MinY = 0, MaxY = 2, Building = B("r" + i, 1, 2, 0, 0.7),
            }).ToList();
            var mix = MixSplit.Of(lots.Select(l => l.Building).Append(B("w", 3, 2, 1, 0.3)));
            var removed = mix.Surplus(lots.Select(l => (l, 1)).ToList(), 10, _ => 3, new System.Random(1));
            Assert.Equal(3, removed.Count);
            var xs = removed.Select(l => l.MinX).OrderBy(x => x).ToList();
            Assert.Equal(xs[0] + 2, xs[2]);
        }

        [Fact]
        public void The_bigger_share_goes_first()
        {
            var mix = MixSplit.Of(new[] { B("a", 1, 1, 0, 0.3), B("b", 1, 1, 1, 0.7) });
            Assert.Equal((1, 0.7), (mix.First, mix.FirstShare));
        }

        [Fact]
        public void InGroup_copies_without_touching_the_original()
        {
            var original = new BuildingInfo { PrefabName = "x", LotWidth = 2, LotDepth = 3, Level = 4, Corner = CornerKind.Left };
            var copy = original.InGroup(1, 0.25);
            Assert.Equal(0, original.Group);
            Assert.Equal(1.0, original.Share);
            Assert.Equal((1, 0.25, "x", 2, 3, 4, CornerKind.Left), (copy.Group, copy.Share, copy.PrefabName, copy.LotWidth, copy.LotDepth, copy.Level, copy.Corner));
        }
    }
}
