using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class BuildingFilter
    {
        public NumberFilter Width = NumberFilter.All;
        public NumberFilter Depth = NumberFilter.All;
        public NumberFilter Level = NumberFilter.All;
        public int MinHeight;
        public int MaxHeight;

        public bool Matches(BuildingInfo b) =>
            Width.Matches(b.LotWidth) && Depth.Matches(b.LotDepth) && Level.Matches(b.Level) && HeightMatches(b.Height);

        bool HeightMatches(int height) =>
            (MinHeight <= 0 && MaxHeight <= 0)
            || (height > 0 && height >= MinHeight && (MaxHeight <= 0 || height <= MaxHeight));

        public List<BuildingInfo> Apply(IEnumerable<BuildingInfo> source) =>
            source.Where(Matches).OrderBy(b => b.PrefabName, System.StringComparer.Ordinal).ToList();
    }
}
