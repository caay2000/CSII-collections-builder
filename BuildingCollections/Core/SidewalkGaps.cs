using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class SidewalkGap
    {
        public Pt From;
        public Pt To;
        public double Length;
    }

    public static class SidewalkGaps
    {
        const double Step = 0.1;
        const double RayStart = 0.5;
        const double RayReach = 4;
        const double RayStep = 0.5;
        const double MinLength = 0.5;

        public static List<SidewalkGap> Find(IEnumerable<LotStrip> strips, IEnumerable<PlannedLot> lots)
        {
            var quads = lots.Select(l => l.Footprint(0)).ToList();
            var gaps = new List<SidewalkGap>();
            foreach (var st in strips)
            {
                double depth = st.Inward + 1.5;
                var band = Bounds.Of(new[]
                {
                    At(st, 0, st.Inward), At(st, st.ChunkLength, st.Inward),
                    At(st, 0, st.Inward + RayReach), At(st, st.ChunkLength, st.Inward + RayReach),
                }).Grow(0.5);
                var near = quads.Where(q => band.Overlaps(q)).ToList();
                double start = -1;
                for (double u = 0; u <= st.ChunkLength + 1e-6; u += Step)
                {
                    bool covered = Reached(near, st, u);
                    if (!covered && start < 0)
                    {
                        start = u;
                    }

                    if (start >= 0 && (covered || u + Step > st.ChunkLength + 1e-6))
                    {
                        double end = covered ? u : st.ChunkLength;
                        if (end - start >= MinLength)
                        {
                            gaps.Add(new SidewalkGap { From = At(st, start, depth), To = At(st, end, depth), Length = end - start });
                        }

                        start = -1;
                    }
                }
            }

            return gaps;
        }

        static bool Reached(List<Quad> quads, LotStrip st, double u)
        {
            for (double d = st.Inward + RayStart; d <= st.Inward + RayReach + 1e-6; d += RayStep)
            {
                if (FrontCoverage.Covered(quads, At(st, u, d)))
                {
                    return true;
                }
            }

            return false;
        }

        static Pt At(LotStrip st, double u, double depth) => st.ChunkStart + st.T * u + st.N * depth;
    }
}
