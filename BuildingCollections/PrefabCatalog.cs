using System;
using System.Collections.Generic;
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

        /// <summary>Árbol, arbusto u otra planta: va en la decoración, no en las parcelas.</summary>
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
        static EntityQuery plantQuery;
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
                    plantQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlantData>(), ComponentType.ReadOnly<PrefabData>(), ComponentType.Exclude<BuildingData>());
                    queryWorld = world;
                    lastCount = -1;
                }

                int count = query.CalculateEntityCount() + plantQuery.CalculateEntityCount();
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

            int plants = 0;
            using (var entities = plantQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    if (!prefabSystem.TryGetPrefab(e, out PrefabBase prefab) || prefab == null || byName.ContainsKey(prefab.name))
                    {
                        continue;
                    }

                    string icon = null;
                    try { icon = ImageSystem.GetThumbnail(prefab); } catch { }
                    byName[prefab.name] = new CatalogEntry
                    {
                        Prefab = e,
                        IsDecor = true,
                        Icon = icon ?? string.Empty,
                        Info = new BuildingInfo { PrefabName = prefab.name, Corner = CornerKind.None },
                    };
                    plants++;
                }
            }

            Version++;
            Mod.Log.Info($"Catálogo: {byName.Count - plants} edificios y {plants} plantas ({duplicates} nombres repetidos ignorados).");
        }
    }
}
