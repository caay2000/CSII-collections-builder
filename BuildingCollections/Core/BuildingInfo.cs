using System;

namespace BuildingCollections.Core
{
    [Flags]
    public enum CornerKind
    {
        None = 0,
        Left = 1,
        Front = 2,
        Right = 4,
    }

    public sealed class BuildingInfo
    {
        public string PrefabName { get; set; }
        public int LotWidth { get; set; }
        public int LotDepth { get; set; }
        public int Level { get; set; }
        public CornerKind Corner { get; set; } = CornerKind.Front;

        public int Group { get; set; }

        public double Share { get; set; } = 1;

        public BuildingInfo InGroup(int group, double share) => new BuildingInfo
        {
            PrefabName = PrefabName, LotWidth = LotWidth, LotDepth = LotDepth, Level = Level, Corner = Corner,
            Group = group, Share = share,
        };

        public bool IsPlain => Corner == CornerKind.Front || Corner == CornerKind.None;

        public override string ToString() => $"{PrefabName} {LotWidth}x{LotDepth} L{Level} {Corner}";
    }
}
