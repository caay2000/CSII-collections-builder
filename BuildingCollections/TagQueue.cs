using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections
{
    /// <summary>
    /// Cola de cambios de etiquetas de Find It. Find It guarda su fichero entero en cada AddTag / RemoveTag, así que
    /// hacerlos de golpe congela el juego varios segundos; aquí se reparten entre fotogramas (BcUISystem la vacía).
    /// </summary>
    public static class TagQueue
    {
        static readonly Queue<(bool add, string prefab, string tag)> queue = new Queue<(bool, string, string)>();

        static int skipped;

        /// <summary>El bloque falló una vez (Find It cambió): el resto de la partida, de una en una.</summary>
        static bool batchFailed;

        public static int Count => queue.Count;

        public static void Add(string prefab, string tag) => queue.Enqueue((true, prefab, tag));

        public static void Remove(string prefab, string tag) => queue.Enqueue((false, prefab, tag));

        /// <summary>Procesa hasta <paramref name="max"/> cambios. Devuelve true si la cola acaba de vaciarse.</summary>
        public static bool Process(int max)
        {
            if (queue.Count == 0)
            {
                return false;
            }

            // Todo de una vez con un solo guardado del fichero de Find It; si no se puede, de uno en uno como antes.
            if (!batchFailed)
            {
                var all = queue.ToList();
                try
                {
                    if (FindItBridge.TryApplyBatch(all, out int notFound))
                    {
                        queue.Clear();
                        skipped += notFound;
                        Mod.Log.Info($"Etiquetas: {all.Count} cambios aplicados con un solo guardado de Find It.");
                        Flush();
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Etiquetas en bloque fallaron ({e.Message}); se sigue de una en una.");
                }

                batchFailed = true;
            }

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
                    Mod.Log.Warn($"Etiqueta de Find It no actualizada ({prefab}): {e.Message}");
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
                Mod.Log.Info($"Etiquetas: {skipped} edificios sin id en Find It (no cargados), sin cambiar.");
                skipped = 0;
            }
        }
    }
}
