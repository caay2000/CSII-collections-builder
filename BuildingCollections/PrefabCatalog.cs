using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingCollections.Core;
using Game.Prefabs;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace BuildingCollections
{
    public sealed class CatalogEntry
    {
        public Entity Prefab;
        public BuildingInfo Info;
        public int AccessType;
        public string Icon;

        public bool IsDecor;
    }

    public static class PrefabCatalog
    {
        static readonly Dictionary<string, CatalogEntry> byName = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        static EntityQuery query;
        static EntityQuery treeQuery;
        static readonly List<CatalogEntry> trees = new List<CatalogEntry>();

        public static IReadOnlyList<CatalogEntry> Trees => trees;

        static bool IsUnwantedTree(string name)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(name, @"Dead|Stump|Fallen|Burnt|Sapling|Log(?![a-z])");
        }
        static World queryWorld;
        static int lastCount = -1;
        static bool lastSwap;

        public static int Version { get; private set; }

        public static bool TryGet(string name, out CatalogEntry entry) => byName.TryGetValue(name ?? string.Empty, out entry);

        public static void Refresh(World world, bool swapCornerSides)
        {
            try
            {
                if (queryWorld != world)
                {
                    query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<PrefabData>());
                    treeQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<TreeData>(), ComponentType.ReadOnly<PrefabData>(), ComponentType.Exclude<BuildingData>());
                    queryWorld = world;
                    lastCount = -1;
                }

                int count = query.CalculateEntityCount() + treeQuery.CalculateEntityCount();
                if (count == lastCount && swapCornerSides == lastSwap)
                {
                    return;
                }

                Rebuild(world, swapCornerSides);
                lastCount = count;
                lastSwap = swapCornerSides;
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        static void Rebuild(World world, bool swap)
        {
            var em = world.EntityManager;
            var prefabSystem = world.GetOrCreateSystemManaged<PrefabSystem>();
            byName.Clear();
            int duplicates = 0;
            var probe = new List<string>();
            using (var entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    if (!prefabSystem.TryGetPrefab(e, out BuildingPrefab prefab) || prefab == null)
                    {
                        continue;
                    }

                    if (byName.ContainsKey(prefab.name))
                    {
                        duplicates++;
                        continue;
                    }

                    var data = em.GetComponentData<BuildingData>(e);
                    int level = em.HasComponent<SpawnableBuildingData>(e) ? em.GetComponentData<SpawnableBuildingData>(e).m_Level : 0;
                    int access = (int)prefab.m_AccessType;
                    string icon = null;
                    try { icon = ImageSystem.GetThumbnail(prefab); } catch { }

                    byName[prefab.name] = new CatalogEntry
                    {
                        Prefab = e,
                        AccessType = access,
                        Icon = icon ?? string.Empty,
                        Info = new BuildingInfo
                        {
                            PrefabName = prefab.name,
                            LotWidth = data.m_LotSize.x,
                            LotDepth = data.m_LotSize.y,
                            Level = level,
                            Corner = AccessMapping.ToCorner(access, swap),
                            CornerAngle = AngledCorner.Angle(prefab.name),
                        },
                    };

                    if (AngledCorner.WorthProbing(prefab.name))
                    {
                        probe.Add(ProbeLine(em, e, byName[prefab.name]));
                    }
                }
            }

            trees.Clear();
            using (var entities = treeQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    if (!prefabSystem.TryGetPrefab(e, out PrefabBase prefab) || prefab == null || !prefab.isBuiltin ||
                        em.HasComponent<PlaceholderObjectData>(e) || IsUnwantedTree(prefab.name))
                    {
                        continue;
                    }

                    trees.Add(new CatalogEntry { Prefab = e, IsDecor = true, Info = new BuildingInfo { PrefabName = prefab.name, Corner = CornerKind.None } });
                }
            }

            trees.Sort((a, b) => string.CompareOrdinal(a.Info.PrefabName, b.Info.PrefabName));
            Mod.Log.Info($"Base-game trees for decoration ({trees.Count}): {string.Join(", ", trees.Select(t => t.Info.PrefabName))}");

            Version++;
            Mod.Log.Info($"Building catalog: {byName.Count} prefabs ({duplicates} duplicate names ignored).");
            probe.Sort(StringComparer.OrdinalIgnoreCase);
            Mod.Log.Info($"[angled] {probe.Count} buildings with corner or angle names, {byName.Values.Count(c => c.Info.IsAngledCorner)} with an angle:\n  " + string.Join("\n  ", probe));
        }

        static string ProbeLine(EntityManager em, Entity e, CatalogEntry entry)
        {
            var info = entry.Info;
            string geometry = "no geometry";
            if (em.HasComponent<ObjectGeometryData>(e))
            {
                var g = em.GetComponentData<ObjectGeometryData>(e);
                geometry = string.Format(CultureInfo.InvariantCulture, "bounds x {0:F2}..{1:F2} z {2:F2}..{3:F2} height {4:F2}, size {5:F2}x{6:F2}",
                    g.m_Bounds.min.x, g.m_Bounds.max.x, g.m_Bounds.min.z, g.m_Bounds.max.z, g.m_Bounds.max.y, g.m_Size.x, g.m_Size.z);
            }

            return $"{info.PrefabName}: angle {(info.IsAngledCorner ? info.CornerAngle.ToString(CultureInfo.InvariantCulture) : "-")}, side {AngledCorner.Side(info.PrefabName)}, lot {info.LotWidth}x{info.LotDepth}, access {entry.AccessType} ({info.Corner}), {geometry}";
        }
    }
}
