using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    public sealed class LotStrip
    {
        public Pt P0;
        public Pt T;
        public int Width;
        public int Depth;
        public PlanGrid Grid;

        public Pt ChunkStart;
        public double ChunkLength;
        public double Offset;
        public double Slack;
        public double Inward;
        public int BlockIndex;
        public int SideIndex;
        public int ChunkIndex;
        public int ChunkCount;

        public const double ChainTolerance = 2.1;

        public Pt N => new Pt(-T.Y, T.X);

        public Pt ChunkEnd => new Pt(ChunkStart.X + T.X * ChunkLength, ChunkStart.Y + T.Y * ChunkLength);

        public static bool Chained(LotStrip first, LotStrip second)
        {
            if (first == null || second == null || first.BlockIndex != second.BlockIndex || first.SideIndex != second.SideIndex
                || second.ChunkIndex != first.ChunkIndex + 1)
            {
                return false;
            }

            var d = second.ChunkStart - first.ChunkEnd;
            return Pt.Dot(d, d) < ChainTolerance * ChainTolerance;
        }

        public static LotStrip Next(LotStrip strip, IEnumerable<LotStrip> all, int dir) =>
            all.FirstOrDefault(o => o != strip && (dir > 0 ? Chained(strip, o) : Chained(o, strip)));

        public Pt CellCenter(int x, int y) => Add(P0, T, (x + 0.5) * CellFrame.Size, N, (y + 0.5) * CellFrame.Size);

        internal static Pt Add(Pt p, Pt a, double ka, Pt b, double kb) => new Pt(p.X + a.X * ka + b.X * kb, p.Y + a.Y * ka + b.Y * kb);
    }
}
