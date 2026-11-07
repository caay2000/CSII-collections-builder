using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public static class RandomPick
    {
        public static string Other(IReadOnlyList<string> candidates, string current, Random rng)
        {
            var pool = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (pool.Count > 1)
            {
                pool.RemoveAll(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            }

            return pool.Count == 0 ? null : pool[rng.Next(pool.Count)];
        }
    }
}
