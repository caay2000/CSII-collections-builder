namespace BuildingCollections.Core
{
    public readonly struct CellFrame
    {
        public const double Size = 8;

        public readonly Pt Origin;
        public readonly Pt AxisX;
        public readonly Pt AxisY;

        public CellFrame(Pt origin, Pt axisX, Pt axisY)
        {
            Origin = origin;
            AxisX = axisX;
            AxisY = axisY;
        }

        public static CellFrame FromStrip(LotStrip strip) =>
            new CellFrame(strip.CellCenter(0, 0), strip.T, strip.N);

        public Pt CellCenter(int x, int y) => Origin + AxisX * (Size * x) + AxisY * (Size * y);

        public Quad Cell(int x, int y, double inset)
        {
            double half = Size / 2 - inset;
            return Box(CellCenter(x, y), half, half);
        }

        public Quad Rect(int minX, int minY, int maxX, int maxY, double shift = 0, double inset = 0)
        {
            var center = Origin + AxisX * (Size * ((minX + maxX) / 2.0 - 0.5) + shift) + AxisY * (Size * ((minY + maxY) / 2.0 - 0.5));
            return Box(center, Size * (maxX - minX) / 2 - inset, Size * (maxY - minY) / 2 - inset);
        }

        Quad Box(Pt center, double halfX, double halfY)
        {
            var x = AxisX * halfX;
            var y = AxisY * halfY;
            return new Quad(center - x - y, center + x - y, center + x + y, center - x + y);
        }
    }
}
