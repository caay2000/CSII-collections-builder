using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.UI;
using Newtonsoft.Json;

namespace BuildingCollections.Systems
{
    public partial class BcUISystem : UISystemBase
    {
        ValueBinding<string> plopping;
        PrefabSystem prefabSystem;
        List<string> shown = new List<string>();
        readonly Random plopRandom = new Random();
        bool activatingPlop;

        void CreatePlopBindings()
        {
            prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            AddBinding(plopping = new ValueBinding<string>(Group, "plopping", string.Empty));
            AddBinding(new TriggerBinding<string>(Group, "plop", name => Safe(() => Plop(name))));
            AddBinding(new TriggerBinding<string>(Group, "setShown", json => Safe(() =>
                shown = JsonConvert.DeserializeObject<List<string>>(json ?? "[]") ?? new List<string>())));
        }

        string ActivePrefabName()
        {
            var active = toolSystem.activeTool;
            return active != null && active != builder && active.GetPrefab() is BuildingPrefab p ? p.name : string.Empty;
        }

        void UpdatePlopping() => plopping.Update(ActivePrefabName());

        bool Plop(string name)
        {
            if (!PrefabCatalog.TryGet(name, out var entry) || !prefabSystem.TryGetPrefab(entry.Prefab, out BuildingPrefab prefab) || prefab == null)
            {
                status.Update($"{name} is not loaded in this game.");
                return false;
            }

            wantTool = false;
            activatingPlop = true;
            try
            {
                toolSystem.ActivatePrefabTool(prefab);
            }
            finally
            {
                activatingPlop = false;
            }

            Mod.Log.Info($"[plop] {prefab.name} from '{selected.value}'.");
            return true;
        }

        bool TryPlopAnother()
        {
            var current = ActivePrefabName();
            var collection = CollectionStore.Find(selected.value);
            if (string.IsNullOrEmpty(current) || collection == null ||
                !collection.Prefabs.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            var members = new HashSet<string>(collection.Prefabs, StringComparer.OrdinalIgnoreCase);
            var pool = shown.Where(members.Contains).Where(n => PrefabCatalog.TryGet(n, out _)).ToList();
            if (pool.Count == 0)
            {
                pool = collection.Prefabs.Where(n => PrefabCatalog.TryGet(n, out _)).ToList();
            }

            var next = RandomPick.Other(pool, current, plopRandom);
            return next != null && Plop(next);
        }
    }
}
