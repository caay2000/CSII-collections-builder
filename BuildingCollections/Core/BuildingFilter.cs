using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    /// <summary>Filtros de tamaño (ancho y fondo por separado, como Find It) y de nivel.</summary>
    public sealed class BuildingFilter
    {
        public NumberFilter Width = NumberFilter.All;
        public NumberFilter Depth = NumberFilter.All;
        public NumberFilter Level = NumberFilter.All;

        public bool Matches(BuildingInfo b) =>
            Width.Matches(b.LotWidth) && Depth.Matches(b.LotDepth) && Level.Matches(b.Level);

        /// <summary>Candidatos en orden estable (por nombre), para que la misma regla dé el mismo conjunto.</summary>
        public List<BuildingInfo> Apply(IEnumerable<BuildingInfo> source) =>
            source.Where(Matches).OrderBy(b => b.PrefabName, System.StringComparer.Ordinal).ToList();
    }
}
