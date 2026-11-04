using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;

namespace BuildingCollections
{
    /// <summary>
    /// Integración opcional con Tree Controller (sin dependencia: por reflexión). Lee sus sets de árboles (los del
    /// juego base que trae —caducifolios, perennes, arbustos— y los 5 personalizados), que guarda en
    /// <c>TreeControllerUISystem.m_PrefabSetsLookup</c> (id → repositorio con <c>PrefabNames</c>). Se rellena al
    /// cargar la partida; antes, o sin el mod, no hay sets.
    /// </summary>
    public static class TreeControllerBridge
    {
        const string SystemType = "Tree_Controller.Tools.TreeControllerUISystem";

        /// <summary>Ids de los sets, en el orden de sus botones.</summary>
        public static readonly string[] SetIds =
        {
            "YYTC-wild-deciduous-trees", "YYTC-evergreen-trees", "YYTC-wild-bushes",
            "YYTC-custom-set-1", "YYTC-custom-set-2", "YYTC-custom-set-3", "YYTC-custom-set-4", "YYTC-custom-set-5",
        };

        static Type type;
        static int typeLookups;
        static bool unsupported;
        static FieldInfo lookupField;

        /// <summary>Nombres de prefab de cada set no vacío; vacío si Tree Controller no está (o aún no ha cargado).</summary>
        public static Dictionary<string, string[]> Sets(World world)
        {
            var result = new Dictionary<string, string[]>();
            if (unsupported || world == null)
            {
                return result;
            }

            try
            {
                if (type == null)
                {
                    type = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => { try { return a.GetType(SystemType, false); } catch { return null; } })
                        .FirstOrDefault(t => t != null);
                    if (type == null)
                    {
                        unsupported = ++typeLookups >= 3;
                        return result;
                    }

                    lookupField = type.GetField("m_PrefabSetsLookup", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (lookupField == null)
                    {
                        Mod.Log.Warn("Tree Controller encontrado, pero sin m_PrefabSetsLookup: sets desactivados.");
                        unsupported = true;
                        return result;
                    }
                }

                var system = world.GetExistingSystemManaged(type);
                if (system == null || !(lookupField.GetValue(system) is IDictionary lookup))
                {
                    return result;
                }

                foreach (DictionaryEntry e in lookup)
                {
                    var names = e.Value?.GetType().GetProperty("PrefabNames", BindingFlags.Public | BindingFlags.Instance)?.GetValue(e.Value) as string[];
                    if (e.Key is string id && names != null && names.Length > 0)
                    {
                        result[id] = names;
                    }
                }
            }
            catch (Exception e)
            {
                var inner = (e as TargetInvocationException)?.InnerException ?? e;
                Mod.Log.Warn("Tree Controller: no se pudieron leer los sets: " + inner.Message);
                unsupported = true;
            }

            return result;
        }
    }
}
