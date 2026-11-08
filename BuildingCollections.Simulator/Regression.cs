using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public sealed class Metrics
    {
        public string Scenario { get; set; }
        public float Overlap { get; set; }
        public int MissedCorners { get; set; }
        public double GapMeters { get; set; }
        public double MaxGap { get; set; }
        public int GapsBetweenBuildings { get; set; }
        public int EmptyCells { get; set; }

        public string Key => $"{Scenario} @ {Overlap:0.0#}";

        public bool Strict => Overlap <= FootprintMask.StrictOverlap;
    }

    public sealed class Baseline
    {
        public string Catalog { get; set; }
        public int[] Seeds { get; set; }
        public List<Metrics> Runs { get; set; } = new List<Metrics>();
    }

    public sealed class Verdict
    {
        public List<string> Regressions = new List<string>();
        public List<string> Improvements = new List<string>();
        public List<string> Missing = new List<string>();
        public List<string> Added = new List<string>();

        public bool Passed => Regressions.Count == 0 && Missing.Count == 0;
    }

    public readonly struct Slack
    {
        readonly double fixedPart;
        readonly double perRoot;
        readonly double share;

        Slack(double fixedPart, double perRoot, double share)
        {
            this.fixedPart = fixedPart;
            this.perRoot = perRoot;
            this.share = share;
        }

        public static Slack Noise(double fixedPart, double perRoot) => new Slack(fixedPart, perRoot, 0);

        public static Slack Share(double fixedPart, double share) => new Slack(fixedPart, 0, share);

        public double Allowed(double baseline) => fixedPart + perRoot * Math.Sqrt(Math.Max(0, baseline)) + share * baseline;
    }

    public sealed class MetricRule
    {
        public string Name;
        public Func<Metrics, double> Value;
        public Func<Metrics, Slack> PerScenario;
        public Func<float, Slack> PerOverlap;
    }

    public static class Regression
    {
        public static readonly float[] Overlaps = { FootprintMask.StrictOverlap, 0.5f, 1f, 2f };

        public const int SeedCount = 24;

        public static int[] Seeds(int first = 1) => Enumerable.Range(first, SeedCount).ToArray();

        public static readonly IReadOnlyList<MetricRule> Rules = new List<MetricRule>
        {
            new MetricRule
            {
                Name = "missed corners", Value = m => m.MissedCorners,
                PerScenario = _ => Slack.Noise(0, 1), PerOverlap = _ => Slack.Share(0, 0),
            },
            new MetricRule
            {
                Name = "gaps between two buildings", Value = m => m.GapsBetweenBuildings,
                PerScenario = m => m.Strict ? Slack.Noise(6, 3) : Slack.Noise(3, 3),
                PerOverlap = o => o <= FootprintMask.StrictOverlap ? Slack.Share(10, 0.03) : Slack.Share(5, 0.03),
            },
            new MetricRule
            {
                Name = "uncovered meters", Value = m => m.GapMeters,
                PerScenario = _ => Slack.Noise(20, 4), PerOverlap = _ => Slack.Share(20, 0.01),
            },
            new MetricRule
            {
                Name = "largest gap", Value = m => m.MaxGap,
                PerScenario = _ => Slack.Noise(6, 0), PerOverlap = null,
            },
            new MetricRule
            {
                Name = "empty street cells", Value = m => m.EmptyCells,
                PerScenario = _ => Slack.Noise(6, 2.5), PerOverlap = _ => Slack.Share(10, 0.015),
            },
        };

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public static string BaselinePath => Path.Combine(GameBlocks.ProjectFolder, "regression-baseline.json");

        public static Baseline Measure(IEnumerable<Scenario> scenarios, string catalog, int[] seeds)
        {
            var jobs = scenarios.SelectMany(s => Overlaps.Select(o => (scenario: s, overlap: o))).ToList();
            return new Baseline
            {
                Catalog = catalog,
                Seeds = seeds,
                Runs = jobs.AsParallel().AsOrdered()
                    .Select(j => Aggregate(j.scenario.Name, j.overlap, seeds.Select(seed => Simulation.Simulate(j.scenario, j.overlap, seed)).ToList()))
                    .ToList(),
            };
        }

        static Metrics Aggregate(string name, float overlap, List<Run> runs) => new Metrics
        {
            Scenario = name,
            Overlap = overlap,
            MissedCorners = runs.Sum(r => r.Corners.Count - r.CornersOk),
            GapMeters = Math.Round(runs.Sum(r => r.GapMeters), 1),
            MaxGap = Math.Round(runs.Max(r => r.MaxGap), 1),
            GapsBetweenBuildings = runs.Sum(r => r.Result.Log.Count(l => l.Contains(FrontCoverage.BetweenOwn))),
            EmptyCells = runs.Sum(r => r.Result.FrontCells.Count - r.Result.FrontFilled),
        };

        public static Verdict Compare(Baseline expected, Baseline actual)
        {
            var verdict = new Verdict();
            var now = actual.Runs.ToDictionary(m => m.Key);
            foreach (var before in expected.Runs)
            {
                if (!now.TryGetValue(before.Key, out var after))
                {
                    verdict.Missing.Add(before.Key);
                    continue;
                }

                foreach (var rule in Rules)
                {
                    Judge(verdict, $"{before.Key}: {rule.Name}", rule.Value(before), rule.Value(after), rule.PerScenario(before));
                }
            }

            var known = new HashSet<string>(expected.Runs.Select(m => m.Key));
            verdict.Added.AddRange(actual.Runs.Where(m => !known.Contains(m.Key)).Select(m => m.Key));
            var common = expected.Runs.Where(m => now.ContainsKey(m.Key)).ToList();
            foreach (var overlap in Overlaps)
            {
                var group = common.Where(m => m.Overlap == overlap).ToList();
                foreach (var rule in Rules.Where(r => r.PerOverlap != null))
                {
                    double was = group.Sum(rule.Value), current = group.Sum(m => rule.Value(now[m.Key]));
                    Judge(verdict, $"TOTAL @ {overlap:0.0#}: {rule.Name}", was, current, rule.PerOverlap(overlap));
                }
            }

            return verdict;
        }

        static void Judge(Verdict verdict, string what, double was, double now, Slack slack)
        {
            double allowed = slack.Allowed(was);
            string line = $"{what} {was:0.#} -> {now:0.#} (allowed ±{allowed:0.#})";
            if (now > was + allowed + 1e-6)
            {
                verdict.Regressions.Add(line);
            }
            else if (now < was - allowed - 1e-6)
            {
                verdict.Improvements.Add(line);
            }
        }

        public static Baseline Load(string path) => JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path));

        public static void Save(Baseline baseline, string path) => File.WriteAllText(path, JsonSerializer.Serialize(baseline, Json) + Environment.NewLine);

        public static string Mismatch(Baseline expected, string catalog, int[] seeds) =>
            expected.Catalog != catalog ? $"baseline catalog '{expected.Catalog}' differs from '{catalog}'"
            : !expected.Seeds.SequenceEqual(seeds) ? "baseline seeds differ from the current ones"
            : null;

        public static int Run(bool update, string catalog, int firstSeed)
        {
            var seeds = Seeds(firstSeed);
            var actual = Measure(Scenarios.All(), catalog, seeds);
            if (update)
            {
                Save(actual, BaselinePath);
                Console.WriteLine($"Baseline written: {BaselinePath} ({actual.Runs.Count} runs, seeds {seeds.First()}-{seeds.Last()}).");
                return 0;
            }

            if (!File.Exists(BaselinePath))
            {
                Console.WriteLine($"No baseline at {BaselinePath}. Run 'regress --update' first.");
                return 2;
            }

            var expected = Load(BaselinePath);
            var mismatch = firstSeed == 1 ? Mismatch(expected, catalog, seeds) : null;
            if (mismatch != null)
            {
                Console.WriteLine($"Cannot compare: {mismatch}. Run 'regress --update'.");
                return 2;
            }

            var verdict = Compare(expected, actual);
            verdict.Improvements.ForEach(l => Console.WriteLine("BETTER  " + l));
            verdict.Regressions.ForEach(l => Console.WriteLine("WORSE   " + l));
            verdict.Missing.ForEach(l => Console.WriteLine("MISSING " + l));
            verdict.Added.ForEach(l => Console.WriteLine("NEW     " + l));
            Console.WriteLine(verdict.Passed
                ? $"Regression passed ({verdict.Improvements.Count} improvements). Run 'regress --update' to record them."
                : $"Regression FAILED: {verdict.Regressions.Count} worse, {verdict.Missing.Count} missing.");
            return verdict.Passed ? 0 : 1;
        }
    }
}
