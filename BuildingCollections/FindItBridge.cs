using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BuildingCollections.Core;
using Unity.Mathematics;

namespace BuildingCollections
{
    public sealed class FindItPrefab
    {
        public int Id;
        public BuildingInfo Info;
    }

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
                Mod.Log.Info(utilType == null ? "Find It not found." : "Find It found.");
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Could not resolve Find It: {e.Message}");
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
                Mod.Log.Warn($"Find It: FindItUtil.{method} does not exist.");
                return null;
            }

            return m.Invoke(null, args);
        }

        public static List<FindItPrefab> GetFilteredBuildings()
        {
            try
            {
                return ToPrefabs(CallStatic("GetFilteredPrefabs") as IEnumerable);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Find It GetFilteredPrefabs failed: {e.Message}");
                return new List<FindItPrefab>();
            }
        }

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
                Mod.Log.Warn($"Find It AllPrefabs failed: {e.Message}");
            }

            return result;
        }

        static Dictionary<string, int> idCache;

        public static void RefreshIds()
        {
            var cache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (IsAvailable)
                {
                    foreach (var kv in GetAllByName())
                    {
                        cache[kv.Key] = kv.Value.Id;
                    }
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Find It: could not read the prefab list: {e.Message}");
            }

            idCache = cache;
        }

        public static bool TryGetId(string name, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(name) || !IsAvailable)
            {
                return false;
            }

            if (idCache == null)
            {
                RefreshIds();
            }

            return idCache.TryGetValue(name, out id);
        }

        public static string[] GetAllTags()
        {
            try { return CallStatic("GetAllTags") as string[] ?? new string[0]; }
            catch (Exception e) { Mod.Log.Warn($"Find It GetAllTags failed: {e.Message}"); return new string[0]; }
        }

        public static bool TryApplyBatch(IReadOnlyList<(bool add, string prefab, string tag)> changes, out int skipped, out long saveMs)
        {
            skipped = 0;
            saveMs = 0;
            Resolve();
            if (utilType == null)
            {
                return false;
            }

            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var dataField = utilType.GetField("customPrefabsData", S);
            var getIndex = utilType.GetMethod("GetPrefabIndex", S, null, new[] { typeof(int) }, null);
            var save = utilType.GetMethod("SaveCustomPrefabData", S, null, Type.EmptyTypes, null);
            var normalize = utilType.GetMethod("NormalizeTag", S, null, new[] { typeof(string) }, null);
            var dict = dataField?.GetValue(null) as IDictionary;
            var dataType = dataField?.FieldType.IsGenericType == true ? dataField.FieldType.GetGenericArguments()[1] : null;
            var dataTags = dataType?.GetProperty("Tags");
            if (dict == null || getIndex == null || save == null || dataTags == null || dataTags.PropertyType != typeof(List<string>))
            {
                Mod.Log.Warn("Find It: its internals have changed; tagging one at a time.");
                return false;
            }

            var canon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in GetAllTags())
            {
                if (!string.IsNullOrEmpty(t) && !canon.ContainsKey(t)) canon[t] = t;
            }

            bool refreshed = false;
            foreach (var (add, prefab, rawTag) in changes)
            {
                var index = FindIndex(getIndex, prefab);
                if (index == null && !refreshed)
                {
                    RefreshIds();
                    refreshed = true;
                    index = FindIndex(getIndex, prefab);
                }

                var it = index?.GetType();
                var name = it?.GetProperty("PrefabName")?.GetValue(index) as string;
                var indexTags = it?.GetProperty("Tags");
                if (string.IsNullOrEmpty(name) || indexTags == null)
                {
                    skipped++;
                    continue;
                }

                string tag = normalize?.Invoke(null, new object[] { rawTag }) as string ?? rawTag;
                if (string.IsNullOrEmpty(tag))
                {
                    continue;
                }

                if (canon.TryGetValue(tag, out var known)) tag = known;
                else if (add) canon[tag] = tag;

                var data = dict.Contains(name) ? dict[name] : null;
                var tags = data != null ? dataTags.GetValue(data) as List<string> : null;
                if (add)
                {
                    if (it.GetProperty("CreatorTags")?.GetValue(index) is string[] creator && creator.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (data == null)
                    {
                        data = Activator.CreateInstance(dataType);
                        dict[name] = data;
                    }

                    if (tags == null)
                    {
                        tags = new List<string>();
                        dataTags.SetValue(data, tags);
                    }

                    if (tags.Any(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    tags.Add(tag);
                    tags.Sort(StringComparer.OrdinalIgnoreCase);
                }
                else if (tags == null || tags.RemoveAll(x => string.Equals(x, tag, StringComparison.OrdinalIgnoreCase)) == 0)
                {
                    continue;
                }

                indexTags.SetValue(index, new List<string>(tags));
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            save.Invoke(null, null);
            saveMs = watch.ElapsedMilliseconds;
            return true;
        }

        static object FindIndex(MethodInfo getIndex, string prefab)
        {
            if (!TryGetId(prefab, out int id) || !(getIndex.Invoke(null, new object[] { id }) is object index))
            {
                return null;
            }

            var name = index.GetType().GetProperty("PrefabName")?.GetValue(index) as string;
            return string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase) ? index : null;
        }

        public static bool AddTag(int id, string tag) => Tag("AddTag", id, tag);

        public static bool RemoveTag(int id, string tag) => Tag("RemoveTag", id, tag);

        static bool Tag(string method, int id, string tag)
        {
            try { return CallStatic(method, id, tag) is bool b && b; }
            catch (Exception e) { Mod.Log.Warn($"Find It {method} failed: {e.Message}"); return false; }
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
