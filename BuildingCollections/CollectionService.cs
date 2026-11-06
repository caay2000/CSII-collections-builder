using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections
{
    public static class CollectionService
    {
        public static string Create(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return "The name is empty.";
            }

            if (CollectionStore.Find(name) != null)
            {
                return $"Collection '{name}' already exists.";
            }

            if (TagInUse(CollectionLogic.NormalizeTag(name), null))
            {
                return $"Another collection already uses the tag #{CollectionLogic.NormalizeTag(name)}.";
            }

            CollectionStore.Collections.Add(new BuildingCollection { Name = name, Tag = CollectionLogic.NormalizeTag(name) });
            CollectionStore.Save();
            Mod.Log.Info($"Collection '{name}' created (tag #{CollectionLogic.NormalizeTag(name)}).");
            return null;
        }

        public static string AddAllFromFindIt(string collectionName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            if (!FindItBridge.IsAvailable)
            {
                return "Find It is not installed or active.";
            }

            var filtered = FindItBridge.GetFilteredBuildings();
            if (filtered.Count == 0)
            {
                return "Find It returns no buildings with the current filter.";
            }

            int added = CollectionLogic.AddRange(c.Prefabs, filtered.Select(f => f.Info.PrefabName));
            FindItBridge.RefreshIds();
            foreach (var f in filtered)
            {
                TagQueue.Add(f.Info.PrefabName, c.Tag);
            }

            CollectionStore.Save();
            Mod.Log.Info($"'{c.Name}': {filtered.Count} in Find It, {added} new; {filtered.Count} tags queued. Total {c.Prefabs.Count}.");
            return null;
        }

        public static string AddPrefab(string collectionName, string prefabName)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            if (CollectionLogic.AddRange(c.Prefabs, new[] { prefabName }) == 0)
            {
                return $"'{prefabName}' was already in '{c.Name}'.";
            }

            if (!FindItBridge.TryGetId(prefabName, out _))
            {
                FindItBridge.RefreshIds();
            }

            TagQueue.Add(prefabName, c.Tag);
            CollectionStore.Save();
            Mod.Log.Info($"'{c.Name}': added '{prefabName}'. Total {c.Prefabs.Count} ({watch.ElapsedMilliseconds} ms).");
            return null;
        }

        public static string RemoveBuildings(string collectionName, IEnumerable<string> prefabNames)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            int removed = 0;
            foreach (var name in prefabNames.Where(n => !string.IsNullOrEmpty(n)).ToList())
            {
                if (CollectionLogic.Remove(c.Prefabs, name))
                {
                    removed++;
                    TagQueue.Remove(name, c.Tag);
                }
            }

            CollectionStore.Save();
            Mod.Log.Info($"'{c.Name}': removed {removed}. Total {c.Prefabs.Count}.");
            return null;
        }

        public static string Clear(string collectionName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            RemoveBuildings(collectionName, c.Prefabs.ToList());
            Mod.Log.Info($"Collection '{c.Name}' emptied.");
            return null;
        }

        public static string Rename(string collectionName, string newName, out string finalName)
        {
            finalName = null;
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            newName = (newName ?? string.Empty).Trim();
            if (newName.Length == 0)
            {
                return "The name is empty.";
            }

            var other = CollectionStore.Find(newName);
            if (other != null && other != c)
            {
                return $"Collection '{newName}' already exists.";
            }

            string oldName = c.Name, oldTag = c.Tag, newTag = CollectionLogic.NormalizeTag(newName);
            if (TagInUse(newTag, c))
            {
                return $"Another collection already uses the tag #{newTag}.";
            }

            c.Name = newName;
            if (!string.Equals(oldTag, newTag, System.StringComparison.OrdinalIgnoreCase))
            {
                c.Tag = newTag;
                FindItBridge.RefreshIds();
                foreach (var p in c.Prefabs)
                {
                    TagQueue.Remove(p, oldTag);
                    TagQueue.Add(p, newTag);
                }
            }

            CollectionStore.Save();
            finalName = newName;
            Mod.Log.Info($"Collection '{oldName}' renamed to '{newName}' (tag #{oldTag} -> #{newTag}).");
            return null;
        }

        static bool TagInUse(string tag, BuildingCollection except) =>
            CollectionStore.Collections.Any(o => o != except && string.Equals(o.Tag, tag, System.StringComparison.OrdinalIgnoreCase));

        public static string Clone(string collectionName, out string newName)
        {
            newName = null;
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            newName = c.Name + " copy";
            for (int n = 2; CollectionStore.Find(newName) != null; n++)
            {
                newName = $"{c.Name} copy {n}";
            }

            var copy = new BuildingCollection { Name = newName, Tag = CollectionLogic.NormalizeTag(newName) };
            CollectionLogic.AddRange(copy.Prefabs, c.Prefabs);
            CollectionStore.Collections.Add(copy);
            FindItBridge.RefreshIds();
            foreach (var p in copy.Prefabs)
            {
                TagQueue.Add(p, copy.Tag);
            }

            CollectionStore.Save();
            Mod.Log.Info($"Collection '{c.Name}' cloned as '{newName}' ({copy.Prefabs.Count} buildings).");
            return null;
        }

        public static void SyncFindItTags()
        {
            int n = 0;
            foreach (var c in CollectionStore.Collections)
            {
                foreach (var p in c.Prefabs)
                {
                    TagQueue.Force(true, p, c.Tag);
                    n++;
                }
            }

            Mod.Log.Info($"Find It tags enabled: {n} to apply.");
        }

        public static string Delete(string collectionName)
        {
            var c = CollectionStore.Find(collectionName);
            if (c == null)
            {
                return "No collection selected.";
            }

            Clear(collectionName);
            CollectionStore.Collections.Remove(c);
            CollectionStore.Save();
            Mod.Log.Info($"Collection '{c.Name}' deleted.");
            return null;
        }
    }
}
