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
    /// <summary>
    /// Herramienta del constructor de manzanas. Modo bloque: clic selecciona la manzana (los bloques de zona cuyas
    /// celdas visibles se tocan con las del clicado); clic derecho borra. Modo polígono: clics ponen vértices, clic
    /// en el primero cierra y selecciona las celdas de dentro; clic derecho quita el último vértice. La vista previa y la construcción usan el mismo camino que el
    /// juego al colocar un objeto (<see cref="ObjectToolBaseSystem.CreateDefinitions"/>), con la posición y la
    /// rotación que usa ZoneSpawnSystem para una parcela.
    /// </summary>
    public partial class BlockBuilderToolSystem : ObjectToolBaseSystem
    {
        sealed class SelectedBlock
        {
            public Entity Entity;
            public Block Block;
            public int4 Area;   // celdas válidas: x en [x, y), profundidad en [z, w)
            public bool[] Include;  // celdas elegidas (índice y * m_Size.x + x); null = todas (modo bloque)
            public List<(float3 a, float3 b)> Outline = new List<(float3, float3)>();

            /// <summary>Parcelas propias (sin celdas de zona): la rejilla calculada al seleccionar. null = bloque del juego.</summary>
            public PlanGrid Virtual;
        }

        sealed class Planned
        {
            public string Name;
            public Entity Prefab;
            public Block Block;
            public int2 Min;
            public int2 Max;
            public bool Corner;

            /// <summary>Planta de la decoración: se coloca en Pos / Rot, no en una parcela.</summary>
            public bool IsDecor;
            public float3 Pos;
            public quaternion Rot;
        }

        readonly List<SelectedBlock> selection = new List<SelectedBlock>();
        readonly List<Planned> preview = new List<Planned>();
        EntityQuery blockQuery;
        EntityQuery tempBuildingQuery;
        EntityQuery definitionQuery;
        EntityQuery roadQuery;
        EntityQuery buildingQuery;

        // Área seleccionada como en Street Builder: relleno (franjas) y contorno, ya a la altura del terreno.
        readonly List<(float3 a, float3 b)> areaFill = new List<(float3, float3)>();
        readonly List<(float3 a, float3 b)> areaEdge = new List<(float3, float3)>();
        const float FillStripe = 2f;
        OverlayRenderSystem overlay;
        CityConfigurationSystem city;
        BuiltGuardSystem guard;
        readonly List<float3> polygon = new List<float3>();

        // Polígono que se adapta a las calles: bordillo (índice en curbs) y posición de cada vértice enganchado.
        readonly List<int> polyCurb = new List<int>();
        readonly List<double> polyArc = new List<double>();
        List<List<Pt>> curbs = new List<List<Pt>>();
        float2 curbCenter;
        int curbAge = int.MaxValue;
        bool hoverSnapped;
        CurbHit hoverHit;
        const float SnapDistance = 6f;
        const float CurbRadius = 800f;
        const double HugTolerance = 1.5;

        // Contorno del área seleccionada (para la decoración) y árboles que ya hay en ella.
        List<Pt> areaOutline;

        // Zonas donde pueden ir árboles: el contorno por la acera de cada manzana (nunca la calzada) y, en modo
        // polígono, el polígono que las recorta.
        List<List<Pt>> decorAreas = new List<List<Pt>>();
        List<Pt> decorClip;
        EntityQuery treeQuery;
        string mode = "block";
        float3 hoverPos;
        bool hasHover;
        bool definitionsDirty;
        bool buildRequested;
        int freeFrontCells;

        public override string toolID => "BuildingCollections.BlockBuilder";

        public bool AreaSelected => selection.Count > 0;

        public bool Previewing => preview.Count > 0;

        /// <summary>Parcelas: edificios de la vista previa, o celdas frontales libres si aún no hay vista previa.</summary>
        public int Lots => Previewing ? preview.Count(p => !p.IsDecor) : freeFrontCells;

        /// <summary>Bloques de zona seleccionados.</summary>
        public int Blocks => selection.Count;

        public string Message { get; private set; } = string.Empty;

        public int MessageSerial { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            blockQuery = GetEntityQuery(
                ComponentType.ReadOnly<Block>(),
                ComponentType.ReadOnly<Cell>(),
                ComponentType.ReadOnly<ValidArea>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            tempBuildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Temp>(),
                ComponentType.ReadOnly<Building>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Owner>());
            definitionQuery = GetDefinitionQuery();
            roadQuery = GetEntityQuery(
                ComponentType.ReadOnly<Edge>(),
                ComponentType.ReadOnly<Curve>(),
                ComponentType.ReadOnly<Road>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            treeQuery = GetEntityQuery(
                ComponentType.ReadOnly<Game.Objects.Tree>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            buildingQuery = GetEntityQuery(
                ComponentType.ReadOnly<Building>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            overlay = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            city = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            guard = World.GetOrCreateSystemManaged<BuiltGuardSystem>();
            mode = Mod.Settings?.Mode == "polygon" ? "polygon" : "block";
            Mod.Log.Info("BlockBuilderToolSystem creado.");
        }

        public override PrefabBase GetPrefab() => null;

        public override bool TrySetPrefab(PrefabBase prefab) => false;

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Terrain;
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            requireZones = true;
            applyMode = ApplyMode.None;

            // Como las herramientas del juego: un primer fotograma Clear borra restos Temp de otras herramientas, y
            // no se ejecuta un Build pedido mientras la herramienta no estaba activa.
            buildRequested = false;
            definitionsDirty = true;

            // Las calles pueden haber cambiado mientras la herramienta no estaba activa: bordillos de nuevo.
            if (polygon.Count == 0)
            {
                curbs = new List<List<Pt>>();
                curbAge = int.MaxValue;
            }

            // UpdateActions() de la clase base es private protected: un mod externo no puede sobrescribirlo,
            // así que hay que habilitar las acciones de entrada a mano o nunca llegan clic ni clic derecho.
            applyAction.shouldBeEnabled = true;
            secondaryApplyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            Mod.Log.Info("Herramienta del constructor activada.");
        }

        protected override void OnStopRunning()
        {
            // Al cambiar de herramienta el juego borra las entidades Temp: la vista previa ya no existe.
            preview.Clear();
            base.OnStopRunning();
        }

        // ---------------- API para BcUISystem (hilo principal) ----------------

        /// <summary>Cambia entre "block" y "polygon". Al cambiar se descarta la selección.</summary>
        public void SetMode(string newMode)
        {
            newMode = newMode == "polygon" ? "polygon" : "block";
            if (newMode != mode)
            {
                mode = newMode;
                ClearSelection();
            }
        }

        public void ClearSelection()
        {
            polygon.Clear();
            polyCurb.Clear();
            polyArc.Clear();
            selection.Clear();
            preview.Clear();
            areaFill.Clear();
            areaEdge.Clear();
            areaOutline = null;
            decorAreas = new List<List<Pt>>();
            decorClip = null;
            freeFrontCells = 0;
            definitionsDirty = true;
            Say("Selection cleared.");
        }

        /// <summary>Calcula una combinación nueva con la semilla dada y pide al juego la vista previa.</summary>
        public void GeneratePreview(IReadOnlyList<CatalogEntry> candidates, int density, int seed, bool leaveGaps,
            IReadOnlyList<CatalogEntry> decor = null, int decorDensity = 50)
        {
            try
            {
                preview.Clear();
                definitionsDirty = true;
                var cfg = DevConfig.Load();
                var usable = candidates.Where(c => c.Info.Corner != CornerKind.None && c.Info.LotWidth > 0 && c.Info.LotDepth > 0).ToList();
                var plain = usable.Where(c => AccessMapping.FitsFront(c.Info.Corner)).Select(c => c.Info).ToList();
                var corners = usable.Where(c => AccessMapping.FitsCorner(c.Info.Corner)).Select(c => c.Info).ToList();
                var prefabs = usable.ToDictionary(c => c.Info.PrefabName, c => c.Prefab, StringComparer.OrdinalIgnoreCase);

                int index = 0;
                var log = new StringBuilder($"[vista previa] semilla {seed}, densidad {density}%, huecos {leaveGaps}, {plain.Count} normales, {corners.Count} esquineros.\n");

                // Huellas (en el suelo) de lo ya planificado: las celdas compartidas entre bloques que queden libres
                // detrás de un edificio poco profundo se aprovechan desde la otra calle, sin solaparse.
                // Con Anarchy encendido (y nuestra herramienta en su lista) se admite un solape pequeño entre edificios
                // de calles distintas; sin él, ninguno (0,05 m solo para que dos que se tocan no cuenten).
                bool anarchy = AnarchyBridge.Enabled;
                float overlap = anarchy ? (float)math.clamp(cfg.AnarchyOverlap, 0.05, 2.0) : 0.05f;
                log.Append($"  solape admitido {overlap:F2} m{(anarchy ? " (Anarchy)" : string.Empty)}.\n");
                var footprints = new List<(Quad quad, SelectedBlock owner)>();
                Quad Footprint(SelectedBlock s, Placement p)
                {
                    var min = new int2(s.Area.x + p.X, s.Area.z + p.Y);
                    var max = min + new int2(p.Building.LotWidth, p.Building.LotDepth);
                    return ToQuad(ZoneUtils.CalculateCorners(s.Block, new ValidArea { m_Area = new int4(min.x, max.x, min.y, max.y) }));
                }

                List<Quad> Others(SelectedBlock s) => footprints.Where(f => f.owner != s).Select(f => f.quad).ToList();

                // Primero los bloques con más celdas propias (no compartidas): las calles largas antes que las laterales.
                var grids = new List<(SelectedBlock s, PlanGrid grid)>();
                foreach (var s in selection.OrderByDescending(OwnedCells).ToList())
                {
                    PlanGrid grid;
                    if (s.Virtual != null)
                    {
                        grid = new PlanGrid
                        {
                            Width = s.Virtual.Width, Depth = s.Virtual.Depth, Free = (bool[,])s.Virtual.Free.Clone(),
                            LeftCorner = s.Virtual.LeftCorner, RightCorner = s.Virtual.RightCorner,
                        };
                    }
                    else if (!TryZoneGrid(s, out grid))
                    {
                        continue;
                    }

                    grids.Add((s, grid));
                }

                // Pasada 1: cada bloque reserva sus esquinas antes de que nadie rellene. Dos bloques que se tocan en una
                // esquina se la disputan; sin esto, el relleno del primero le quitaba la esquina al segundo.
                var cornerPlans = new Dictionary<SelectedBlock, List<Placement>>();
                foreach (var (s, grid) in grids)
                {
                    MaskFootprints(grid, s.Block, s.Area, Others(s), overlap);
                    var cp = GridPlanner.PlanCorners(grid, plain, corners, seed + 7919 * index++);
                    cornerPlans[s] = cp;
                    footprints.AddRange(cp.Select(p => (Footprint(s, p), s)));
                }

                // Pasada 2: relleno, respetando las esquinas propias y las huellas de los demás.
                foreach (var (s, grid) in grids)
                {
                    int masked = MaskFootprints(grid, s.Block, s.Area, Others(s), overlap);
                    var plan = GridPlanner.Plan(grid, plain, corners, density, seed + 7919 * index++, leaveGaps, cornerPlans[s]);
                    footprints.RemoveAll(f => f.owner == s);
                    foreach (var p in plan)
                    {
                        var min = new int2(s.Area.x + p.X, s.Area.z + p.Y);
                        var max = min + new int2(p.Building.LotWidth, p.Building.LotDepth);
                        footprints.Add((Footprint(s, p), s));
                        preview.Add(new Planned
                        {
                            Name = p.Building.PrefabName,
                            Prefab = prefabs[p.Building.PrefabName],
                            Block = s.Block,
                            Min = min,
                            Max = max,
                            Corner = p.IsCorner,
                        });
                    }

                    log.AppendLine($"  {(s.Virtual != null ? "tira" : "bloque " + s.Entity.Index)}: rejilla {grid.Width}x{grid.Depth}, {grid.FreeCount()} libres ({masked} tapadas por otros bloques), esquinas L={grid.LeftCorner} R={grid.RightCorner} -> {string.Join("; ", plan.Select(p => $"x{p.X} {p.Building}{(p.IsCorner ? " [esquina]" : string.Empty)}"))}");
                }

                // Decoración del espacio sobrante (patio, huecos): plantas de la colección elegida, lejos de la acera y de
                // los edificios (los planificados y los que ya hay), y sin pisar árboles existentes.
                int buildingsPlanned = preview.Count;
                int plants = 0;
                if (decor != null && decor.Count > 0 && areaOutline != null && decorAreas.Count > 0)
                {
                    var blockers = footprints.Select(f => f.quad).ToList();
                    blockers.AddRange(BuildingFootprints(areaOutline));
                    var existingTrees = TreePoints(areaOutline);
                    var spots = new List<Pt>();
                    foreach (var area in decorAreas)
                    {
                        spots.AddRange(Decor.Scatter(area, blockers, existingTrees, Decor.SpacingFor(decorDensity), 2.5, 2.0, seed + spots.Count, clip: decorClip));
                    }

                    var rng = new System.Random(seed ^ 0x5bd1e995);
                    foreach (var q in spots)
                    {
                        var pick = decor[rng.Next(decor.Count)];
                        preview.Add(new Planned
                        {
                            Name = pick.Info.PrefabName,
                            Prefab = pick.Prefab,
                            IsDecor = true,
                            Pos = new float3((float)q.X, 0, (float)q.Y),
                            Rot = quaternion.RotateY((float)(rng.NextDouble() * Math.PI * 2)),
                        });
                    }

                    plants = spots.Count;
                    log.AppendLine($"  decoración: {plants} plantas de {decor.Count} tipos, densidad {decorDensity}%.");
                }

                Mod.Log.Info(log.ToString().TrimEnd());
                Say(buildingsPlanned == 0 && plants == 0 ? "Nothing fits in the selected area with these filters."
                    : plants > 0 ? $"Preview: {buildingsPlanned} buildings, {plants} plants." : $"Preview: {buildingsPlanned} buildings.");
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                preview.Clear();
                Say("Preview failed (see log).");
            }
        }

        /// <summary>Rejilla de un bloque de zona del juego, releído (puede haber cambiado desde la selección).</summary>
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

        public void RequestBuild() => buildRequested = true;

        // ---------------- Bucle de la herramienta ----------------

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                DrawSelection();

                hasHover = GetRaycastResult(out Entity _, out RaycastHit hit);
                hoverSnapped = false;
                if (hasHover)
                {
                    hoverPos = hit.m_HitPosition;
                    if (mode == "polygon" && selection.Count == 0)
                    {
                        SnapHover();
                    }
                }

                if (cancelAction.WasPressedThisFrame())
                {
                    if (mode == "polygon" && polygon.Count > 0 && selection.Count == 0)
                    {
                        polygon.RemoveAt(polygon.Count - 1);
                        polyCurb.RemoveAt(polyCurb.Count - 1);
                        polyArc.RemoveAt(polyArc.Count - 1);
                        Say(polygon.Count == 0 ? "Polygon cleared." : $"{polygon.Count} points.");
                    }
                    else
                    {
                        ClearSelection();
                    }
                }
                else if (applyAction.WasPressedThisFrame() && hasHover)
                {
                    if (mode == "polygon")
                    {
                        PolygonClick(hoverPos);
                    }
                    else
                    {
                        SelectAt(hoverPos);
                    }
                }

                if (buildRequested)
                {
                    buildRequested = false;
                    return Build(inputDeps);
                }

                if (definitionsDirty)
                {
                    definitionsDirty = false;
                    applyMode = ApplyMode.Clear;
                    return CreatePreviewDefinitions(inputDeps);
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }

            applyMode = ApplyMode.None;
            return inputDeps;
        }

        JobHandle Build(JobHandle inputDeps)
        {
            var cfg = DevConfig.Load();
            if (preview.Count == 0)
            {
                Say("Generate a preview first.");
                applyMode = ApplyMode.None;
                return inputDeps;
            }

            if (cfg.RequireNoErrors && !AnarchyBridge.Enabled && !GetAllowApply())
            {
                // Se quitan de la vista previa los que el juego marca en rojo (chocan con algo) y se pide otro Build.
                int removed = RemoveErrored();
                Mod.Log.Info($"[build] Cancelado: hay entidades con error ({m_ErrorQuery.CalculateEntityCount()}); quitados {removed} de la vista previa.");
                Say(removed > 0 ? $"Removed {removed} items that collide with something (red). Press Build again." : "Some items in the preview have errors (red). Try Reshuffle.");
                definitionsDirty = removed > 0;
                applyMode = ApplyMode.None;
                return inputDeps;
            }

            var temps = new List<Entity>();
            using (var arr = tempBuildingQuery.ToEntityArray(Allocator.Temp))
            {
                temps.AddRange(arr);
            }

            guard.Track(temps, cfg.ReportAfterFrames);
            Mod.Log.Info($"[build] Aplicando: {preview.Count} planificados, {temps.Count} edificios temporales.");
            Say($"Built {temps.Count} buildings.");
            preview.Clear();
            definitionsDirty = true; // el siguiente fotograma borra las definiciones ya aplicadas
            applyMode = ApplyMode.Apply;
            return inputDeps;
        }

        /// <summary>Quita de la vista previa lo que está junto a una entidad con error. Devuelve cuántos.</summary>
        int RemoveErrored()
        {
            var bad = new List<float3>();
            using (var errors = m_ErrorQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in errors)
                {
                    // Un objeto pegado a un edificio (atrezo, árbol del edificio) cuenta como su edificio.
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

            int before = preview.Count;
            preview.RemoveAll(p =>
            {
                if (p.IsDecor)
                {
                    return bad.Any(b => math.distance(b.xz, p.Pos.xz) < 1f);
                }

                var lot = ToQuad(ZoneUtils.CalculateCorners(p.Block, new ValidArea { m_Area = new int4(p.Min.x, p.Max.x, p.Min.y, p.Max.y) }));
                return bad.Any(b => lot.Contains(new Pt(b.x, b.z)));
            });
            int removed = before - preview.Count;
            if (removed > 0)
            {
                Mod.Log.Info($"[build] Quitados por error: {removed} ({string.Join(", ", bad.Take(8).Select(b => $"({b.x:F0},{b.z:F0})"))}).");
            }

            return removed;
        }

        JobHandle CreatePreviewDefinitions(JobHandle inputDeps)
        {
            // Como ObjectToolSystem.UpdateDefinitions: se borran las definiciones anteriores antes de crear las nuevas.
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
                    t = new Transform(ZoneUtils.GetPosition(p.Block, p.Min, p.Max), ZoneUtils.GetRotation(p.Block));
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
                    0f, 0f, 0f, 0f, 0f, RandomSeed.Next(), Snap.None, Game.Tools.AgeMask.Mature, false, default(PlacementOverrides), handle);
                points.Dispose(handle);
            }

            m_TerrainSystem.AddCPUHeightReader(handle);
            return handle;
        }

        // ---------------- Selección ----------------

        /// <summary>Bloques de zona cerca de un punto, con sus celdas visibles (centros en el suelo).</summary>
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
            var cfg = DevConfig.Load();

            // Como Street Builder: la manzana es todo lo que queda dentro de las calles que rodean el clic. Si el
            // clic no está encerrado por calles, se cae al método de celdas que se tocan.
            if (cfg.UseRoadFace)
            {
                var face = TraceRoadFace(point.xz, cfg.RoadSearchRadius, out int roads, out var path);
                if (face != null && cfg.UseVirtualLots && SelectVirtual(face, path, $"manzana de calles ({path.Count} tramos), clic en ({point.x:F1}, {point.z:F1})"))
                {
                    return;
                }

                if (face != null)
                {
                    if (SelectInside(face, $"manzana de calles ({face.Count} puntos, {roads} tramos cerca), clic en ({point.x:F1}, {point.z:F1})"))
                    {
                        return;
                    }

                    Mod.Log.Info("[selección] La manzana de calles no contiene celdas de zona; se prueba por celdas.");
                }
                else
                {
                    Mod.Log.Info($"[selección] clic en ({point.x:F1}, {point.z:F1}): no está encerrado por calles ({roads} tramos cerca); se prueba por celdas.");
                }
            }
            var blocks = GatherBlocks(point.xz, cfg.SearchRadius, out int platter);
            var click = new Pt(point.x, point.z);

            // Bloque de partida: el de la celda visible que contiene el clic (cuadrado de 8 m girado con el bloque, más
            // un margen opcional). Un clic en la calle no cae en ninguna celda y no selecciona nada.
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
                Mod.Log.Info($"[selección] clic en ({point.x:F1}, {point.z:F1}): ningún bloque de zona ({blocks.Count} bloques cerca, {platter} de Platter ignorados).");
                Say("Click inside a zoned block.");
                return;
            }

            var group = BlockGrouping.FloodByCells(blocks.Select(b => b.cells).ToList(), start, ZoneUtils.CELL_SIZE + cfg.BlockTouchTolerance);
            if (group.Count >= 256)
            {
                Mod.Log.Warn("[selección] Manzana cortada en 256 bloques.");
            }
            SetSelection(group.Select(i => blocks[i].block), $"clic en ({point.x:F1}, {point.z:F1}), tolerancia {cfg.BlockTouchTolerance} m, {platter} de Platter ignorados");
        }

        void PolygonClick(float3 point)
        {
            if (selection.Count > 0)
            {
                // Polígono ya cerrado: un clic nuevo empieza otro.
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
                return; // doble clic en el mismo sitio
            }

            polygon.Add(point);
            polyCurb.Add(hoverSnapped ? hoverHit.Curb : -1);
            polyArc.Add(hoverSnapped ? hoverHit.Arc : 0);
            Say(polygon.Count < 3 ? $"{polygon.Count} points. Keep clicking corners." : $"{polygon.Count} points. Click the first point to close.");
        }

        void SelectPolygon()
        {
            var cfg = DevConfig.Load();
            var verts = polygon.Select(p => new Pt(p.x, p.z)).ToList();
            CurbSnap.Sides(verts, polyCurb, polyArc, curbs, HugTolerance, out var sides, out var frontage);

            // Todas las manzanas de calles que caen dentro del polígono, cada una con sus parcelas por la acera y
            // recortadas por el polígono: así un polígono sobre varias manzanas usa también las calles de dentro.
            if (cfg.UseVirtualLots && SelectFacesInPolygon(VirtualLots.Outline(sides), $"polígono de {polygon.Count} vértices"))
            {
                return;
            }

            // Lados pegados a una calle: parcelas propias mirando a ella, como en el modo bloque.
            if (cfg.UseVirtualLots && frontage.Any(f => f) &&
                SelectVirtualSides(sides, frontage, $"polígono de {polygon.Count} vértices ({frontage.Count(f => f)} lados con calle)"))
            {
                return;
            }

            var poly = VirtualLots.Outline(sides);
            if (!SelectInside(poly, $"polígono de {polygon.Count} vértices"))
            {
                polygon.Clear();
                polyCurb.Clear();
                polyArc.Clear();
                Say("The polygon touches no street and contains no zoned cells.");
            }
        }

        /// <summary>Engancha el cursor al bordillo más cercano (a menos de SnapDistance).</summary>
        void SnapHover()
        {
            // Mientras se dibuja no se recalculan los bordillos: los vértices guardan su índice.
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

        /// <summary>Bordillos (los dos bordes exteriores de cada calle) cerca de un punto.</summary>
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

        /// <summary>Puntos (a la altura del terreno) del lado del polígono entre dos vértices, siguiendo el bordillo si toca.</summary>
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

        /// <summary>Selecciona las celdas visibles cuyo centro cae dentro del contorno. Devuelve false si no hay ninguna.</summary>
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

            SetSelection(chosen, $"{description}, {platter} de Platter ignorados");
            SetArea(poly);
            return true;
        }

        /// <summary>Contorno (x, z) de la manzana de calles que contiene el punto, o null.</summary>
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

        /// <summary>Calles a nivel del suelo cerca de un punto, como grafo (polilíneas por el eje entre nodos).</summary>
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
                    // Cerca si lo está la caja de sus puntos de control (la curva queda dentro): así no se pierde un
                    // tramo largo que pasa al lado del clic con los extremos lejos.
                    float2 lo = math.min(math.min(bz.a.xz, bz.b.xz), math.min(bz.c.xz, bz.d.xz));
                    float2 hi = math.max(math.max(bz.a.xz, bz.b.xz), math.max(bz.c.xz, bz.d.xz));
                    if (math.distancesq(math.clamp(point, lo, hi), point) > (float)(radius * radius))
                    {
                        continue;
                    }

                    // Puentes y túneles no cierran manzanas en el suelo.
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

        /// <summary>
        /// Parcelas propias a lo largo de la acera de cada calle de la manzana, como Street Builder: no hacen falta
        /// celdas de zona, así que se llenan también las diagonales y las esquinas. Evita los edificios existentes.
        /// </summary>
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

        /// <summary>Parcelas propias a lo largo de los lados con calle (frontage null = todos) de un contorno.</summary>
        bool SelectVirtualSides(List<List<Pt>> sides, List<bool> frontage, string description)
        {
            var outline = VirtualLots.Outline(sides);
            if (outline.Count < 3)
            {
                return false;
            }

            var obstacles = BuildingFootprints(outline);
            var strips = VirtualLots.Build(sides, obstacles, frontage: frontage);
            if (!SelectStrips(strips, outline, $"{description}: {sides.Count} aceras, {obstacles.Count} edificios existentes"))
            {
                return false;
            }

            decorAreas = new List<List<Pt>> { outline };
            decorClip = null;
            return true;
        }

        /// <summary>
        /// Busca las manzanas de calles dentro del polígono (probando puntos en rejilla) y genera sus parcelas
        /// recortadas por el polígono.
        /// </summary>
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
            // Paso de 12 a 25 m (manzanas pequeñas no se saltan) y como mucho unas 1500 muestras.
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
                        giant++; // una "manzana" enorme (campo dentro de una ronda): no se rellena
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
                    strips.AddRange(VirtualLots.Build(sides, obstacles, clip: clip));
                }
            }

            if (faces.Count >= 60 || samples >= 1500 || giant > 0)
            {
                Mod.Log.Warn($"[selección] polígono: {faces.Count} manzanas (tope 60), {samples} muestras (tope 1500), {giant} manzanas enormes ignoradas.");
            }

            if (faces.Count == 0 || !SelectStrips(strips, clip, $"{description}: {faces.Count} manzanas, {sideCount} aceras, {obstacles.Count} edificios existentes"))
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

        /// <summary>Convierte las tiras en bloques equivalentes a los del juego y las deja seleccionadas.</summary>
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

                // Bloque equivalente al del juego: y = 0 en la acera, x = 0 en P0 (ver ZoneUtils.GetCellPosition).
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
            var log = new StringBuilder($"[selección] {description}, {strips.Count} tiras.\n");
            foreach (var s in chosen)
            {
                for (int x = 0; x < s.Virtual.Width; x++)
                {
                    if (s.Virtual.Free[x, 0]) freeFrontCells++;
                }

                log.AppendLine($"  tira {s.Virtual.Width}x{s.Virtual.Depth} dir=({s.Block.m_Direction.x:F2},{s.Block.m_Direction.y:F2}) libres={s.Virtual.FreeCount()} esquinas L={s.Virtual.LeftCorner} R={s.Virtual.RightCorner}");
            }

            Mod.Log.Info(log.ToString().TrimEnd());
            Say($"{chosen.Count} street fronts, {freeFrontCells} free roadside cells.");
            return true;
        }

        /// <summary>
        /// Borde de la calle (acera exterior) del lado de la manzana, en el sentido del recorrido. Se comprueba con
        /// el contorno por el eje que el lado elegido cae dentro; si no, se usa el otro.
        /// </summary>
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

            // Tres muestras por lado: con una sola, en tramos cortos o recorridos dos veces (calle sin salida) caía
            // justo en el borde del contorno y el resultado era al azar. Solo se cambia de lado si el otro gana claro.
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

        /// <summary>Huellas de los edificios existentes dentro de la caja del contorno.</summary>
        List<Quad> BuildingFootprints(List<Pt> outline)
        {
            // Se filtra por la caja de la huella, no por el centro: un edificio grande puede tener el centro lejos y
            // aun así meterse en la manzana.
            double minX = outline.Min(p => p.X), maxX = outline.Max(p => p.X);
            double minZ = outline.Min(p => p.Y), maxZ = outline.Max(p => p.Y);
            const double Reach = 300; // ningún edificio tiene el centro más lejos que esto de su borde
            var result = new List<Quad>();
            using (var entities = buildingQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    var tr = EntityManager.GetComponentData<Transform>(e);
                    if (tr.m_Position.x < minX - Reach || tr.m_Position.x > maxX + Reach || tr.m_Position.z < minZ - Reach || tr.m_Position.z > maxZ + Reach)
                    {
                        continue;
                    }

                    var prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!EntityManager.HasComponent<ObjectGeometryData>(prefab))
                    {
                        continue;
                    }

                    var bounds = EntityManager.GetComponentData<ObjectGeometryData>(prefab).m_Bounds;
                    float3 P(float x, float z) => tr.m_Position + math.rotate(tr.m_Rotation, new float3(x, 0, z));
                    var a = P(bounds.min.x, bounds.min.z);
                    var b = P(bounds.max.x, bounds.min.z);
                    var c = P(bounds.max.x, bounds.max.z);
                    var d = P(bounds.min.x, bounds.max.z);
                    var q = new Quad(new Pt(a.x, a.z), new Pt(b.x, b.z), new Pt(c.x, c.z), new Pt(d.x, d.z));
                    if (q.MaxX >= minX && q.MinX <= maxX && q.MaxY >= minZ && q.MinY <= maxZ)
                    {
                        result.Add(q);
                    }
                }
            }

            return result;
        }

        /// <summary>Árboles que ya hay dentro de la caja del contorno.</summary>
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

        /// <summary>Prepara el dibujo del área: franjas paralelas que la rellenan y su contorno.</summary>
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
            var log = new StringBuilder($"[selección] {description}: {selection.Count} bloques.\n");
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

        /// <summary>Contorno de las celdas elegidas y visibles: los lados que no tocan otra celda elegida.</summary>
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

            // Según ZoneUtils.GetCellPosition: x crece hacia -Right(dir) e y crece hacia -dir.
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

        static Quad ToQuad(Quad2 q) => new Quad(new Pt(q.a.x, q.a.y), new Pt(q.b.x, q.b.y), new Pt(q.c.x, q.c.y), new Pt(q.d.x, q.d.y));

        /// <summary>Celdas visibles del bloque que no comparte con otro: indica cuánto "le pertenece" la zona.</summary>
        int OwnedCells(SelectedBlock s)
        {
            if (s.Virtual != null)
            {
                return s.Virtual.Width * 100; // parcelas propias: las aceras más largas primero
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

        /// <summary>Marca como ocupadas las celdas cuyo centro cae dentro de un edificio ya planificado en otro bloque.</summary>
        static int MaskFootprints(PlanGrid grid, Block block, int4 area, List<Quad> footprints, float overlap)
        {
            if (footprints.Count == 0)
            {
                return 0;
            }

            // Solape real entre la celda (metida "overlap" m: lo que se admite que se pisen) y cada
            // huella. Antes se probaban el centro y las esquinas metidas 1 m: entre rejillas que no coinciden (dos
            // calles que se cruzan, con el sobrante de una tira en la esquina) se colaban solapes pequeños, y el juego
            // marcaba en rojo los dos edificios.
            int masked = 0;
            float2 r = MathUtils.Right(block.m_Direction) * (ZoneUtils.CELL_SIZE * 0.5f - overlap);
            float2 f = block.m_Direction * (ZoneUtils.CELL_SIZE * 0.5f - overlap);
            for (int y = 0; y < grid.Depth; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.Free[x, y])
                    {
                        continue;
                    }

                    float2 c = ZoneUtils.GetCellPosition(block, new int2(area.x + x, area.z + y)).xz;
                    Pt P(float2 v) => new Pt(v.x, v.y);
                    var cell = new Quad(P(c + r + f), P(c - r + f), P(c - r - f), P(c + r - f));
                    foreach (var fp in footprints)
                    {
                        if (fp.MaxX >= cell.MinX && fp.MinX <= cell.MaxX && fp.MaxY >= cell.MinY && fp.MinY <= cell.MaxY && Quad.Distance(fp, cell) <= 0)
                        {
                            grid.Free[x, y] = false;
                            masked++;
                            break;
                        }
                    }
                }
            }

            return masked;
        }


        /// <summary>Un bloque de Platter es el que pertenece a una entidad con algún componente de un tipo "Platter.*".</summary>
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

        /// <summary>
        /// Celda utilizable: visible y sin bloquear ni ocupar. Las compartidas con otro bloque (Shared) sí valen: el
        /// reparto entre bloques lo hace MaskFootprints con lo ya planificado.
        /// </summary>
        static bool IsFree(Cell c) =>
            (c.m_State & CellFlags.Visible) != 0 &&
            (c.m_State & (CellFlags.Blocked | CellFlags.Occupied)) == 0;

        /// <summary>
        /// Rejilla del área válida del bloque. En el juego la fila 0 es siempre la que da a la calle (Roadside), y
        /// x = 0 es el primer índice de celda del bloque.
        /// </summary>
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

            // Igual que LotSizeJobs: esquina izquierda = RoadLeft en la primera celda, derecha = RoadRight en la última.
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
            sb.AppendLine($"  bloque {s.Entity.Index}: pos=({b.m_Position.x:F1},{b.m_Position.z:F1}) dir=({b.m_Direction.x:F2},{b.m_Direction.y:F2}) size={b.m_Size.x}x{b.m_Size.y} area=[{s.Area.x},{s.Area.y})x[{s.Area.z},{s.Area.w}) libres={grid.FreeCount()} celda0={first} celdaFin={last}");
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

        void DrawSelection()
        {
            bool snapMarker = mode == "polygon" && selection.Count == 0 && hasHover && hoverSnapped;
            if (selection.Count == 0 && polygon.Count == 0 && !snapMarker)
            {
                return;
            }

            var buffer = overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            if (areaEdge.Count > 0)
            {
                var fill = new UnityEngine.Color(0.3f, 0.85f, 0.45f, Previewing ? 0.18f : 0.35f);
                var edgeColor = new UnityEngine.Color(1f, 0.82f, 0.2f, 1f);
                foreach (var (a, b) in areaFill)
                {
                    buffer.DrawLine(fill, fill, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(a, b), FillStripe, new float2(0, 0));
                }

                foreach (var (a, b) in areaEdge)
                {
                    buffer.DrawLine(edgeColor, edgeColor, 0f, OverlayRenderSystem.StyleFlags.Projected, new Line3.Segment(a, b), 1.2f, new float2(1, 1));
                }
            }

            var color = Previewing ? new UnityEngine.Color(0.3f, 0.9f, 0.4f, 0.9f) : new UnityEngine.Color(1f, 0.8f, 0.2f, 0.9f);
            foreach (var s in selection)
            {
                foreach (var (a, b) in s.Outline)
                {
                    buffer.DrawLine(color, new Line3.Segment(a, b), 0.8f);
                }
            }

            var snapColor = new UnityEngine.Color(0.3f, 0.95f, 0.85f, 1f);
            if (snapMarker)
            {
                buffer.DrawCircle(snapColor, hoverPos, 4f);
            }

            if (polygon.Count > 0)
            {
                var lineColor = new UnityEngine.Color(0.4f, 0.75f, 1f, 0.95f);
                void Path(List<float3> pts, float width, UnityEngine.Color color)
                {
                    for (int k = 1; k < pts.Count; k++)
                    {
                        buffer.DrawLine(color, new Line3.Segment(pts[k - 1], pts[k]), width);
                    }
                }

                // Cerrado, el contorno ya lo dibuja el área; mientras se dibuja, cada lado sigue el bordillo si toca.
                bool closed = selection.Count > 0;
                if (!closed)
                {
                    for (int i = 1; i < polygon.Count; i++)
                    {
                        Path(PolygonSegment(polygon[i - 1], polyCurb[i - 1], polyArc[i - 1], polygon[i], polyCurb[i], polyArc[i]), 1f, polyCurb[i] >= 0 && polyCurb[i] == polyCurb[i - 1] ? snapColor : lineColor);
                    }

                    if (hasHover)
                    {
                        int last = polygon.Count - 1;
                        bool follows = hoverSnapped && polyCurb[last] >= 0 && polyCurb[last] == hoverHit.Curb;
                        Path(PolygonSegment(polygon[last], polyCurb[last], polyArc[last], hoverPos, hoverSnapped ? hoverHit.Curb : -1, hoverHit.Arc), 0.6f, follows ? snapColor : lineColor);
                    }
                }

                // Punto de cierre: más grande, y resaltado cuando el cursor está encima.
                bool nearFirst = !closed && hasHover && polygon.Count >= 3 && math.distance(hoverPos.xz, polygon[0].xz) <= 12f;
                buffer.DrawCircle(nearFirst ? new UnityEngine.Color(0.3f, 0.9f, 0.4f, 1f) : lineColor, polygon[0], nearFirst ? 8f : 5f);
                for (int i = 1; i < polygon.Count; i++)
                {
                    buffer.DrawCircle(lineColor, polygon[i], 3f);
                }
            }

            overlay.AddBufferWriter(default(JobHandle));
        }

        void Say(string message)
        {
            Message = message;
            MessageSerial++;
        }
    }
}
