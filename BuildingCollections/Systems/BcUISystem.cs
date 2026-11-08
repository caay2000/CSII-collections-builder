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
        ValueBinding<string> mixBuildings;
        ValueBinding<string> builderMessage;
        ValueBinding<int> builderMessageError;
        bool mixDirty = true;
        ValueBinding<string> selected;
        ValueBinding<string> status;
        ValueBinding<string> builderBinding;
        ValueBinding<string> plan;
        ValueBinding<int> escapes;

        bool collectionsDirty = true;
        bool buildingsDirty = true;
        int catalogVersion = -1;
        int messageSerial;
        int frame;
        int anarchyTick;
        int seedCounter;
        int saveCountdown = -1;
        bool wantTool;
        readonly AnarchyKeeper anarchy = new AnarchyKeeper();
        const int AnarchyCheckFrames = 10;
        ToolBaseSystem lastTool;
        ToolBaseSystem lastNonDefault;
        int defaultFrames;
        const int ExitFrames = 30;
        const int ReactivateFrames = 3;

        sealed class BuilderDto
        {
            public string mode = "block";
            public int widthOp, widthValue, depthOp, depthValue, levelOp, levelValue;
            public int density = 100;
            public bool trees;
            public int decorDensity = 50;

            public float overlapMeters = 0.5f;

            public string treeSet = string.Empty;
            public int treeAges = 4;

            public string mix = string.Empty;
            public int mixShare = 50;
        }

        string treeSetsJson = "[]";

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
            AddBinding(mixBuildings = new ValueBinding<string>(Group, "mixBuildings", "[]"));
            AddBinding(builderMessage = new ValueBinding<string>(Group, "builderMessage", string.Empty));
            AddBinding(builderMessageError = new ValueBinding<int>(Group, "builderMessageError", 0));
            AddBinding(selected = new ValueBinding<string>(Group, "selected", string.Empty));
            AddBinding(status = new ValueBinding<string>(Group, "status", string.Empty));
            AddBinding(builderBinding = new ValueBinding<string>(Group, "builder", "{}"));
            AddBinding(plan = new ValueBinding<string>(Group, "plan", "{}"));
            AddBinding(escapes = new ValueBinding<int>(Group, "escapes", 0));

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
                var deleted = selected.value;
                var error = CollectionService.Delete(deleted);
                Report(error);
                if (error == null)
                {
                    FollowMix(deleted, string.Empty);
                }

                SetSelected(CollectionStore.Collections.FirstOrDefault()?.Name ?? string.Empty);
            })));
            AddBinding(new TriggerBinding<string>(Group, "rename", name => Safe(() =>
            {
                var oldName = selected.value;
                var error = CollectionService.Rename(oldName, name, out var newName);
                Report(error);
                if (error == null)
                {
                    FollowMix(oldName, newName);

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

            CreatePlopBindings();
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

            anarchy.Restore();
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            try
            {
                var keyboard = Keyboard.current;
                if (panelOpen.value && keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    escapes.Update(escapes.value + 1);
                }

                var active = toolSystem.activeTool;
                if (active != lastTool)
                {
                    Mod.Log.Info($"[tool] {lastTool?.toolID ?? "-"} -> {active?.toolID ?? "-"}");
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
                    if (lastNonDefault != null && lastNonDefault != builder && defaultFrames >= ReactivateFrames)
                    {
                        toolSystem.activeTool = builder;
                        active = builder;
                        lastNonDefault = builder;
                        defaultFrames = 0;
                    }
                    else if (lastNonDefault == builder && defaultFrames > ExitFrames)
                    {
                        wantTool = false;
                    }
                }

                lastTool = active;

                toolActive.Update(toolSystem.activeTool == builder);
                UpdatePlopping();

                plan.Update($"{{\"areaSelected\":{Js(builder.AreaSelected)},\"previewing\":{Js(builder.Previewing)},\"lots\":{builder.Lots},\"blocks\":{builder.Blocks},\"anarchy\":{Js(AnarchyBridge.Enabled)},\"anarchyAvailable\":{Js(AnarchyBridge.Available)},\"ptgAvailable\":{Js(PtgBridge.Available)},\"filled\":{builder.FrontFilled},\"front\":{builder.FrontTotal},\"treeSets\":{treeSetsJson}}}");

                if (builder.MessageSerial != messageSerial)
                {
                    messageSerial = builder.MessageSerial;
                    status.Update(builder.Message);
                    builderMessage.Update(builder.Message);
                    builderMessageError.Update(builder.MessageIsError ? builder.MessageSerial : 0);
                }

                if (anarchyTick % AnarchyCheckFrames == 0)
                {
                    anarchy.Update(panelOpen.value && wantTool && OverlapWanted);
                }

                if (anarchyTick++ % 120 == 0)
                {
                    AnarchyBridge.TryRegister(World, builder.toolID);
                    var sets = TreeControllerBridge.Sets(World);
                    treeSetsJson = JsonConvert.SerializeObject(TreeControllerBridge.SetIds.Where(sets.ContainsKey).ToArray());
                }

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

                if (TagQueue.Count > 0 && TagQueue.Settled)
                {
                    int before = TagQueue.Count;
                    if (TagQueue.Process(6))
                    {
                        Mod.Log.Info("Find It tags updated.");
                        status.Update("OK");
                    }
                    else if (before / 25 != TagQueue.Count / 25)
                    {
                        status.Update($"Updating Find It tags... {TagQueue.Count} left");
                    }
                }

                if (saveCountdown >= 0 && --saveCountdown < 0)
                {
                    Mod.Settings?.ApplyAndSave();
                }

                if (collectionsDirty)
                {
                    collectionsDirty = false;
                    mixDirty = true;
                    collections.Update(JsonConvert.SerializeObject(
                        CollectionStore.Collections.Select(c => new { name = c.Name, tag = c.Tag, count = c.Prefabs.Count })));
                }

                if (buildingsDirty)
                {
                    buildingsDirty = false;
                    mixDirty = true;
                    buildings.Update(JsonConvert.SerializeObject(BuildingsOfSelected()));
                }

                if (mixDirty)
                {
                    mixDirty = false;
                    mixBuildings.Update(JsonConvert.SerializeObject(MixBuildings()));
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        static string Js(bool b) => b ? "true" : "false";

        void FollowMix(string oldName, string newName)
        {
            var s = Mod.Settings;
            if (s != null && string.Equals(s.MixCollection, oldName, StringComparison.OrdinalIgnoreCase))
            {
                s.MixCollection = newName;
                saveCountdown = 60;
                mixDirty = true;
                PushBuilder();
            }
        }

        BuildingCollection MixOf(BuildingCollectionsSettings s)
        {
            var m = string.IsNullOrEmpty(s?.MixCollection) ? null : CollectionStore.Find(s.MixCollection);
            return m == null || string.Equals(m.Name, selected.value, StringComparison.OrdinalIgnoreCase) ? null : m;
        }

        List<object> MixBuildings()
        {
            var list = new List<object>();
            var m = MixOf(Mod.Settings);
            if (m == null)
            {
                return list;
            }

            foreach (var name in m.Prefabs)
            {
                if (PrefabCatalog.TryGet(name, out var e))
                {
                    list.Add(new { id = name, name, w = e.Info.LotWidth, d = e.Info.LotDepth, level = e.Info.Level, icon = string.Empty });
                }
            }

            return list;
        }

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
                    missing++;
                    list.Add(new { id = name, name = name + " (not loaded)", w = 0, d = 0, level = 0, icon = string.Empty });
                }
            }

            if (missing > 0)
            {
                Mod.Log.Info($"'{c.Name}': {missing} of {c.Prefabs.Count} buildings have no game data (not loaded yet or uninstalled).");
            }

            return list;
        }

        static bool OverlapWanted => (Mod.Settings?.OverlapMeters ?? 0.5f) > FootprintMask.StrictOverlap;

        public void TogglePanel() => SetPanelOpen(!panelOpen.value);

        public void ReshufflePreview()
        {
            if (toolSystem.activeTool == builder && builder.AreaSelected)
            {
                Safe(GeneratePreview);
            }
            else if (toolSystem.activeTool != builder)
            {
                Safe(() => TryPlopAnother());
            }
        }

        public void BuildPreview()
        {
            if (toolSystem.activeTool == builder && builder.Previewing)
            {
                Safe(builder.RequestBuild);
            }
        }

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
                toolSystem.activeTool = defaultTool;
            }
        }

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
            builderMessageError.Update(0);
            builderMessage.Update(string.Empty);
            buildingsDirty = true;
            collectionsDirty = true;
            if (Mod.Settings != null)
            {
                Mod.Settings.SelectedCollection = name;
                saveCountdown = 60;
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

        void OnPrefabChanged(PrefabBase prefab)
        {
            try
            {
                if (IsOwnPlop(prefab) || !panelOpen.value || string.IsNullOrEmpty(selected.value) || !(prefab is BuildingPrefab))
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
                trees = s.Trees,
                decorDensity = s.DecorDensity,
                overlapMeters = s.OverlapMeters,
                treeSet = s.TreeSet ?? string.Empty,
                treeAges = s.TreeAges,
                mix = s.MixCollection ?? string.Empty,
                mixShare = s.MixShare,
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
            s.Trees = f.trees;
            s.DecorDensity = Clamp((int)Math.Round(f.decorDensity / 10.0, MidpointRounding.AwayFromZero) * 10, 10, 100);
            s.OverlapMeters = Math.Min(2f, Math.Max(0f, (float)Math.Round(f.overlapMeters * 2, MidpointRounding.AwayFromZero) / 2));
            s.TreeSet = TreeControllerBridge.SetIds.Contains(f.treeSet) ? f.treeSet : string.Empty;
            s.TreeAges = (f.treeAges & 15) == 0 ? 4 : f.treeAges & 15;
            s.MixCollection = CollectionStore.Find(f.mix ?? string.Empty)?.Name ?? string.Empty;
            s.MixShare = Clamp((int)Math.Round(f.mixShare / 5.0, MidpointRounding.AwayFromZero) * 5, 5, 95);
            mixDirty = true;
            builderMessageError.Update(0);
            builderMessage.Update(string.Empty);
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

            var clock = System.Diagnostics.Stopwatch.StartNew();
            PrefabCatalog.Refresh(World, DevConfig.Load().SwapCornerSides);
            var filter = s.ToFilter();
            List<CatalogEntry> Matching(BuildingCollection col) => col.Prefabs
                .Select(n => PrefabCatalog.TryGet(n, out var e) ? e : null)
                .Where(e => e != null && filter.Matches(e.Info))
                .OrderBy(e => e.Info.PrefabName, StringComparer.Ordinal)
                .ToList();
            var candidates = Matching(c);

            var mix = MixOf(s);
            if (mix != null)
            {
                double share = s.MixShare / 100.0;
                var firstNames = new HashSet<string>(candidates.Select(e => e.Info.PrefabName), StringComparer.OrdinalIgnoreCase);
                var second = Matching(mix).Where(e => !firstNames.Contains(e.Info.PrefabName)).ToList();
                candidates = candidates.Select(e => Tagged(e, 0, share))
                    .Concat(second.Select(e => Tagged(e, 1, 1 - share))).ToList();
                Mod.Log.Info($"[preview] mix: {c.Name} {s.MixShare}% ({firstNames.Count} buildings) + {mix.Name} {100 - s.MixShare}% ({second.Count}).");
            }
            int seed = unchecked(Environment.TickCount * 31 + ++seedCounter);
            var decor = s.Trees ? TreeDecor(s.TreeSet) : new List<CatalogEntry>();
            long prep = clock.ElapsedMilliseconds;
            builder.GeneratePreview(candidates, s.Density, seed, decor, s.DecorDensity, s.TreeAges);
            Mod.Log.Info($"[preview] total {clock.ElapsedMilliseconds} ms (catalog and filters {prep} ms).");
        }

        static CatalogEntry Tagged(CatalogEntry e, int group, double share) => new CatalogEntry
        {
            Prefab = e.Prefab, Info = e.Info.InGroup(group, share), AccessType = e.AccessType, Icon = e.Icon, IsDecor = e.IsDecor,
        };

        List<CatalogEntry> TreeDecor(string set)
        {
            if (string.IsNullOrEmpty(set) || !TreeControllerBridge.Sets(World).TryGetValue(set, out var names))
            {
                return PrefabCatalog.Trees.ToList();
            }

            var prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var result = new List<CatalogEntry>();
            foreach (var name in names.Distinct())
            {
                if (prefabSystem.TryGetPrefab(new PrefabID("StaticObjectPrefab", name), out PrefabBase prefab) && prefab != null)
                {
                    result.Add(new CatalogEntry { Prefab = prefabSystem.GetEntity(prefab), IsDecor = true, Info = new BuildingInfo { PrefabName = name } });
                }
            }

            Mod.Log.Info($"[trees] Tree Controller set {set}: {result.Count} of {names.Length} prefabs found.");
            return result.Count > 0 ? result : PrefabCatalog.Trees.ToList();
        }

        static CompareOp ToOp(int v) => v >= 0 && v <= 3 ? (CompareOp)v : CompareOp.Equal;

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
