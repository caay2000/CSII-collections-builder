using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildingCollections.Core;
using Unity.Mathematics;

namespace BuildingCollections
{
    /// <summary>Un prefab tal como lo conoce Find It: su id interno más los datos que necesitamos.</summary>
    public sealed class FindItPrefab
    {
        public int Id;
        public BuildingInfo Info;
    }

    /// <summary>
    /// Acceso a Find It por reflexión, sin dependencia dura (regla R3.5 / R7). Las partes que no son API
    /// documentada (FindItUtil.GetFilteredPrefabs, AddTag, RemoveTag, AllPrefabs) pueden cambiar sin aviso, así
    /// que todo va con try/catch y devuelve "no disponible" en lugar de lanzar.
    /// </summary>
    public static class FindItBridge
    {
        static Type utilType;
        static bool resolved;

        public static bool IsAvailable
        {
            get { Resolve(); return utilType != null; }
        }

        static void Resolve()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "FindIt");
                utilType = asm?.GetType("FindIt.Utilities.FindItUtil");
                Mod.Log.Info(utilType == null ? "Find It no encontrado." : "Find It encontrado.");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"No se pudo resolver Find It: {e.Message}");
            }
        }

        static object CallStatic(string method, params object[] args)
        {
            Resolve();
            if (utilType == null)
            {
                return null;
            }

            var m = utilType.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, args.Select(a => a?.GetType() ?? typeof(object)).ToArray(), null)
                    ?? utilType.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m == null)
            {
                Mod.Log.Warn($"Find It: no existe FindItUtil.{method}.");
                return null;
            }

            return m.Invoke(null, args);
        }

        /// <summary>Edificios de la lista filtrada que Find It muestra ahora mismo.</summary>
        public static List<FindItPrefab> GetFilteredBuildings()
        {
            try
            {
                return ToPrefabs(CallStatic("GetFilteredPrefabs") as IEnumerable);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Find It GetFilteredPrefabs falló: {e.Message}");
                return new List<FindItPrefab>();
            }
        }

        /// <summary>Todos los prefabs que Find It tiene indexados (para resolver nombre a id y datos).</summary>
        public static Dictionary<string, FindItPrefab> GetAllByName()
        {
            var result = new Dictionary<string, FindItPrefab>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Resolve();
                var all = utilType?.GetProperty("AllPrefabs", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IDictionary;
                if (all != null)
                {
                    foreach (var p in ToPrefabs(all.Values))
                    {
                        result[p.Info.PrefabName] = p;
                    }
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Find It AllPrefabs falló: {e.Message}");
            }

            return result;
        }

        static Dictionary<string, int> idCache;

        /// <summary>Id de Find It de un prefab por nombre. La caché se rehace si el nombre no está (Find It indexa tarde).</summary>
        public static bool TryGetId(string name, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(name) || !IsAvailable)
            {
                return false;
            }

            if (idCache == null || !idCache.ContainsKey(name))
            {
                idCache = GetAllByName().ToDictionary(kv => kv.Key, kv => kv.Value.Id, StringComparer.OrdinalIgnoreCase);
            }

            return idCache.TryGetValue(name, out id);
        }

        public static string[] GetAllTags()
        {
            try { return CallStatic("GetAllTags") as string[] ?? new string[0]; }
            catch (Exception e) { Mod.Log.Warn($"Find It GetAllTags falló: {e.Message}"); return new string[0]; }
        }

        public static bool AddTag(int id, string tag) => Tag("AddTag", id, tag);

        public static bool RemoveTag(int id, string tag) => Tag("RemoveTag", id, tag);

        static bool Tag(string method, int id, string tag)
        {
            try { return CallStatic(method, id, tag) is bool b && b; }
            catch (Exception e) { Mod.Log.Warn($"Find It {method} falló: {e.Message}"); return false; }
        }

        static List<FindItPrefab> ToPrefabs(IEnumerable items)
        {
            var list = new List<FindItPrefab>();
            if (items == null)
            {
                return list;
            }

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var t = item.GetType();
                string name = t.GetProperty("PrefabName")?.GetValue(item) as string;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                int id = t.GetProperty("Id") != null ? (int)t.GetProperty("Id").GetValue(item) : 0;
                int2 lot = t.GetProperty("LotSize") != null ? (int2)t.GetProperty("LotSize").GetValue(item) : default;
                int level = t.GetProperty("BuildingLevel") != null ? (int)t.GetProperty("BuildingLevel").GetValue(item) : 0;
                int corner = t.GetProperty("CornerType") != null ? Convert.ToInt32(t.GetProperty("CornerType").GetValue(item)) : 2;

                // Solo edificios con parcela: el resto (props, árboles...) tiene LotSize 0.
                if (lot.x <= 0 || lot.y <= 0)
                {
                    continue;
                }

                list.Add(new FindItPrefab
                {
                    Id = id,
                    Info = new BuildingInfo { PrefabName = name, LotWidth = lot.x, LotDepth = lot.y, Level = level, Corner = (CornerKind)corner },
                });
            }

            return list;
        }
    }
}
