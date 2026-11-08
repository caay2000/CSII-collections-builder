using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    internal sealed class CornerClaim
    {
        public int Plan;
        public bool AtEnd;
        public LotStrip Strip;
        public Quad Cells;
        public Pt Vertex;
        public int Order;
    }

    internal static class CornerClaims
    {
        const int CornerDepth = 2;
        const double Inset = 0.5;
        const double NearbyCorner = 6;

        internal static CornerClaim For(int plan, LotStrip strip, int order, bool atEnd)
        {
            int x = atEnd ? strip.Width - 1 : 0;
            return new CornerClaim
            {
                Plan = plan, AtEnd = atEnd, Strip = strip, Order = order,
                Cells = CellFrame.FromStrip(strip).Rect(x, 0, x + 1, CornerDepth, 0, Inset),
                Vertex = atEnd ? strip.ChunkEnd : strip.ChunkStart,
            };
        }

        internal static List<CornerClaim> Covering(IEnumerable<CornerClaim> claims)
        {
            var kept = new List<CornerClaim>();
            var covering = new List<CornerClaim>();
            foreach (var claim in claims.OrderByDescending(c => c.Strip.Width).ThenBy(c => c.Order))
            {
                var winner = kept.FirstOrDefault(k => k.Strip != claim.Strip && Quad.Touch(k.Cells, claim.Cells));
                if (winner == null)
                {
                    kept.Add(claim);
                    covering.Add(claim);
                }
                else if (winner.Cells.DistanceTo(claim.Vertex) <= NearbyCorner)
                {
                    covering.Add(claim);
                }
            }

            return covering;
        }
    }
}
