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
        [InlineData(140)]
        public void An_open_corner_gets_the_same_model_on_both_streets_with_the_lot_corners_together(double angle)
        {
            var result = Plan(Block(angle), 0.5f, out _);

            var pairs = result.Lots.Where(l => l.Corner).GroupBy(l => l.Building).Where(g => g.Count() >= 2).ToList();
            Assert.NotEmpty(pairs);
            Assert.Contains(result.Log, l => l.Contains("on both streets"));
            var fronts = result.Lots.Where(l => l.Corner).SelectMany(l => l.Footprint(0).P.Take(2).Select(p => (l, p))).ToList();
            Assert.Contains(fronts, x => fronts.Any(y => y.l != x.l && y.l.Building == x.l.Building && Distance(x.p, y.p) < 0.01));
        }

        [Fact]
        public void An_open_corner_pair_never_crosses_its_copy_and_covers_more_sidewalk_or_cells()
        {
            int pairs = 0;
            foreach (double angle in Enumerable.Range(19, 12).Select(step => step * 5.0))
            {
                var result = Plan(Block(angle), 2f, out _);

                foreach (var pair in result.Lots.Where(l => l.Corner).GroupBy(l => l.Building).Where(g => g.Count() == 2))
                {
                    pairs++;
                    var a = pair.First().Footprint(-0.5).P;
                    var b = pair.Last().Footprint(-0.5).P;
                    Assert.True(pair.First().Footprint(0).P.Count(p => Polygon.Contains(b, p)) >= 2, $"{angle}°: {pair.Key} crosses its copy");
                    Assert.True(pair.Last().Footprint(0).P.Count(p => Polygon.Contains(a, p)) >= 2, $"{angle}°: {pair.Key} crosses its copy");
                }

                foreach (var line in result.Log.Where(l => l.Contains(" m uncovered")))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"([\d.,]+) -> ([\d.,]+) m uncovered, (\d+) -> (\d+) street cells filled");
                    Assert.True(m.Success, line);
                    double before = double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                    double after = double.Parse(m.Groups[2].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                    bool moreCells = int.Parse(m.Groups[4].Value) > int.Parse(m.Groups[3].Value);
                    Assert.True(before - after >= 0.95 || moreCells, $"{angle}°: {line}");
                }
            }

            Assert.True(pairs > 0);
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

                Assert.Contains(run.Result.Log, l => l.Contains(" on both streets, "));
            }
        }

        [Fact]
        public void Without_overlap_no_corner_is_paired()
        {
            var result = Plan(Block(120), FootprintMask.StrictOverlap, out _);

            Assert.DoesNotContain(result.Log, l => l.Contains("on both streets"));
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

            Assert.DoesNotContain(result.Log, l => l.Contains("left only") && l.Contains("on both streets"));
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

        [Fact]
        public void Corner_twins_go_side_on_with_the_long_facade_on_each_street_and_the_short_one_at_the_corner()
        {
            var corners = new[]
            {
                new BuildingInfo { PrefabName = "Corner05L_L3_1x2", LotWidth = 1, LotDepth = 2, Level = 3, Corner = CornerKind.Front | CornerKind.Left },
                new BuildingInfo { PrefabName = "Corner05R_L3_1x2", LotWidth = 1, LotDepth = 2, Level = 3, Corner = CornerKind.Front | CornerKind.Right },
            };
            var found = new List<string>();
            foreach (double angle in Enumerable.Range(19, 12).Select(step => step * 5.0))
            {
                var sides = Block(angle);
                var strips = VirtualLots.Build(sides, new List<Quad>(), curveOverlap: 2);
                var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
                {
                    Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
                }).ToList();
                var result = PreviewPlanner.Plan(new PreviewRequest
                {
                    Blocks = blocks, Existing = new List<Quad>(), Outline = VirtualLots.Outline(sides), Plain = Catalog, Corners = corners, Overlap = 2f, Seed = 3,
                });

                foreach (var lot in result.Lots.Where(l => l.FrontAlong != 0))
                {
                    found.Add($"{angle}");
                    Assert.Equal(2, lot.Width);
                    Assert.Equal(1, lot.Depth);
                    var front = lot.Owner.Frame.AxisX * lot.FrontAlong;
                    var expected = lot.FrontAlong > 0 ? CornerKind.Left : CornerKind.Right;
                    Assert.Equal(expected, lot.Building.Corner & (CornerKind.Left | CornerKind.Right));
                    var corner = result.Lots.Single(o => o != lot && o.FrontAlong != 0 && o.Owner != lot.Owner && Distance(o.Center, lot.Center) < 24);
                    Assert.NotEqual(lot.Building.Corner, corner.Building.Corner);
                    Assert.True(Pt.Dot(corner.Center - lot.Center, front) > 0, $"{angle}°: the short facade does not face the corner");
                }
            }

            Assert.NotEmpty(found);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        public void The_twin_facing_the_street_does_not_depend_on_the_handedness_of_the_grid(int handedness)
        {
            var axisX = new Pt(0.6, 0.8);
            var axisY = new Pt(-axisX.Y, axisX.X) * handedness;
            Pt Turn(Pt v, int sign) => new Pt(-v.Y * sign, v.X * sign);
            int toSide = Pt.Cross(axisY * -1, axisX * -1) > 0 ? 1 : -1;

            foreach (int frontAlong in new[] { 1, -1 })
            {
                var leftFacade = Turn(axisX * frontAlong, toSide);
                var facingStreet = Pt.Dot(leftFacade, axisY) < -0.99 ? CornerKind.Left : CornerKind.Right;

                Assert.Equal(facingStreet, OpenCornerPairs.SideFacingStreet(frontAlong));
            }
        }

        static double Distance(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));
    }
}
