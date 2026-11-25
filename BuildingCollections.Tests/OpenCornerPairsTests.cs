using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class OpenCornerPairsTests
    {
        static readonly IReadOnlyList<BuildingInfo> Catalog = new[]
        {
            new BuildingInfo { PrefabName = "1x2", LotWidth = 1, LotDepth = 2 },
            new BuildingInfo { PrefabName = "2x2", LotWidth = 2, LotDepth = 2 },
            new BuildingInfo { PrefabName = "3x2", LotWidth = 3, LotDepth = 2 },
        };

        static List<List<Pt>> Block(double angle)
        {
            const double length = 160;
            double radians = angle * Math.PI / 180;
            var a = new Pt(0, 0);
            var b = new Pt(length, 0);
            var c = new Pt(length - length * Math.Cos(radians), length * Math.Sin(radians));
            var d = new Pt(-40, c.Y);
            var points = new[] { a, b, c, d };
            return points.Select((p, i) => new List<Pt> { p, points[(i + 1) % points.Length] }).ToList();
        }

        static PreviewResult Plan(List<List<Pt>> sides, float overlap, out List<PlanBlock> blocks)
        {
            var strips = VirtualLots.Build(sides, new List<Quad>(), curveOverlap: overlap > FootprintMask.StrictOverlap ? overlap : 0);
            blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
            }).ToList();
            return PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Existing = new List<Quad>(), Outline = VirtualLots.Outline(sides), Plain = Catalog, Corners = new List<BuildingInfo>(), Overlap = overlap, Seed = 3,
            });
        }

        [Theory]
        [InlineData(110)]
        [InlineData(120)]
        public void An_open_corner_gets_a_building_on_each_street_with_the_lot_corners_together(double angle)
        {
            var result = Plan(Block(angle), 0.5f, out _);

            Assert.Contains(result.Log, l => l.Contains("): paired "));
            var fronts = result.Lots.Where(l => l.Corner).SelectMany(l => l.Footprint(0).P.Take(2).Select(p => (l, p))).ToList();
            Assert.Contains(fronts, x => fronts.Any(y => y.l != x.l && Distance(x.p, y.p) < 0.01));
        }

        [Fact]
        public void A_clear_open_corner_is_paired_on_every_shuffle_moving_neighbours_when_needed()
        {
            var scenario = BuildingCollections.Simulator.GameBlocks.All().Single(s => s.Name.Contains("(13111, -8496)"));
            var sizes = new[] { (1, 2), (2, 1), (2, 2), (2, 3), (3, 2), (3, 3) };
            var plain = sizes.Select(size => new BuildingInfo { PrefabName = $"{size.Item1}x{size.Item2}", LotWidth = size.Item1, LotDepth = size.Item2 }).ToList();
            var corners = new List<BuildingInfo>
            {
                new BuildingInfo { PrefabName = "corner L", LotWidth = 2, LotDepth = 2, Corner = CornerKind.Front | CornerKind.Left },
                new BuildingInfo { PrefabName = "corner R", LotWidth = 2, LotDepth = 2, Corner = CornerKind.Front | CornerKind.Right },
            };

            foreach (int seed in Enumerable.Range(1, 12))
            {
                var run = BuildingCollections.Simulator.Simulation.Simulate(scenario, 2f, seed, plain, corners);

                Assert.Contains(run.Result.Log, l => l.Contains("): paired "));
            }
        }

        [Fact]
        public void Without_overlap_no_corner_is_paired()
        {
            var result = Plan(Block(120), FootprintMask.StrictOverlap, out _);

            Assert.DoesNotContain(result.Log, l => l.Contains("): paired "));
        }

        [Fact]
        public void Models_without_a_front_access_are_never_paired()
        {
            var sides = Block(120);
            var strips = VirtualLots.Build(sides, new List<Quad>(), curveOverlap: 0.5);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
            }).ToList();
            var leftOnly = new BuildingInfo { PrefabName = "left only", LotWidth = 2, LotDepth = 2, Corner = CornerKind.Left };

            var result = PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Existing = new List<Quad>(), Outline = VirtualLots.Outline(sides), Plain = Catalog, Corners = new[] { leftOnly }, Overlap = 0.5f, Seed = 3,
            });

            Assert.DoesNotContain(result.Log, l => l.Contains("left only") && l.Contains("): paired "));
        }

        [Fact]
        public void A_pair_keeps_the_mix_group_of_the_lots_it_replaces()
        {
            var sides = Block(120);
            var strips = VirtualLots.Build(sides, new List<Quad>(), curveOverlap: 0.5);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
            }).ToList();
            var plain = Catalog.Select(b => b.InGroup(0, 1)).ToList();

            var result = PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Existing = new List<Quad>(), Outline = VirtualLots.Outline(sides), Plain = plain, Corners = new List<BuildingInfo>(), Overlap = 0.5f, Seed = 3,
            });

            Assert.All(result.Lots, l => Assert.Equal(0, l.Building.Group));
        }

        static double Distance(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));
    }
}
