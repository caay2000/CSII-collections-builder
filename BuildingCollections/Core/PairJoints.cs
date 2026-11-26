using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildingCollections.Core
{
    public sealed class PairJoints
    {
        const double Joined = 0.3;
        const double JointMargin = 0.01;
        const double InsideInset = 0.1;
        const int MaxExtraLots = 1;
        const int MaxNewLots = 3;
        const double BackToBackCos = -0.5;

        readonly List<PlannedLot> lots;
        readonly IReadOnlyList<Quad> existing;
        readonly IReadOnlyList<Pt> outline;
        readonly IReadOnlyList<BuildingInfo> plain;
        readonly float overlap;
        readonly Random random;
        readonly IList<string> log;

        PairJoints(List<PlannedLot> lots, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline, IReadOnlyList<BuildingInfo> plain, float overlap, int seed, IList<string> log)
        {
            this.lots = lots;
            this.existing = existing;
            this.outline = outline;
            this.plain = plain;
            this.overlap = overlap;
            this.log = log;
            random = new Random(seed ^ 0x2f6b9c1d);
        }

        public static void Apply(List<PlannedLot> lots, IReadOnlyList<Quad> existing, IReadOnlyList<Pt> outline, IReadOnlyList<BuildingInfo> plain, float overlap, int seed, IList<string> log)
        {
            var joints = new PairJoints(lots, existing, outline, plain, overlap, seed, log);
            foreach (var pair in lots.Where(l => l.Paired && l.Owner.Strip != null).ToList())
            {
                joints.Join(pair);
            }
        }

        sealed class Row
        {
            public PlannedLot Pair;
            public LotStrip Strip;
            public int Inward;
            public double Inner;
            public List<PlannedLot> Chain;
            public PlannedLot Anchor;
            public List<PlannedLot> Rest;
            public bool EndsInPair;

            public double Near(PlannedLot lot)
            {
                var (from, to) = Span(lot, Strip);
                return Inward > 0 ? from : to;
            }

            public double Far(PlannedLot lot)
            {
                var (from, to) = Span(lot, Strip);
                return Inward > 0 ? to : from;
            }

            public double Distance(PlannedLot lot) => (Near(lot) - Inner) * Inward;

            public double Length => (Near(Anchor) - Inner) * Inward;

            public double At(double u) => Inner + Inward * u;
        }

        void Join(PlannedLot pair)
        {
            var row = RowFrom(pair);
            string side = $"  open corner pair {pair.Building.PrefabName} (side {row.Strip.SideIndex})";
            if (row.Rest.Count == 0)
            {
                log.Add($"{side}: empty street beside it, {Extend(row)} buildings added from the pair inwards.");
                return;
            }

            double gap = row.Distance(row.Rest[0]);
            if (gap <= Joined)
            {
                return;
            }

            string how = row.Anchor != null && Respace(row) ? "closed by spreading the row up to the next corner"
                : !row.EndsInPair && FarEndIsACorner(row) && Slide(row, gap) ? "closed by sliding the rest of the street towards it"
                : "left open";
            log.Add($"{side}: {gap:F1} m to the next building {how}.");
        }

        Row RowFrom(PlannedLot pair)
        {
            var strip = pair.Owner.Strip;
            var (from, to) = Span(pair, strip);
            int inward = (from + to) / 2 > strip.ChunkLength / 2 ? -1 : 1;
            var row = new Row { Pair = pair, Strip = strip, Inward = inward, Inner = inward > 0 ? to : from, Chain = new List<PlannedLot>() };
            var ahead = lots
                .Where(l => l.Owner == pair.Owner && l != pair)
                .Where(l => row.Distance(l) > -overlap - JointMargin)
                .OrderBy(row.Distance)
                .ToList();
            row.Rest = ahead.TakeWhile(l => !l.Paired).ToList();
            row.EndsInPair = row.Rest.Count < ahead.Count;
            foreach (var lot in ahead)
            {
                bool adjacent = row.Chain.Count == 0 || Math.Abs((row.Near(lot) - row.Far(row.Chain[row.Chain.Count - 1])) * inward) <= overlap + JointMargin;
                if (lot.Corner || !adjacent)
                {
                    row.Anchor = lot;
                    break;
                }

                row.Chain.Add(lot);
            }

            return row;
        }

        static bool FarEndIsACorner(Row row) => row.Inward > 0 ? row.Strip.ChunkIndex == row.Strip.ChunkCount - 1 : row.Strip.ChunkIndex == 0;

        bool Slide(Row row, double gap)
        {
            var moved = row.Rest.Select(l => Moved(l, row, -gap)).ToList();
            var others = lots.Where(l => !row.Rest.Contains(l)).ToList();
            if (!moved.Select((l, i) => Fits(l, others, row.Rest[i])).All(ok => ok))
            {
                return false;
            }

            for (int i = 0; i < row.Rest.Count; i++)
            {
                row.Rest[i].Shift = moved[i].Shift;
                row.Rest[i].MinX = moved[i].MinX;
                row.Rest[i].MaxX = moved[i].MaxX;
            }

            return true;
        }

        bool Respace(Row row)
        {
            double length = row.Length;
            var others = lots.Where(l => !row.Chain.Contains(l)).ToList();
            foreach (var widths in WidthRows(row, length))
            {
                var placed = Place(row, widths, length, others);
                if (placed != null)
                {
                    lots.RemoveAll(row.Chain.Contains);
                    lots.AddRange(placed);
                    return true;
                }
            }

            return false;
        }

        IEnumerable<List<int>> WidthRows(Row row, double length)
        {
            var current = row.Chain.Select(l => l.Width).ToList();
            var widths = plain.Select(b => b.LotWidth).Distinct().OrderBy(w => w).ToList();
            var rows = new List<List<int>> { current };
            rows.AddRange(widths.Select(w => current.Append(w).ToList()));
            if (current.Count == 0)
            {
                var pairs = widths.SelectMany(a => widths.Select(b => new List<int> { a, b })).ToList();
                rows.AddRange(pairs);
                rows.AddRange(pairs.SelectMany(p => widths.Select(c => p.Append(c).ToList())));
            }

            for (int i = 0; i < current.Count; i++)
            {
                int at = i;
                rows.AddRange(widths.Where(w => w != current[at]).Select(w => current.Select((c, j) => j == at ? w : c).ToList()));
                if (current.Count > 1)
                {
                    rows.Add(current.Where((c, j) => j != at).ToList());
                }
            }

            return rows
                .Where(r => r.Count > 0 && r.Count <= Math.Max(current.Count + MaxExtraLots, MaxNewLots))
                .Select(r => (r, excess: r.Sum() * CellFrame.Size - length))
                .Where(o => o.excess >= -Joined && o.excess <= (overlap - JointMargin) * (o.r.Count + 1))
                .OrderBy(o => Changes(o.r, current))
                .ThenBy(o => Math.Abs(o.excess))
                .Select(o => o.r);
        }

        static int Changes(List<int> row, List<int> current) =>
            Math.Abs(row.Count - current.Count) + row.Zip(current, (a, b) => a == b ? 0 : 1).Sum();

        List<PlannedLot> Place(Row row, List<int> widths, double length, List<PlannedLot> others)
        {
            double joint = Math.Max(0, (widths.Sum() * CellFrame.Size - length) / (widths.Count + 1));
            double u = -joint;
            var placed = new List<PlannedLot>();
            for (int i = 0; i < widths.Count; i++)
            {
                double w = widths[i] * CellFrame.Size;
                var template = i < row.Chain.Count && row.Chain[i].Width == widths[i] ? row.Chain[i] : null;
                var lot = Models(row, widths[i], template)
                    .Select(model => At(row, model, template, Math.Min(row.At(u), row.At(u + w))))
                    .FirstOrDefault(l => Fits(l, others.Concat(placed).ToList()));
                if (lot == null)
                {
                    return null;
                }

                placed.Add(lot);
                u += w - joint;
            }

            return placed;
        }

        IEnumerable<BuildingInfo> Models(Row row, int width, PlannedLot template)
        {
            if (template != null)
            {
                yield return template.Building;
            }

            int group = row.Pair.Building.Group;
            int depth = row.Chain.Count > 0 ? row.Chain.Max(l => l.Depth) : row.Pair.Owner.Grid.Depth;
            var options = plain.Where(b => b.LotWidth == width && b.Group == group && b.LotDepth <= depth).ToList();
            foreach (var model in options.OrderBy(b => b.LotDepth).ThenBy(_ => random.Next()))
            {
                yield return model;
            }
        }

        int Extend(Row row)
        {
            var others = lots.ToList();
            var widths = plain.Select(b => b.LotWidth).Distinct().OrderByDescending(w => w).ToList();
            double room = row.Inward > 0 ? row.Strip.ChunkLength - row.Inner : row.Inner;
            double u = 0;
            int added = 0;
            while (u + CellFrame.Size <= room + overlap)
            {
                var lot = widths
                    .SelectMany(w => Models(row, w, null).Select(model => At(row, model, null, Math.Min(row.At(u), row.At(u + w * CellFrame.Size)))))
                    .FirstOrDefault(l => Fits(l, others));
                if (lot == null)
                {
                    break;
                }

                lots.Add(lot);
                others.Add(lot);
                added++;
                u += lot.Width * CellFrame.Size;
            }

            return added;
        }

        static PlannedLot At(Row row, BuildingInfo model, PlannedLot template, double along)
        {
            var block = row.Pair.Owner;
            var frame = block.Frame;
            var point = row.Strip.ChunkStart + row.Strip.T * along;
            double sign = Pt.Dot(frame.AxisX, row.Strip.T) > 0 ? 1 : -1;
            double x = Pt.Dot(point - frame.Origin, frame.AxisX) + CellFrame.Size / 2 - (sign > 0 ? 0 : model.LotWidth * CellFrame.Size);
            int minX = (int)Math.Round(x / CellFrame.Size);
            int minY = template?.MinY ?? block.AreaY;
            return new PlannedLot
            {
                Building = model,
                Owner = block,
                MinX = minX,
                MinY = minY,
                MaxX = minX + model.LotWidth,
                MaxY = minY + model.LotDepth,
                Shift = (float)(x - minX * CellFrame.Size),
            };
        }

        static PlannedLot Moved(PlannedLot lot, Row row, double move)
        {
            var (from, to) = Span(lot, row.Strip);
            var moved = At(row, lot.Building, lot, Math.Min(from, to) + row.Inward * move);
            moved.MinY = lot.MinY;
            moved.MaxY = lot.MaxY;
            return moved;
        }

        bool Fits(PlannedLot lot, List<PlannedLot> others, PlannedLot before = null)
        {
            var strict = lot.Footprint(FootprintMask.StrictOverlap);
            if (existing.Any(q => Quad.Touch(q, strict)) || !lot.Footprint(InsideInset).P.All(p => Polygon.Contains(outline, p)))
            {
                return false;
            }

            var loose = lot.Footprint(overlap);
            var wasLoose = before?.Footprint(overlap);
            return others
                .Where(other => Quad.Touch(other.Footprint(0), loose))
                .All(other => (wasLoose != null && Quad.Touch(other.Footprint(0), wasLoose)) || BacksOnly(lot, other, loose));
        }

        bool BacksOnly(PlannedLot lot, PlannedLot other, Quad loose)
        {
            if (other.Owner == lot.Owner || SameSide(other.Owner, lot.Owner) || !(Facing(other.Owner, lot.Owner) || VirtualLots.BacksMayOverlap(other.Owner.Strip, lot.Owner.Strip)))
            {
                return false;
            }

            var front = lot.Footprint(lot.MinX, lot.MinY, lot.MaxX, lot.MinY + 1, lot.Shift, overlap);
            return !Quad.Touch(other.Footprint(other.MinX, other.MinY, other.MaxX, other.MinY + 1, other.Shift, 0), loose)
                && !Quad.Touch(other.Footprint(0), front);
        }

        static bool Facing(PlanBlock a, PlanBlock b) => Pt.Dot(a.Frame.AxisY, b.Frame.AxisY) < BackToBackCos;

        static bool SameSide(PlanBlock a, PlanBlock b) =>
            a.Strip != null && b.Strip != null && a.Strip.BlockIndex == b.Strip.BlockIndex && a.Strip.SideIndex == b.Strip.SideIndex;

        static (double From, double To) Span(PlannedLot lot, LotStrip strip)
        {
            var along = lot.Footprint(0).P.Select(p => Pt.Dot(p - strip.ChunkStart, strip.T)).ToList();
            return (along.Min(), along.Max());
        }
    }
}
