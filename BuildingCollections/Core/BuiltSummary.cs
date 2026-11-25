using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildingCollections.Core
{
    public static class BuiltSummary
    {
        static readonly Regex PdxFolder = new Regex(@"pdx_mods[\\/](\d+)_(\d+)", RegexOptions.IgnoreCase);

        public static string Source(string assetPath, bool builtin)
        {
            if (builtin)
            {
                return "game";
            }

            var m = PdxFolder.Match(assetPath ?? string.Empty);
            return m.Success ? $"pdx {m.Groups[1].Value} v{m.Groups[2].Value}" : "local";
        }

        public static IEnumerable<string> Lines(IEnumerable<(string name, string source)> buildings) =>
            buildings
                .GroupBy(b => (b.name, b.source))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.name, StringComparer.Ordinal)
                .Select(g => $"  [built] {g.Count()}x {g.Key.name} ({g.Key.source})");
    }
}
