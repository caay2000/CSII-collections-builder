using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingCollections.Core;
using Game.Prefabs;
using Game.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

                    if (byName[prefab.name].Info.IsAngledCorner)
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
            Mod.Log.Info($"[angled] {probe.Count} buildings with a corner angle in their name:\n  " + string.Join("\n  ", probe));
        }

        static string ProbeLine(EntityManager em, Entity e, CatalogEntry entry)
        {
            var info = entry.Info;
            string lot = string.Format(CultureInfo.InvariantCulture, "lot x {0:F2}..{1:F2} z {2:F2}..{3:F2}", -info.LotWidth * 4f, info.LotWidth * 4f, -info.LotDepth * 4f, info.LotDepth * 4f);
            string mesh = TryMeshBounds(em, e, out var min, out var max)
                ? string.Format(CultureInfo.InvariantCulture, "mesh x {0:F2}..{1:F2} z {2:F2}..{3:F2}", min.x, max.x, min.y, max.y)
                : "no mesh";
            return $"{info.PrefabName}: angle {info.CornerAngle}, lot {info.LotWidth}x{info.LotDepth}, access {entry.AccessType} ({info.Corner}), {lot}, {mesh}";
        }

        public static bool TryMeshBounds(EntityManager em, Entity prefab, out float2 min, out float2 max)
        {
            min = new float2(float.MaxValue);
            max = new float2(float.MinValue);
            if (!em.HasBuffer<SubMesh>(prefab))
            {
                return false;
            }

            var subMeshes = em.GetBuffer<SubMesh>(prefab, true);
            bool any = false;
            for (int i = 0; i < subMeshes.Length; i++)
            {
                var sm = subMeshes[i];
                if (!em.HasComponent<MeshData>(sm.m_SubMesh))
                {
                    continue;
                }

                var b = em.GetComponentData<MeshData>(sm.m_SubMesh).m_Bounds;
                foreach (var corner in new[] { new float3(b.min.x, 0, b.min.z), new float3(b.max.x, 0, b.min.z), new float3(b.max.x, 0, b.max.z), new float3(b.min.x, 0, b.max.z) })
                {
                    var p = sm.m_Position + math.rotate(sm.m_Rotation, corner);
                    min = math.min(min, p.xz);
                    max = math.max(max, p.xz);
                    any = true;
                }
            }

            return any;
        }
    }
}
