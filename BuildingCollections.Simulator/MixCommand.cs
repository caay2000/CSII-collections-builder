using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class MixCommand
    {
        static readonly BuildingInfo[] Wide =
        {
            Simulation.B("wide 3x2", 3, 2), Simulation.B("wide 2x3", 2, 3), Simulation.B("wide 3x3", 3, 3), Simulation.B("wide 4x4", 4, 4),
        };

        static readonly BuildingInfo[] Row =
        {
            Simulation.B("row 1x2", 1, 2), Simulation.B("row 2x2", 2, 2),
        };

        static readonly BuildingInfo[] RowCorners =
        {
            Simulation.B("row corner 1x2 L", 1, 2, CornerKind.Front | CornerKind.Left), Simulation.B("row corner 1x2 R", 1, 2, CornerKind.Front | CornerKind.Right),
            Simulation.B("row corner 2x2 L", 2, 2, CornerKind.Front | CornerKind.Left), Simulation.B("row corner 2x2 R", 2, 2, CornerKind.Front | CornerKind.Right),
        };

        public static int Run(string[] args)
        {
            string filter = args.Length > 1 ? args[1] : null;
            int[] seeds = { 7, 11, 13 };
            var scenarios = Scenarios.All().Where(s => filter == null || filter.Split('|').Any(f => s.Name.Contains(f))).ToList();
            foreach (int share in new[] { 80, 50, 20 })
            {
                var plain = Wide.Select(b => b.InGroup(0, share / 100.0)).Concat(Row.Select(b => b.InGroup(1, 1 - share / 100.0))).ToList();
                var corners = RowCorners.Select(b => b.InGroup(1, 1 - share / 100.0)).ToList();
                var runs = scenarios.SelectMany(s => seeds.Select(seed => Simulation.Simulate(s, 0.5f, seed, plain, corners))).ToList();
                foreach (var r in runs.Where(r => Environment.GetEnvironmentVariable("SIM_MIX_ALL") == "1"))
                {
                    Console.WriteLine($"  {r.Scenario.Name,-58} wide {r.Result.FirstGroupShare,5:P0}  cells {r.Result.FrontFilled}/{r.Result.FrontCells.Count}");
                    if (Environment.GetEnvironmentVariable("SIM_LOG") == "1")
                    {
                        r.Result.Log.ForEach(Console.WriteLine);
                    }
                }

                var shares = runs.Select(r => r.Result.FirstGroupShare ?? 0).ToList();
                int filled = runs.Sum(r => r.Result.FrontFilled);
                int total = runs.Sum(r => r.Result.FrontCells.Count);
                Console.WriteLine($"wide (no corners, no 1-wide) {share}% / row {100 - share}%: wide gets {shares.Average():P1} on average " +
                    $"(min {shares.Min():P0}, max {shares.Max():P0}) over {runs.Count} runs; street cells filled {(double)filled / total:P1}.");
            }

            return 0;
        }
    }
}
