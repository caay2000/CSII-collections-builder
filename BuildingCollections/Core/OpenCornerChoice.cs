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

            var wanted = best.OpenCornerPairs.Select(_ => PairKind.Twins).ToList();
            int plans = 1;
            var notes = new List<string>();
            for (int i = 0; i < wanted.Count; i++)
            {
                var placed = best.OpenCornerPairs[i];
                if (placed == PairKind.None)
                {
                    continue;
                }

                var without = run(With(wanted, i, PairKind.None));
                plans++;
                var bar = Score.Of(without, request);
                var current = Score.Of(best, request);
                if (current.NotWorseThan(bar))
                {
                    notes.Add($"  open corner {i + 1}: pair kept ({current}; without it {bar}).");
                    continue;
                }

                if (placed == PairKind.Twins)
                {
                    var same = run(With(wanted, i, PairKind.SameModel));
                    plans++;
                    var score = Score.Of(same, request);
                    if (same.OpenCornerPairs[i] == PairKind.SameModel && score.NotWorseThan(bar))
                    {
                        notes.Add($"  open corner {i + 1}: the same model on both streets instead of corner twins ({score}; twins {current}; without a pair {bar}).");
                        wanted[i] = PairKind.SameModel;
                        best = same;
                        continue;
                    }
                }

                notes.Add($"  open corner {i + 1}: pair dropped ({current}; without it {bar}).");
                wanted[i] = PairKind.None;
                best = without;
            }

            best.Log.AddRange(notes);
            best.Timings.Add($"open corner choice {plans} plans {clock.ElapsedMilliseconds} ms");
            return best;
        }

        static List<PairKind> With(List<PairKind> kinds, int index, PairKind kind) => kinds.Select((k, i) => i == index ? kind : k).ToList();

        readonly struct Score
        {
            readonly int filled;
            readonly int ownGaps;
            readonly double uncovered;
            readonly double largest;

            Score(int filled, int ownGaps, List<double> gaps)
            {
                this.filled = filled;
                this.ownGaps = ownGaps;
                uncovered = gaps.Sum();
                largest = gaps.DefaultIfEmpty(0).Max();
            }

            public static Score Of(PreviewResult result, PreviewRequest request)
            {
                var strips = request.Blocks.Where(b => b.Strip != null).Select(b => b.Strip).ToList();
                return new Score(
                    result.FrontFilled,
                    FrontCoverage.GapsBetweenOwn(result.Lots, request.Existing, request.Blocks),
                    SidewalkGaps.Find(strips, result.Lots).Select(g => g.Length).ToList());
            }

            public bool NotWorseThan(Score other) =>
                filled >= other.filled && ownGaps <= other.ownGaps && uncovered <= other.uncovered + UncoveredTolerance && largest <= other.largest + LargestGapTolerance;

            public override string ToString() => $"{filled} street cells filled, {ownGaps} gaps between buildings of one street, {uncovered:F1} m uncovered, largest gap {largest:F1} m";
        }
    }
}
