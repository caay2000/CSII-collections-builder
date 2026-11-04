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

        /// <summary>Mezcla de colecciones: 0 = primera colección, 1 = segunda.</summary>
        public int Group { get; set; }

        /// <summary>Mezcla: parte (0 a 1) de los edificios que deben salir de la colección de este edificio.</summary>
        public double Share { get; set; } = 1;

        /// <summary>Copia para una mezcla (el catálogo es compartido y no se modifica).</summary>
        public BuildingInfo InGroup(int group, double share) => new BuildingInfo
        {
            PrefabName = PrefabName, LotWidth = LotWidth, LotDepth = LotDepth, Level = Level, Corner = Corner,
            Group = group, Share = share,
        };

        /// <summary>Edificio normal: solo admite el lado frontal.</summary>
        public bool IsPlain => Corner == CornerKind.Front || Corner == CornerKind.None;

        public override string ToString() => $"{PrefabName} {LotWidth}x{LotDepth} L{Level} {Corner}";
    }
}
