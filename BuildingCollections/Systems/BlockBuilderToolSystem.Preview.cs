using System;
using System.Collections.Generic;
using System.Globalization;
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
        public void GeneratePreview(IReadOnlyList<CatalogEntry> candidates, int density, int seed,
            IReadOnlyList<CatalogEntry> decor = null, int decorDensity = 50, int ageMask = 4)
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var times = new List<string>();
                void Lap(string what)
                {
                    times.Add($"{what} {clock.ElapsedMilliseconds} ms");
                    clock.Restart();
                }

                preview.Clear();
                FrontFilled = FrontTotal = 0;
                frontCells = new List<Pt>();
                autoCleanPasses = 0;
                definitionsDirty = true;
                var cfg = DevConfig.Load();
                var placeable = candidates.Where(c => c.Info.Corner != CornerKind.None && c.Info.LotWidth > 0 && c.Info.LotDepth > 0).ToList();
                var angled = placeable.Where(c => c.Info.IsAngledCorner).Select(c => c.Info.PrefabName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var usable = placeable.Where(c => !c.Info.IsAngledCorner).ToList();
                var plain = usable.Where(c => AccessMapping.FitsFront(c.Info.Corner)).Select(c => c.Info).ToList();
                var corners = usable.Where(c => AccessMapping.FitsCorner(c.Info.Corner)).Select(c => c.Info).ToList();
                var prefabs = usable.GroupBy(c => c.Info.PrefabName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Prefab, StringComparer.OrdinalIgnoreCase);

                var log = new StringBuilder($"[preview] seed {seed}, density {density}%, {plain.Count} regular, {corners.Count} corner buildings.\n");
                if (angled.Count > 0)
                {
                    log.Append($"  {angled.Count} angled corner buildings left out (not supported yet): {string.Join(", ", angled)}.\n");
                }

                bool anarchy = AnarchyBridge.Enabled;
                float overlap = CurrentOverlap();

                if (reselect != null && Math.Abs(CurveOverlap(overlap) - stripsOverlap) > 1e-4)
                {
                    log.Append($"  overlap changed ({stripsOverlap:F2} -> {CurveOverlap(overlap):F2} m): selecting the area again.\n");
                    reselect();
                }
                log.Append($"  overlap allowed {overlap:F2} m{(anarchy ? " (Anarchy)" : string.Empty)}.\n");

                var existing = areaOutline != null ? BuildingFootprints(areaOutline) : new List<Quad>();
                Lap("existing buildings");
                var planned = PreviewPlanner.Plan(new PreviewRequest
                {
                    Blocks = PlanBlocks(),
                    Existing = existing,
                    Outline = areaOutline,
                    Plain = plain,
                    Corners = corners,
                    Overlap = overlap,
                    Density = density,
                    Seed = seed,
                });
                times.AddRange(planned.Timings);
                planned.Log.ForEach(line => log.AppendLine(line));
                DataLog.Proposed(planned.Lots).ToList().ForEach(line => log.AppendLine(line));
                frontCells = planned.FrontCells;
                foreach (var lot in planned.Lots)
                {
                    var owner = (SelectedBlock)lot.Owner.Tag;
                    preview.Add(new Planned { Lot = lot, Prefab = prefabs[lot.Building.PrefabName], Block = owner.Block });
                }

                FrontTotal = frontCells.Count;
                FrontFilled = planned.FrontFilled;
                clock.Restart();

                int buildingsPlanned = preview.Count;
                var blockers = planned.Lots.Select(l => l.Footprint(0)).Concat(existing).ToList();
                int plants = PlanDecor(decor, blockers, decorDensity, ageMask, seed, log);
                Lap("decoration");
                log.AppendLine($"  timings: {string.Join(", ", times)}.");
                Mod.Log.Info(log.ToString().TrimEnd());
                Say(buildingsPlanned == 0 && plants == 0 ? "Nothing fits in the selected area with these filters."
                    : plants > 0 ? $"Preview: {buildingsPlanned} buildings{MixNote(planned)}, {plants} plants." : $"Preview: {buildingsPlanned} buildings{MixNote(planned)}.", error: buildingsPlanned == 0 && plants == 0);
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                preview.Clear();
                Say("Preview failed (see log).", error: true);
            }
        }

        int PlanDecor(IReadOnlyList<CatalogEntry> decor, List<Quad> blockers, int decorDensity, int ageMask, int seed, StringBuilder log)
        {
            if (decor == null || decor.Count == 0 || areaOutline == null || decorAreas.Count == 0)
            {
                return 0;
            }

            const double PlantMargin = 0.5;
            var existingTrees = TreePoints(areaOutline);
            var radii = decor.Select(d => PlantRadius(d.Prefab)).ToList();
            var spots = new List<Pt>();
            foreach (var area in decorAreas)
            {
                spots.AddRange(Decor.Scatter(area, blockers, existingTrees, Decor.SpacingFor(decorDensity), 2.5,
                    radii.Min() + PlantMargin, seed + spots.Count, clip: decorClip));
            }

            var rng = new System.Random(seed ^ 0x5bd1e995);
            int tooBig = 0;
            foreach (var q in spots)
            {
                int k = Decor.PickFitting(q, blockers, radii, PlantMargin, rng);
                if (k < 0)
                {
                    tooBig++;
                    continue;
                }

                preview.Add(new Planned
                {
                    Prefab = decor[k].Prefab,
                    IsDecor = true,
                    Age = (byte)((ageMask & 15) == 0 ? 4 : ageMask & 15),
                    Pos = new float3((float)q.X, 0, (float)q.Y),
                    Rot = quaternion.RotateY((float)(rng.NextDouble() * Math.PI * 2)),
                });
            }

            int plants = spots.Count - tooBig;
            log.AppendLine($"  decoration: {plants} plants of {decor.Count} kinds, density {decorDensity}%; {tooBig} points with room for none; radii {radii.Min():F1}-{radii.Max():F1} m.");
            return plants;
        }

        List<PlanBlock> PlanBlocks()
        {
            var blocks = new List<PlanBlock>();
            foreach (var s in selection.ToList())
            {
                PlanGrid grid;
                if (s.Virtual != null)
                {
                    grid = s.Virtual;
                }
                else if (!TryZoneGrid(s, out grid))
                {
                    continue;
                }

                blocks.Add(new PlanBlock
                {
                    Frame = FrameOf(s.Block),
                    AreaX = s.Area.x,
                    AreaY = s.Area.z,
                    Grid = grid,
                    Strip = s.Strip,
                    Priority = OwnedCells(s),
                    IsVirtual = s.Virtual != null,
                    Label = s.Strip != null ? $"strip side {s.Strip.SideIndex} segment {s.Strip.ChunkIndex + 1}" : s.Virtual != null ? "strip" : "block " + s.Entity.Index,
                    Tag = s,
                });
            }

            return blocks;
        }

        static string MixNote(PreviewResult planned) => planned.FirstGroupShare is double first
            ? $" (mix {Math.Round(first * 100):0}% / {Math.Round((1 - first) * 100):0}%)"
            : string.Empty;

        static CellFrame FrameOf(Block b)
        {
            var o = ZoneUtils.GetCellPosition(b, new int2(0, 0)).xz;
            var x = math.normalizesafe(ZoneUtils.GetCellPosition(b, new int2(1, 0)).xz - o);
            var y = math.normalizesafe(ZoneUtils.GetCellPosition(b, new int2(0, 1)).xz - o);
            return new CellFrame(new Pt(o.x, o.y), new Pt(x.x, x.y), new Pt(y.x, y.y));
        }

        bool TryZoneGrid(SelectedBlock s, out PlanGrid grid)
        {
            grid = null;
            if (!EntityManager.Exists(s.Entity) || EntityManager.HasComponent<Deleted>(s.Entity))
            {
                selection.Remove(s);
                return false;
            }

            s.Block = EntityManager.GetComponentData<Block>(s.Entity);
            s.Area = EntityManager.GetComponentData<ValidArea>(s.Entity).m_Area;
            if (s.Area.y <= s.Area.x || s.Area.w <= s.Area.z)
            {
                return false;
            }

            var cells = EntityManager.GetBuffer<Cell>(s.Entity, true);
            if (cells.Length < s.Block.m_Size.x * s.Block.m_Size.y || (s.Include != null && s.Include.Length != cells.Length))
            {
                return false;
            }

            s.Outline = BuildOutline(s, cells, m_TerrainSystem.GetHeightData());
            grid = BuildGrid(s.Block, s.Area, cells, s.Include);
            return true;
        }

        void AutoClean()
        {
            if (settleFrames < 0 || ++settleFrames < SettleFrames)
            {
                return;
            }

            if (preview.Count == 0 || autoCleanPasses >= MaxAutoCleanPasses)
            {
                settleFrames = -1;
                return;
            }

            if (GetAllowApply())
            {
                if (settleFrames >= WatchFrames)
                {
                    settleFrames = -1;
                }

                return;
            }

            settleFrames = -1;
            if (!DevConfig.Load().RequireNoErrors)
            {
                return;
            }

            autoCleanPasses++;
            int removed = RemoveErrored();
            if (removed > 0)
            {
                FrontFilled = CountFront();
                Mod.Log.Info($"[preview] Auto clean (pass {autoCleanPasses}): removed {removed} red elements.");
                definitionsDirty = true;
            }
        }

        int RemoveErrored()
        {
            var bad = new List<float3>();
            using (var errors = m_ErrorQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in errors)
                {
                    var root = e;
                    for (int k = 0; k < 8 && EntityManager.HasComponent<Owner>(root); k++)
                    {
                        var owner = EntityManager.GetComponentData<Owner>(root).m_Owner;
                        if (owner == Entity.Null || !EntityManager.Exists(owner))
                        {
                            break;
                        }

                        root = owner;
                    }

                    if (EntityManager.HasComponent<Transform>(root))
                    {
                        bad.Add(EntityManager.GetComponentData<Transform>(root).m_Position);
                    }
                }
            }

            var drop = new HashSet<Planned>();
            int buildingsKept = 0;
            var plants = preview.Where(p => p.IsDecor).ToList();
            foreach (var p in plants)
            {
                if (bad.Any(b => math.distance(b.xz, p.Pos.xz) < 1f))
                {
                    drop.Add(p);
                }
            }

            foreach (var p in preview.Where(p => !p.IsDecor))
            {
                var lot = p.Lot.Footprint(0);
                if (!bad.Any(b => lot.Contains(new Pt(b.x, b.z))))
                {
                    continue;
                }

                var culprits = plants.Where(t => lot.DistanceTo(new Pt(t.Pos.x, t.Pos.z)) < PlantRadius(t.Prefab) + 1.0).ToList();
                if (culprits.Count > 0)
                {
                    drop.UnionWith(culprits);
                    buildingsKept++;
                }
                else
                {
                    drop.Add(p);
                }
            }

            preview.RemoveAll(drop.Contains);
            int removed = drop.Count;
            if (removed > 0)
            {
                Mod.Log.Info($"[build] Removed for errors: {drop.Count(p => !p.IsDecor)} buildings and {drop.Count(p => p.IsDecor)} plants; " +
                    $"{buildingsKept} red buildings stay after removing the plants touching them ({string.Join(", ", bad.Take(8).Select(b => $"({b.x:F0},{b.z:F0})"))}).");
            }

            return removed;
        }

        JobHandle CreatePreviewDefinitions(JobHandle inputDeps)
        {
            inputDeps = DestroyDefinitions(definitionQuery, m_ToolOutputBarrier, inputDeps);
            if (preview.Count == 0)
            {
                return inputDeps;
            }

            var cfg = DevConfig.Load();
            var heights = m_TerrainSystem.GetHeightData();
            JobHandle handle = inputDeps;
            foreach (var p in preview)
            {
                if (!EntityManager.Exists(p.Prefab))
                {
                    continue;
                }

                Transform t;
                float y;
                if (p.IsDecor)
                {
                    t = new Transform(p.Pos, p.Rot);
                    y = TerrainUtils.SampleHeight(ref heights, p.Pos);
                }
                else
                {
                    var axis = FrontAxis(p.Block) * p.Shift;
                    t = new Transform(ZoneUtils.GetPosition(p.Block, p.Min, p.Max) + new float3(axis.x, 0, axis.y), ZoneUtils.GetRotation(p.Block));
                    float3 front = BuildingUtils.CalculateFrontPosition(t, p.Max.y - p.Min.y);
                    y = TerrainUtils.SampleHeight(ref heights, front);
                }

                if (!p.IsDecor && cfg.UsePlacementOffset && EntityManager.HasComponent<PlaceableObjectData>(p.Prefab))
                {
                    var pod = EntityManager.GetComponentData<PlaceableObjectData>(p.Prefab);
                    if ((pod.m_Flags & (Game.Objects.PlacementFlags.Shoreline | Game.Objects.PlacementFlags.Floating)) == 0)
                    {
                        y += pod.m_PlacementOffset.y;
                    }
                }

                t.m_Position.y = y;
                var points = new NativeList<ControlPoint>(1, Allocator.TempJob);
                points.Add(new ControlPoint { m_Position = t.m_Position, m_HitPosition = t.m_Position, m_Rotation = t.m_Rotation });
                handle = CreateDefinitions(
                    p.Prefab, Entity.Null, Entity.Null, Entity.Null, Entity.Null, Entity.Null, city.defaultTheme, points,
                    default(NativeReference<AttachmentData>), false, city.leftHandTraffic, false, false,
                    0f, 0f, 0f, 0f, 0f, RandomSeed.Next(), Snap.None, (Game.Tools.AgeMask)p.Age, false, default(PlacementOverrides), handle);
                points.Dispose(handle);
            }

            m_TerrainSystem.AddCPUHeightReader(handle);
            return handle;
        }

        double PlantRadius(Entity prefab)
        {
            if (!EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                return 2.0;
            }

            var g = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
            float box = math.cmax(math.max(math.abs(g.m_Bounds.min.xz), math.abs(g.m_Bounds.max.xz)));
            float r = (g.m_Flags & Game.Objects.GeometryFlags.Circular) != 0 ? math.max(g.m_Size.x * 0.5f, box) : box;
            return math.clamp(r, 0.5f, 10f);
        }

        List<Quad> BuildingFootprints(List<Pt> outline)
        {
            var result = new List<Quad>();
            if (outline == null || outline.Count == 0)
            {
                return result;
            }

            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            double minZ = outline.Min(p => p.Y), maxZ = outline.Max(p => p.Y);
            foreach (var (prefab, tr) in NearbyBuildings(outline))
            {
                var q = BoundsQuad(prefab, tr);
                if (q != null && q.MaxX >= minX && q.MinX <= maxX && q.MaxY >= minZ && q.MinY <= maxZ)
                {
                    result.Add(q);
                }
            }

            return result;
        }

        IEnumerable<string> AngledPlaced(List<Pt> outline)
        {
            var prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            foreach (var (prefab, tr) in NearbyBuildings(outline))
            {
                if (!prefabSystem.TryGetPrefab(prefab, out PrefabBase p) || p == null || AngledCorner.Angle(p.name) == 0 || !EntityManager.HasComponent<BuildingData>(prefab))
                {
                    continue;
                }

                var lot = EntityManager.GetComponentData<BuildingData>(prefab).m_LotSize;
                float hw = lot.x * 4f, hd = lot.y * 4f;
                var lotQuad = Corners(tr, -hw, -hd, hw, hd);
                var bounds = BoundsQuad(prefab, tr);
                float3 forward = math.rotate(tr.m_Rotation, new float3(0, 0, 1));
                double yaw = math.degrees(math.atan2(forward.x, forward.z));
                yield return string.Format(CultureInfo.InvariantCulture, "  [data] angled placed {0} angle {1} side {2} lot {3}x{4}: pos {5:F2},{6:F2} yaw {7:F1} lot {8} bounds {9}",
                    p.name.Replace(":", "_"), AngledCorner.Angle(p.name), AngledCorner.Side(p.name), lot.x, lot.y, tr.m_Position.x, tr.m_Position.z, yaw,
                    PointList(lotQuad), bounds == null ? "-" : PointList(bounds));
            }
        }

        static string PointList(Quad q) => string.Join(";", q.P.Select(pt => string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2}", pt.X, pt.Y)));

        static Quad Corners(Transform tr, float minX, float minZ, float maxX, float maxZ)
        {
            float3 P(float x, float z) => tr.m_Position + math.rotate(tr.m_Rotation, new float3(x, 0, z));
            var a = P(minX, minZ);
            var b = P(maxX, minZ);
            var c = P(maxX, maxZ);
            var d = P(minX, maxZ);
            return new Quad(new Pt(a.x, a.z), new Pt(b.x, b.z), new Pt(c.x, c.z), new Pt(d.x, d.z));
        }

        Quad BoundsQuad(Entity prefab, Transform tr)
        {
            if (!EntityManager.HasComponent<ObjectGeometryData>(prefab))
            {
                return null;
            }

            var bounds = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
            return Corners(tr, bounds.min.x, bounds.min.z, bounds.max.x, bounds.max.z);
        }

        IEnumerable<(Entity Prefab, Transform Transform)> NearbyBuildings(List<Pt> outline)
        {
            if (outline == null || outline.Count == 0)
            {
                yield break;
            }

            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            double minZ = outline.Min(p => p.Y), maxZ = outline.Max(p => p.Y);
            const double Reach = 300;
            var found = new List<(Entity, Transform)>();
            using (var transforms = buildingQuery.ToComponentDataArray<Transform>(Allocator.Temp))
            using (var prefabRefs = buildingQuery.ToComponentDataArray<PrefabRef>(Allocator.Temp))
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    var tr = transforms[i];
                    if (tr.m_Position.x >= minX - Reach && tr.m_Position.x <= maxX + Reach && tr.m_Position.z >= minZ - Reach && tr.m_Position.z <= maxZ + Reach)
                    {
                        found.Add((prefabRefs[i].m_Prefab, tr));
                    }
                }
            }

            foreach (var item in found)
            {
                yield return item;
            }
        }

        List<Pt> TreePoints(List<Pt> outline)
        {
            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            double minZ = outline.Min(p => p.Y), maxZ = outline.Max(p => p.Y);
            var result = new List<Pt>();
            using (var transforms = treeQuery.ToComponentDataArray<Transform>(Allocator.Temp))
            {
                foreach (var tr in transforms)
                {
                    if (tr.m_Position.x >= minX && tr.m_Position.x <= maxX && tr.m_Position.z >= minZ && tr.m_Position.z <= maxZ)
                    {
                        result.Add(new Pt(tr.m_Position.x, tr.m_Position.z));
                    }
                }
            }

            return result;
        }

        int OwnedCells(SelectedBlock s)
        {
            if (s.Virtual != null)
            {
                return s.Virtual.Width * 100;
            }

            if (!EntityManager.Exists(s.Entity))
            {
                return 0;
            }

            var cells = EntityManager.GetBuffer<Cell>(s.Entity, true);
            int n = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if ((cells[i].m_State & CellFlags.Visible) != 0 && (cells[i].m_State & CellFlags.Shared) == 0)
                {
                    n++;
                }
            }

            return n;
        }

        const float StrictOverlap = FootprintMask.StrictOverlap;

        const float OverlapOn = 0.5f;

        static float2 FrontAxis(Block b) =>
            math.normalizesafe(ZoneUtils.GetCellPosition(b, new int2(1, 0)).xz - ZoneUtils.GetCellPosition(b, new int2(0, 0)).xz);

        int CountFront() => FrontCoverage.CountFilled(preview.Where(p => !p.IsDecor).Select(p => p.Lot), frontCells);
    }
}
