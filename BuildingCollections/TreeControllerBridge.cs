using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Entities;

namespace BuildingCollections
{
    public static class TreeControllerBridge
    {
        const string SystemType = "Tree_Controller.Tools.TreeControllerUISystem";

        public static readonly string[] SetIds =
        {
            "YYTC-wild-deciduous-trees", "YYTC-evergreen-trees", "YYTC-wild-bushes",
            "YYTC-custom-set-1", "YYTC-custom-set-2", "YYTC-custom-set-3", "YYTC-custom-set-4", "YYTC-custom-set-5",
        };

        static Type type;
        static int typeLookups;
        static bool unsupported;
        static FieldInfo lookupField;

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
                        Mod.Log.Warn("Tree Controller found, but without m_PrefabSetsLookup: sets disabled.");
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
                Mod.Log.Warn("Tree Controller: could not read the sets: " + inner.Message);
                unsupported = true;
            }

            return result;
        }
    }
}
