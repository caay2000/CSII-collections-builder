using System;

namespace BuildingCollections.Core
{
    /// <summary>Esquinas que admite un edificio. Mismos valores que el filtro de Find It.</summary>
    [Flags]
    public enum CornerKind
    {
        None = 0,
        Left = 1,
        Front = 2,
        Right = 4,
    }

    /// <summary>Datos de un edificio relevantes para filtrar y colocar. Sin dependencias del juego.</summary>
    public sealed class BuildingInfo
    {
        public string PrefabName { get; set; }
        public int LotWidth { get; set; }
        public int LotDepth { get; set; }
        public int Level { get; set; }
        public CornerKind Corner { get; set; } = CornerKind.Front;

        /// <summary>Edificio normal: solo admite el lado frontal.</summary>
        public bool IsPlain => Corner == CornerKind.Front || Corner == CornerKind.None;

        public override string ToString() => $"{PrefabName} {LotWidth}x{LotDepth} L{Level} {Corner}";
    }
}
