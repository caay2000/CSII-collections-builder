using System.Globalization;
using System.Text.RegularExpressions;

namespace BuildingCollections.Core
{
    public static class AngledCorner
    {
        const int RightAngleTolerance = 2;

        const string Degree = @"(?:°|[Dd]eg(?:ree)?s?|DEG(?:REE)?S?)";

        static readonly Regex AnglePattern = new Regex(
            @"(?<![\d.])(\d{2,3})[\s_-]*" + Degree + @"(?![a-z])|" + Degree + @"[\s_-]*(\d{2,3})(?![\d.])",
            RegexOptions.CultureInvariant);

        public static int Angle(string prefabName)
        {
            var m = AnglePattern.Match(prefabName ?? string.Empty);
            if (!m.Success)
            {
                return 0;
            }

            string digits = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            int angle = int.Parse(digits, CultureInfo.InvariantCulture);
            bool usable = angle > 0 && angle < 180 && System.Math.Abs(angle - 90) > RightAngleTolerance;
            return usable ? angle : 0;
        }
    }
}
