using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class AngledCornerTests
    {
        [Theory]
        [InlineData("EU Corner 1 Left - 117 Degree (~4x4)", 117)]
        [InlineData("EU Corner 2 Left - 45 Degree (~5x6)", 45)]
        [InlineData("UK London Townhouse 63 Deg Right", 63)]
        [InlineData("UK_London_Townhouse_117_Deg_Left", 117)]
        [InlineData("Corner135Degrees", 135)]
        [InlineData("Shop 63° corner", 63)]
        public void Reads_the_angle_from_the_name(string name, int angle)
        {
            Assert.Equal(angle, AngledCorner.Angle(name));
        }

        [Theory]
        [InlineData("EU Corner 90 Degree")]
        [InlineData("Block 91 deg")]
        [InlineData("NA_ResidentialHigh_Corner01")]
        [InlineData("Apartment 117 Degrade")]
        [InlineData("Tower 1.117 Deg")]
        [InlineData("Ruin 200 Degree")]
        [InlineData("")]
        [InlineData(null)]
        public void Names_without_a_usable_angle_give_zero(string name)
        {
            Assert.Equal(0, AngledCorner.Angle(name));
        }

        [Theory]
        [InlineData("EU Corner 1 Left - 117 Degree", "Left")]
        [InlineData("UK_London_Townhouse_63_Deg_RIGHT", "Right")]
        [InlineData("Brightside 63 Deg", "-")]
        public void Reads_the_side_from_the_name(string name, string side)
        {
            Assert.Equal(side, AngledCorner.Side(name));
        }
    }
}
