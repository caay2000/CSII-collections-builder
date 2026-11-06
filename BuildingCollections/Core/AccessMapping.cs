namespace BuildingCollections.Core
{
    public static class AccessMapping
    {
        public static CornerKind ToCorner(int accessType, bool swapSides = false)
        {
            CornerKind k;
            switch (accessType)
            {
                case 0: k = CornerKind.Front; break;
                case 1: k = CornerKind.Left; break;
                case 2: k = CornerKind.Right; break;
                case 3: k = CornerKind.Left | CornerKind.Right; break;
                case 4: k = CornerKind.Left; break;
                case 5: k = CornerKind.Right; break;
                case 6: k = CornerKind.Front; break;
                case 7: k = CornerKind.Left | CornerKind.Front | CornerKind.Right; break;
                default: k = CornerKind.None; break;
            }

            if (swapSides)
            {
                bool l = (k & CornerKind.Left) != 0, r = (k & CornerKind.Right) != 0;
                k &= ~(CornerKind.Left | CornerKind.Right);
                if (l) k |= CornerKind.Right;
                if (r) k |= CornerKind.Left;
            }

            return k;
        }

        public static bool FitsFront(CornerKind k) => (k & CornerKind.Front) != 0;

        public static bool FitsCorner(CornerKind k) => (k & (CornerKind.Left | CornerKind.Right)) != 0;
    }
}
