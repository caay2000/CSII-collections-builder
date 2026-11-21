using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace BuildingCollections.Systems
{
    public static class FenceLanes
    {
        public static bool IsFence(EntityManager entities, Entity prefab) =>
            entities.HasComponent<NetLaneData>(prefab)
            && entities.HasComponent<NetLaneGeometryData>(prefab)
            && entities.HasComponent<UtilityLaneData>(prefab)
            && (entities.GetComponentData<UtilityLaneData>(prefab).m_UtilityTypes & UtilityTypes.Fence) != 0;

        public static IEnumerable<Entity> OfPrefab(EntityManager entities, Entity buildingPrefab)
        {
            if (!entities.HasBuffer<Game.Prefabs.SubLane>(buildingPrefab))
            {
                yield break;
            }

            var lanes = entities.GetBuffer<Game.Prefabs.SubLane>(buildingPrefab, true);
            for (int i = 0; i < lanes.Length; i++)
            {
                if (IsFence(entities, lanes[i].m_Prefab))
                {
                    yield return lanes[i].m_Prefab;
                }
            }
        }
    }

    public partial class ParcelFenceSystem : GameSystemBase
    {
        const int SettleFrames = 8;
        const int LaneWaitFrames = 120;
        const int PlacementWaitFrames = 900;
        const int ResolveEveryFrames = 10;
        const float SamePlace = 1f;

        sealed class Target
        {
            public Entity Prefab;
            public float2 Position;
            public Entity Building;
            public int Frames;
            public int LanesRemoved;
        }

        readonly List<Target> pending = new List<Target>();
        ModificationEndBarrier barrier;
        EntityQuery placedQuery;
        int resolveCountdown;

        public void Track(IEnumerable<Entity> temporaryBuildings)
        {
            foreach (var temp in temporaryBuildings)
            {
                if (EntityManager.HasComponent<PrefabRef>(temp) && EntityManager.HasComponent<Transform>(temp))
                {
                    pending.Add(new Target
                    {
                        Prefab = EntityManager.GetComponentData<PrefabRef>(temp).m_Prefab,
                        Position = EntityManager.GetComponentData<Transform>(temp).m_Position.xz,
                    });
                }
            }

            resolveCountdown = ResolveEveryFrames;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            barrier = World.GetOrCreateSystemManaged<ModificationEndBarrier>();
            placedQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            pending.Clear();
        }

        protected override void OnUpdate()
        {
            if (pending.Count == 0)
            {
                return;
            }

            try
            {
                if (--resolveCountdown <= 0)
                {
                    resolveCountdown = ResolveEveryFrames;
                    Resolve();
                }

                Process();
            }
            catch (Exception e)
            {
                pending.Clear();
                Mod.Log.Error(e);
            }
        }

        void Resolve()
        {
            var open = pending.FindAll(t => t.Building == Entity.Null);
            if (open.Count == 0)
            {
                return;
            }

            using (var entities = placedQuery.ToEntityArray(Allocator.Temp))
            using (var transforms = placedQuery.ToComponentDataArray<Transform>(Allocator.Temp))
            using (var prefabs = placedQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                var taken = new HashSet<Entity>();
                pending.ForEach(t => taken.Add(t.Building));
                var byPrefab = new Dictionary<Entity, List<Target>>();
                foreach (var target in open)
                {
                    if (!byPrefab.TryGetValue(target.Prefab, out var group))
                    {
                        byPrefab[target.Prefab] = group = new List<Target>();
                    }

                    group.Add(target);
                }

                for (int i = 0; i < entities.Length; i++)
                {
                    if (!byPrefab.TryGetValue(prefabs[i].m_Prefab, out var group) || taken.Contains(entities[i]))
                    {
                        continue;
                    }

                    var position = transforms[i].m_Position.xz;
                    int match = group.FindIndex(t => math.distance(t.Position, position) <= SamePlace);
                    if (match >= 0)
                    {
                        group[match].Building = entities[i];
                        group[match].Frames = 0;
                        group.RemoveAt(match);
                        taken.Add(entities[i]);
                    }
                }
            }
        }

        void Process()
        {
            int houses = 0, lanes = 0, lost = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var target = pending[i];
                target.Frames++;
                if (target.Building == Entity.Null)
                {
                    if (target.Frames > PlacementWaitFrames)
                    {
                        pending.RemoveAt(i);
                        lost++;
                    }
                }
                else if (!EntityManager.Exists(target.Building) || EntityManager.HasComponent<Deleted>(target.Building))
                {
                    pending.RemoveAt(i);
                    lost++;
                }
                else if (target.Frames >= SettleFrames)
                {
                    var found = CurrentFences(target.Building);
                    if (found.Count > 0 || target.Frames >= LaneWaitFrames)
                    {
                        target.LanesRemoved += Remove(target.Building, found);
                    }

                    if (target.Frames >= LaneWaitFrames)
                    {
                        lanes += target.LanesRemoved;
                        houses++;
                        pending.RemoveAt(i);
                    }
                }
            }

            if (houses > 0 || lost > 0)
            {
                Mod.Log.Info($"[fences] {houses} buildings handed to Better Bulldozer, {lanes} original fence lanes removed, {lost} not found, {pending.Count} pending.");
            }
        }

        List<(Entity Lane, Entity Prefab)> CurrentFences(Entity building)
        {
            var result = new List<(Entity, Entity)>();
            if (!EntityManager.HasBuffer<Game.Net.SubLane>(building))
            {
                return result;
            }

            var lanes = EntityManager.GetBuffer<Game.Net.SubLane>(building, true);
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i].m_SubLane;
                if (EntityManager.HasComponent<PrefabRef>(lane) && !EntityManager.HasComponent<Deleted>(lane))
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                    if (FenceLanes.IsFence(EntityManager, prefab))
                    {
                        result.Add((lane, prefab));
                    }
                }
            }

            return result;
        }

        int Remove(Entity building, List<(Entity Lane, Entity Prefab)> found)
        {
            var prefabs = new HashSet<Entity>();
            foreach (var (_, prefab) in found)
            {
                prefabs.Add(prefab);
            }

            foreach (var prefab in FenceLanes.OfPrefab(EntityManager, EntityManager.GetComponentData<PrefabRef>(building).m_Prefab))
            {
                prefabs.Add(prefab);
            }

            BetterBulldozerBridge.Remember(EntityManager, building, prefabs);
            var commands = barrier.CreateCommandBuffer();
            foreach (var (lane, _) in found)
            {
                commands.AddComponent<Deleted>(lane);
            }

            return found.Count;
        }
    }
}
