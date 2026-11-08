using Game;
using Game.Common;
using Unity.Entities;

namespace BuildingCollections.Systems
{
    public partial class LegacyMarkerSystem : GameSystemBase
    {
        EntityQuery legacyQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            legacyQuery = GetEntityQuery(ComponentType.ReadOnly<BcBuilt>(), ComponentType.Exclude<Deleted>());
            RequireForUpdate(legacyQuery);
        }

        protected override void OnUpdate()
        {
            int count = legacyQuery.CalculateEntityCount();
            EntityManager.RemoveComponent<BcBuilt>(legacyQuery);
            Mod.Log.Info($"[build] Removed the old BcBuilt marker from {count} buildings; save the game once to drop it from the file.");
        }
    }
}
