using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildingCollections.Core
{
    public sealed class CornerTwin
    {
        public BuildingInfo Left;
        public BuildingInfo Right;

        public BuildingInfo For(CornerKind side) => side == CornerKind.Left ? Left : Right;
    }

    public static class CornerTwins
    {
        static readonly Regex SideToken = new Regex(@"Left|Right|(?<=\d)[LR](?=_)|(?<= )[LR]$");

        public static List<CornerTwin> Find(IEnumerable<BuildingInfo> corners)
        {
            var sided = corners.Where(b => Side(b) != CornerKind.None).ToList();
            var rights = sided.Where(b => Side(b) == CornerKind.Right).ToLookup(Key);
            return sided
                .Where(b => Side(b) == CornerKind.Left)
                .SelectMany(left => rights[Key(left)].Select(right => new CornerTwin { Left = left, Right = right }))
                .ToList();
        }

        public static CornerKind Side(BuildingInfo b)
        {
            var side = b.Corner & (CornerKind.Left | CornerKind.Right);
            return side == CornerKind.Left || side == CornerKind.Right ? side : CornerKind.None;
        }

        static (string, int, int, int, int) Key(BuildingInfo b) => (SideToken.Replace(b.PrefabName, string.Empty), b.LotWidth, b.LotDepth, b.Level, b.Group);
    }
}
