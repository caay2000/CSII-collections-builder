using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class PolygonClipTests
    {
        const double Tolerance = 1.0;

        static List<Pt> L(params double[] xy)
        {
            var r = new List<Pt>();
            for (int i = 0; i < xy.Length; i += 2) r.Add(new Pt(xy[i], xy[i + 1]));
            return r;
        }

        static BuildingInfo B(string name, int w, int d, CornerKind corner = CornerKind.Front) =>
            new BuildingInfo { PrefabName = name, LotWidth = w, LotDepth = d, Corner = corner };

        static readonly List<BuildingInfo> Plain = new List<BuildingInfo> { B("a1x1", 1, 1), B("a1x2", 1, 2), B("a2x2", 2, 2), B("a3x2", 3, 2) };

        static readonly List<BuildingInfo> Corners = new List<BuildingInfo>
        {
            B("cL", 1, 2, CornerKind.Front | CornerKind.Left), B("cR", 1, 2, CornerKind.Front | CornerKind.Right),
        };

        static List<List<Pt>> Rect(double w, double h) => new List<List<Pt>> { L(0, 0, w, 0), L(w, 0, w, h), L(w, h, 0, h), L(0, h, 0, 0) };

        static PreviewResult PlanClipped(List<List<Pt>> sides, List<Pt> clip, float overlap, int seed)
        {
            var strips = VirtualLots.Build(sides, new Quad[0], clip: clip, curveOverlap: overlap > FootprintMask.StrictOverlap ? overlap : 0);
            var blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
                Label = $"strip side {s.SideIndex} segment {s.ChunkIndex + 1}",
            }).ToList();
            return PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Outline = clip, Plain = Plain, Corners = Corners, Overlap = overlap, Density = 100, Seed = seed,
            });
        }

        public static IEnumerable<object[]> Cases()
        {
            var clips = new Dictionary<string, List<Pt>>
            {
                ["half"] = L(-20, -20, 61, -20, 61, 96, -20, 96),
                ["slanted"] = L(-20, -20, 45, -20, 85, 96, -20, 96),
                ["notch"] = L(-20, -20, 143, -20, 143, 30, 70, 30, 70, 96, -20, 96),
            };
            foreach (var clip in clips)
            {
                foreach (var overlap in new[] { 0.5f, 1f, 2f })
                {
                    foreach (var seed in new[] { 1, 7, 42 })
                    {
                        yield return new object[] { clip.Key, clip.Value, overlap, seed };
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Clipped_selection_keeps_every_building_inside_the_clip_and_the_block(string name, List<Pt> clip, float overlap, int seed)
        {
            var sides = Rect(123, 76);
            var face = VirtualLots.Outline(sides);
            var r = PlanClipped(sides, clip, overlap, seed);
            Assert.NotEmpty(r.Lots);
            foreach (var lot in r.Lots)
            {
                var q = lot.Footprint(Tolerance);
                foreach (var c in q.P)
                {
                    Assert.True(Polygon.Contains(clip, c), $"{name}: {lot.Building.PrefabName} corner ({c.X:F1}, {c.Y:F1}) outside the clip");
                    Assert.True(Polygon.Contains(face, c), $"{name}: {lot.Building.PrefabName} corner ({c.X:F1}, {c.Y:F1}) outside the block");
                }
            }
        }
    }
}
