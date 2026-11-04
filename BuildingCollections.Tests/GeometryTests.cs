using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class GeometryTests
    {
        static Quad Rect(double x0, double y0, double x1, double y1) =>
            new Quad(new Pt(x0, y0), new Pt(x1, y0), new Pt(x1, y1), new Pt(x0, y1));

        [Fact]
        public void Contains_works_with_both_windings()
        {
            var cw = new Quad(new Pt(0, 0), new Pt(0, 10), new Pt(10, 10), new Pt(10, 0));
            Assert.True(cw.Contains(new Pt(5, 5)));
            Assert.True(Rect(0, 0, 10, 10).Contains(new Pt(5, 5)));
            Assert.False(Rect(0, 0, 10, 10).Contains(new Pt(11, 5)));
        }

        [Fact]
        public void Distance_between_separate_touching_and_overlapping()
        {
            Assert.Equal(8, Quad.Distance(Rect(0, 0, 10, 10), Rect(18, 0, 28, 10)), 6);
            Assert.Equal(0, Quad.Distance(Rect(0, 0, 10, 10), Rect(10, 0, 20, 10)), 6);
            Assert.Equal(0, Quad.Distance(Rect(0, 0, 10, 10), Rect(5, 5, 20, 20)), 6);
        }

        [Fact]
        public void Flood_groups_touching_blocks_and_skips_the_other_side_of_the_road()
        {
            // Manzana: cuatro bloques en L que se tocan; al otro lado de una calle de 12 m, otro bloque.
            var quads = new[]
            {
                Rect(0, 0, 48, 48),     // 0 inicio
                Rect(48, 0, 96, 48),    // 1 tocando a 0
                Rect(96.5, 0, 140, 48), // 2 a 0,5 m de 1
                Rect(0, 60, 48, 108),   // 3 al otro lado de la calle (12 m)
            };
            var g = BlockGrouping.Flood(quads, 0, 2.0).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 0, 1, 2 }, g);
        }
    }

    public class AccessMappingTests
    {
        [Theory]
        [InlineData(0, CornerKind.Front)]
        [InlineData(1, CornerKind.Left)]
        [InlineData(2, CornerKind.Right)]
        [InlineData(3, CornerKind.Left | CornerKind.Right)]
        [InlineData(4, CornerKind.Left)]
        [InlineData(5, CornerKind.Right)]
        [InlineData(6, CornerKind.Front)]
        [InlineData(7, CornerKind.Left | CornerKind.Front | CornerKind.Right)]
        [InlineData(8, CornerKind.None)]
        [InlineData(9, CornerKind.None)]
        public void Maps_access_type(int access, CornerKind expected) => Assert.Equal(expected, AccessMapping.ToCorner(access));

        [Fact]
        public void Swap_exchanges_left_and_right_only()
        {
            Assert.Equal(CornerKind.Right, AccessMapping.ToCorner(1, swapSides: true));
            Assert.Equal(CornerKind.Front, AccessMapping.ToCorner(0, swapSides: true));
        }

        [Fact]
        public void All_fits_front_and_corners()
        {
            var k = AccessMapping.ToCorner(7);
            Assert.True(AccessMapping.FitsFront(k));
            Assert.True(AccessMapping.FitsCorner(k));
        }
    }
}
