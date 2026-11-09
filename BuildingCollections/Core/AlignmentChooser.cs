using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.StripCells;

namespace BuildingCollections.Core
{
    internal sealed class SidePlan
    {
        public int Side;
        public Side Shape;
        public List<List<LotStrip>> Options;
        public string AlternativeNote;
    }

    internal sealed class AlignmentChooser
    {
        public static IEnumerable<LotStrip> Choose(List<SidePlan> plans, int sideCount, IList<string> notes, Func<List<List<LotStrip>>, int> pickBest = null)
        {
            var candidates = new AlignmentChooser(plans, sideCount).Candidates();
            int chosen = candidates.Count > 1 && pickBest != null ? pickBest(candidates.Select(c => Strips(plans, c)).ToList()) : 0;
            var pick = candidates[chosen];
            for (int i = 0; i < plans.Count; i++)
            {
                if (pick[i] == 1)
                {
                    notes?.Add($"side {plans[i].Side}: {plans[i].AlternativeNote}");
                }
            }

            if (chosen > 0)
            {
                notes?.Add($"alignment: option {chosen + 1} of {candidates.Count} with the same corners leaves less sidewalk uncovered.");
            }

            return Strips(plans, pick);
        }

        static List<LotStrip> Strips(List<SidePlan> plans, int[] pick) => plans.SelectMany((p, i) => p.Options[pick[i]]).ToList();

        const int MaxCandidates = 6;

        const int CornerWeight = 100000;
        const int RoundedWeight = 10000;
        const int CellWeight = 10;
        const int ChangeWeight = 5000;

        const long ExhaustiveLimit = 1 << 12;

        readonly List<SidePlan> plans;
        readonly int[] prev;
        readonly CornerClaim[][][] claims;
        readonly List<CornerClaim> active = new List<CornerClaim>();
        readonly int[][] own;
        readonly int[][] rounded;
        readonly bool[][] takesStart;
        readonly bool[][] takesEnd;

        public AlignmentChooser(List<SidePlan> plans, int sideCount)
        {
            this.plans = plans;
            var bySide = plans.Select((p, i) => (p.Side, i)).ToDictionary(x => x.Side, x => x.i);
            prev = plans.Select(p => bySide.TryGetValue((p.Side - 1 + sideCount) % sideCount, out int pi) ? pi : -1).ToArray();
            own = plans.Select(p => p.Options.Select((o, c) => OwnScore(p, o, c)).ToArray()).ToArray();
            rounded = plans.Select(p => p.Options.Select(o => RoundedScore(p, o)).ToArray()).ToArray();
            takesStart = plans.Select(p => p.Options.Select(o => o.Count > 0 && TakesCorner(o[0], false)).ToArray()).ToArray();
            takesEnd = plans.Select(p => p.Options.Select(o => o.Count > 0 && p.Shape.EndCorner && TakesCorner(o[o.Count - 1], true)).ToArray()).ToArray();
            claims = plans.Select((p, i) => p.Options.Select((o, c) => Claims(i, o, takesStart[i][c], takesEnd[i][c])).ToArray()).ToArray();
            CornerClaims.Rank(claims.SelectMany(o => o).SelectMany(c => c));
        }

        static CornerClaim[] Claims(int plan, List<LotStrip> option, bool start, bool end)
        {
            var list = new List<CornerClaim>();
            if (start)
            {
                list.Add(CornerClaims.For(plan, option[0], plan * 1000, false));
            }

            if (end)
            {
                list.Add(CornerClaims.For(plan, option[option.Count - 1], plan * 1000 + option.Count - 1, true));
            }

            return list.ToArray();
        }

        static int OwnScore(SidePlan plan, List<LotStrip> option, int choice)
        {
            if (option.Count == 0)
            {
                return -choice * ChangeWeight;
            }

            int cells = 0;
            foreach (var st in option)
            {
                for (int x = 0; x < st.Width; x++)
                {
                    cells += st.Grid.Free[x, 0] ? 1 : 0;
                }
            }

            return RoundedScore(plan, option) + cells * CellWeight - choice * ChangeWeight;
        }

        static int RoundedScore(SidePlan plan, List<LotStrip> option)
        {
            if (option.Count == 0)
            {
                return 0;
            }

            int count = (plan.Shape.StartRounded && option[0].Grid.LeftCorner ? 1 : 0)
                + (plan.Shape.EndRounded && option[option.Count - 1].Grid.RightCorner ? 1 : 0);
            return count * RoundedWeight;
        }

        int StartCornerScore(int i, int choice, int prevChoice)
        {
            if (!plans[i].Shape.StartCorner || plans[i].Options[choice].Count == 0)
            {
                return 0;
            }

            bool taken = takesStart[i][choice] || (prev[i] >= 0 && takesEnd[prev[i]][prevChoice]);
            return taken ? CornerWeight : 0;
        }

        int Options(int i) => plans[i].Options.Count;

        public List<int[]> Candidates()
        {
            int n = plans.Count;
            if (n == 0)
            {
                return new List<int[]> { new int[0] };
            }

            if (Combinations() <= ExhaustiveLimit)
            {
                return Exhaustive();
            }

            return new List<int[]> { Best() };
        }

        int[] Best()
        {
            int n = plans.Count;
            var best = new int[n];
            bool wraps = prev[0] == n - 1;
            var bestScore = Score.None;
            for (int first = 0; first < (wraps ? Options(0) : 1); first++)
            {
                var (score, choice) = Chain(first, wraps);
                if (score.Beats(bestScore))
                {
                    bestScore = score;
                    best = choice;
                }
            }

            return Improve(best);
        }

        int[] Improve(int[] choice)
        {
            var current = Evaluate(choice);
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < plans.Count; i++)
                {
                    int was = choice[i];
                    for (int c = 0; c < Options(i); c++)
                    {
                        if (c == was)
                        {
                            continue;
                        }

                        choice[i] = c;
                        var score = Evaluate(choice);
                        if (score.Beats(current))
                        {
                            current = score;
                            was = c;
                            changed = true;
                        }
                    }

                    choice[i] = was;
                }
            }

            return choice;
        }

        long Combinations()
        {
            long total = 1;
            for (int i = 0; i < plans.Count && total <= ExhaustiveLimit; i++)
            {
                total *= Math.Max(1, Options(i));
            }

            return total;
        }

        List<int[]> Exhaustive()
        {
            int n = plans.Count;
            var choice = new int[n];
            var all = new List<(Score score, int[] choice)>();
            while (true)
            {
                all.Add((Evaluate(choice), (int[])choice.Clone()));
                int i = 0;
                while (i < n && ++choice[i] >= Options(i))
                {
                    choice[i++] = 0;
                }

                if (i == n)
                {
                    break;
                }
            }

            all.Sort((a, b) => a.score.Beats(b.score) ? -1 : b.score.Beats(a.score) ? 1 : 0);
            int tier = all[0].score.Tier;
            return all.TakeWhile(x => x.score.Tier == tier).Take(MaxCandidates).Select(x => x.choice).ToList();
        }

        Score Evaluate(int[] choice)
        {
            active.Clear();
            for (int i = 0; i < plans.Count; i++)
            {
                active.AddRange(claims[i][choice[i]]);
            }

            var kept = CornerClaims.Covering(active);
            var total = new Score(0, 0);
            for (int i = 0; i < plans.Count; i++)
            {
                int c = choice[i];
                bool taken = plans[i].Shape.StartCorner && plans[i].Options[c].Count > 0
                    && kept.Any(k => (k.Plan == i && !k.AtEnd) || (k.Plan == prev[i] && k.AtEnd));
                total += new Score(own[i][c] + (taken ? CornerWeight : 0), 0, rounded[i][c] + (taken ? CornerWeight : 0)) + Pick(i, c);
            }

            return total;
        }

        static Score Pick(int i, int choice) => new Score(0, i < 62 ? (long)choice << i : 0);

        (Score score, int[] choice) Chain(int fixedFirst, bool wraps)
        {
            int n = plans.Count;
            var value = new Score[n][];
            var from = new int[n][];
            for (int i = 0; i < n; i++)
            {
                value[i] = new Score[Options(i)];
                from[i] = new int[Options(i)];
                for (int c = 0; c < Options(i); c++)
                {
                    value[i][c] = Score.None;
                    if (i == 0)
                    {
                        if (!wraps || c == fixedFirst)
                        {
                            value[0][c] = new Score(own[0][c] + (wraps ? 0 : StartCornerScore(0, c, 0)), 0) + Pick(0, c);
                        }

                        continue;
                    }

                    for (int pc = 0; pc < Options(i - 1); pc++)
                    {
                        if (value[i - 1][pc].IsNone)
                        {
                            continue;
                        }

                        var v = value[i - 1][pc] + new Score(own[i][c] + StartCornerScore(i, c, prev[i] == i - 1 ? pc : 0), 0) + Pick(i, c);
                        if (v.Beats(value[i][c]))
                        {
                            value[i][c] = v;
                            from[i][c] = pc;
                        }
                    }
                }
            }

            int last = -1;
            var total = Score.None;
            for (int c = 0; c < Options(n - 1); c++)
            {
                if (value[n - 1][c].IsNone)
                {
                    continue;
                }

                var v = value[n - 1][c] + new Score(wraps ? StartCornerScore(0, fixedFirst, c) : 0, 0);
                if (v.Beats(total))
                {
                    total = v;
                    last = c;
                }
            }

            var choice = new int[n];
            choice[n - 1] = last;
            for (int i = n - 1; i > 0; i--)
            {
                choice[i - 1] = from[i][choice[i]];
            }

            return (total, choice);
        }

        readonly struct Score
        {
            public static readonly Score None = new Score(int.MinValue, long.MaxValue);

            readonly int points;
            readonly long order;

            public Score(int points, long order, int tier = 0)
            {
                this.points = points;
                this.order = order;
                Tier = tier;
            }

            public int Tier { get; }

            public bool IsNone => points == int.MinValue;

            public bool Beats(Score other) => other.IsNone || points > other.points || (points == other.points && order < other.order);

            public static Score operator +(Score a, Score b) => new Score(a.points + b.points, a.order + b.order, a.Tier + b.Tier);
        }
    }
}
