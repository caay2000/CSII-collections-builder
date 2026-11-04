using System;
using System.Collections.Generic;

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

            if (queue.Count == 0 && skipped > 0)
            {
                Mod.Log.Info($"Etiquetas: {skipped} edificios sin id en Find It (no cargados), sin cambiar.");
                skipped = 0;
            }

            return queue.Count == 0;
        }
    }
}
