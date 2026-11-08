using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "bench")
            {
                Bench.Run(args.Length > 1 ? int.Parse(args[1]) : 10, args.Length > 2 ? int.Parse(args[2]) : 3);
                return 0;
            }

            if (args.Length > 0 && args[0] == "regress")
            {
                Simulation.Plain = Simulation.Catalogs[Simulation.DefaultCatalog];
                int from = Array.IndexOf(args, "--first-seed");
                return Regression.Run(args.Contains("--update"), Simulation.DefaultCatalog, from > 0 ? int.Parse(args[from + 1]) : 1);
            }

            string output = args.Length > 0 ? args[0] : "simulation.html";
            int seed = args.Length > 1 ? int.Parse(args[1]) : 7;
            var overlaps = new[] { FootprintMask.StrictOverlap, 0.5f, 1f, 2f };
            string filter = args.Length > 2 ? args[2] : null;
            string catalog = Environment.GetEnvironmentVariable("SIM_CATALOG") ?? Simulation.DefaultCatalog;
            if (!Simulation.Catalogs.TryGetValue(catalog, out var plain))
            {
                Console.Error.WriteLine($"Unknown catalog '{catalog}'. Available: {string.Join(", ", Simulation.Catalogs.Keys)}");
                return 2;
            }

            Simulation.Plain = plain;
            Console.WriteLine($"Catalog: {catalog} ({string.Join(", ", plain.Select(b => b.PrefabName))})");
            var runs = Scenarios.All()
                .Where(s => filter == null || filter.Split('|').Any(f => s.Name.Contains(f)))
                .SelectMany(s => overlaps.Select(o => Simulation.Simulate(s, o, seed)))
                .ToList();
            File.WriteAllText(output, HtmlReport.Render(runs, overlaps, $"{Version()}, catalog {catalog}", seed));
            foreach (var r in runs)
            {
                Console.WriteLine($"{r.Scenario.Name,-58} overlap {r.Overlap,4:0.0#}: {r.Result.FrontFilled,3}/{r.Result.FrontCells.Count,-3} cells, {r.Gaps.Count,2} gaps, {r.GapMeters,6:0.0} m, max {r.MaxGap,5:0.0} m, corners {r.CornersOk}/{r.Corners.Count} {string.Join(" ", r.Corners.Where(c => c.State != CornerState.Corner).Select(c => $"[{c.Angle:0}° {c.State}]"))}");
            }

            if (Environment.GetEnvironmentVariable("SIM_WHY") == "1")
            {
                foreach (var r in runs.Where(r => Math.Abs(r.Overlap - 0.5f) < 0.01f))
                {
                    Console.WriteLine($"WHY {r.Scenario.Name,-50} cells {r.Result.FrontFilled}/{r.Result.FrontCells.Count} corners {r.CornersOk}/{r.Corners.Count} gaps: {string.Join(" ", r.RealGaps.Where(g => g.Length >= 0.75).Select(g => g.Length.ToString("0.0")))}");
                }
            }

            if (Environment.GetEnvironmentVariable("SIM_LOG") == "1")
            {
                foreach (var r in runs)
                {
                    Console.WriteLine($"== {r.Scenario.Name} overlap {r.Overlap}");
                    r.Result.Log.ForEach(Console.WriteLine);
                }
            }

            Console.WriteLine($"Report: {Path.GetFullPath(output)}");
            return 0;
        }

        static string Version()
        {
            var csproj = Path.Combine(AppContext.BaseDirectory, "../../../../BuildingCollections/BuildingCollections.csproj");
            if (!File.Exists(csproj))
            {
                return "?";
            }

            var m = Regex.Match(File.ReadAllText(csproj), "<Version>([^<]+)</Version>");
            return m.Success ? "v" + m.Groups[1].Value : "?";
        }
    }
}
