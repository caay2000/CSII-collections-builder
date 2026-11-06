using System;
using System.Collections.Generic;
using System.Linq;
using static BuildingCollections.Core.PolylineMath;

namespace BuildingCollections.Core
{
    internal sealed class StripSettings
    {
        public List<Pt> Outline;
        public IReadOnlyList<Quad> Obstacles;
        public IReadOnlyList<Pt> Clip;
        public int MaxDepth;
        public int BlockIndex;
        public IList<string> Notes;
        public double CurveTolerance;
        public double CurveOverlap;
    }

    internal sealed class SideGeometry
    {
        public readonly List<Pt> Points;
        public readonly List<(int from, int to)> AllChunks;
        public readonly List<(int from, int to)> Chunks;

        public SideGeometry(List<Pt> points, List<(int from, int to)> allChunks)
        {
            Points = points;
            AllChunks = allChunks;
            Chunks = allChunks.Where(c => CellsIn(Length(c)) >= 1).ToList();
        }

        public (Pt a, Pt b) Ends((int from, int to) chunk) => (Points[chunk.from], Points[chunk.to]);

        public double Length((int from, int to) chunk) => Dist(Points[chunk.from], Points[chunk.to]);

        public static int CellsIn(double length) => (int)Math.Floor((length + 0.5) / CellFrame.Size);
    }

    internal sealed class SideStripBuilder
    {
        const double CornerFitStep = 0.25;
        const double MaxCurveTrim = 4;

        readonly Side side;
        readonly int sideIndex;
        readonly StripSettings settings;
        readonly List<Pt> basePoints;
        readonly bool curved;
        readonly Pt? prevDir;
        readonly Pt? nextDir;
        readonly SideGeometry initial;

        SideStripBuilder(Side side, int sideIndex, StripSettings settings, List<Pt> points, List<(int from, int to)> raw, Pt? prevDir, Pt? nextDir)
        {
            this.side = side;
            this.sideIndex = sideIndex;
            this.settings = settings;
            this.prevDir = prevDir;
            this.nextDir = nextDir;
            basePoints = points;
            curved = raw.Count > 1;
            initial = curved
                ? Snap(CurveSnapper.SnapChunks(points, settings.CurveTolerance, settings.CurveOverlap, settings.Notes, sideIndex, StartWedge, EndWedge))
                : new SideGeometry(points, raw);
        }

        const double CornerDepth = 2 * CellFrame.Size;
        const double MaxSquareDeviationDeg = 10;

        double? StartWedge => side.StartCorner && prevDir.HasValue ? Wedge(prevDir.Value, basePoints[1] - basePoints[0]) : null;

        double? EndWedge => side.EndCorner && nextDir.HasValue ? Wedge(basePoints[basePoints.Count - 1] - basePoints[basePoints.Count - 2], nextDir.Value) : null;

        static double? Wedge(Pt dIn, Pt dOut)
        {
            double sin = Math.Abs(Pt.Cross(Unit(dIn), Unit(dOut)));
            double cos = Math.Abs(Pt.Dot(Unit(dIn), Unit(dOut)));
            return cos <= sin * Math.Tan(MaxSquareDeviationDeg * Math.PI / 180) ? CornerDepth * cos / sin : (double?)null;
        }

        static SideGeometry Snap((List<Pt> pts, List<(int from, int to)> chunks) snapped) => new SideGeometry(snapped.pts, snapped.chunks);

        public static SideStripBuilder For(List<Side> merged, List<List<(int from, int to)>> mergedChunks, int sideIndex, StripSettings settings)
        {
            var side = merged[sideIndex];
            var raw = new List<(int from, int to)>(mergedChunks[sideIndex]);
            var pts = new List<Pt>(side.Points);
            Pt? prevDir = null, nextDir = null;
            if (raw.Count > 0 && side.StartCorner)
            {
                int pi = (sideIndex - 1 + merged.Count) % merged.Count;
                var pc = mergedChunks[pi];
                if (pc.Count > 0)
                {
                    var (pf, pt) = pc[pc.Count - 1];
                    prevDir = merged[pi].Points[pt] - merged[pi].Points[pf];
                    var e = CornerGeometry.ExtendToCorner(pts[raw[0].to], pts[0], merged[pi].Points[pf], merged[pi].Points[pt]);
                    if (Dist(e, pts[0]) > 1e-6)
                    {
                        pts.Insert(0, e);
                        raw = raw.Select((c, i) => (i == 0 ? 0 : c.from + 1, c.to + 1)).ToList();
                    }
                }
            }

            if (raw.Count > 0 && side.EndCorner)
            {
                int ni = (sideIndex + 1) % merged.Count;
                var nc = mergedChunks[ni];
                if (nc.Count > 0)
                {
                    var (nf, nt) = nc[0];
                    nextDir = merged[ni].Points[nt] - merged[ni].Points[nf];
                    var last = raw[raw.Count - 1];
                    var e = CornerGeometry.ExtendToCorner(pts[last.from], pts[pts.Count - 1], merged[ni].Points[nf], merged[ni].Points[nt]);
                    if (Dist(e, pts[pts.Count - 1]) > 1e-6)
                    {
                        pts.Add(e);
                        raw[raw.Count - 1] = (last.from, pts.Count - 1);
                    }
                }
            }

            return new SideStripBuilder(side, sideIndex, settings, pts, raw, prevDir, nextDir);
        }

        public SidePlan Plan()
        {
            Describe();
            var fitted = Enumerable.Range(0, initial.Chunks.Count).Select(k => FittedStrip(initial, k, false)).ToList();
            var options = new List<List<LotStrip>> { fitted };
            string alternative = null;
            if (initial.Chunks.Count == 1 && side.StartCorner && side.EndCorner)
            {
                options.Add(new List<LotStrip> { FittedStrip(initial, 0, true) });
                alternative = "aligned to the end corner.";
            }
            else if (curved)
            {
                double trimStart = side.StartCorner ? CurveTrim(false) : 0;
                double trimEnd = side.EndCorner ? CurveTrim(true) : 0;
                var trimmed = trimStart + trimEnd > 0 ? Resnap(trimStart, trimEnd) : null;
                if (trimmed != null && trimmed.Chunks.Count > 0)
                {
                    options.Add(Enumerable.Range(0, trimmed.Chunks.Count).Select(k => MakeStrip(trimmed, k, false, 0, 0)).ToList());
                    alternative = "using the trimmed curve so the corner building fits.";
                    settings.Notes?.Add($"side {sideIndex}: curve trimmed {trimStart:F2} m at the start and {trimEnd:F2} m at the end in case the corner building needs it.");
                }
            }

            return new SidePlan { Side = sideIndex, Shape = side, Options = options, AlternativeNote = alternative };
        }

        void Describe()
        {
            if (settings.Notes == null)
            {
                return;
            }

            var all = initial.AllChunks;
            var parts = new List<string>();
            for (int c = 0; c < all.Count; c++)
            {
                string turnNext = "";
                if (c + 1 < all.Count)
                {
                    var d1 = initial.Points[all[c].to] - initial.Points[all[c].from];
                    var d2 = initial.Points[all[c + 1].to] - initial.Points[all[c + 1].from];
                    turnNext = $", turns {Angle(d1, d2):F0}°";
                }

                parts.Add($"{initial.Length(all[c]):F1} m{(initial.Chunks.Contains(all[c]) ? "" : " (DISCARDED: < 1 cell)")}{turnNext}");
            }

            var p0 = side.Points[0];
            var p1 = side.Points[side.Points.Count - 1];
            settings.Notes.Add($"side {sideIndex}: {side.Points.Count} points, length {Length(side.Points):F1} m, from ({p0.X:F1},{p0.Y:F1}) to ({p1.X:F1},{p1.Y:F1}), start corner={side.StartCorner} end corner={side.EndCorner}; straight segments: {string.Join("; ", parts)}");
        }

        SideGeometry Resnap(double trimStart, double trimEnd) =>
            Snap(CurveSnapper.SnapChunks(CurveSnapper.Trim(basePoints, trimStart, trimEnd), settings.CurveTolerance, settings.CurveOverlap, null, sideIndex, StartWedge, EndWedge));

        LotStrip MakeStrip(SideGeometry geo, int k, bool alignEnd, double extraStart, double extraEnd)
        {
            var chunk = geo.Chunks[k];
            var (a, b) = geo.Ends(chunk);
            var d = b - a;
            double len = Math.Sqrt(Pt.Dot(d, d));
            int width = SideGeometry.CellsIn(len);

            var t = new Pt(d.X / len, d.Y / len);
            bool first = k == 0, last = k == geo.Chunks.Count - 1;

            double sStart = first && side.StartCorner && prevDir.HasValue ? Math.Max(CornerGeometry.AcuteSetback(prevDir.Value, t), extraStart) : 0;
            double sEnd = last && side.EndCorner && nextDir.HasValue ? Math.Max(CornerGeometry.AcuteSetback(t, nextDir.Value), extraEnd) : 0;
            if (sStart + sEnd > 0 && SideGeometry.CellsIn(len - sStart - sEnd) >= 1)
            {
                width = SideGeometry.CellsIn(len - sStart - sEnd);
            }
            else
            {
                sStart = sEnd = 0;
            }

            double slack = Math.Max(0, len - sStart - sEnd - width * CellFrame.Size);
            bool toStart = first && side.StartCorner && !(alignEnd && last && side.EndCorner);
            double offset = sStart + (toStart ? 0 : last && side.EndCorner ? slack : first ? 0 : last ? slack : slack / 2);
            double inward = Inward(geo, chunk, a, t, offset, width);

            var strip = new LotStrip
            {
                P0 = LotStrip.Add(a, t, offset, new Pt(-t.Y, t.X), inward > 0 ? inward + 0.1 : 0), T = t, Width = width, Depth = settings.MaxDepth,
                ChunkStart = a, ChunkLength = len, Offset = offset, Slack = slack, Inward = inward,
                BlockIndex = settings.BlockIndex, SideIndex = sideIndex, ChunkIndex = k, ChunkCount = geo.Chunks.Count,
            };
            var grid = PlanGrid.AllFree(width, settings.MaxDepth, false, false);
            grid.LeftCorner = first && side.StartCorner && offset - sStart < 1;
            grid.RightCorner = last && side.EndCorner && len - sEnd - (offset + width * CellFrame.Size) < 1;
            MarkFree(strip, grid);
            strip.Grid = grid;
            return strip;
        }

        static double Inward(SideGeometry geo, (int from, int to) chunk, Pt a, Pt t, double offset, int width)
        {
            double inward = 0;
            for (int m = chunk.from + 1; m < chunk.to; m++)
            {
                double along = Pt.Dot(t, geo.Points[m] - a);
                if (along >= offset - 0.5 && along <= offset + width * CellFrame.Size + 0.5)
                {
                    inward = Math.Max(inward, Pt.Cross(t, geo.Points[m] - a));
                }
            }

            return inward;
        }

        void MarkFree(LotStrip strip, PlanGrid grid)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                bool open = true;
                for (int y = 0; y < grid.Depth; y++)
                {
                    open = open && (settings.Clip == null || StripCells.CellInside(strip, x, y, settings.Clip))
                        && StripCells.CellFits(strip, x, y, settings.Outline, settings.Obstacles);
                    grid.Free[x, y] = open;
                }
            }
        }

        LotStrip FittedStrip(SideGeometry geo, int k, bool alignEnd)
        {
            var strip = MakeStrip(geo, k, alignEnd, 0, 0);
            if (geo.Chunks.Count != 1)
            {
                return strip;
            }

            bool atEnd = alignEnd || (strip.Grid.RightCorner && !strip.Grid.LeftCorner);
            bool shiftable = atEnd ? nextDir.HasValue : prevDir.HasValue;
            if (!shiftable || !ReachesCorner(strip, atEnd) || StripCells.CornerCellFits(strip, atEnd, settings.Outline))
            {
                return strip;
            }

            for (double extra = CornerFitStep; extra <= 2 * CellFrame.Size; extra += CornerFitStep)
            {
                var moved = atEnd ? MakeStrip(geo, k, alignEnd, 0, extra) : MakeStrip(geo, k, alignEnd, extra, 0);
                if (!ReachesCorner(moved, atEnd))
                {
                    break;
                }

                if (StripCells.CornerCellFits(moved, atEnd, settings.Outline))
                {
                    settings.Notes?.Add($"side {sideIndex}: {(atEnd ? "end" : "start")} corner set back {extra:F2} m so the corner building fits.");
                    return moved;
                }
            }

            return strip;
        }

        static bool ReachesCorner(LotStrip strip, bool atEnd) => atEnd ? strip.Grid.RightCorner : strip.Grid.LeftCorner;

        double CurveTrim(bool atEnd)
        {
            bool Takes(SideGeometry geo)
            {
                var tip = MakeStrip(geo, atEnd ? geo.Chunks.Count - 1 : 0, false, 0, 0);
                return ReachesCorner(tip, atEnd) && StripCells.CornerCellFits(tip, atEnd, settings.Outline);
            }

            if (initial.Chunks.Count == 0 || Takes(initial))
            {
                return 0;
            }

            for (double trim = CornerFitStep; trim <= MaxCurveTrim; trim += CornerFitStep)
            {
                var geo = Resnap(atEnd ? 0 : trim, atEnd ? trim : 0);
                if (geo.Chunks.Count > 0 && Takes(geo))
                {
                    return trim;
                }
            }

            return 0;
        }
    }
}
