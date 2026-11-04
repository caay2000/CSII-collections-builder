using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Colossal.UI.Binding;
using Game.Prefabs;
using Game.Tools;
using Game.UI;
using Newtonsoft.Json;
using UnityEngine.InputSystem;

namespace BuildingCollections.Systems
{
    /// <summary>
    /// Puente entre el panel React y el mod (grupo "BuildingCollections", contrato en UI/src/bindings.ts). Las listas
    /// viajan como JSON y solo se vuelven a enviar cuando cambian.
    /// </summary>
    public partial class BcUISystem : UISystemBase
    {
        public const string Group = "BuildingCollections";

        ToolSystem toolSystem;
        DefaultToolSystem defaultTool;
        BlockBuilderToolSystem builder;

        ValueBinding<bool> panelOpen;
        ValueBinding<bool> toolActive;
        ValueBinding<bool> findItAvailable;
        ValueBinding<string> collections;
        ValueBinding<string> buildings;
        ValueBinding<string> selected;
        ValueBinding<string> status;
        ValueBinding<string> builderBinding;
        ValueBinding<string> plan;

        bool collectionsDirty = true;
        bool buildingsDirty = true;
        int catalogVersion = -1;
        int messageSerial;
        int frame;
        int seedCounter;
        int saveCountdown = -1;

        sealed class BuilderDto
        {
            public string mode = "block";
            public int widthOp, widthValue, depthOp, depthValue, levelOp, levelValue;
            public int density = 100;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            defaultTool = World.GetOrCreateSystemManaged<DefaultToolSystem>();
            builder = World.GetOrCreateSystemManaged<BlockBuilderToolSystem>();

            AddBinding(panelOpen = new ValueBinding<bool>(Group, "panelOpen", false));
            AddBinding(toolActive = new ValueBinding<bool>(Group, "toolActive", false));
            AddBinding(findItAvailable = new ValueBinding<bool>(Group, "findItAvailable", false));
            AddBinding(collections = new ValueBinding<string>(Group, "collections", "[]"));
            AddBinding(buildings = new ValueBinding<string>(Group, "buildings", "[]"));
            AddBinding(selected = new ValueBinding<string>(Group, "selected", string.Empty));
            AddBinding(status = new ValueBinding<string>(Group, "status", string.Empty));
            AddBinding(builderBinding = new ValueBinding<string>(Group, "builder", "{}"));
            AddBinding(plan = new ValueBinding<string>(Group, "plan", "{}"));

            AddBinding(new TriggerBinding<bool>(Group, "setPanelOpen", v => Safe(() => SetPanelOpen(v))));
            AddBinding(new TriggerBinding<string>(Group, "select", name => Safe(() => SetSelected(name))));
            AddBinding(new TriggerBinding<string>(Group, "create", name => Safe(() =>
            {
                name = (name ?? string.Empty).Trim();
                var error = CollectionService.Create(name);
                Report(error);
                if (error == null)
                {
                    SetSelected(CollectionStore.Find(name)?.Name ?? string.Empty);
                }
            })));
            AddBinding(new TriggerBinding(Group, "addAll", () => Safe(() => Report(CollectionService.AddAllFromFindIt(selected.value)))));
            AddBinding(new TriggerBinding(Group, "clear", () => Safe(() => Report(CollectionService.Clear(selected.value)))));
            AddBinding(new TriggerBinding(Group, "delete", () => Safe(() =>
            {
                Report(CollectionService.Delete(selected.value));
                SetSelected(CollectionStore.Collections.FirstOrDefault()?.Name ?? string.Empty);
            })));
            AddBinding(new TriggerBinding<string>(Group, "removeBuildings", json => Safe(() =>
                Report(CollectionService.RemoveBuildings(selected.value, JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>())))));
            AddBinding(new TriggerBinding<string>(Group, "setBuilder", json => Safe(() => ApplyBuilder(json))));
            AddBinding(new TriggerBinding<bool>(Group, "setToolActive", v => Safe(() => SetToolActive(v))));
            AddBinding(new TriggerBinding(Group, "generatePreview", () => Safe(() => IfToolActive(GeneratePreview))));
            AddBinding(new TriggerBinding(Group, "clearSelection", () => Safe(builder.ClearSelection)));
            AddBinding(new TriggerBinding(Group, "build", () => Safe(() => IfToolActive(builder.RequestBuild))));

            toolSystem.EventPrefabChanged += OnPrefabChanged;

            var saved = Mod.Settings?.SelectedCollection;
            var first = CollectionStore.Find(saved)?.Name ?? CollectionStore.Collections.FirstOrDefault()?.Name ?? string.Empty;
            selected.Update(first);
            PushBuilder();
        }

        protected override void OnDestroy()
        {
            if (toolSystem != null)
            {
                toolSystem.EventPrefabChanged -= OnPrefabChanged;
            }

            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            try
            {
                toolActive.Update(toolSystem.activeTool == builder);
                plan.Update($"{{\"areaSelected\":{Js(builder.AreaSelected)},\"previewing\":{Js(builder.Previewing)},\"lots\":{builder.Lots}}}");

                if (builder.MessageSerial != messageSerial)
                {
                    messageSerial = builder.MessageSerial;
                    status.Update(builder.Message);
                }

                // Cada ~2 s (o al abrir el panel) se comprueba si han llegado prefabs nuevos.
                if (panelOpen.value && (++frame % 120 == 0 || catalogVersion < 0))
                {
                    findItAvailable.Update(FindItBridge.IsAvailable);
                    PrefabCatalog.Refresh(World, DevConfig.Load().SwapCornerSides);
                    if (PrefabCatalog.Version != catalogVersion)
                    {
                        catalogVersion = PrefabCatalog.Version;
                        buildingsDirty = true;
                    }
                }

                // Guardado de ajustes agrupado: el deslizador manda muchos cambios seguidos.
                if (saveCountdown >= 0 && --saveCountdown < 0)
                {
                    Mod.Settings?.ApplyAndSave();
                }

                if (collectionsDirty)
                {
                    collectionsDirty = false;
                    collections.Update(JsonConvert.SerializeObject(
                        CollectionStore.Collections.Select(c => new { name = c.Name, tag = c.Tag, count = c.Prefabs.Count })));
                }

                if (buildingsDirty)
                {
                    buildingsDirty = false;
                    buildings.Update(JsonConvert.SerializeObject(BuildingsOfSelected()));
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        static string Js(bool b) => b ? "true" : "false";

        List<object> BuildingsOfSelected()
        {
            var c = CollectionStore.Find(selected.value);
            var list = new List<object>();
            if (c == null)
            {
                return list;
            }

            int missing = 0;
            foreach (var name in c.Prefabs)
            {
                if (PrefabCatalog.TryGet(name, out var e))
                {
                    list.Add(new { id = name, name, w = e.Info.LotWidth, d = e.Info.LotDepth, level = e.Info.Level, icon = e.Icon });
                }
                else
                {
                    // Aún sin cargar (assets que llegan tarde) o ya no instalado: se muestra pero no se borra.
                    missing++;
                    list.Add(new { id = name, name = name + " (not loaded)", w = 0, d = 0, level = 0, icon = string.Empty });
                }
            }

            if (missing > 0)
            {
                Mod.Log.Info($"'{c.Name}': {missing} de {c.Prefabs.Count} edificios sin datos en el juego (aún no cargados o desinstalados).");
            }

            return list;
        }

        // ---------------- Panel y herramienta ----------------

        public void TogglePanel() => SetPanelOpen(!panelOpen.value);

        void SetPanelOpen(bool open)
        {
            panelOpen.Update(open);
            if (open)
            {
                catalogVersion = -1;
            }
            else
            {
                SetToolActive(false);
            }
        }

        void SetToolActive(bool active)
        {
            if (active)
            {
                toolSystem.activeTool = builder;
            }
            else if (toolSystem.activeTool == builder)
            {
                // Solo se quita si es la nuestra: si el usuario cogió otra herramienta, se respeta.
                toolSystem.activeTool = defaultTool;
            }
        }

        /// <summary>Vista previa y Build solo con la herramienta activa (si no, el juego no tiene dónde aplicarlos).</summary>
        void IfToolActive(Action action)
        {
            if (toolSystem.activeTool != builder)
            {
                status.Update("Open the Builder tab to use the block tool.");
                return;
            }

            action();
        }

        void SetSelected(string name)
        {
            name = name ?? string.Empty;
            selected.Update(name);
            buildingsDirty = true;
            collectionsDirty = true;
            if (Mod.Settings != null)
            {
                Mod.Settings.SelectedCollection = name;
                Mod.Settings.ApplyAndSave();
            }
        }

        void Report(string error)
        {
            status.Update(error ?? "OK");
            if (error != null)
            {
                Mod.Log.Warn(error);
            }

            collectionsDirty = true;
            buildingsDirty = true;
        }

        void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                status.Update("Error (see BuildingCollections.log)");
            }
        }

        // ---------------- Shift+clic en Find It o en el menú del juego ----------------

        void OnPrefabChanged(PrefabBase prefab)
        {
            try
            {
                if (!panelOpen.value || string.IsNullOrEmpty(selected.value) || !(prefab is BuildingPrefab))
                {
                    return;
                }

                var kb = Keyboard.current;
                if (kb == null || !kb.shiftKey.isPressed)
                {
                    return;
                }

                var error = CollectionService.AddPrefab(selected.value, prefab.name);
                status.Update(error ?? $"Added {prefab.name} to {selected.value}.");
                collectionsDirty = true;
                buildingsDirty = true;
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        // ---------------- Constructor ----------------

        void PushBuilder()
        {
            var s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            builderBinding.Update(JsonConvert.SerializeObject(new BuilderDto
            {
                mode = s.Mode == "polygon" ? "polygon" : "block",
                widthOp = (int)s.WidthOp, widthValue = s.WidthValue,
                depthOp = (int)s.DepthOp, depthValue = s.DepthValue,
                levelOp = (int)s.LevelOp, levelValue = s.LevelValue,
                density = s.Density,
            }));
        }

        void ApplyBuilder(string json)
        {
            var s = Mod.Settings;
            if (s == null)
            {
                return;
            }

            var f = JsonConvert.DeserializeObject<BuilderDto>(json) ?? new BuilderDto();
            s.Mode = f.mode == "polygon" ? "polygon" : "block";
            s.WidthOp = ToOp(f.widthOp); s.WidthValue = Clamp(f.widthValue, 0, 5);
            s.DepthOp = ToOp(f.depthOp); s.DepthValue = Clamp(f.depthValue, 0, 5);
            s.LevelOp = ToOp(f.levelOp); s.LevelValue = Clamp(f.levelValue, 0, 5);
            s.Density = Clamp(f.density, 1, 100);
            saveCountdown = 60;
            PushBuilder();
        }

        void GeneratePreview()
        {
            var s = Mod.Settings;
            var c = CollectionStore.Find(selected.value);
            if (s == null || c == null)
            {
                status.Update("Choose a collection.");
                return;
            }

            PrefabCatalog.Refresh(World, DevConfig.Load().SwapCornerSides);
            var filter = s.ToFilter();
            var candidates = c.Prefabs
                .Select(n => PrefabCatalog.TryGet(n, out var e) ? e : null)
                .Where(e => e != null && filter.Matches(e.Info))
                .OrderBy(e => e.Info.PrefabName, StringComparer.Ordinal)
                .ToList();
            int seed = unchecked(Environment.TickCount * 31 + ++seedCounter);
            builder.GeneratePreview(candidates, s.Density, seed);
        }

        static CompareOp ToOp(int v) => v >= 0 && v <= 3 ? (CompareOp)v : CompareOp.Equal;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
