using System.Collections.Generic;

namespace BuildingCollections.Core
{
    public sealed class PlanBlock
    {
        public CellFrame Frame;
        public int AreaX;
        public int AreaY;
        public PlanGrid Grid;
        public LotStrip Strip;
        public int Priority;
        public bool IsVirtual;
        public string Label;
        public object Tag;
    }

    public sealed class PlannedLot
    {
        public BuildingInfo Building;
        public PlanBlock Owner;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public bool Corner;
        public float Shift;

        public int Width => MaxX - MinX;

        public int Depth => MaxY - MinY;

        public Quad Footprint(double inset) => Footprint(MinX, MinY, MaxX, MaxY, Shift, inset);

        public Quad Footprint(int minX, int minY, int maxX, int maxY, double shift, double inset) =>
            Owner.Frame.Rect(minX, minY, maxX, maxY, shift, inset);

        public LotBox Box => new LotBox(MinX, MinY, MaxX, MaxY);

        public Quad Footprint(LotBox box, double shift, double inset) => Footprint(box.MinX, box.MinY, box.MaxX, box.MaxY, shift, inset);
    }

    public readonly struct LotBox
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MaxX;
        public readonly int MaxY;

        public LotBox(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public LotBox Moved(int dx) => new LotBox(MinX + dx, MinY, MaxX + dx, MaxY);
    }

    public sealed class PreviewRequest
    {
        public IReadOnlyList<PlanBlock> Blocks = new List<PlanBlock>();
        public IReadOnlyList<Quad> Existing = new List<Quad>();
        public IReadOnlyList<Pt> Outline;
        public IReadOnlyList<BuildingInfo> Plain = new List<BuildingInfo>();
        public IReadOnlyList<BuildingInfo> Corners = new List<BuildingInfo>();
        public float Overlap;
        public int Density = 100;
        public int Seed;
    }

    public sealed class PreviewResult
    {
        public List<PlannedLot> Lots = new List<PlannedLot>();
        public List<Pt> FrontCells = new List<Pt>();
        public List<Pt> Thinned = new List<Pt>();
        public int FrontFilled;
        public double? FirstGroupShare;
        public List<string> Log = new List<string>();
        public List<string> Timings = new List<string>();
    }
}
