using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Game.Prefabs;
using Game.UI;
using Unity.Collections;
using Unity.Entities;

namespace BuildingCollections
{
    /// <summary>Un edificio tal como lo lee el mod del propio juego.</summary>
    public sealed class CatalogEntry
    {
        public Entity Prefab;
        public BuildingInfo Info;
        public int AccessType;
        public string Icon;

        /// <summary>Árbol de la decoración, no un edificio.</summary>
        public bool IsDecor;
    }

    /// <summary>
    /// Índice nombre de prefab → datos del edificio, leído del juego (BuildingData, SpawnableBuildingData,
    /// BuildingPrefab.m_AccessType) y no de Find It. Se reconstruye cuando cambia el número de prefabs de edificio,
    /// porque Extra Assets Importer y otros añaden assets mucho después de cargar el juego.
    /// </summary>
    public static class PrefabCatalog
    {
        static readonly Dictionary<string, CatalogEntry> byName = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        static EntityQuery query;
        static EntityQuery treeQuery;
        static readonly List<CatalogEntry> trees = new List<CatalogEntry>();

        /// <summary>Árboles del juego base (sin muertos, tocones ni troncos) para la decoración.</summary>
        public static IReadOnlyList<CatalogEntry> Trees => trees;

        static bool IsUnwantedTree(string name)
        {
            foreach (var bad in new[] { "Dead", "Stump", "Log", "Fallen", "Burnt", "Sapling" })
            {
                if (name.IndexOf(bad, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
        static World queryWorld;
        static int lastCount = -1;
        static bool lastSwap;

        /// <summary>Se incrementa cada vez que el índice cambia, para que la UI sepa que debe refrescar.</summary>
        public static int Version { get; private set; }

        public static bool TryGet(string name, out CatalogEntry entry) => byName.TryGetValue(name ?? string.Empty, out entry);

        /// <summary>Reconstruye el índice si cambió el número de prefabs de edificio o el criterio de esquinas.</summary>
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
                        },
                    };
                }
            }

            // Árboles del juego base para decorar el espacio sobrante (no los de mods: así el resultado es previsible).
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
            Mod.Log.Info($"Árboles del juego para la decoración ({trees.Count}): {string.Join(", ", trees.Select(t => t.Info.PrefabName))}");

            Version++;
            Mod.Log.Info($"Catálogo de edificios: {byName.Count} prefabs ({duplicates} nombres repetidos ignorados).");
        }
    }
}
