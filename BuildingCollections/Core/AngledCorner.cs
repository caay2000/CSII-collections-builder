using System.Globalization;
using System.Text.RegularExpressions;

namespace BuildingCollections.Core
{
    public static class AngledCorner
    {
        const int RightAngleTolerance = 2;

        static readonly Regex AnglePattern = new Regex(@"(?<![\d.])(\d{2,3})[\s_-]*(?:°|deg(?:ree)?s?(?![a-z]))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex SidePattern = new Regex(@"(?<![a-z])(left|right)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex ProbePattern = new Regex(@"deg|corner|angle|°", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static int Angle(string prefabName)
        {
            var m = AnglePattern.Match(prefabName ?? string.Empty);
            if (!m.Success)
            {
                return 0;
            }

            int angle = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            bool usable = angle > 0 && angle < 180 && System.Math.Abs(angle - 90) > RightAngleTolerance;
            return usable ? angle : 0;
        }

        public static string Side(string prefabName)
        {
            var m = SidePattern.Match(prefabName ?? string.Empty);
            return m.Success ? char.ToUpperInvariant(m.Value[0]) + m.Value.Substring(1).ToLowerInvariant() : "-";
        }

        public static bool WorthProbing(string prefabName) => ProbePattern.IsMatch(prefabName ?? string.Empty);
    }
}
