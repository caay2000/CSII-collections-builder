using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using BuildingCollections.Simulator;
using Xunit;

namespace BuildingCollections.Tests
{
    public class AngledCornerPlacerTests
    {
        static List<List<Pt>> Parallelogram(double angle)
        {
            const double w = 140, h = 90;
            double dx = h / Math.Tan(angle * Math.PI / 180);
            return Scenarios.Polygon(new Pt(0, 0), new Pt(w, 0), new Pt(w + dx, h), new Pt(dx, h));
        }

        static AngledCornerPlacer Place(List<List<Pt>> sides, IEnumerable<BuildingInfo> buildings) =>
            AngledCornerPlacer.Place(sides, null, VirtualLots.Outline(sides), new List<Quad>(), buildings, 7);

        static bool Inside(IReadOnlyList<Pt> outline, Pt p, double slack) =>
            Polygon.Contains(outline, p) || outline.Select((a, i) => Distance(p, a, outline[(i + 1) % outline.Count])).Min() <= slack;

        static double Distance(Pt p, Pt a, Pt b)
        {
            var d = b - a;
            double t = Math.Max(0, Math.Min(1, Pt.Dot(p - a, d) / Pt.Dot(d, d)));
            var e = p - (a + d * t);
            return Math.Sqrt(Pt.Dot(e, e));
        }

        [Theory]
        [InlineData(45)]
        [InlineData(63)]
        public void An_acute_corner_gets_a_building_of_its_angle_whose_whole_shape_stays_in_the_block(int angle)
        {
            var sides = Parallelogram(angle);
            var outline = VirtualLots.Outline(sides);

            var placer = Place(sides, Simulation.AngledCorners);

            var acute = placer.Placed.Where(l => l.Building.CornerAngle == angle).ToList();
            Assert.Equal(2, acute.Count);
            foreach (var lot in acute)
            {
                Assert.All(lot.Footprint(0.3).P, p => Assert.True(Inside(outline, p, 0)));
                Assert.All(lot.Wings(0.5).SelectMany(q => q.P), p => Assert.True(Inside(outline, p, AngledCornerPlacer.Overhang)));
            }
        }

        [Fact]
        public void The_obtuse_corners_of_the_same_block_get_the_matching_buildings()
        {
            var placer = Place(Parallelogram(45), Simulation.AngledCorners);

            Assert.Equal(2, placer.Placed.Count(l => l.Building.CornerAngle == 135));
        }

        [Fact]
        public void An_obtuse_corner_a_few_degrees_off_keeps_the_ends_of_every_wing_row_off_the_sidewalk()
        {
            var sides = Parallelogram(53);
            var outline = VirtualLots.Outline(sides);

            var placer = Place(sides, Simulation.AngledCorners);

            var obtuse = placer.Placed.Where(l => l.Building.CornerAngle > 90).ToList();
            Assert.NotEmpty(obtuse);
            foreach (var lot in obtuse)
            {
                Assert.All(lot.Wings().SelectMany(q => q.P), p => Assert.True(Inside(outline, p, 0.75)));
            }
        }

        [Fact]
        public void A_corner_far_from_every_building_angle_gets_none()
        {
            var placer = Place(Parallelogram(75), Simulation.AngledCorners);

            Assert.Empty(placer.Placed);
        }

        [Fact]
        public void A_corner_close_to_a_building_angle_gets_the_closest_one_inside_the_block()
        {
            var sides = Parallelogram(54);
            var outline = VirtualLots.Outline(sides);

            var placer = Place(sides, Simulation.AngledCorners);

            var acute = placer.Placed.Where(l => l.Building.CornerAngle < 90).ToList();
            Assert.Equal(2, acute.Count);
            Assert.All(acute, lot => Assert.True(Math.Abs(lot.Building.CornerAngle - 54) <= AngledCornerPlacer.MaxMismatch));
            foreach (var lot in acute)
            {
                Assert.All(lot.Footprint(0.3).P, p => Assert.True(Inside(outline, p, 0)));
                Assert.All(lot.Wings(0.5).SelectMany(q => q.P), p => Assert.True(Inside(outline, p, AngledCornerPlacer.Overhang)));
            }
        }

        [Fact]
        public void Buildings_without_a_shape_are_never_placed()
        {
            var shapeless = Simulation.AngledCorners.Select(b => { var c = b.InGroup(0, 1); c.Footprint = null; return c; });

            var placer = Place(Parallelogram(45), shapeless);

            Assert.Empty(placer.Placed);
        }

        [Fact]
        public void A_placed_building_faces_its_street_and_is_turned_not_mirrored()
        {
            var placer = Place(Parallelogram(45), Simulation.AngledCorners);

            foreach (var lot in placer.Placed)
            {
                var frame = lot.Owner.Frame;
                var outward = frame.AxisY * -1;
                Assert.Equal(1, Pt.Cross(frame.AxisX, outward), 6);
                var front = lot.Center + outward * (lot.Depth * CellFrame.Size / 2 + 1);
                Assert.False(Polygon.Contains(VirtualLots.Outline(Parallelogram(45)), front));
            }
        }

        [Fact]
        public void The_preview_keeps_an_angled_corner_only_when_it_leaves_no_more_gaps_between_buildings()
        {
            var scenario = GameBlocks.All().Single(s => s.Name.StartsWith("block with a 45° and a 135° corner"));

            var run = Simulation.Simulate(scenario, 1f, 7, Simulation.Plain, Simulation.Corners.Concat(Simulation.AngledCorners).ToList());

            Assert.Contains(run.Result.Lots, l => l.Building.CornerAngle == 45);
            Assert.Contains(run.Result.Log, l => l.Contains("left out") || l.Contains("kept"));
            Assert.True(run.GapMeters <= 4.05, $"{run.GapMeters:F1} m of gaps between buildings");
        }

        [Fact]
        public void No_angled_building_goes_over_the_sidewalk()
        {
            foreach (int angle in new[] { 45, 54, 63 })
            {
                var sides = Parallelogram(angle);
                var outline = VirtualLots.Outline(sides);

                var placer = Place(sides, Simulation.AngledCorners);

                Assert.All(placer.Choices.SelectMany(c => c).SelectMany(l => l.Wings(0.5)).SelectMany(q => q.P), p => Assert.True(Inside(outline, p, 0)));
            }
        }

        [Fact]
        public void Each_corner_offers_the_first_setback_that_fits_and_a_few_further_from_the_corner()
        {
            var placer = Place(Parallelogram(45), Simulation.AngledCorners);

            Assert.Equal(placer.Placed.Count, placer.Choices.Count);
            Assert.All(placer.Choices.Zip(placer.Placed), c => Assert.Same(c.Second, c.First[0]));
            Assert.Contains(placer.Choices, c => c.Count > 1);
        }

        [Fact]
        public void A_corner_next_to_a_curved_side_gets_no_angled_building()
        {
            var scenario = Scenarios.All().Single(s => s.Name == "side curved inwards (sagitta 16 m) ending at a corner");
            var sides = scenario.Sides;

            var placer = Place(sides, Simulation.AngledCorners);

            Assert.Empty(placer.Placed);
            Assert.Contains(placer.Log, l => l.Contains("one of its sides is curved"));
        }
    }
}
