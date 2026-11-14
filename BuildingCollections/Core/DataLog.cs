using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BuildingCollections.Core
{
    public static class DataLog
    {
        const string Tag = "  [data] ";

        public static IEnumerable<string> Sides(IReadOnlyList<List<Pt>> sides, IReadOnlyList<bool> frontage) =>
            sides.Select((s, i) => $"{Tag}sidewalk {i} street={(frontage == null || (i < frontage.Count && frontage[i]) ? 1 : 0)}: {Points(s)}");

        public static IEnumerable<string> Existing(IEnumerable<Quad> quads) => quads.Select(q => $"{Tag}existing: {Points(q.P)}");

        public static IEnumerable<string> Proposed(IEnumerable<PlannedLot> lots) =>
            lots.Select(l => $"{Tag}proposed {l.Building.LotWidth}x{l.Building.LotDepth}{(l.Corner ? " corner" : string.Empty)} {Escape(l.Building.PrefabName)}: {Points(l.Footprint(0).P)}");

        public static IEnumerable<string> Roads(IEnumerable<(int id, RoadEdge edge)> roads) =>
            roads.Select(r => $"{Tag}road {r.id} nodes {r.edge.Start}-{r.edge.End}: {Points(r.edge.Points)}");

        static string Points(IEnumerable<Pt> pts) => string.Join(";", pts.Select(p => string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2}", p.X, p.Y)));

        static string Escape(string name) => name.Replace(":", "_");
    }
}
