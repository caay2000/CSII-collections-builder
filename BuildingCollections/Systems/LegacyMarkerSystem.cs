using Game;
using Game.Buildings;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace BuildingCollections.Systems
{
    public partial class LegacyMarkerSystem : GameSystemBase
    {
        EntityQuery legacyQuery;
        EntityQuery configQuery;
        IconCommandSystem iconSystem;
        bool waitingLogged;

        protected override void OnCreate()
        {
            base.OnCreate();
            legacyQuery = GetEntityQuery(ComponentType.ReadOnly<BcBuilt>(), ComponentType.Exclude<Deleted>());
            configQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingConfigurationData>());
            iconSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
            RequireForUpdate(legacyQuery);
        }

        protected override void OnUpdate()
        {
            var plopped = PtgBridge.PloppedType;
            var spawned = PtgBridge.SpawnedType;
            if (plopped == null || spawned == null)
            {
                if (!waitingLogged)
                {
                    waitingLogged = true;
                    Mod.Log.Warn($"[build] {legacyQuery.CalculateEntityCount()} buildings still carry the old BcBuilt marker; they are handed over once Plop the Growables is active.");
                }

                return;
            }

            var ploppedType = ComponentType.ReadWrite(plopped);
            var spawnedType = ComponentType.ReadWrite(spawned);
            var notification = configQuery.IsEmptyIgnoreFilter ? Entity.Null : configQuery.GetSingleton<BuildingConfigurationData>().m_CondemnedNotification;
            var icons = iconSystem.CreateCommandBuffer();
            int unflagged = 0;
            using (var entities = legacyQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    if (EntityManager.HasComponent(e, spawnedType))
                    {
                        EntityManager.RemoveComponent(e, spawnedType);
                    }

                    if (!EntityManager.HasComponent(e, ploppedType))
                    {
                        EntityManager.AddComponent(e, ploppedType);
                    }

                    if (EntityManager.HasComponent<Condemned>(e))
                    {
                        EntityManager.RemoveComponent<Condemned>(e);
                        unflagged++;
                        if (notification != Entity.Null)
                        {
                            icons.Remove(e, notification);
                        }
                    }
                }

                EntityManager.RemoveComponent<BcBuilt>(legacyQuery);
                Mod.Log.Info($"[build] Handed {entities.Length} buildings with the old BcBuilt marker over to Plop the Growables ({unflagged} were condemned); the save no longer depends on this mod.");
            }
        }
    }
}
