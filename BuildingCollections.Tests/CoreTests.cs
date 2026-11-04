using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class NumberFilterTests
    {
        [Fact] public void All_matches_anything() => Assert.True(NumberFilter.All.Matches(99));

        [Theory]
        [InlineData(CompareOp.Equal, 2, 2, true)]
        [InlineData(CompareOp.Equal, 2, 3, false)]
        [InlineData(CompareOp.NotEqual, 2, 3, true)]
        [InlineData(CompareOp.NotEqual, 2, 2, false)]
        [InlineData(CompareOp.Greater, 2, 3, true)]
        [InlineData(CompareOp.Greater, 2, 2, false)]
        [InlineData(CompareOp.Less, 2, 1, true)]
        [InlineData(CompareOp.Less, 2, 2, false)]
        public void Operators(CompareOp op, int value, int number, bool expected) =>
            Assert.Equal(expected, NumberFilter.Of(op, value).Matches(number));
    }

    public class BuildingFilterTests
    {
        static BuildingInfo B(string n, int w, int d, int l) => new BuildingInfo { PrefabName = n, LotWidth = w, LotDepth = d, Level = l };

        [Fact]
        public void Filters_size_and_level_and_sorts_by_name()
        {
            var all = new[] { B("c", 2, 2, 1), B("a", 2, 2, 3), B("b", 3, 2, 1), B("d", 2, 4, 1) };
            var f = new BuildingFilter { Width = NumberFilter.Of(CompareOp.Equal, 2), Depth = NumberFilter.Of(CompareOp.Equal, 2), Level = NumberFilter.Of(CompareOp.Less, 3) };
            Assert.Equal(new[] { "c" }, f.Apply(all).Select(x => x.PrefabName));

            var g = new BuildingFilter { Width = NumberFilter.Of(CompareOp.Equal, 2) };
            Assert.Equal(new[] { "a", "c", "d" }, g.Apply(all).Select(x => x.PrefabName));
        }
    }

    public class CollectionLogicTests
    {
        [Fact]
        public void AddRange_keeps_existing_and_skips_duplicates_ignoring_case()
        {
            var list = new List<string> { "A", "B" };
            int added = CollectionLogic.AddRange(list, new[] { "b", "C", "c", "", "D" });
            Assert.Equal(2, added);
            Assert.Equal(new[] { "A", "B", "C", "D" }, list);
        }

        [Fact]
        public void Remove_ignores_case()
        {
            var list = new List<string> { "A", "B" };
            Assert.True(CollectionLogic.Remove(list, "a"));
            Assert.False(CollectionLogic.Remove(list, "zzz"));
            Assert.Equal(new[] { "B" }, list);
        }

        [Theory]
        [InlineData("#My Collection", "My-Collection")]
        [InlineData("  a   b  ", "a-b")]
        [InlineData("", "")]
        public void NormalizeTag(string input, string expected) => Assert.Equal(expected, CollectionLogic.NormalizeTag(input));
    }

    public class GridPlannerTests
    {
        static BuildingInfo B(string n, int w, int d, CornerKind c = CornerKind.Front) =>
            new BuildingInfo { PrefabName = n, LotWidth = w, LotDepth = d, Level = 1, Corner = c };

        static readonly BuildingInfo[] Plain = { B("p2", 2, 2), B("p3", 3, 2) };
        static readonly BuildingInfo[] Corners = { B("cl", 2, 2, CornerKind.Left), B("cr", 2, 2, CornerKind.Right) };

        static void AssertNoOverlapAndInside(PlanGrid g, List<Placement> plan)
        {
            var seen = new HashSet<(int, int)>();
            foreach (var p in plan)
            {
                for (int i = 0; i < p.Building.LotWidth; i++)
                {
                    for (int j = 0; j < p.Building.LotDepth; j++)
                    {
                        int x = p.X + i, y = p.Y + j;
                        Assert.InRange(x, 0, g.Width - 1);
                        Assert.InRange(y, 0, g.Depth - 1);
                        Assert.True(g.Free[x, y]);
                        Assert.True(seen.Add((x, y)), "celda duplicada");
                    }
                }
            }
        }

        [Fact]
        public void Corners_get_corner_buildings_and_middle_gets_plain()
        {
            var g = PlanGrid.AllFree(10, 3, true, true);
            var plan = GridPlanner.Plan(g, Plain, Corners, 100, 1);
            AssertNoOverlapAndInside(g, plan);
            var first = plan.OrderBy(p => p.X).First();
            var last = plan.OrderBy(p => p.X).Last();
            Assert.Equal("cl", first.Building.PrefabName);
            Assert.Equal("cr", last.Building.PrefabName);
            Assert.All(plan.Where(p => p != first && p != last), p => Assert.False(p.IsCorner));
        }

        [Fact]
        public void No_corner_buildings_falls_back_to_plain_in_corners()
        {
            var g = PlanGrid.AllFree(10, 3, true, true);
            var plan = GridPlanner.Plan(g, Plain, new BuildingInfo[0], 100, 1);
            AssertNoOverlapAndInside(g, plan);
            Assert.Equal(0, plan.OrderBy(p => p.X).First().X);
            Assert.All(plan, p => Assert.False(p.IsCorner));
        }

        [Fact]
        public void Same_seed_same_result_different_seed_differs()
        {
            var g = PlanGrid.AllFree(24, 3, false, false);
            string Sig(int seed) => string.Join(",", GridPlanner.Plan(g, Plain, Corners, 100, seed).Select(p => $"{p.X}:{p.Building.PrefabName}"));
            Assert.Equal(Sig(5), Sig(5));
            Assert.Contains(Enumerable.Range(6, 20), s => Sig(s) != Sig(5));
        }

        [Theory]
        [InlineData(100)]
        [InlineData(50)]
        [InlineData(25)]
        public void Density_never_exceeds_target(int density)
        {
            var g = PlanGrid.AllFree(30, 3, true, true);
            var plan = GridPlanner.Plan(g, Plain, Corners, density, 3);
            AssertNoOverlapAndInside(g, plan);
            Assert.True(plan.Sum(p => p.Cells) <= System.Math.Floor(g.FreeCount() * density / 100.0) || plan.All(p => p.IsCorner));
        }

        [Fact]
        public void Zero_density_places_nothing() =>
            Assert.Empty(GridPlanner.Plan(PlanGrid.AllFree(10, 3, true, true), Plain, Corners, 0, 1));

        [Fact]
        public void Respects_blocked_cells()
        {
            var g = PlanGrid.AllFree(10, 3, false, false);
            g.Free[4, 0] = false;
            AssertNoOverlapAndInside(g, GridPlanner.Plan(g, Plain, Corners, 100, 9));
        }
    }
}
