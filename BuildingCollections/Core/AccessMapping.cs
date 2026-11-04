namespace BuildingCollections.Core
{
    /// <summary>
    /// Traduce <c>BuildingPrefab.m_AccessType</c> (se pasa como entero para no depender del juego) a las esquinas que
    /// admite el edificio. Valores del enum <c>Game.Prefabs.BuildingAccessType</c>: Front, LeftCorner, RightCorner,
    /// LeftAndRightCorner, LeftAndBackCorner, RightAndBackCorner, FrontAndBack, All, OnRoad, OnRoadArea.
    /// </summary>
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
                default: k = CornerKind.None; break; // OnRoad / OnRoadArea: no van en una parcela de manzana
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

        /// <summary>Sirve para el relleno normal (fachada a la calle).</summary>
        public static bool FitsFront(CornerKind k) => (k & CornerKind.Front) != 0;

        /// <summary>Sirve para alguna esquina.</summary>
        public static bool FitsCorner(CornerKind k) => (k & (CornerKind.Left | CornerKind.Right)) != 0;
    }
}
