using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildingCollections.Core;
using Colossal.Mathematics;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace BuildingCollections.Systems
{
    public partial class BlockBuilderToolSystem : ObjectToolBaseSystem
    {
        JobHandle Build(JobHandle inputDeps)
        {
            var cfg = DevConfig.Load();
            if (preview.Count == 0)
            {
                Say("Generate a preview first.", error: true);
                applyMode = ApplyMode.None;
                return inputDeps;
            }

            if (cfg.RequireNoErrors && !GetAllowApply())
            {
                int removed = RemoveErrored();
                FrontFilled = CountFront();
                Mod.Log.Info($"[build] Cancelled: there are entities with errors ({m_ErrorQuery.CalculateEntityCount()}); removed {removed} from the preview.");
                Say(removed > 0 ? $"Removed {removed} items that collide with something (red). Press Build again." : "Some items in the preview have errors (red). Try Reshuffle.", error: true);
                definitionsDirty = removed > 0;
                applyMode = ApplyMode.None;
                return inputDeps;
            }

            var temps = new List<Entity>();
            using (var arr = tempBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                temps.AddRange(arr);
            }

            guard.Track(temps, cfg.ReportAfterFrames);
            uint frame = World.GetOrCreateSystemManaged<SimulationSystem>().frameIndex;
            Mod.Log.Info($"[build] frame {frame}, {Origin}: applying {preview.Count} planned, {temps.Count} temporary buildings.\n"
                + string.Join("\n", BuiltSummary.Lines(Describe(temps))));
            SessionMarker.Record($"build of {temps.Count} buildings from {Origin}, frame {frame}");
            Origin = UnknownOrigin;
            int filled = CountFront(), total = FrontTotal;
            ClearSelection();
            Say(total > 0 ? $"Built {temps.Count} buildings: {filled} of {total} street-front cells filled ({100 * filled / total}%)." : $"Built {temps.Count} buildings.");
            definitionsDirty = true;
            applyMode = ApplyMode.Apply;
            return inputDeps;
        }

        const string UnknownOrigin = "unknown collection";

        public string Origin { get; set; } = UnknownOrigin;

        IEnumerable<(string name, string source)> Describe(IEnumerable<Entity> entities)
        {
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            foreach (var e in entities)
            {
                if (EntityManager.HasComponent<PrefabRef>(e)
                    && prefabs.TryGetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, out var prefab))
                {
                    yield return (prefab.name, SourceOf(prefab));
                }
            }
        }

        static string SourceOf(PrefabBase prefab)
        {
            try
            {
                return prefab.asset == null ? "unknown" : BuiltSummary.Source(prefab.asset.path, prefab.asset.isBuiltin);
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
