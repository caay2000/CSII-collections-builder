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
    /// Herramienta del constructor de manzanas. Clic: selecciona la manzana (los bloques de zona conectados con el
    /// clicado). Clic derecho: borra la selección. La vista previa y la construcción usan el mismo camino que el
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
            public Quad2 Corners;
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

        public void ClearSelection()
        {
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
                foreach (var s in selection.ToList())
                {
                    if (!EntityManager.Exists(s.Entity) || EntityManager.HasComponent<Deleted>(s.Entity))
                    {
                        continue;
                    }

                    // El bloque puede haber cambiado desde la selección (obras en la calle): se relee.
                    s.Block = EntityManager.GetComponentData<Block>(s.Entity);
                    s.Area = EntityManager.GetComponentData<ValidArea>(s.Entity).m_Area;
                    if (s.Area.y <= s.Area.x || s.Area.w <= s.Area.z)
                    {
                        continue;
                    }

                    s.Corners = ZoneUtils.CalculateCorners(s.Block, new ValidArea { m_Area = s.Area });

                    var cells = EntityManager.GetBuffer<Cell>(s.Entity, true);
                    if (cells.Length < s.Block.m_Size.x * s.Block.m_Size.y)
                    {
                        continue;
                    }

                    var grid = BuildGrid(s.Block, s.Area, cells);
                    var plan = GridPlanner.Plan(grid, plain, corners, density, seed + 7919 * index++);
                    foreach (var p in plan)
                    {
                        var min = new int2(s.Area.x + p.X, s.Area.z + p.Y);
                        preview.Add(new Planned
                        {
                            Name = p.Building.PrefabName,
                            Prefab = prefabs[p.Building.PrefabName],
                            Block = s.Block,
                            Min = min,
                            Max = min + new int2(p.Building.LotWidth, p.Building.LotDepth),
                            Corner = p.IsCorner,
                        });
                    }

                    log.AppendLine($"  bloque {s.Entity.Index}: rejilla {grid.Width}x{grid.Depth}, {grid.FreeCount()} libres, esquinas L={grid.LeftCorner} R={grid.RightCorner} -> {string.Join("; ", plan.Select(p => $"x{p.X} {p.Building}{(p.IsCorner ? " [esquina]" : string.Empty)}"))}");
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

                if (cancelAction.WasPressedThisFrame())
                {
                    ClearSelection();
                }
                else if (applyAction.WasPressedThisFrame() && GetRaycastResult(out Entity _, out RaycastHit hit))
                {
                    SelectAt(hit.m_HitPosition);
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

        void SelectAt(float3 point)
        {
            var cfg = DevConfig.Load();
            if (Mod.Settings?.Mode == "polygon")
            {
                Mod.Log.Info("Modo polígono aún no disponible: se usa la selección de manzana.");
            }

            var blocks = new List<SelectedBlock>();
            int platter = 0;
            using (var entities = blockQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in entities)
                {
                    // Prefiltro barato: solo bloques cerca del clic (una manzana enorme sigue cabiendo en el radio).
                    var pos = EntityManager.GetComponentData<Block>(e).m_Position;
                    if (math.distancesq(pos.xz, point.xz) > cfg.SearchRadius * cfg.SearchRadius)
                    {
                        continue;
                    }

                    var area = EntityManager.GetComponentData<ValidArea>(e).m_Area;
                    if (area.y <= area.x || area.w <= area.z)
                    {
                        continue;
                    }

                    if (IsPlatterBlock(e))
                    {
                        platter++;
                        continue;
                    }

                    var block = EntityManager.GetComponentData<Block>(e);
                    var v = new ValidArea { m_Area = area };
                    blocks.Add(new SelectedBlock { Entity = e, Block = block, Area = area, Corners = ZoneUtils.CalculateCorners(block, v) });
                }
            }

            var quads = blocks.Select(b => ToQuad(b.Corners)).ToList();
            var click = new Pt(point.x, point.z);
            int start = quads.FindIndex(q => q.Contains(click));
            if (start < 0)
            {
                var clickQuad = new Quad(click, click, click, click);
                double best = cfg.ClickSnapDistance;
                for (int i = 0; i < quads.Count; i++)
                {
                    double d = Quad.Distance(quads[i], clickQuad);
                    if (d <= best)
                    {
                        best = d;
                        start = i;
                    }
                }
            }

            if (start < 0)
            {
                Mod.Log.Info($"[selección] clic en ({point.x:F1}, {point.z:F1}): ningún bloque de zona ({blocks.Count} bloques, {platter} de Platter ignorados).");
                Say("Click inside a zoned block.");
                return;
            }

            var group = BlockGrouping.Flood(quads, start, cfg.BlockTouchTolerance);
            selection.Clear();
            selection.AddRange(group.Select(i => blocks[i]));
            preview.Clear();
            definitionsDirty = true;

            freeFrontCells = 0;
            var log = new StringBuilder($"[selección] clic en ({point.x:F1}, {point.z:F1}): {selection.Count} bloques (tolerancia {cfg.BlockTouchTolerance} m, {platter} de Platter ignorados).\n");
            foreach (var s in selection)
            {
                var cells = EntityManager.GetBuffer<Cell>(s.Entity, true);
                var grid = BuildGrid(s.Block, s.Area, cells);
                for (int x = 0; x < grid.Width; x++)
                {
                    if (grid.Free[x, 0]) freeFrontCells++;
                }

                AppendBlock(log, s, cells, grid, cfg.LogCells);
            }

            Mod.Log.Info(log.ToString().TrimEnd());
            Say($"{selection.Count} blocks selected, {freeFrontCells} free roadside cells.");
        }

        static Quad ToQuad(Quad2 q) => new Quad(new Pt(q.a.x, q.a.y), new Pt(q.b.x, q.b.y), new Pt(q.c.x, q.c.y), new Pt(q.d.x, q.d.y));

        /// <summary>Un bloque de Platter es el que pertenece a una entidad con algún componente de un tipo "Platter.*".</summary>
        bool IsPlatterBlock(Entity block)
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

        /// <summary>Celda utilizable: visible (no tapada por otro bloque o por la calle) y sin bloquear, ocupar ni compartir.</summary>
        static bool IsFree(Cell c) =>
            (c.m_State & CellFlags.Visible) != 0 &&
            (c.m_State & (CellFlags.Blocked | CellFlags.Occupied | CellFlags.Shared)) == 0;

        /// <summary>
        /// Rejilla del área válida del bloque. En el juego la fila 0 es siempre la que da a la calle (Roadside), y
        /// x = 0 es el primer índice de celda del bloque.
        /// </summary>
        static PlanGrid BuildGrid(Block block, int4 area, DynamicBuffer<Cell> cells)
        {
            int w = area.y - area.x, d = area.w - area.z, size = block.m_Size.x;
            var grid = PlanGrid.AllFree(w, d, false, false);
            for (int y = 0; y < d; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    grid.Free[x, y] = IsFree(cells[(area.z + y) * size + area.x + x]);
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
            if (selection.Count == 0)
            {
                return;
            }

            var buffer = overlay.GetBuffer(out JobHandle deps);
            deps.Complete();
            var color = Previewing ? new UnityEngine.Color(0.3f, 0.9f, 0.4f, 0.9f) : new UnityEngine.Color(1f, 0.8f, 0.2f, 0.9f);
            foreach (var s in selection)
            {
                var q = s.Corners;
                float y = s.Block.m_Position.y + 0.5f;
                var a = new float3(q.a.x, y, q.a.y);
                var b = new float3(q.b.x, y, q.b.y);
                var c = new float3(q.c.x, y, q.c.y);
                var d = new float3(q.d.x, y, q.d.y);
                buffer.DrawLine(color, new Line3.Segment(a, b), 1f);
                buffer.DrawLine(color, new Line3.Segment(b, c), 1f);
                buffer.DrawLine(color, new Line3.Segment(c, d), 1f);
                buffer.DrawLine(color, new Line3.Segment(d, a), 1f);
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
