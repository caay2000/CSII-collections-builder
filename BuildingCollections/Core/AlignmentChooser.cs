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
        public static IEnumerable<LotStrip> Choose(List<SidePlan> plans, int sideCount, IList<string> notes)
        {
            var pick = new AlignmentChooser(plans, sideCount).Best();
            for (int i = 0; i < plans.Count; i++)
            {
                if (pick[i] == 1)
                {
                    notes?.Add($"side {plans[i].Side}: {plans[i].AlternativeNote}");
                }
            }

            return plans.SelectMany((p, i) => p.Options[pick[i]]);
        }

        const int CornerWeight = 100000;
        const int RoundedWeight = 10000;
        const int CellWeight = 10;
        const int ChangeWeight = 5000;

        readonly List<SidePlan> plans;
        readonly int[] prev;
        readonly int[][] own;
        readonly bool[][] takesStart;
        readonly bool[][] takesEnd;

        public AlignmentChooser(List<SidePlan> plans, int sideCount)
        {
            this.plans = plans;
            var bySide = plans.Select((p, i) => (p.Side, i)).ToDictionary(x => x.Side, x => x.i);
            prev = plans.Select(p => bySide.TryGetValue((p.Side - 1 + sideCount) % sideCount, out int pi) ? pi : -1).ToArray();
            own = plans.Select(p => p.Options.Select((o, c) => OwnScore(p, o, c)).ToArray()).ToArray();
            takesStart = plans.Select(p => p.Options.Select(o => o.Count > 0 && TakesCorner(o[0], false)).ToArray()).ToArray();
            takesEnd = plans.Select(p => p.Options.Select(o => o.Count > 0 && p.Shape.EndCorner && TakesCorner(o[o.Count - 1], true)).ToArray()).ToArray();
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

            int rounded = (plan.Shape.StartRounded && option[0].Grid.LeftCorner ? 1 : 0)
                + (plan.Shape.EndRounded && option[option.Count - 1].Grid.RightCorner ? 1 : 0);
            return rounded * RoundedWeight + cells * CellWeight - choice * ChangeWeight;
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

        public int[] Best()
        {
            int n = plans.Count;
            var best = new int[n];
            if (n == 0)
            {
                return best;
            }

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

            return best;
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

            public Score(int points, long order)
            {
                this.points = points;
                this.order = order;
            }

            public bool IsNone => points == int.MinValue;

            public bool Beats(Score other) => other.IsNone || points > other.points || (points == other.points && order < other.order);

            public static Score operator +(Score a, Score b) => new Score(a.points + b.points, a.order + b.order);
        }
    }
}
