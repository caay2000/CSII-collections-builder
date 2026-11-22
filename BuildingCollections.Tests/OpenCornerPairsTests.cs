using System;
using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Xunit;

namespace BuildingCollections.Tests
{
    public class OpenCornerPairsTests
    {
        static readonly IReadOnlyList<BuildingInfo> Catalog = new[]
        {
            new BuildingInfo { PrefabName = "1x2", LotWidth = 1, LotDepth = 2 },
            new BuildingInfo { PrefabName = "2x2", LotWidth = 2, LotDepth = 2 },
            new BuildingInfo { PrefabName = "3x2", LotWidth = 3, LotDepth = 2 },
        };

        static List<List<Pt>> Block(double angle)
        {
            const double length = 160;
            double radians = angle * Math.PI / 180;
            var a = new Pt(0, 0);
            var b = new Pt(length, 0);
            var c = new Pt(length - length * Math.Cos(radians), length * Math.Sin(radians));
            var d = new Pt(-40, c.Y);
            var points = new[] { a, b, c, d };
            return points.Select((p, i) => new List<Pt> { p, points[(i + 1) % points.Length] }).ToList();
        }

        static PreviewResult Plan(List<List<Pt>> sides, float overlap, out List<PlanBlock> blocks)
        {
            var strips = VirtualLots.Build(sides, new List<Quad>(), curveOverlap: overlap > FootprintMask.StrictOverlap ? overlap : 0);
            blocks = strips.Where(s => s.Grid.FreeCount() > 0).Select(s => new PlanBlock
            {
                Frame = CellFrame.FromStrip(s), Grid = s.Grid, Strip = s, Priority = s.Width * 100, IsVirtual = true,
            }).ToList();
            return PreviewPlanner.Plan(new PreviewRequest
            {
                Blocks = blocks, Existing = new List<Quad>(), Outline = VirtualLots.Outline(sides), Plain = Catalog, Corners = new List<BuildingInfo>(), Overlap = overlap, Seed = 3,
            });
        }

        [Theory]
        [InlineData(110)]
        [InlineData(120)]
        public void An_open_corner_gets_the_same_model_on_both_streets_with_the_lot_corners_together(double angle)
        {
            var result = Plan(Block(angle), 0.5f, out _);

            var pairs = result.Lots.Where(l => l.Corner).GroupBy(l => l.Building).Where(g => g.Count() >= 2).ToList();
            Assert.NotEmpty(pairs);
            Assert.Contains(result.Log, l => l.Contains("on both streets"));
            var fronts = result.Lots.Where(l => l.Corner).SelectMany(l => l.Footprint(0).P.Take(2).Select(p => (l, p))).ToList();
            Assert.Contains(fronts, x => fronts.Any(y => y.l != x.l && y.l.Building == x.l.Building && Distance(x.p, y.p) < 0.01));
        }

        [Fact]
        public void Without_overlap_no_corner_is_paired()
        {
            var result = Plan(Block(120), FootprintMask.StrictOverlap, out _);

            Assert.DoesNotContain(result.Log, l => l.Contains("on both streets"));
        }

        static double Distance(Pt a, Pt b) => Math.Sqrt(Pt.Dot(a - b, a - b));
    }
}
