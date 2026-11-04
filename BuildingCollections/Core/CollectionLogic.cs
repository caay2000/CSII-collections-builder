using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    /// <summary>Operaciones puras sobre la lista de prefabs de una colección.</summary>
    public static class CollectionLogic
    {
        /// <summary>Suma prefabs sin duplicados (sin distinguir mayúsculas). Devuelve cuántos eran nuevos.</summary>
        public static int AddRange(List<string> prefabs, IEnumerable<string> toAdd)
        {
            var set = new HashSet<string>(prefabs, StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var name in toAdd.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                if (set.Add(name))
                {
                    prefabs.Add(name);
                    added++;
                }
            }

            return added;
        }

        public static bool Remove(List<string> prefabs, string name) =>
            prefabs.RemoveAll(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase)) > 0;

        /// <summary>Misma normalización que Find It: sin '#', espacios a guiones.</summary>
        public static string NormalizeTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var parts = tag.Trim().TrimStart('#').Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("-", parts);
        }
    }
}
