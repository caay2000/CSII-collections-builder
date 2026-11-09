using System.Collections.Generic;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace BuildingCollections.Systems
{
    public partial class BuiltGuardSystem : GameSystemBase
    {
        readonly List<(Entity entity, int frames)> pending = new List<(Entity, int)>();
        readonly List<Entity> report = new List<Entity>();
        int reportCountdown = -1;

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
            Settle();
            Report();
        }

        void Settle()
        {
            if (pending.Count == 0)
            {
                return;
            }

            int placed = 0, lost = 0;
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
                    report.Add(e);
                    pending.RemoveAt(i);
                    placed++;
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

            if (placed > 0 || lost > 0)
            {
                Mod.Log.Info($"[build] {placed} buildings placed, {lost} not applied or deleted, {pending.Count} pending.");
            }
        }

        void Report()
        {
            if (reportCountdown < 0 || --reportCountdown > 0)
            {
                return;
            }

            reportCountdown = -1;
            int alive = 0, condemned = 0, construction = 0;
            foreach (var e in report)
            {
                if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e))
                {
                    continue;
                }

                alive++;
                if (EntityManager.HasComponent<Condemned>(e)) condemned++;
                if (EntityManager.HasComponent<Game.Objects.UnderConstruction>(e)) construction++;
            }

            Mod.Log.Info($"[build] State after waiting: {alive} of {report.Count} still standing, {condemned} with Condemned, {construction} under construction; Plop the Growables {(PtgBridge.Available ? "active" : "missing")}.");
            report.Clear();
        }
    }
}
