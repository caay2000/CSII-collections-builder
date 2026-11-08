using System.Linq;
using BuildingCollections.Core;
using BuildingCollections.Simulator;
using Xunit;

namespace BuildingCollections.Tests
{
    public class CornerClaimsTests
    {
        static LotStrip Strip(Pt start, Pt dir, double length, int width) => new LotStrip
        {
            P0 = start, T = dir, Width = width, Depth = 6, ChunkStart = start, ChunkLength = length,
        };

        static System.Collections.Generic.List<CornerClaim> Resolve(params CornerClaim[] claims)
        {
            CornerClaims.Rank(claims);
            return CornerClaims.Covering(claims.ToList());
        }

        [Fact]
        public void Across_a_short_side_the_wider_strip_keeps_its_corner_and_the_far_corner_is_lost()
        {
            var narrow = Strip(new Pt(48, 24), new Pt(-1, 0), 48, 6);
            var wide = Strip(new Pt(0, 0), new Pt(1, 0), 160, 20);
            var narrowEnd = CornerClaims.For(0, narrow, 0, true);
            var wideStart = CornerClaims.For(1, wide, 1000, false);

            var covering = Resolve(narrowEnd, wideStart);

            Assert.Contains(wideStart, covering);
            Assert.DoesNotContain(narrowEnd, covering);
        }

        [Fact]
        public void A_lost_claim_still_covers_its_corner_when_the_winning_corner_building_is_next_to_it()
        {
            var shortSide = Strip(new Pt(0, 20), new Pt(0, -1), 20, 2);
            var nearSide = Strip(new Pt(0, 4), new Pt(1, 0), 160, 20);

            var covering = Resolve(CornerClaims.For(0, shortSide, 0, false), CornerClaims.For(1, nearSide, 1000, false));

            Assert.Equal(2, covering.Count);
        }

        [Fact]
        public void A_short_side_between_two_corners_does_not_leave_the_near_square_corner_without_a_building()
        {
            Simulation.Plain = Simulation.Catalogs[Simulation.DefaultCatalog];
            var block = GameBlocks.All().Single(s => s.Name == "block_18681_-11093");
            for (int seed = 1; seed <= 8; seed++)
            {
                var run = Simulation.Simulate(block, 0.5f, seed);
                Assert.True(run.GapMeters < 5, $"seed {seed}: {run.GapMeters:0.0} m of sidewalk uncovered");
            }
        }
    }
}
