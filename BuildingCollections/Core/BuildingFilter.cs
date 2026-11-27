using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class BuildingFilter
    {
        public const int TopHeight = 200;
        public const int NoMaxHeight = TopHeight + 1;

        public NumberFilter Width = NumberFilter.All;
        public NumberFilter Depth = NumberFilter.All;
        public NumberFilter Level = NumberFilter.All;
        public int MinHeight;
        public int MaxHeight;

        public bool Matches(BuildingInfo b) =>
            Width.Matches(b.LotWidth) && Depth.Matches(b.LotDepth) && Level.Matches(b.Level) && HeightMatches(b.Height);

        bool NoMax => MaxHeight <= 0 || MaxHeight > TopHeight;

        bool HeightMatches(int height) =>
            (MinHeight <= 0 && NoMax)
            || (height > 0 && height >= MinHeight && (NoMax || height <= MaxHeight));

        public List<BuildingInfo> Apply(IEnumerable<BuildingInfo> source) =>
            source.Where(Matches).OrderBy(b => b.PrefabName, System.StringComparer.Ordinal).ToList();
    }
}
