using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BuildingCollections.Core
{
    public static class OpenCornerChoice
    {
        const double UncoveredTolerance = 0.5;
        const double LargestGapTolerance = 2;
        const double JointUncoveredTolerance = 3;
        static readonly PairKind[] Order = { PairKind.Narrow, PairKind.Twins, PairKind.SameModel };

        public static PreviewResult Plan(PreviewRequest request, Func<IReadOnlyList<PairKind>, PreviewResult> run)
        {
            if (!request.PairOpenCorners)
            {
                return run(null);
            }

            var clock = Stopwatch.StartNew();
            var best = run(new List<PairKind>());
            if (best.OpenCornerPairs.All(k => k == PairKind.None))
            {
                return best;
            }

            var placeable = best.OpenCornerPairs.Select(k => k != PairKind.None).ToList();
            var wanted = placeable.Select(_ => PairKind.None).ToList();
            var without = run(wanted);
            int plans = 2;
            var notes = new List<string>();
            for (int i = 0; i < wanted.Count; i++)
            {
                if (!placeable[i])
                {
                    continue;
                }

                var bar = Score.Of(without, request);
                var options = new List<(PairKind Kind, PreviewResult Result, Score Score)>();
                var runs = new Dictionary<PairKind, PreviewResult>();
                foreach (var kind in Order)
                {
                    if (!runs.TryGetValue(kind, out var result))
                    {
                        result = run(With(wanted, i, kind));
                        plans++;
                        if (!runs.ContainsKey(result.OpenCornerPairs[i]))
                        {
                            runs[result.OpenCornerPairs[i]] = result;
                        }
                    }

                    if (result.OpenCornerPairs[i] == kind)
                    {
                        options.Add((kind, result, Score.Of(result, request)));
                    }
                }

                var passing = options.Where(o => o.Score.NotWorseThan(bar)).ToList();
                var chosen = passing.FirstOrDefault(o => passing.All(other => o.Score.NotWorseThan(other.Score)));
                if (chosen.Result == null)
                {
                    chosen = passing.OrderBy(o => o.Score.Uncovered).FirstOrDefault();
                }

                string tried = string.Concat(options.Where(o => chosen.Result == null || o.Kind != chosen.Kind).Select(o => $"; {Describe(o.Kind)} {o.Score}"));
                if (chosen.Result == null)
                {
                    notes.Add($"  open corner {i + 1}: pair dropped (without it {bar}{tried}).");
                    continue;
                }

                notes.Add($"  open corner {i + 1}: {Describe(chosen.Kind)} kept ({chosen.Score}; without a pair {bar}{tried}).");
                wanted[i] = chosen.Kind;
                without = chosen.Result;
            }

            best = without;
            var allNarrow = placeable.Select(p => p ? PairKind.Narrow : PairKind.None).ToList();
            if (placeable.Count(p => p) > 1 && placeable.Where((p, i) => p && wanted[i] == PairKind.None).Any())
            {
                var joint = run(allNarrow);
                plans++;
                var jointScore = Score.Of(joint, request);
                var bestScore = Score.Of(best, request);
                if (joint.OpenCornerPairs.SequenceEqual(allNarrow) && jointScore.NoNewGapsAndClose(bestScore))
                {
                    notes.Add($"  open corners together: narrow same-model pairs on all of them ({jointScore}; one at a time {bestScore}).");
                    best = joint;
                }
            }

            best.Log.AddRange(notes);
            best.Timings.Add($"open corner choice {plans} plans {clock.ElapsedMilliseconds} ms");
            return best;
        }

        static string Describe(PairKind kind) => kind == PairKind.Narrow ? "narrow same-model pair" : kind == PairKind.Twins ? "corner twins" : "same-model pair";

        static List<PairKind> With(List<PairKind> kinds, int index, PairKind kind) => kinds.Select((k, i) => i == index ? kind : k).ToList();

        readonly struct Score
        {
            readonly int filled;
            readonly int ownGaps;
            readonly int allGaps;
            readonly double uncovered;
            readonly double largest;

            Score(int filled, int ownGaps, int allGaps, List<double> gaps)
            {
                this.filled = filled;
                this.ownGaps = ownGaps;
                this.allGaps = allGaps;
                uncovered = gaps.Sum();
                largest = gaps.DefaultIfEmpty(0).Max();
            }

            public static Score Of(PreviewResult result, PreviewRequest request)
            {
                var strips = request.Blocks.Where(b => b.Strip != null).Select(b => b.Strip).ToList();
                return new Score(
                    result.FrontFilled,
                    FrontCoverage.GapsBetweenOwn(result.Lots, request.Existing, request.Blocks),
                    FrontCoverage.GapsBetweenBuildings(result.Lots, request.Existing, request.Blocks),
                    SidewalkGaps.Find(strips, result.Lots).Select(g => g.Length).ToList());
            }

            public double Uncovered => uncovered;

            public bool NotWorseThan(Score other) =>
                filled >= other.filled && (ownGaps <= other.ownGaps || allGaps < other.allGaps) && uncovered <= other.uncovered + UncoveredTolerance && largest <= other.largest + LargestGapTolerance;

            public bool NoNewGapsAndClose(Score other) =>
                filled >= other.filled && ownGaps <= other.ownGaps && allGaps <= other.allGaps && uncovered <= other.uncovered + JointUncoveredTolerance && largest <= other.largest + LargestGapTolerance;

            public override string ToString() => $"{filled} street cells filled, {ownGaps} gaps between buildings of one street ({allGaps} in all), {uncovered:F1} m uncovered, largest gap {largest:F1} m";
        }
    }
}
