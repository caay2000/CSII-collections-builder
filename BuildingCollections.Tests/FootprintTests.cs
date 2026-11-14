using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class FootprintTests
    {
        static IEnumerable<(Pt, Pt, Pt)> Box(Pt origin, Pt along, Pt across)
        {
            var a = origin;
            var b = new Pt(origin.X + along.X, origin.Y + along.Y);
            var c = new Pt(b.X + across.X, b.Y + across.Y);
            var d = new Pt(origin.X + across.X, origin.Y + across.Y);
            yield return (a, b, c);
            yield return (a, c, d);
        }

        static bool Covered(List<FootprintRect> rects, double x, double z) =>
            rects.Any(r => x >= r.MinX && x <= r.MaxX && z >= r.MinZ && z <= r.MaxZ);

        [Fact]
        public void A_rectangle_becomes_one_rect_of_its_size()
        {
            var footprint = Footprint.FromTriangles(Box(new Pt(0, 0), new Pt(16, 0), new Pt(0, 8)));

            var rects = footprint.Rects();

            Assert.Single(rects);
            Assert.Equal(new FootprintRect(0, 0, 16, 8), rects[0]);
        }

        [Fact]
        public void A_wing_turned_45_degrees_leaves_the_courtyard_between_the_wings_free()
        {
            double s = Math.Sqrt(0.5);
            var main = Box(new Pt(-8, -8), new Pt(16, 0), new Pt(0, 16));
            var wing = Box(new Pt(8, 8), new Pt(20 * s, -20 * s), new Pt(-8 * s, -8 * s));

            var rects = Footprint.FromTriangles(main.Concat(wing)).Rects();

            Assert.True(Covered(rects, 0, 0));
            Assert.True(Covered(rects, 8 + 10 * s - 4 * s, 8 - 10 * s - 4 * s));
            Assert.False(Covered(rects, 20, -10));
            Assert.False(Covered(rects, 20, 5));
        }

        [Fact]
        public void A_vertical_wall_seen_from_above_still_marks_its_line()
        {
            var wall = new[] { (new Pt(0, 0.5), new Pt(10, 0.5), new Pt(10, 0.5)) };

            var footprint = Footprint.FromTriangles(wall);

            Assert.Equal(11, footprint.CellCount);
        }

        [Fact]
        public void Rects_survive_a_round_trip_through_the_log_format()
        {
            var rects = new List<FootprintRect> { new FootprintRect(-8, -8, 8, 8), new FootprintRect(8, -2.5, 12.25, 7) };

            Assert.Equal(rects, Footprint.Decode(Footprint.Encode(rects)));
        }
    }
}
