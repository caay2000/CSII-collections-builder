using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public sealed class Gap
    {
        public Pt From;
        public Pt To;
        public double Length;
        public bool Wedge;
        public bool Sliver => !Wedge && Length <= EndGapCloser.MaxSpreadSliver;
    }

    public sealed class Run
    {
        public Scenario Scenario;
        public float Overlap;
        public List<LotStrip> Strips;
        public List<Pt> Outline;
        public PreviewResult Result;
        public List<Gap> Gaps;
        public List<CornerResult> Corners;

        public int CornersOk => Corners.Count(c => c.State == CornerState.Corner);

        public IEnumerable<Gap> RealGaps => Gaps.Where(g => !g.Wedge && !g.Sliver);

        public IEnumerable<Gap> Slivers => Gaps.Where(g => g.Sliver);

        public double GapMeters => RealGaps.Sum(g => g.Length);

        public double WedgeMeters => Gaps.Where(g => g.Wedge).Sum(g => g.Length);

        public double MaxGap => RealGaps.Select(g => g.Length).DefaultIfEmpty(0).Max();

        public double FilledShare => Result.FrontCells.Count == 0 ? 0 : (double)Result.FrontFilled / Result.FrontCells.Count;
    }

    public static class Simulation
    {
        static readonly IReadOnlyList<BuildingInfo> Common = new List<BuildingInfo>
        {
            B("1x2", 1, 2), B("2x2", 2, 2), B("3x2", 3, 2), B("2x3", 2, 3), B("3x3", 3, 3),
        };

        public static readonly IReadOnlyDictionary<string, IReadOnlyList<BuildingInfo>> Catalogs = new Dictionary<string, IReadOnlyList<BuildingInfo>>
        {
            ["game"] = Common,
            ["with-2x1"] = Common.Append(B("2x1", 2, 1)).ToList(),
            ["full"] = Common.Append(B("2x1", 2, 1)).Append(B("1x1", 1, 1)).ToList(),
        };

        public const string DefaultCatalog = "game";

        public static IReadOnlyList<BuildingInfo> Plain { get; set; } = Catalogs[DefaultCatalog];

        public static readonly IReadOnlyList<BuildingInfo> Corners = new List<BuildingInfo>
        {
            B("corner 1x1 L", 1, 1, CornerKind.Front | CornerKind.Left), B("corner 1x1 R", 1, 1, CornerKind.Front | CornerKind.Right),
            B("corner 1x2 L", 1, 2, CornerKind.Front | CornerKind.Left), B("corner 1x2 R", 1, 2, CornerKind.Front | CornerKind.Right),
            B("corner 2x2 L", 2, 2, CornerKind.Front | CornerKind.Left), B("corner 2x2 R", 2, 2, CornerKind.Front | CornerKind.Right),
        };

        static BuildingInfo B(string name, int w, int d, CornerKind corner = CornerKind.Front) =>
            new BuildingInfo { PrefabName = name, LotWidth = w, LotDepth = d, Corner = corner };

        static readonly ConcurrentDictionary<(Scenario, double), Lazy<(List<LotStrip>, List<string>)>> StripCache =
            new ConcurrentDictionary<(Scenario, double), Lazy<(List<LotStrip>, List<string>)>>();

        static (List<LotStrip> strips, List<string> notes) StripsFor(Scenario scenario, double curve) =>
            StripCache.GetOrAdd((scenario, curve), key => new Lazy<(List<LotStrip>, List<string>)>(() =>
            {
                var notes = new List<string>();
                var strips = VirtualLots.Build(scenario.Sides, scenario.Existing, frontage: scenario.Frontage, notes: notes, curveOverlap: curve);
                return (strips, notes);
            })).Value;

        public static Run Simulate(Scenario scenario, float overlap, int seed)
        {
            var curve = overlap > FootprintMask.StrictOverlap ? overlap : 0;
            var (strips, built) = StripsFor(scenario, curve);
            var notes = new List<string>(built);
            var outline = VirtualLots.Outline(scenario.Sides);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s),
                Grid = s.Grid,
                Strip = s,
                Priority = s.Width * 100,
                IsVirtual = true,
                Label = $"strip side {s.SideIndex} segment {s.ChunkIndex + 1}",
            }).ToList();
            var result = PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Existing = scenario.Existing, Outline = outline, Plain = Plain, Corners = Corners, Overlap = overlap, Seed = seed,
            });
            result.Log.InsertRange(0, notes);
            var gaps = FindGaps(strips, result);
            var corners = CornerCheck.Evaluate(scenario.Sides, result.Lots);
            MarkWedges(gaps, corners);
            return new Run { Scenario = scenario, Overlap = overlap, Strips = strips, Outline = outline, Result = result, Gaps = gaps, Corners = corners };
        }

        const double WedgeDepth = 16;
        const double WedgeSlack = 1.5;
        const double WedgeFactor = 1.25;
        const double WedgeReach = 3;

        static void MarkWedges(List<Gap> gaps, List<CornerResult> corners)
        {
            foreach (var g in gaps)
            {
                double allowed = 0;
                foreach (var c in corners.Where(c => Math.Abs(c.Angle - 90) > 5 && c.Angle < 175))
                {
                    if (Dist(g.From, c.At) <= WedgeReach || Dist(g.To, c.At) <= WedgeReach)
                    {
                        allowed += WedgeFactor * WedgeDepth / Math.Abs(Math.Tan(c.Angle * Math.PI / 180)) + WedgeSlack;
                    }
                }

                g.Wedge = allowed > 0 && g.Length <= allowed;
            }
        }

        static double Dist(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));

        static List<Gap> FindGaps(List<LotStrip> strips, PreviewResult result) =>
            SidewalkGaps.Find(strips, result.Lots).Select(g => new Gap { From = g.From, To = g.To, Length = g.Length }).ToList();
    }
}
