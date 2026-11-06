using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class MixTests
    {
        static BuildingInfo B(string n, int group, double share) =>
            new BuildingInfo { PrefabName = n, LotWidth = 1, LotDepth = 1, Level = 1 }.InGroup(group, share);

        [Theory]
        [InlineData(0.7)]
        [InlineData(0.3)]
        public void Share_follows_the_slider_even_with_uneven_collections(double share)
        {
            var plain = new[] { B("a", 0, share) }.Concat(Enumerable.Range(0, 9).Select(i => B("b" + i, 1, 1 - share))).ToList();
            int first = 0, total = 0;
            for (int seed = 0; seed < 200; seed++)
            {
                var plan = GridPlanner.Plan(PlanGrid.AllFree(20, 1, false, false), plain, new BuildingInfo[0], 100, seed);
                first += plan.Count(p => p.Building.Group == 0);
                total += plan.Count;
            }

            Assert.Equal(4000, total);
            Assert.InRange(first / (double)total, share - 0.05, share + 0.05);
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
