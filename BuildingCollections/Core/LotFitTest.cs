using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    internal sealed class LotFitTest
    {
        const double ProbeInset = 0.5;

        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> thinned;
        readonly IReadOnlyList<Pt> outline;
        readonly List<Quad> others;
        readonly int dir;

        public LotFitTest(IReadOnlyList<Quad> existing, IReadOnlyList<Pt> thinned, IReadOnlyList<Pt> outline, List<Quad> others, int dir)
        {
            this.existing = existing;
            this.thinned = thinned;
            this.outline = outline;
            this.others = others;
            this.dir = dir;
        }

        public bool Fits(PlannedLot lot, LotBox box, float shift, float inset, List<Quad> ignore)
        {
            var strict = lot.Footprint(box, shift, inset);
            bool Hits(Quad q) => Quad.Touch(q, strict) && (ignore == null || !ignore.Contains(q));
            if (existing.Any(Hits) || others.Any(Hits))
            {
                return false;
            }

            if (ignore == null && thinned.Any(strict.Contains))
            {
                return false;
            }

            return LeadingSideInside(lot, box, shift);
        }

        bool LeadingSideInside(PlannedLot lot, LotBox box, float shift)
        {
            var ax = lot.Owner.Frame.AxisX;
            var probe = lot.Footprint(box, shift, ProbeInset);
            var center = (probe.P[0] + probe.P[1] + probe.P[2] + probe.P[3]) * 0.25;
            foreach (var c in probe.P)
            {
                var d = c - center;
                if ((d.X * ax.X + d.Y * ax.Y) * dir > 0 && !Polygon.Contains(outline, c))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
