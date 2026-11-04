using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildingCollections.Core;
using Colossal.Mathematics;
using Game.Buildings;
using Game.City;
using Game.Common;
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
        }

        sealed class Planned
        {
            public string Name;
            public Entity Prefab;
            public Block Block;
            public int2 Min;
            public int2 Max;
            public bool Corner;
        }

        readonly List<SelectedBlock> selection = new List<SelectedBlock>();
        readonly List<Planned> preview = new List<Planned>();
        EntityQuery blockQuery;
        EntityQuery tempBuildingQuery;
        EntityQuery definitionQuery;
        OverlayRenderSystem overlay;
        CityConfigurationSystem city;
        BuiltGuardSystem guard;
        readonly List<float3> polygon = new List<float3>();
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
        public int Lots => Previewing ? preview.Count : freeFrontCells;

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
            selection.Clear();
            preview.Clear();
            freeFrontCells = 0;
            definitionsDirty = true;
            Say("Selection cleared.");
        }

        /// <summary>Calcula una combinación nueva con la semilla dada y pide al juego la vista previa.</summary>
        public void GeneratePreview(IReadOnlyList<CatalogEntry> candidates, int density, int seed)
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
                var log = new StringBuilder($"[vista previa] semilla {seed}, densidad {density}%, {plain.Count} normales, {corners.Count} esquineros.\n");

                // Huellas (en el suelo) de lo ya planificado: las celdas compartidas entre bloques que queden libres
                // detrás de un edificio poco profundo se aprovechan desde la otra calle, sin solaparse.
                var footprints = new List<Quad>();

                // Primero los bloques con más celdas propias (no compartidas): las calles largas antes que las laterales.
                foreach (var s in selection.OrderByDescending(OwnedCells).ToList())
                {
                    if (!EntityManager.Exists(s.Entity) || EntityManager.HasComponent<Deleted>(s.Entity))
                    {
                        selection.Remove(s);
                        continue;
                    }

                    // El bloque puede haber cambiado desde la selección (obras en la calle): se relee.
                    s.Block = EntityManager.GetComponentData<Block>(s.Entity);
                    s.Area = EntityManager.GetComponentData<ValidArea>(s.Entity).m_Area;
                    if (s.Area.y <= s.Area.x || s.Area.w <= s.Area.z)
                    {
                        continue;
                    }

                    var cells = EntityManager.GetBuffer<Cell>(s.Entity, true);
                    if (cells.Length < s.Block.m_Size.x * s.Block.m_Size.y || (s.Include != null && s.Include.Length != cells.Length))
                    {
                        continue;
                    }

                    s.Outline = BuildOutline(s, cells, m_TerrainSystem.GetHeightData());
                    var grid = BuildGrid(s.Block, s.Area, cells, s.Include);
                    int masked = MaskFootprints(grid, s.Block, s.Area, footprints);
                    var plan = GridPlanner.Plan(grid, plain, corners, density, seed + 7919 * index++);
                    foreach (var p in plan)
                    {
                        var min = new int2(s.Area.x + p.X, s.Area.z + p.Y);
                        var max = min + new int2(p.Building.LotWidth, p.Building.LotDepth);
                        footprints.Add(ToQuad(ZoneUtils.CalculateCorners(s.Block, new ValidArea { m_Area = new int4(min.x, max.x, min.y, max.y) })));
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

                    log.AppendLine($"  bloque {s.Entity.Index}: rejilla {grid.Width}x{grid.Depth}, {grid.FreeCount()} libres ({masked} tapadas por otros bloques), esquinas L={grid.LeftCorner} R={grid.RightCorner} -> {string.Join("; ", plan.Select(p => $"x{p.X} {p.Building}{(p.IsCorner ? " [esquina]" : string.Empty)}"))}");
                }

                Mod.Log.Info(log.ToString().TrimEnd());
                Say(preview.Count == 0 ? "Nothing fits in the selected area with these filters." : $"Preview: {preview.Count} buildings.");
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                preview.Clear();
                Say("Preview failed (see log).");
            }
        }

        public void RequestBuild() => buildRequested = true;

        // ---------------- Bucle de la herramienta ----------------

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                DrawSelection();

                hasHover = GetRaycastResult(out Entity _, out RaycastHit hit);
                if (hasHover)
                {
                    hoverPos = hit.m_HitPosition;
                }

                if (cancelAction.WasPressedThisFrame())
                {
                    if (mode == "polygon" && polygon.Count > 0 && selection.Count == 0)
                    {
                        polygon.RemoveAt(polygon.Count - 1);
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

            if (cfg.RequireNoErrors && !GetAllowApply())
            {
                Mod.Log.Info($"[build] Cancelado: hay entidades con error ({m_ErrorQuery.CalculateEntityCount()}).");
                Say("Some buildings in the preview have errors (red). Try a new combination.");
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

                var t = new Transform(ZoneUtils.GetPosition(p.Block, p.Min, p.Max), ZoneUtils.GetRotation(p.Block));
                float3 front = BuildingUtils.CalculateFrontPosition(t, p.Max.y - p.Min.y);
                float y = TerrainUtils.SampleHeight(ref heights, front);
                if (cfg.UsePlacementOffset && EntityManager.HasComponent<PlaceableObjectData>(p.Prefab))
                {
                    var pod = EntityManager.GetComponentData<PlaceableObjectData>(p.Prefab);
                    if ((pod.m_Flags & (PlacementFlags.Shoreline | PlacementFlags.Floating)) == 0)
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
                definitionsDirty = true;
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
            Say(polygon.Count < 3 ? $"{polygon.Count} points. Keep clicking corners." : $"{polygon.Count} points. Click the first point to close.");
        }

        void SelectPolygon()
        {
            var cfg = DevConfig.Load();
            var poly = polygon.Select(p => new Pt(p.x, p.z)).ToList();
            float2 centroid = float2.zero;
            foreach (var p in polygon)
            {
                centroid += p.xz;
            }

            centroid /= polygon.Count;
            double radius = polygon.Max(p => (double)math.distance(p.xz, centroid)) + 80;
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
                polygon.Clear();
                Say("The polygon contains no zoned cells.");
                return;
            }

            SetSelection(chosen, $"polígono de {polygon.Count} vértices, {platter} de Platter ignorados");
        }

        void SetSelection(IEnumerable<SelectedBlock> blocks, string description)
        {
            var cfg = DevConfig.Load();
            selection.Clear();
            selection.AddRange(blocks);
            preview.Clear();
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
        static int MaskFootprints(PlanGrid grid, Block block, int4 area, List<Quad> footprints)
        {
            if (footprints.Count == 0)
            {
                return 0;
            }

            int masked = 0;
            float2 r = MathUtils.Right(block.m_Direction) * (ZoneUtils.CELL_SIZE * 0.5f - 1f);
            float2 f = block.m_Direction * (ZoneUtils.CELL_SIZE * 0.5f - 1f);
            for (int y = 0; y < grid.Depth; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.Free[x, y])
                    {
                        continue;
                    }

                    // Centro y esquinas (metidas 1 m) de la celda: así también se tapa una celda de una rejilla girada
                    // cuyo centro cae justo fuera del edificio pero cuyo cuerpo lo pisa.
                    float2 c = ZoneUtils.GetCellPosition(block, new int2(area.x + x, area.z + y)).xz;
                    var probes = new[] { c, c + r + f, c + r - f, c - r + f, c - r - f };
                    bool hit = false;
                    foreach (var fp in footprints)
                    {
                        foreach (var q in probes)
                        {
                            if (fp.Contains(new Pt(q.x, q.y)))
                            {
                                hit = true;
                                break;
                            }
                        }

                        if (hit) break;
                    }

                    if (hit)
                    {
                        grid.Free[x, y] = false;
                        masked++;
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
            if (selection.Count == 0 && polygon.Count == 0)
            {
                return;
            }

            var buffer = overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var color = Previewing ? new UnityEngine.Color(0.3f, 0.9f, 0.4f, 0.9f) : new UnityEngine.Color(1f, 0.8f, 0.2f, 0.9f);
            foreach (var s in selection)
            {
                foreach (var (a, b) in s.Outline)
                {
                    buffer.DrawLine(color, new Line3.Segment(a, b), 0.8f);
                }
            }

            if (polygon.Count > 0)
            {
                var lineColor = new UnityEngine.Color(0.4f, 0.75f, 1f, 0.95f);
                for (int i = 1; i < polygon.Count; i++)
                {
                    buffer.DrawLine(lineColor, new Line3.Segment(polygon[i - 1], polygon[i]), 1f);
                }

                bool closed = selection.Count > 0;
                if (closed)
                {
                    buffer.DrawLine(lineColor, new Line3.Segment(polygon[polygon.Count - 1], polygon[0]), 1f);
                }
                else if (hasHover)
                {
                    buffer.DrawLine(lineColor, new Line3.Segment(polygon[polygon.Count - 1], hoverPos), 0.6f);
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
