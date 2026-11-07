using System;
using System.Collections.Generic;

namespace BuildingCollections.Core
{
    public readonly struct Bounds
    {
        public readonly double MinX, MinY, MaxX, MaxY;

        public Bounds(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public static Bounds Of(Quad q) => new Bounds(q.MinX, q.MinY, q.MaxX, q.MaxY);

        public static Bounds Of(IEnumerable<Pt> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            return new Bounds(minX, minY, maxX, maxY);
        }

        public static Bounds Of(PlanBlock block) =>
            Of(block.Frame.Rect(block.AreaX, block.AreaY, block.AreaX + block.Grid.Width, block.AreaY + block.Grid.Depth));

        public Bounds Grow(double margin) => new Bounds(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

        public Bounds Union(Quad q) => new Bounds(Math.Min(MinX, q.MinX), Math.Min(MinY, q.MinY), Math.Max(MaxX, q.MaxX), Math.Max(MaxY, q.MaxY));

        public bool Overlaps(Quad q) => q.MaxX >= MinX && q.MinX <= MaxX && q.MaxY >= MinY && q.MinY <= MaxY;

        public bool Contains(Pt p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;
    }
}
