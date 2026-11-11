using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class AngledCornerTests
    {
        [Theory]
        [InlineData("EU_ResidentialCorner1Deg117L", 117)]
        [InlineData("EU_ResidentialCorner5Deg45R", 45)]
        [InlineData("EU_ResidentialCorner2Deg135L", 135)]
        [InlineData("UK_LondonTownhouse117DegL", 117)]
        [InlineData("UK_LondonTownhouse63DegR", 63)]
        [InlineData("EU Corner 1 Left - 117 Degree (~4x4)", 117)]
        [InlineData("UK London Townhouse 63 Deg Right", 63)]
        [InlineData("Corner135Degrees", 135)]
        [InlineData("Shop 63° corner", 63)]
        [InlineData("CORNER 135 DEGREES", 135)]
        public void Reads_the_angle_from_the_name(string name, int angle)
        {
            Assert.Equal(angle, AngledCorner.Angle(name));
        }

        [Theory]
        [InlineData("EU Corner 90 Degree")]
        [InlineData("EU_ResidentialCorner1Deg90L")]
        [InlineData("Block 91 deg")]
        [InlineData("NA_ResidentialHigh_Corner01")]
        [InlineData("CargoHarborComplexSidegate01")]
        [InlineData("Apartment 117 Degrade")]
        [InlineData("Tower 1.117 Deg")]
        [InlineData("Ruin 200 Degree")]
        [InlineData("Deg1171")]
        [InlineData("")]
        [InlineData(null)]
        public void Names_without_a_usable_angle_give_zero(string name)
        {
            Assert.Equal(0, AngledCorner.Angle(name));
        }
    }
}
