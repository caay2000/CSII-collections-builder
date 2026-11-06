using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections
{
    public static class TagQueue
    {
        static readonly Queue<(bool add, string prefab, string tag)> queue = new Queue<(bool, string, string)>();

        static int skipped;

        static bool batchFailed;

        static DateTime lastChange;

        const double SettleSeconds = 1.0;

        public static bool Settled => (DateTime.UtcNow - lastChange).TotalSeconds >= SettleSeconds;

        public static int Count => queue.Count;

        public static bool Enabled => Mod.Settings?.FindItTags ?? true;

        public static void Add(string prefab, string tag)
        {
            if (Enabled && !string.IsNullOrEmpty(tag)) Enqueue(true, prefab, tag);
        }

        public static void Remove(string prefab, string tag)
        {
            if (Enabled && !string.IsNullOrEmpty(tag)) Enqueue(false, prefab, tag);
        }

        internal static void Force(bool add, string prefab, string tag)
        {
            if (!string.IsNullOrEmpty(tag)) Enqueue(add, prefab, tag);
        }

        static void Enqueue(bool add, string prefab, string tag)
        {
            queue.Enqueue((add, prefab, tag));
            lastChange = DateTime.UtcNow;
        }

        public static bool Process(int max)
        {
            if (queue.Count == 0)
            {
                return false;
            }

            if (!batchFailed)
            {
                var all = queue.ToList();
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    if (all.Any(c => !FindItBridge.TryGetId(c.prefab, out _)))
                    {
                        FindItBridge.RefreshIds();
                    }

                    long idsMs = watch.ElapsedMilliseconds;
                    if (FindItBridge.TryApplyBatch(all, out int notFound, out long saveMs))
                    {
                        queue.Clear();
                        skipped += notFound;
                        Mod.Log.Info($"Tags: {all.Count} changes applied with a single Find It save ({watch.ElapsedMilliseconds} ms: ids {idsMs} ms, Find It save {saveMs} ms).");
                        Flush();
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Bulk tagging failed ({e.Message}); continuing one at a time.");
                }

                batchFailed = true;
            }

            FindItBridge.RefreshIds();
            for (int i = 0; i < max && queue.Count > 0; i++)
            {
                var (add, prefab, tag) = queue.Dequeue();
                try
                {
                    if (FindItBridge.TryGetId(prefab, out int id))
                    {
                        if (add) FindItBridge.AddTag(id, tag);
                        else FindItBridge.RemoveTag(id, tag);
                    }
                    else
                    {
                        skipped++;
                    }
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Find It tag not updated ({prefab}): {e.Message}");
                }
            }

            if (queue.Count == 0)
            {
                Flush();
            }

            return queue.Count == 0;
        }

        static void Flush()
        {
            if (skipped > 0)
            {
                Mod.Log.Info($"Tags: {skipped} buildings without a Find It id (not loaded), unchanged.");
                skipped = 0;
            }
        }
    }
}
