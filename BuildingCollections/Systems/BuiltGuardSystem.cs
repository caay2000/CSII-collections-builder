using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace BuildingCollections.Systems
{
    public struct BcBuilt : IComponentData, IQueryTypeParameter, IEmptySerializable
    {
    }

    public partial class BuiltGuardSystem : GameSystemBase
    {
        readonly List<(Entity entity, int frames)> pending = new List<(Entity, int)>();
        readonly List<Entity> report = new List<Entity>();
        int reportCountdown = -1;
        EntityQuery condemnedQuery;
        EntityQuery configQuery;
        IconCommandSystem iconSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            condemnedQuery = GetEntityQuery(ComponentType.ReadOnly<BcBuilt>(), ComponentType.ReadOnly<Condemned>(), ComponentType.Exclude<Deleted>());
            configQuery = GetEntityQuery(ComponentType.ReadOnly<BuildingConfigurationData>());
            iconSystem = World.GetOrCreateSystemManaged<IconCommandSystem>();
        }

        public void Track(IEnumerable<Entity> entities, int reportAfterFrames)
        {
            foreach (var e in entities)
            {
                pending.Add((e, 0));
            }

            reportCountdown = reportAfterFrames;
        }

        protected override void OnUpdate()
        {
            TagPending();
            Unflag();
            Report();
        }

        void TagPending()
        {
            if (pending.Count == 0)
            {
                return;
            }

            int tagged = 0, lost = 0;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var (e, frames) = pending[i];
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                {
                    pending.RemoveAt(i);
                    lost++;
                }
                else if (!EntityManager.HasComponent<Temp>(e))
                {
                    EntityManager.AddComponent<BcBuilt>(e);
                    report.Add(e);
                    pending.RemoveAt(i);
                    tagged++;
                }
                else if (frames > 900)
                {
                    pending.RemoveAt(i);
                    lost++;
                }
                else
                {
                    pending[i] = (e, frames + 1);
                }
            }

            if (tagged > 0 || lost > 0)
            {
                Mod.Log.Info($"[build] {tagged} buildings tagged as ours, {lost} not applied or deleted, {pending.Count} pending.");
            }
        }

        readonly ThrottledLog unflagLog = new ThrottledLog(TimeSpan.FromSeconds(30));

        void Unflag()
        {
            if (condemnedQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            using (var entities = condemnedQuery.ToEntityArray(Allocator.Temp))
            {
                Entity notification = Entity.Null;
                if (!configQuery.IsEmptyIgnoreFilter)
                {
                    notification = configQuery.GetSingleton<BuildingConfigurationData>().m_CondemnedNotification;
                }

                var icons = iconSystem.CreateCommandBuffer();
                foreach (var e in entities)
                {
                    EntityManager.RemoveComponent<Condemned>(e);
                    if (notification != Entity.Null)
                    {
                        icons.Remove(e, notification);
                    }
                }

                unflagLog.Info("unflag", $"[build] Removed Condemned from {entities.Length} of our buildings.");
            }
        }

        void Report()
        {
            if (reportCountdown < 0 || --reportCountdown > 0)
            {
                return;
            }

            reportCountdown = -1;
            int alive = 0, condemned = 0, construction = 0, tagged = 0;
            foreach (var e in report)
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                {
                    continue;
                }

                alive++;
                if (EntityManager.HasComponent<Condemned>(e)) condemned++;
                if (EntityManager.HasComponent<Game.Objects.UnderConstruction>(e)) construction++;
                if (EntityManager.HasComponent<BcBuilt>(e)) tagged++;
            }

            Mod.Log.Info($"[build] State after waiting: {alive} of {report.Count} still standing, {tagged} tagged, {condemned} with Condemned, {construction} under construction.");
            report.Clear();
        }
    }
}
