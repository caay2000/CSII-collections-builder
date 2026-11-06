using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    internal static class StripCells
    {
        internal static bool CellInside(LotStrip s, int x, int y, IReadOnlyList<Pt> clip)
        {
            var c = s.CellCenter(x, y);
            double h = CellFrame.Size / 2 - 0.5;
            return Polygon.Contains(clip, c) &&
                Polygon.Contains(clip, LotStrip.Add(c, s.T, -h, s.N, -h)) && Polygon.Contains(clip, LotStrip.Add(c, s.T, h, s.N, -h)) &&
                Polygon.Contains(clip, LotStrip.Add(c, s.T, h, s.N, h)) && Polygon.Contains(clip, LotStrip.Add(c, s.T, -h, s.N, h));
        }

        internal static bool CellFits(LotStrip s, int x, int y, List<Pt> outline, IReadOnlyList<Quad> obstacles)
        {
            var c = s.CellCenter(x, y);
            double h = CellFrame.Size / 2 - 0.5;
            var corners = new[]
            {
                LotStrip.Add(c, s.T, -h, s.N, -h), LotStrip.Add(c, s.T, h, s.N, -h),
                LotStrip.Add(c, s.T, h, s.N, h), LotStrip.Add(c, s.T, -h, s.N, h),
            };
            if (!Polygon.Contains(outline, c))
            {
                return false;
            }

            foreach (var p in corners)
            {
                if (!Polygon.Contains(outline, p))
                {
                    return false;
                }
            }

            if (obstacles.Count > 0)
            {
                double e = CellFrame.Size / 2 - 0.05;
                var cell = new Quad(LotStrip.Add(c, s.T, -e, s.N, -e), LotStrip.Add(c, s.T, e, s.N, -e),
                    LotStrip.Add(c, s.T, e, s.N, e), LotStrip.Add(c, s.T, -e, s.N, e));
                foreach (var o in obstacles)
                {
                    if (o.MaxX >= cell.MinX && o.MinX <= cell.MaxX && o.MaxY >= cell.MinY && o.MinY <= cell.MaxY && Quad.Distance(o, cell) <= 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool CornerCellFits(LotStrip strip, bool atEnd, List<Pt> outline)
        {
            int x = atEnd ? strip.Width - 1 : 0;
            var none = new Quad[0];
            return strip.Width > 0 && strip.Depth >= 2 && CellFits(strip, x, 0, outline, none) && CellFits(strip, x, 1, outline, none);
        }

        internal static bool TakesCorner(LotStrip strip, bool atEnd)
        {
            var g = strip.Grid;
            int x = atEnd ? strip.Width - 1 : 0;
            return strip.Width > 0 && strip.Depth >= 2 && (atEnd ? g.RightCorner : g.LeftCorner) && g.Free[x, 0] && g.Free[x, 1];
        }
    }
}
