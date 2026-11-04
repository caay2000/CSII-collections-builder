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
        bool wantTool;   // la pestaña Builder está abierta y quiere la herramienta
        ToolBaseSystem lastTool;
        ToolBaseSystem lastNonDefault;   // última herramienta activa que no era la normal
        int defaultFrames;               // fotogramas seguidos con la herramienta normal activa
        const int ExitFrames = 30;
        const int ReactivateFrames = 3;

        sealed class BuilderDto
        {
            public string mode = "block";
            public int widthOp, widthValue, depthOp, depthValue, levelOp, levelValue;
            public int density = 100;
            public bool leaveGaps;
            public string decor = string.Empty;
            public int decorDensity = 50;
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
                string deleted = selected.value;
                Report(CollectionService.Delete(selected.value));
                if (Mod.Settings != null && string.Equals(Mod.Settings.DecorCollection, deleted, StringComparison.OrdinalIgnoreCase))
                {
                    Mod.Settings.DecorCollection = string.Empty;
                    saveCountdown = 60;
                    PushBuilder();
                }

                SetSelected(CollectionStore.Collections.FirstOrDefault()?.Name ?? string.Empty);
            })));
            AddBinding(new TriggerBinding<string>(Group, "rename", name => Safe(() =>
            {
                string oldName = selected.value;
                var error = CollectionService.Rename(selected.value, name, out var newName);
                Report(error);
                if (error == null)
                {
                    // La colección de decoración se guarda por nombre: sigue a la colección renombrada.
                    if (Mod.Settings != null && string.Equals(Mod.Settings.DecorCollection, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        Mod.Settings.DecorCollection = newName;
                        saveCountdown = 60;
                        PushBuilder();
                    }

                    SetSelected(newName);
                }
            })));
            AddBinding(new TriggerBinding(Group, "clone", () => Safe(() =>
            {
                var error = CollectionService.Clone(selected.value, out var newName);
                Report(error);
                if (error == null)
                {
                    SetSelected(newName);
                }
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
            if (saveCountdown >= 0)
            {
                Mod.Settings?.ApplyAndSave();
            }

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
                // Si el usuario usó otra herramienta (p. ej. una carretera) y volvió a la normal con la pestaña Builder
                // abierta, se reactiva la nuestra. Al elegir otra herramienta desde la barra el juego pasa a veces por
                // la normal un fotograma (nuestra -> normal -> carretera): por eso salir de la nuestra a la normal
                // solo cuenta como salida a propósito (Esc) si la normal sigue activa unos fotogramas.
                var active = toolSystem.activeTool;
                if (active != lastTool)
                {
                    Mod.Log.Info($"[herramienta] {lastTool?.toolID ?? "-"} -> {active?.toolID ?? "-"}");
                }

                if (active == defaultTool)
                {
                    defaultFrames++;
                }
                else
                {
                    defaultFrames = 0;
                    if (active != null)
                    {
                        lastNonDefault = active;
                    }
                }

                if (wantTool && panelOpen.value && active == defaultTool)
                {
                    // Se espera unos fotogramas: al cambiar de una herramienta a otra (carretera -> zonas) el juego
                    // también puede pasar un instante por la normal, y no hay que quitarle la herramienta nueva.
                    if (lastNonDefault != null && lastNonDefault != builder && defaultFrames >= ReactivateFrames)
                    {
                        toolSystem.activeTool = builder;
                        active = builder;
                        lastNonDefault = builder;
                        defaultFrames = 0;
                    }
                    else if (lastNonDefault == builder && defaultFrames > ExitFrames)
                    {
                        wantTool = false; // salió de la nuestra a propósito (Esc): no se reactiva hasta volver a la pestaña
                    }
                }

                lastTool = active;

                toolActive.Update(toolSystem.activeTool == builder);
                plan.Update($"{{\"areaSelected\":{Js(builder.AreaSelected)},\"previewing\":{Js(builder.Previewing)},\"lots\":{builder.Lots},\"blocks\":{builder.Blocks}}}");

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

                // Etiquetas de Find It repartidas entre fotogramas (cada una guarda el fichero de Find It).
                if (TagQueue.Count > 0)
                {
                    int before = TagQueue.Count;
                    if (TagQueue.Process(6))
                    {
                        Mod.Log.Info("Etiquetas de Find It actualizadas.");
                        status.Update("OK");
                    }
                    else if (before / 25 != TagQueue.Count / 25)
                    {
                        status.Update($"Updating Find It tags... {TagQueue.Count} left");
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
                    list.Add(new { id = name, name, w = e.Info.LotWidth, d = e.Info.LotDepth, level = e.Info.Level, icon = e.Icon, decor = e.IsDecor });
                }
                else
                {
                    // Aún sin cargar (assets que llegan tarde) o ya no instalado: se muestra pero no se borra.
                    missing++;
                    list.Add(new { id = name, name = name + " (not loaded)", w = 0, d = 0, level = 0, icon = string.Empty, decor = false });
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
            wantTool = active;
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
                if (!panelOpen.value || string.IsNullOrEmpty(selected.value) || prefab == null)
                {
                    return;
                }

                // Edificios, y también árboles y plantas (para la decoración del espacio sobrante).
                if (!(prefab is BuildingPrefab) &&
                    !(World.GetOrCreateSystemManaged<PrefabSystem>().TryGetEntity(prefab, out Unity.Entities.Entity pe) && EntityManager.HasComponent<PlantData>(pe)))
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
                leaveGaps = s.LeaveGaps,
                decor = s.DecorCollection ?? string.Empty,
                decorDensity = s.DecorDensity,
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
            s.Density = Clamp((int)Math.Round(f.density / 10.0, MidpointRounding.AwayFromZero) * 10, 10, 100);
            s.LeaveGaps = f.leaveGaps;
            s.DecorCollection = f.decor ?? string.Empty;
            s.DecorDensity = Clamp((int)Math.Round(f.decorDensity / 10.0, MidpointRounding.AwayFromZero) * 10, 10, 100);
            builder.SetMode(s.Mode);
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
            // Decoración: las plantas de la colección elegida (puede ser la misma que la de los edificios).
            var decorCollection = CollectionStore.Find(s.DecorCollection);
            var decor = decorCollection == null ? new List<CatalogEntry>() : decorCollection.Prefabs
                .Select(n => PrefabCatalog.TryGet(n, out var e) ? e : null)
                .Where(e => e != null && e.IsDecor)
                .OrderBy(e => e.Info.PrefabName, StringComparer.Ordinal)
                .ToList();
            builder.GeneratePreview(candidates, s.Density, seed, s.LeaveGaps, decor, s.DecorDensity);
        }

        static CompareOp ToOp(int v) => v >= 0 && v <= 3 ? (CompareOp)v : CompareOp.Equal;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
