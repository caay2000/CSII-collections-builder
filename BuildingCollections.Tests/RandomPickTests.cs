using System;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class RandomPickTests
    {
        [Fact]
        public void Never_repeats_the_current_building_when_there_is_another()
        {
            var rng = new Random(1);
            var names = new[] { "A", "B", "C" };
            var picks = Enumerable.Range(0, 200).Select(_ => RandomPick.Other(names, "b", rng)).ToList();
            Assert.DoesNotContain("B", picks);
            Assert.Contains("A", picks);
            Assert.Contains("C", picks);
        }

        [Fact]
        public void Single_building_is_picked_again_and_empty_gives_null()
        {
            var rng = new Random(1);
            Assert.Equal("A", RandomPick.Other(new[] { "A" }, "A", rng));
            Assert.Equal("A", RandomPick.Other(new[] { "A", "a" }, "A", rng));
            Assert.Null(RandomPick.Other(new string[0], "A", rng));
        }
    }
}
