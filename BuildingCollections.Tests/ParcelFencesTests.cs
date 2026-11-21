using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class ParcelFencesTests
    {
        static readonly PlanBlock Row = new PlanBlock { Frame = new CellFrame(new Pt(4, 4), new Pt(1, 0), new Pt(0, 1)) };

        static PlannedLot Lot(int minX, int maxX, int depth, float shift = 0, bool corner = false) =>
            new PlannedLot { Owner = Row, MinX = minX, MaxX = maxX, MinY = 0, MaxY = depth, Shift = shift, Corner = corner };

        static bool Along(FenceSegment s, double y) => System.Math.Abs(s.A.Y - y) < 0.01 && System.Math.Abs(s.B.Y - y) < 0.01;

        static bool Across(FenceSegment s, double x) => System.Math.Abs(s.A.X - x) < 0.6 && System.Math.Abs(s.B.X - x) < 0.6;

        [Fact]
        public void Neighbours_share_one_fence_and_the_street_front_stays_open()
        {
            var fences = ParcelFences.Plan(new List<PlannedLot> { Lot(0, 2, 3), Lot(2, 4, 3) });

            Assert.Equal(5, fences.Count);
            Assert.DoesNotContain(fences, f => Along(f, 0));
            Assert.Single(fences, f => Across(f, 16));
        }

        [Fact]
        public void Overlapping_neighbours_do_not_get_a_double_fence()
        {
            var fences = ParcelFences.Plan(new List<PlannedLot> { Lot(0, 2, 3), Lot(2, 4, 3, shift: -0.5f) });

            Assert.Single(fences, f => Across(f, 16));
        }

        [Fact]
        public void A_deeper_neighbour_keeps_the_whole_shared_side()
        {
            var fences = ParcelFences.Plan(new List<PlannedLot> { Lot(0, 2, 3), Lot(2, 4, 2) });

            var shared = Assert.Single(fences, f => Across(f, 16));
            Assert.Equal(24, shared.Length, 3);
        }

        [Fact]
        public void A_corner_lot_leaves_its_street_side_open()
        {
            var fences = ParcelFences.Plan(new List<PlannedLot> { Lot(0, 2, 3, corner: true), Lot(2, 4, 3) });

            Assert.DoesNotContain(fences, f => Across(f, 0));
            Assert.Single(fences, f => Across(f, 16));
        }

        [Fact]
        public void A_rear_on_the_block_outline_gets_no_fence()
        {
            var outline = new List<Pt> { new Pt(0, 0), new Pt(32, 0), new Pt(32, 24), new Pt(0, 24) };

            var fences = ParcelFences.Plan(new List<PlannedLot> { Lot(0, 2, 3), Lot(2, 4, 3) }, outline);

            Assert.DoesNotContain(fences, f => Along(f, 24));
        }

        [Fact]
        public void Neighbours_on_a_curve_share_one_fence()
        {
            double angle = 10 * System.Math.PI / 180;
            var turned = new PlanBlock
            {
                Frame = new CellFrame(new Pt(20, 4), new Pt(System.Math.Cos(angle), System.Math.Sin(angle)), new Pt(-System.Math.Sin(angle), System.Math.Cos(angle))),
            };
            var lots = new List<PlannedLot>
            {
                Lot(0, 2, 3),
                new PlannedLot { Owner = turned, MinX = 0, MaxX = 2, MinY = 0, MaxY = 3 },
            };

            var fences = ParcelFences.Plan(lots);

            Assert.Equal(5, fences.Count);
        }

        [Fact]
        public void No_lots_means_no_fences()
        {
            Assert.Empty(ParcelFences.Plan(new List<PlannedLot>()));
        }
    }
}
