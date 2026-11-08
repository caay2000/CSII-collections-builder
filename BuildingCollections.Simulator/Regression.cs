using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class Regression
    {
        public static readonly float[] Overlaps = { FootprintMask.StrictOverlap, 0.5f, 1f, 2f };

        public const int SeedCount = 24;

        public static int[] Seeds(int first = 1) => Enumerable.Range(first, SeedCount).ToArray();

        public static readonly IReadOnlyList<MetricRule> Rules = new List<MetricRule>
        {
            new MetricRule("missed corners", m => m.MissedCorners, _ => Slack.Noise(0, 1), _ => Slack.Share(0, 0)),
            new MetricRule("gaps between two buildings", m => m.GapsBetweenBuildings,
                m => m.Strict ? Slack.Noise(6, 3) : Slack.Noise(3, 3),
                o => o <= FootprintMask.StrictOverlap ? Slack.Share(10, 0.03) : Slack.Share(5, 0.03)),
            new MetricRule("uncovered meters", m => m.GapMeters, _ => Slack.Noise(20, 4), _ => Slack.Share(20, 0.01)),
            new MetricRule("largest gap", m => m.MaxGap, _ => Slack.Noise(6, 0), null),
            new MetricRule("empty street cells", m => m.EmptyCells, _ => Slack.Noise(6, 2.5), _ => Slack.Share(10, 0.015)),
        };

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
            var now = Index(actual);
            var before = Index(expected);
            verdict.Added.AddRange(now.Keys.Where(k => !before.ContainsKey(k)));
            foreach (var was in expected.Runs)
            {
                if (!now.TryGetValue(was.Key, out var current))
                {
                    verdict.Missing.Add(was.Key);
                    continue;
                }

                foreach (var rule in Rules)
                {
                    Judge(verdict, $"{was.Key}: {rule.Name}", rule.Value(was), rule.Value(current), rule.PerScenario(was));
                }
            }

            CompareTotals(verdict, expected.Runs.Where(m => now.ContainsKey(m.Key)).ToList(), now);
            return verdict;
        }

        static void CompareTotals(Verdict verdict, List<Metrics> common, Dictionary<string, Metrics> now)
        {
            foreach (var overlap in Overlaps)
            {
                var group = common.Where(m => m.Overlap == overlap).ToList();
                foreach (var rule in Rules.Where(r => r.PerOverlap != null))
                {
                    double was = group.Sum(rule.Value), current = group.Sum(m => rule.Value(now[m.Key]));
                    Judge(verdict, $"TOTAL @ {overlap:0.0#}: {rule.Name}", was, current, rule.PerOverlap(overlap));
                }
            }
        }

        static Dictionary<string, Metrics> Index(Baseline baseline)
        {
            var index = new Dictionary<string, Metrics>();
            foreach (var m in baseline.Runs)
            {
                if (index.ContainsKey(m.Key))
                {
                    throw new InvalidOperationException($"Two scenarios share the name '{m.Scenario}'.");
                }

                index[m.Key] = m;
            }

            return index;
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
    }
}
