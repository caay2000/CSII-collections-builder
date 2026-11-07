using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildingCollections.Core;
using Colossal.Mathematics;
using Game.Buildings;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace BuildingCollections.Systems
{
    public partial class BlockBuilderToolSystem : ObjectToolBaseSystem
    {
        List<(SelectedBlock block, Pt[] cells)> GatherBlocks(float2 center, double radius, out int platter)
        {
            var result = new List<(SelectedBlock, Pt[])>();
            var platterOwners = new Dictionary<Entity, bool>();
            platter = 0;
            using (var entities = blockQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    var block = EntityManager.GetComponentData<Block>(e);
                    if (math.distancesq(block.m_Position.xz, center) > radius * radius)
                    {
                        continue;
                    }

                    var area = EntityManager.GetComponentData<ValidArea>(e).m_Area;
                    if (area.y <= area.x || area.w <= area.z)
                    {
                        continue;
                    }

                    if (IsPlatterBlock(e, platterOwners))
                    {
                        platter++;
                        continue;
                    }

                    var cells = EntityManager.GetBuffer<Cell>(e, true);
                    var centers = new List<Pt>();
                    for (int y = area.z; y < area.w; y++)
                    {
                        for (int x = area.x; x < area.y; x++)
                        {
                            if ((cells[y * block.m_Size.x + x].m_State & CellFlags.Visible) != 0)
                            {
                                var c = ZoneUtils.GetCellPosition(block, new int2(x, y));
                                centers.Add(new Pt(c.x, c.z));
                            }
                        }
                    }

                    if (centers.Count > 0)
                    {
                        result.Add((new SelectedBlock { Entity = e, Block = block, Area = area }, centers.ToArray()));
                    }
                }
            }

            return result;
        }

        void SelectAt(float3 point)
        {
            reselect = () =>
            {
                stripsOverlap = CurveOverlap(CurrentOverlap());
                SelectAtCore(point);
            };
            stripsOverlap = CurveOverlap(CurrentOverlap());
            var clock = System.Diagnostics.Stopwatch.StartNew();
            SelectAtCore(point);
            Mod.Log.Info($"[selection] time: {clock.ElapsedMilliseconds} ms.");
        }

        void SelectAtCore(float3 point)
        {
            var cfg = DevConfig.Load();

            if (cfg.UseRoadFace)
            {
                var face = TraceRoadFace(point.xz, cfg.RoadSearchRadius, out int roads, out var path);
                lastSidesFull = false;

                if (face == null)
                {
                    Mod.Log.Info($"[selection] click at ({point.x:F1}, {point.z:F1}): not enclosed by streets ({roads} road segments nearby); not selected.");
                    Say("This area isn't enclosed by roads. Use Polygon to select it.", error: true);
                    return;
                }

                double area = Polygon.Area(face);
                int maxHa = Math.Max(1, Mod.Settings?.MaxBlockHectares ?? 5);
                if (area > maxHa * 10000.0)
                {
                    Mod.Log.Info($"[selection] click at ({point.x:F1}, {point.z:F1}): block of {area / 10000:F1} ha, over the {maxHa} ha limit; not selected.");
                    Say($"This block is {area / 10000:F1} ha, over the {maxHa} ha limit (Options). Use Polygon, or raise the limit.", error: true);
                    return;
                }

                if (face != null && cfg.UseVirtualLots && SelectVirtual(face, path, $"street block ({path.Count} road segments), click at ({point.x:F1}, {point.z:F1})"))
                {
                    return;
                }

                if (face != null && lastSidesFull)
                {
                    Mod.Log.Info($"[selection] click at ({point.x:F1}, {point.z:F1}): the block is already full; not selected.");
                    Say("This block is already full.", error: true);
                    return;
                }

                if (face != null)
                {
                    if (SelectInside(face, $"street block ({face.Count} points, {roads} road segments nearby), click at ({point.x:F1}, {point.z:F1})"))
                    {
                        return;
                    }

                    Mod.Log.Info("[selection] The street block contains no zoned cells; trying by cells.");
                }
                else
                {
                    Mod.Log.Info($"[selection] click at ({point.x:F1}, {point.z:F1}): not enclosed by streets ({roads} road segments nearby); trying by cells.");
                }
            }
            var blocks = GatherBlocks(point.xz, cfg.SearchRadius, out int platter);
            var click = new Pt(point.x, point.z);

            int start = -1;
            double best = double.MaxValue;
            double half = ZoneUtils.CELL_SIZE * 0.5 + Math.Max(0, cfg.ClickSnapDistance);
            for (int i = 0; i < blocks.Count; i++)
            {
                var dir = blocks[i].block.Block.m_Direction;
                var fwd = new Pt(dir.x, dir.y);
                var right = new Pt(dir.y, -dir.x);
                foreach (var c in blocks[i].cells)
                {
                    var d = click - c;
                    double a = Math.Abs(Pt.Dot(d, right)), b = Math.Abs(Pt.Dot(d, fwd));
                    if (a <= half && b <= half && a + b < best)
                    {
                        best = a + b;
                        start = i;
                    }
                }
            }

            if (start < 0)
            {
                Mod.Log.Info($"[selection] click at ({point.x:F1}, {point.z:F1}): no zone block ({blocks.Count} blocks nearby, {platter} from Platter ignored).");
                Say("Click inside a zoned block.", error: true);
                return;
            }

            var group = BlockGrouping.FloodByCells(blocks.Select(b => b.cells).ToList(), start, ZoneUtils.CELL_SIZE + cfg.BlockTouchTolerance);
            if (group.Count >= 256)
            {
                Mod.Log.Warn("[selection] Block capped at 256 zone blocks.");
            }
            SetSelection(group.Select(i => blocks[i].block), $"click at ({point.x:F1}, {point.z:F1}), tolerance {cfg.BlockTouchTolerance} m, {platter} from Platter ignored");
        }

        void PolygonClick(float3 point)
        {
            if (selection.Count > 0)
            {
                reselect = null;
                selection.Clear();
                preview.Clear();
                polygon.Clear();
                polyCurb.Clear();
                polyArc.Clear();
                areaFill.Clear();
                areaEdge.Clear();
                areaOutline = null;
            decorAreas = new List<List<Pt>>();
            decorClip = null;
                definitionsDirty = true;
                SnapHover();
                point = hoverPos;
            }

            if (polygon.Count >= 3 && math.distance(point.xz, polygon[0].xz) <= 12f)
            {
                SelectPolygon();
                return;
            }

            if (polygon.Count > 0 && math.distance(point.xz, polygon[polygon.Count - 1].xz) < 2f)
            {
                return;
            }

            polygon.Add(point);
            polyCurb.Add(hoverSnapped ? hoverHit.Curb : -1);
            polyArc.Add(hoverSnapped ? hoverHit.Arc : 0);
            Say(polygon.Count < 3 ? $"{polygon.Count} points. Keep clicking corners." : $"{polygon.Count} points. Click the first point to close.");
        }

        bool SelectPolygon()
        {
            reselect = () => SelectPolygon();
            stripsOverlap = CurveOverlap(CurrentOverlap());
            var cfg = DevConfig.Load();
            var verts = polygon.Select(p => new Pt(p.x, p.z)).ToList();
            CurbSnap.Sides(verts, polyCurb, polyArc, curbs, HugTolerance, out var sides, out var frontage);

            if (cfg.UseVirtualLots && SelectFacesInPolygon(VirtualLots.Outline(sides), $"polygon of {polygon.Count} vertices"))
            {
                return true;
            }

            if (cfg.UseVirtualLots && frontage.Any(f => f) &&
                SelectVirtualSides(sides, frontage, $"polygon of {polygon.Count} vertices ({frontage.Count(f => f)} sides with a street)"))
            {
                return true;
            }

            var poly = VirtualLots.Outline(sides);
            if (SelectInside(poly, $"polygon of {polygon.Count} vertices"))
            {
                return true;
            }

            reselect = null;
            polygon.Clear();
            polyCurb.Clear();
            polyArc.Clear();
            Say("The polygon touches no street and contains no zoned cells.", error: true);
            return false;
        }

        void SnapHover()
        {
            if (polygon.Count == 0 && (curbAge++ > 1800 || math.distance(curbCenter, hoverPos.xz) > CurbRadius / 2))
            {
                RebuildCurbs(hoverPos.xz);
            }

            hoverSnapped = CurbSnap.TrySnap(curbs, new Pt(hoverPos.x, hoverPos.z), SnapDistance, out hoverHit);
            if (hoverSnapped)
            {
                var heights = m_TerrainSystem.GetHeightData();
                var p = new float3((float)hoverHit.Point.X, 0, (float)hoverHit.Point.Y);
                p.y = TerrainUtils.SampleHeight(ref heights, p);
                hoverPos = p;
            }
        }

        void RebuildCurbs(float2 center)
        {
            curbs = new List<List<Pt>>();
            curbCenter = center;
            curbAge = 0;
            using (var entities = roadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    if (!EntityManager.HasComponent<EdgeGeometry>(e))
                    {
                        continue;
                    }

                    var g = EntityManager.GetComponentData<EdgeGeometry>(e);
                    var lo = g.m_Bounds.min.xz;
                    var hi = g.m_Bounds.max.xz;
                    if (math.distancesq(math.clamp(center, lo, hi), center) > CurbRadius * CurbRadius)
                    {
                        continue;
                    }

                    if (EntityManager.HasComponent<Game.Net.Elevation>(e))
                    {
                        var el = EntityManager.GetComponentData<Game.Net.Elevation>(e).m_Elevation;
                        if (math.abs(el.x + el.y) * 0.5f >= 3f)
                        {
                            continue;
                        }
                    }

                    foreach (bool left in new[] { true, false })
                    {
                        var pts = Sample(left ? g.m_Start.m_Left : g.m_Start.m_Right);
                        pts.AddRange(Sample(left ? g.m_End.m_Left : g.m_End.m_Right).Skip(1));
                        curbs.Add(pts);
                    }
                }
            }
        }

        List<float3> PolygonSegment(float3 a, int curbA, double arcA, float3 b, int curbB, double arcB)
        {
            if (curbA < 0 || curbA != curbB || curbA >= curbs.Count)
            {
                return new List<float3> { a, b };
            }

            var heights = m_TerrainSystem.GetHeightData();
            return CurbSnap.Path(curbs[curbA], arcA, arcB).Select(q =>
            {
                var p = new float3((float)q.X, 0, (float)q.Y);
                p.y = TerrainUtils.SampleHeight(ref heights, p);
                return p;
            }).ToList();
        }

        bool SelectInside(List<Pt> poly, string description)
        {
            double cx = poly.Average(p => p.X), cz = poly.Average(p => p.Y);
            var centroid = new float2((float)cx, (float)cz);
            double radius = poly.Max(p => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cz) * (p.Y - cz))) + 80;
            var blocks = GatherBlocks(centroid, radius, out int platter);
            var chosen = new List<SelectedBlock>();
            foreach (var (b, _) in blocks)
            {
                var cells = EntityManager.GetBuffer<Cell>(b.Entity, true);
                var include = new bool[cells.Length];
                int count = 0;
                for (int y = b.Area.z; y < b.Area.w; y++)
                {
                    for (int x = b.Area.x; x < b.Area.y; x++)
                    {
                        int i = y * b.Block.m_Size.x + x;
                        if ((cells[i].m_State & CellFlags.Visible) == 0)
                        {
                            continue;
                        }

                        var c = ZoneUtils.GetCellPosition(b.Block, new int2(x, y));
                        if (Polygon.Contains(poly, new Pt(c.x, c.z)))
                        {
                            include[i] = true;
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    b.Include = include;
                    chosen.Add(b);
                }
            }

            if (chosen.Count == 0)
            {
                return false;
            }

            SetSelection(chosen, $"{description}, {platter} from Platter ignored");
            SetArea(poly);
            return true;
        }

        List<Pt> TraceRoadFace(float2 point, double radius, out int count, out List<(Entity edge, bool forward)> path)
        {
            path = null;
            ReadRoadGraph(point, radius, out var edges, out var owners);
            count = edges.Count;
            var face = RoadFace.Trace(edges, new Pt(point.x, point.y), out var steps);
            if (face != null)
            {
                path = steps.Select(st => (owners[st.edge], st.forward)).ToList();
            }

            return face;
        }

        void ReadRoadGraph(float2 point, double radius, out List<RoadEdge> edges, out List<Entity> owners)
        {
            edges = new List<RoadEdge>();
            owners = new List<Entity>();
            var nodes = new Dictionary<Entity, int>();
            int Id(Entity n)
            {
                if (!nodes.TryGetValue(n, out int id))
                {
                    nodes[n] = id = nodes.Count;
                }

                return id;
            }

            using (var entities = roadQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    var curve = EntityManager.GetComponentData<Curve>(e);
                    var bz = curve.m_Bezier;
                    float2 lo = math.min(math.min(bz.a.xz, bz.b.xz), math.min(bz.c.xz, bz.d.xz));
                    float2 hi = math.max(math.max(bz.a.xz, bz.b.xz), math.max(bz.c.xz, bz.d.xz));
                    if (math.distancesq(math.clamp(point, lo, hi), point) > (float)(radius * radius))
                    {
                        continue;
                    }

                    if (EntityManager.HasComponent<Game.Net.Elevation>(e))
                    {
                        var el = EntityManager.GetComponentData<Game.Net.Elevation>(e).m_Elevation;
                        if (math.abs(el.x + el.y) * 0.5f >= 3f)
                        {
                            continue;
                        }
                    }

                    var edge = EntityManager.GetComponentData<Edge>(e);
                    var re = new RoadEdge { Start = Id(edge.m_Start), End = Id(edge.m_End) };
                    int n = math.clamp((int)math.ceil(curve.m_Length / 4f), 1, 64);
                    for (int k = 0; k <= n; k++)
                    {
                        var p = MathUtils.Position(bz, k / (float)n);
                        re.Points.Add(new Pt(p.x, p.z));
                    }

                    edges.Add(re);
                    owners.Add(e);
                }
            }
        }

        bool lastSidesFull;

        bool SelectVirtual(List<Pt> centerFace, List<(Entity edge, bool forward)> path, string description)
        {
            var sides = new List<List<Pt>>();
            foreach (var (edge, forward) in path)
            {
                var side = SidewalkSide(edge, forward, centerFace);
                if (side != null && side.Count >= 2)
                {
                    sides.Add(side);
                }
            }

            return SelectVirtualSides(sides, null, description);
        }

        bool SelectVirtualSides(List<List<Pt>> sides, List<bool> frontage, string description)
        {
            var outline = VirtualLots.Outline(sides);
            if (outline.Count < 3)
            {
                return false;
            }

            var obstacles = BuildingFootprints(outline);
            var notes = new List<string>();
            var strips = VirtualLots.Build(sides, obstacles, frontage: frontage, notes: notes, curveOverlap: stripsOverlap);
            Mod.Log.Info($"[selection] outline: {sides.Count} sidewalks, {outline.Count} points, {obstacles.Count} existing buildings.\n  " + string.Join("\n  ", notes)
                + "\n" + string.Join("\n", DataLog.Sides(sides, frontage).Concat(DataLog.Existing(obstacles))));
            lastSidesFull = strips.Count > 0 && strips.All(st => st.Grid.FreeCount() == 0);
            if (!SelectStrips(strips, outline, $"{description}: {sides.Count} sidewalks, {obstacles.Count} existing buildings"))
            {
                return false;
            }

            decorAreas = new List<List<Pt>> { outline };
            decorClip = null;
            return true;
        }

        bool SelectFacesInPolygon(List<Pt> clip, string description)
        {
            if (clip.Count < 3)
            {
                return false;
            }

            double minX = clip.Min(p => p.X), maxX = clip.Max(p => p.X), minZ = clip.Min(p => p.Y), maxZ = clip.Max(p => p.Y);
            var center = new float2((float)((minX + maxX) / 2), (float)((minZ + maxZ) / 2));
            double radius = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxZ - minZ) * (maxZ - minZ)) / 2 + 600;
            ReadRoadGraph(center, radius, out var edges, out var owners);
            var adjacency = RoadFace.Adjacency(edges);
            var faces = new List<List<Pt>>();
            var outlines = new List<List<Pt>>();
            var strips = new List<LotStrip>();
            var obstacles = BuildingFootprints(clip);
            double step = Math.Max(12, Math.Min(25, Math.Sqrt((maxX - minX) * (maxZ - minZ) / 1500)));
            int samples = 0, giant = 0;
            int sideCount = 0;
            for (double z = minZ + step / 2; z < maxZ && faces.Count < 60; z += step)
            {
                for (double x = minX + step / 2; x < maxX && faces.Count < 60; x += step)
                {
                    var q = new Pt(x, z);
                    if (samples >= 1500 || !Polygon.Contains(clip, q) || faces.Any(f => Polygon.Contains(f, q)))
                    {
                        continue;
                    }

                    samples++;
                    var face = RoadFace.Trace(edges, q, out var steps, adjacency: adjacency);
                    if (face == null)
                    {
                        continue;
                    }

                    faces.Add(face);
                    if (Perimeter(face) > 3000)
                    {
                        giant++;
                        continue;
                    }

                    var sides = new List<List<Pt>>();
                    foreach (var (edge, forward) in steps)
                    {
                        var side = SidewalkSide(owners[edge], forward, face);
                        if (side != null && side.Count >= 2)
                        {
                            sides.Add(side);
                        }
                    }

                    sideCount += sides.Count;
                    outlines.Add(VirtualLots.Outline(sides));
                    var notes = new List<string>();
                    strips.AddRange(VirtualLots.Build(sides, obstacles, clip: clip, notes: notes, curveOverlap: stripsOverlap, blockIndex: faces.Count));
                    Mod.Log.Info($"[selection] polygon, block {faces.Count}: {sides.Count} sidewalks.\n  " + string.Join("\n  ", notes)
                        + "\n" + string.Join("\n", DataLog.Sides(sides, null)));
                }
            }

            if (faces.Count >= 60 || samples >= 1500 || giant > 0)
            {
                Mod.Log.Warn($"[selection] polygon: {faces.Count} blocks (cap 60), {samples} samples (cap 1500), {giant} huge blocks ignored.");
            }

            if (faces.Count == 0 || !SelectStrips(strips, clip, $"{description}: {faces.Count} blocks, {sideCount} sidewalks, {obstacles.Count} existing buildings"))
            {
                return false;
            }

            decorAreas = outlines;
            decorClip = clip;
            return true;
        }

        static double Perimeter(List<Pt> poly)
        {
            double sum = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                var d = poly[(i + 1) % poly.Count] - poly[i];
                sum += Math.Sqrt(Pt.Dot(d, d));
            }

            return sum;
        }

        bool SelectStrips(List<LotStrip> strips, List<Pt> outline, string description)
        {
            var chosen = new List<SelectedBlock>();
            var heights = m_TerrainSystem.GetHeightData();
            foreach (var st in strips)
            {
                if (st.Grid.FreeCount() == 0)
                {
                    continue;
                }

                var t = new float2((float)st.T.X, (float)st.T.Y);
                var dir = new float2(t.y, -t.x);
                var front = new float2((float)st.P0.X, (float)st.P0.Y) + t * (st.Width * 4f);
                var center = front - dir * (st.Depth * 4f);
                var pos = new float3(center.x, 0, center.y);
                pos.y = TerrainUtils.SampleHeight(ref heights, pos);
                chosen.Add(new SelectedBlock
                {
                    Entity = Entity.Null,
                    Block = new Block { m_Position = pos, m_Direction = dir, m_Size = new int2(st.Width, st.Depth) },
                    Area = new int4(0, st.Width, 0, st.Depth),
                    Virtual = st.Grid,
                    Strip = st,
                });
            }

            if (chosen.Count == 0)
            {
                return false;
            }

            selection.Clear();
            selection.AddRange(chosen);
            preview.Clear();
            definitionsDirty = true;
            SetArea(outline);
            freeFrontCells = 0;
            var log = new StringBuilder($"[selection] {description}, {strips.Count} strips.\n");
            foreach (var s in chosen)
            {
                for (int x = 0; x < s.Virtual.Width; x++)
                {
                    if (s.Virtual.Free[x, 0]) freeFrontCells++;
                }

                log.AppendLine($"  strip {s.Virtual.Width}x{s.Virtual.Depth} dir=({s.Block.m_Direction.x:F2},{s.Block.m_Direction.y:F2}) free={s.Virtual.FreeCount()} corners L={s.Virtual.LeftCorner} R={s.Virtual.RightCorner}");
                log.AppendLine($"    {StripInfo(s.Strip)}; front row {Row(s.Virtual, 0)}");
            }

            foreach (var st in strips.Where(st => st.Grid.FreeCount() == 0))
            {
                log.AppendLine($"  strip discarded (0 free) {st.Width}x{st.Depth}: {StripInfo(st)}");
            }

            Mod.Log.Info(log.ToString().TrimEnd());
            Say($"{chosen.Count} street fronts, {freeFrontCells} free roadside cells.");
            return true;
        }

        List<Pt> SidewalkSide(Entity edge, bool forward, List<Pt> centerFace)
        {
            if (!EntityManager.HasComponent<EdgeGeometry>(edge))
            {
                return null;
            }

            var g = EntityManager.GetComponentData<EdgeGeometry>(edge);
            List<Pt> Side(bool left, bool fwd)
            {
                var a = left ? g.m_Start.m_Left : g.m_Start.m_Right;
                var b = left ? g.m_End.m_Left : g.m_End.m_Right;
                var pts = Sample(a);
                var rest = Sample(b);
                pts.AddRange(rest.Skip(1));
                if (!fwd)
                {
                    pts.Reverse();
                }

                return pts;
            }

            int Inside(List<Pt> pts) => new[] { pts.Count / 4, pts.Count / 2, 3 * pts.Count / 4 }.Count(i => Polygon.Contains(centerFace, pts[i]));
            var assumed = Side(forward, forward);
            var other = Side(!forward, forward);
            return Inside(assumed) < 2 && Inside(other) >= 2 ? other : assumed;
        }

        static List<Pt> Sample(Bezier4x3 c)
        {
            float len = MathUtils.Length(c.xz);
            int n = math.clamp((int)math.ceil(len / 4f), 1, 64);
            var pts = new List<Pt>(n + 1);
            for (int k = 0; k <= n; k++)
            {
                var p = MathUtils.Position(c, k / (float)n);
                pts.Add(new Pt(p.x, p.z));
            }

            return pts;
        }

        void SetArea(List<Pt> outline)
        {
            areaFill.Clear();
            areaEdge.Clear();
            areaOutline = outline != null && outline.Count >= 3 ? outline : null;
            if (outline == null || outline.Count < 3)
            {
                return;
            }

            var heights = m_TerrainSystem.GetHeightData();
            float3 P(double x, double z)
            {
                var p = new float3((float)x, 0, (float)z);
                p.y = TerrainUtils.SampleHeight(ref heights, p) + 0.3f;
                return p;
            }

            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                areaEdge.Add((P(a.X, a.Y), P(b.X, b.Y)));
            }

            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            var zs = new List<double>();
            for (double x = minX + FillStripe / 2; x < maxX; x += FillStripe)
            {
                zs.Clear();
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    if ((a.X <= x && b.X > x) || (b.X <= x && a.X > x))
                    {
                        zs.Add(a.Y + (x - a.X) / (b.X - a.X) * (b.Y - a.Y));
                    }
                }

                zs.Sort();
                for (int k = 0; k + 1 < zs.Count; k += 2)
                {
                    areaFill.Add((P(x, zs[k]), P(x, zs[k + 1])));
                }
            }
        }

        void SetSelection(IEnumerable<SelectedBlock> blocks, string description)
        {
            var cfg = DevConfig.Load();
            selection.Clear();
            selection.AddRange(blocks);
            preview.Clear();
            areaFill.Clear();
            areaEdge.Clear();
            areaOutline = null;
            decorAreas = new List<List<Pt>>();
            decorClip = null;
            definitionsDirty = true;

            freeFrontCells = 0;
            var heights = m_TerrainSystem.GetHeightData();
            var log = new StringBuilder($"[selection] {description}: {selection.Count} blocks.\n");
            foreach (var s in selection)
            {
                var cells = EntityManager.GetBuffer<Cell>(s.Entity, true);
                var grid = BuildGrid(s.Block, s.Area, cells, s.Include);
                for (int x = 0; x < grid.Width; x++)
                {
                    if (grid.Free[x, 0]) freeFrontCells++;
                }

                s.Outline = BuildOutline(s, cells, heights);
                AppendBlock(log, s, cells, grid, cfg.LogCells);
            }

            Mod.Log.Info(log.ToString().TrimEnd());
            Say($"{selection.Count} blocks selected, {freeFrontCells} free roadside cells.");
        }

        static List<(float3, float3)> BuildOutline(SelectedBlock s, DynamicBuffer<Cell> cells, TerrainHeightData heights)
        {
            var b = s.Block;
            int size = b.m_Size.x;
            bool In(int x, int y)
            {
                if (x < s.Area.x || x >= s.Area.y || y < s.Area.z || y >= s.Area.w)
                {
                    return false;
                }

                int i = y * size + x;
                return (cells[i].m_State & CellFlags.Visible) != 0 && (s.Include == null || s.Include[i]);
            }

            float2 r = MathUtils.Right(b.m_Direction) * (ZoneUtils.CELL_SIZE * 0.5f);
            float2 f = b.m_Direction * (ZoneUtils.CELL_SIZE * 0.5f);
            var lines = new List<(float3, float3)>();
            float3 P(float2 xz)
            {
                var p = new float3(xz.x, 0, xz.y);
                p.y = TerrainUtils.SampleHeight(ref heights, p) + 0.6f;
                return p;
            }

            for (int y = s.Area.z; y < s.Area.w; y++)
            {
                for (int x = s.Area.x; x < s.Area.y; x++)
                {
                    if (!In(x, y))
                    {
                        continue;
                    }

                    float2 c = ZoneUtils.GetCellPosition(b, new int2(x, y)).xz;
                    if (!In(x - 1, y)) lines.Add((P(c + r + f), P(c + r - f)));
                    if (!In(x + 1, y)) lines.Add((P(c - r + f), P(c - r - f)));
                    if (!In(x, y - 1)) lines.Add((P(c + f + r), P(c + f - r)));
                    if (!In(x, y + 1)) lines.Add((P(c - f + r), P(c - f - r)));
                }
            }

            return lines;
        }

        static string StripInfo(LotStrip st) => st == null ? "(no data)"
            : $"side {st.SideIndex} segment {st.ChunkIndex + 1}/{st.ChunkCount}, sidewalk from ({st.ChunkStart.X:F1},{st.ChunkStart.Y:F1}) length {st.ChunkLength:F2} m, " +
              $"cells from {st.Offset:F2} m, {st.Slack:F2} m left over{(st.Inward > 0 ? $", pushed in {st.Inward:F2} m by the curve" : string.Empty)}";

        static string Row(PlanGrid g, int y)
        {
            var sb = new StringBuilder(g.Width);
            for (int x = 0; x < g.Width; x++)
            {
                sb.Append(g.Free[x, y] ? 'o' : 'x');
            }

            return sb.ToString();
        }

        bool IsPlatterBlock(Entity block, Dictionary<Entity, bool> cache)
        {
            if (!EntityManager.HasComponent<Owner>(block))
            {
                return false;
            }

            Entity owner = EntityManager.GetComponentData<Owner>(block).m_Owner;
            if (owner == Entity.Null || !EntityManager.Exists(owner))
            {
                return false;
            }

            if (cache.TryGetValue(owner, out bool known))
            {
                return known;
            }

            bool result = IsPlatterOwner(owner);
            cache[owner] = result;
            return result;
        }

        bool IsPlatterOwner(Entity owner)
        {
            using (var types = EntityManager.GetComponentTypes(owner, Allocator.Temp))
            {
                foreach (var t in types)
                {
                    var managed = t.GetManagedType();
                    if (managed?.FullName != null && managed.FullName.StartsWith("Platter.", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool IsFree(Cell c) =>
            (c.m_State & CellFlags.Visible) != 0 &&
            (c.m_State & (CellFlags.Blocked | CellFlags.Occupied)) == 0;

        static PlanGrid BuildGrid(Block block, int4 area, DynamicBuffer<Cell> cells, bool[] include)
        {
            int w = area.y - area.x, d = area.w - area.z, size = block.m_Size.x;
            var grid = PlanGrid.AllFree(w, d, false, false);
            for (int y = 0; y < d; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (area.z + y) * size + area.x + x;
                    grid.Free[x, y] = IsFree(cells[i]) && (include == null || include[i]);
                }
            }

            grid.LeftCorner = (cells[area.z * size + area.x].m_State & CellFlags.RoadLeft) != 0;
            grid.RightCorner = (cells[area.z * size + area.y - 1].m_State & CellFlags.RoadRight) != 0;
            return grid;
        }

        static void AppendBlock(StringBuilder sb, SelectedBlock s, DynamicBuffer<Cell> cells, PlanGrid grid, bool withCells)
        {
            var b = s.Block;
            int size = b.m_Size.x;
            var first = cells[s.Area.z * size + s.Area.x].m_State;
            var last = cells[s.Area.z * size + s.Area.y - 1].m_State;
            sb.AppendLine($"  block {s.Entity.Index}: pos=({b.m_Position.x:F1},{b.m_Position.z:F1}) dir=({b.m_Direction.x:F2},{b.m_Direction.y:F2}) size={b.m_Size.x}x{b.m_Size.y} area=[{s.Area.x},{s.Area.y})x[{s.Area.z},{s.Area.w}) free={grid.FreeCount()} cell0={first} cellEnd={last}");
            if (!withCells)
            {
                return;
            }

            for (int y = 0; y < b.m_Size.y; y++)
            {
                var row = new StringBuilder("    ");
                for (int x = 0; x < size; x++)
                {
                    var st = cells[y * size + x].m_State;
                    bool inArea = x >= s.Area.x && x < s.Area.y && y >= s.Area.z && y < s.Area.w;
                    char ch = !inArea ? '-'
                        : (st & CellFlags.Visible) == 0 ? 'v'
                        : (st & CellFlags.Occupied) != 0 ? 'O'
                        : (st & CellFlags.Blocked) != 0 ? 'B'
                        : (st & CellFlags.Shared) != 0 ? 'S'
                        : (st & (CellFlags.RoadLeft | CellFlags.RoadRight)) != 0 ? 'c'
                        : (st & CellFlags.Roadside) != 0 ? 'r'
                        : '.';
                    row.Append(ch);
                }

                sb.AppendLine(row.ToString());
            }
        }
    }
}
