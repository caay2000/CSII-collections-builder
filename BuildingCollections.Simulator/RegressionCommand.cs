using System;
using System.IO;
using System.Linq;

namespace BuildingCollections.Simulator
{
    public static class RegressionCommand
    {
        const string Usage = "Usage: regress [--update] [--first-seed N]";

        public static int Run(string[] args)
        {
            if (!TryFirstSeed(args, out int firstSeed))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            Simulation.Plain = Simulation.Catalogs[Simulation.DefaultCatalog];
            var seeds = Regression.Seeds(firstSeed);
            var actual = Regression.Measure(Scenarios.All(), Simulation.DefaultCatalog, seeds);
            string path = BaselineStore.DefaultPath;
            if (args.Contains("--update"))
            {
                BaselineStore.Save(actual, path);
                Console.WriteLine($"Baseline written: {path} ({actual.Runs.Count} runs, seeds {seeds.First()}-{seeds.Last()}).");
                return 0;
            }

            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"No baseline at {path}. Run 'regress --update' first.");
                return 2;
            }

            var expected = BaselineStore.Load(path);
            var mismatch = firstSeed == 1 ? BaselineStore.Mismatch(expected, Simulation.DefaultCatalog, seeds) : null;
            if (mismatch != null)
            {
                Console.Error.WriteLine($"Cannot compare: {mismatch}. Run 'regress --update'.");
                return 2;
            }

            var verdict = Regression.Compare(expected, actual);
            verdict.Improvements.ForEach(l => Console.WriteLine("BETTER  " + l));
            foreach (var line in verdict.Failures())
            {
                Console.WriteLine(line);
            }

            Console.WriteLine(verdict.Passed
                ? $"Regression passed ({verdict.Improvements.Count} improvements). Run 'regress --update' to record them."
                : $"Regression FAILED: {verdict.Regressions.Count} worse, {verdict.Missing.Count} missing, {verdict.Added.Count} new.");
            return verdict.Passed ? 0 : 1;
        }

        static bool TryFirstSeed(string[] args, out int firstSeed)
        {
            firstSeed = 1;
            int at = Array.IndexOf(args, "--first-seed");
            return at < 0 || (at + 1 < args.Length && int.TryParse(args[at + 1], out firstSeed) && firstSeed > 0);
        }
    }
}
