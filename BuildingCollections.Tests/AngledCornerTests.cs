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
        [InlineData("Townhouse_63DegLeft", 63)]
        [InlineData("CORNER 135 DEGREES", 135)]
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
        [InlineData("Townhouse_63DegLeft", "Left")]
        [InlineData("Leftover 45 Deg", "-")]
        public void Reads_the_side_from_the_name(string name, string side)
        {
            Assert.Equal(side, AngledCorner.Side(name));
        }

        [Theory]
        [InlineData("EU Corner 1 Left - 117 Degree", true)]
        [InlineData("Angled Shop", true)]
        [InlineData("Rectangle Plaza", false)]
        [InlineData("Triangle Park", false)]
        public void Probes_names_about_corners_and_angles(string name, bool probed)
        {
            Assert.Equal(probed, AngledCorner.WorthProbing(name));
        }
    }
}
