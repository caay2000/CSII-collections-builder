using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BuildingCollections.Core
{
    public static class AngledProbe
    {
        const double Slack = 0.5;

        public static PreviewResult Plan(PreviewRequest request, Func<IReadOnlyList<PlannedLot>, PreviewResult> plan)
        {
            var placer = AngledCornerPlacer.Place(request.Sides, request.Frontage, request.Outline, request.Existing, request.Corners, request.Seed);
            var notes = new List<string>(placer.Log);
            var strips = request.Blocks.Select(b => b.Strip).Where(s => s != null).ToList();
            PreviewResult best;
            if (placer.Placed.Count == 0)
            {
                best = plan(new List<PlannedLot>());
            }
            else if (strips.Count == 0)
            {
                best = plan(placer.Placed);
                notes.Add("  angled corners kept without comparing: the area has no sidewalk strips to measure.");
            }
            else
            {
                best = KeepWhatHelps(placer.Placed, request, strips, plan(new List<PlannedLot>()), plan, notes);
            }

            best.Log.InsertRange(0, notes);
            return best;
        }

        static PreviewResult KeepWhatHelps(IReadOnlyList<PlannedLot> candidates, PreviewRequest request, List<LotStrip> strips, PreviewResult best,
            Func<IReadOnlyList<PlannedLot>, PreviewResult> plan, List<string> notes)
        {
            var bestScore = Score.Of(best, request, strips);
            var baseline = bestScore;
            var kept = new List<PlannedLot>();
            foreach (var candidate in candidates)
            {
                var trial = kept.Append(candidate).ToList();
                var result = plan(trial);
                var score = Score.Of(result, request, strips);
                bool keep = score.NoWorseThan(bestScore) && score.NoWorseThan(baseline);
                notes.Add($"  angled corner {candidate.Building.PrefabName}: with it {score}, without it {bestScore}; {(keep ? "kept" : "left out")}.");
                if (keep)
                {
                    kept = trial;
                    best = result;
                    bestScore = score;
                }
            }

            return best;
        }

        readonly struct Score
        {
            readonly double gapMeters;
            readonly int gaps;
            readonly int emptyCells;

            Score(double gapMeters, int gaps, int emptyCells)
            {
                this.gapMeters = gapMeters;
                this.gaps = gaps;
                this.emptyCells = emptyCells;
            }

            public static Score Of(PreviewResult result, PreviewRequest request, List<LotStrip> strips) => new Score(
                CornerWedges.GapsBetweenBuildings(strips, result.Lots, request.Sides),
                FrontCoverage.GapsBetweenOwn(result.Lots, request.Existing, request.Blocks),
                result.FrontCells.Count - result.FrontFilled);

            public bool NoWorseThan(Score other) => gapMeters <= other.gapMeters + Slack && gaps <= other.gaps && emptyCells <= other.emptyCells;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:F1} m in {1} gaps between buildings and {2} empty street cells", gapMeters, gaps, emptyCells);
        }
    }
}
