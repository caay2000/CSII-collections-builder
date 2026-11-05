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

            /// <summary>Tira de la que sale (solo diagnóstico: largo real, sobrante, curva).</summary>
            public LotStrip Strip;
        }

        sealed class Planned
        {
            public string Name;
            public Entity Prefab;
            public Block Block;
            public int2 Min;
            public int2 Max;
            public bool Corner;

            /// <summary>Tira o bloque al que pertenece.</summary>
            public SelectedBlock Owner;

            /// <summary>Desplazamiento (m) a lo largo de la fachada, para cerrar el hueco con el edificio de al lado.</summary>
            public float Shift;

            /// <summary>Planta de la decoración: se coloca en Pos / Rot, no en una parcela.</summary>
            public bool IsDecor;
            public byte Age = (byte)Game.Tools.AgeMask.Mature;
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
        // Limpieza automática: unos fotogramas después de crear las definiciones (el juego ya ha marcado los
        // errores), se quita de la vista previa lo que sale en rojo, para que Build funcione a la primera.
        const int SettleFrames = 10, WatchFrames = 60, MaxAutoCleanPasses = 3;
        int settleFrames = -1;
        int autoCleanPasses;

        public override string toolID => "BuildingCollections.BlockBuilder";

        public bool AreaSelected => selection.Count > 0;

        public bool Previewing => preview.Count > 0;

        /// <summary>Parcelas: edificios de la vista previa, o celdas frontales libres si aún no hay vista previa.</summary>
        public int Lots => Previewing ? preview.Count(p => !p.IsDecor) : freeFrontCells;

        /// <summary>Vista previa: celdas a la calle ocupadas y las que había libres (para comparar con la densidad).</summary>
        public int FrontFilled { get; private set; }

        public int FrontTotal { get; private set; }

        /// <summary>Centros de las celdas a la calle de la selección, cada una una sola vez (la de una esquina da a
        /// dos calles y está en las dos tiras).</summary>
        List<Pt> frontCells = new List<Pt>();

        /// <summary>Centros de los edificios que ha quitado la densidad: cerrar un hueco no debe rellenarlos.</summary>
        List<Pt> thinned = new List<Pt>();

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
            FrontFilled = FrontTotal = 0;
            frontCells = new List<Pt>();
            definitionsDirty = true;
            Say("Selection cleared.");
        }

        /// <summary>Calcula una combinación nueva con la semilla dada y pide al juego la vista previa.</summary>
        public void GeneratePreview(IReadOnlyList<CatalogEntry> candidates, int density, int seed, bool leaveGaps,
            IReadOnlyList<CatalogEntry> decor = null, int decorDensity = 50, int ageMask = 4)
        {
            try
            {
                preview.Clear();
                FrontFilled = FrontTotal = 0;
                frontCells = new List<Pt>();
                autoCleanPasses = 0;
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
                // 0 en las opciones = sin solape, igual que sin Anarchy (0,05 m de margen para que tocarse no cuente).
                // Casilla "Overlap buildings": 0,5 m o nada (un 1 m o 2 m guardado de antes cuenta como 0,5).
                float overlap = anarchy && (Mod.Settings?.AnarchyOverlap ?? 0.5f) > 0 ? OverlapOn : StrictOverlap;
                log.Append($"  solape admitido {overlap:F2} m{(anarchy ? " (Anarchy)" : string.Empty)}.\n");

                // Lo que ya está construido no se pisa nunca, ni con Anarchy: se relee ahora (la selección puede ser de
                // antes de un Build) y se tapan sus celdas sin margen de solape.
                var existing = areaOutline != null ? BuildingFootprints(areaOutline) : new List<Quad>();
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
                frontCells = new List<Pt>();
                thinned = new List<Pt>();
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

                    MaskFootprints(grid, s.Block, s.Area, existing, StrictOverlap);
                    grids.Add((s, grid));
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (!grid.Free[x, 0])
                        {
                            continue;
                        }

                        var c = ZoneUtils.GetCellPosition(s.Block, new int2(s.Area.x + x, s.Area.z)).xz;
                        var pt = new Pt(c.x, c.y);
                        if (!frontCells.Any(o => (o.X - pt.X) * (o.X - pt.X) + (o.Y - pt.Y) * (o.Y - pt.Y) < 4))
                        {
                            frontCells.Add(pt);
                        }
                    }
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

                    // Cada calle al 100 %: la densidad se aplica luego a toda la selección (ver GridPlanner.Thin).
                    var plan = GridPlanner.Plan(grid, plain, corners, 100, seed + 7919 * index++, leaveGaps, cornerPlans[s]);
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
                            Owner = s,
                        });
                    }

                    log.AppendLine($"  {(s.Virtual != null ? "tira" : "bloque " + s.Entity.Index)}: rejilla {grid.Width}x{grid.Depth}, {grid.FreeCount()} libres ({masked} tapadas por otros bloques), esquinas L={grid.LeftCorner} R={grid.RightCorner} -> {string.Join("; ", plan.Select(p => $"x{p.X} {p.Building}{(p.IsCorner ? " [esquina]" : string.Empty)}"))}");
                }

                // Densidad sobre toda la selección: por calle, en calles de 3 o 4 celdas, un 90 % se quedaba en un 70 %.
                if (density < 100)
                {
                    // Cada edificio cuenta las celdas a la calle que cubre (un esquinero, las de sus dos calles); los que no
                    // dan a la calle no entran en el recuento ni se quitan.
                    var taken = new bool[frontCells.Count];
                    var front = new List<(Planned p, int cells)>();
                    foreach (var p in preview.Where(p => !p.IsDecor))
                    {
                        var q = LotQuad(p, p.Shift, 0.5f);
                        int n = 0;
                        for (int i = 0; i < frontCells.Count; i++)
                        {
                            if (!taken[i] && q.Contains(frontCells[i]))
                            {
                                taken[i] = true;
                                n++;
                            }
                        }

                        if (n > 0)
                        {
                            front.Add((p, n));
                        }
                    }

                    var removed = GridPlanner.Thin(front.Select(f => (f.cells, f.p.Corner)).ToList(), frontCells.Count, density,
                        new System.Random(seed ^ 0x1b873593));
                    var gone = new HashSet<Planned>(removed.Select(i => front[i].p));
                    thinned = gone.Select(p => LotQuad(p, p.Shift, 0f)).Select(q => new Pt(q.P.Average(v => v.X), q.P.Average(v => v.Y))).ToList();
                    preview.RemoveAll(gone.Contains);
                    footprints = preview.Where(p => !p.IsDecor).Select(p => (LotQuad(p, p.Shift, 0f), p.Owner)).ToList();
                    log.AppendLine($"  densidad {density}%: quitados {gone.Count} edificios ({removed.Sum(i => front[i].cells)} celdas a la calle) de {frontCells.Count} celdas.");
                }

                // Cada calle se rellena con edificios de 8 m en 8 m: al final suele quedar un trozo de celda hasta el
                // esquinero de la calle de al lado. Con Anarchy se cierra ensanchando uno y apretando la cadena.
                int justified = 0;
                var gapRng = new System.Random(seed ^ 0x2545f491);
                foreach (var (s, grid) in grids)
                {
                    justified += CloseEndGaps(s, existing, overlap, plain, prefabs, gapRng, log);
                }

                if (justified > 0)
                {
                    footprints = preview.Where(p => !p.IsDecor).Select(p => (LotQuad(p, p.Shift, 0f), p.Owner)).ToList();
                }

                // Fachada ocupada: celdas a la calle (cada una una vez, también las de las esquinas) con algún edificio.
                FrontTotal = frontCells.Count;
                FrontFilled = CountFront();
                log.AppendLine($"  fachada: {FrontFilled} de {FrontTotal} celdas a la calle ocupadas.");
                // Solo diagnóstico: si falla, no debe tirar una vista previa buena.
                try
                {
                    LogUncovered(existing, log);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e);
                }

                // Decoración del espacio sobrante (patio, huecos): plantas de la colección elegida, lejos de la acera y de
                // los edificios (los planificados y los que ya hay), y sin pisar árboles existentes.
                int buildingsPlanned = preview.Count;
                int plants = 0;
                if (decor != null && decor.Count > 0 && areaOutline != null && decorAreas.Count > 0)
                {
                    var blockers = footprints.Select(f => f.quad).ToList();
                    blockers.AddRange(existing);
                    var existingTrees = TreePoints(areaOutline);

                    // El juego comprueba el choque con la copa del árbol: cada planta necesita su radio libre hasta
                    // el edificio más cercano, o salen en rojo los dos.
                    var radii = decor.Select(d => PlantRadius(d.Prefab)).ToList();
                    const double PlantMargin = 0.5;
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

                        var pick = decor[k];
                        preview.Add(new Planned
                        {
                            Name = pick.Info.PrefabName,
                            Prefab = pick.Prefab,
                            IsDecor = true,
                            Age = (byte)((ageMask & 15) == 0 ? 4 : ageMask & 15),
                            Pos = new float3((float)q.X, 0, (float)q.Y),
                            Rot = quaternion.RotateY((float)(rng.NextDouble() * Math.PI * 2)),
                        });
                    }

                    plants = spots.Count - tooBig;
                    log.AppendLine($"  decoración: {plants} plantas de {decor.Count} tipos, densidad {decorDensity}%; {tooBig} puntos sin sitio para ninguna; radios {radii.Min():F1}-{radii.Max():F1} m.");
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
                    settleFrames = preview.Count > 0 ? 0 : -1;
                    applyMode = ApplyMode.Clear;
                    return CreatePreviewDefinitions(inputDeps);
                }

                AutoClean();
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

            // Sin excepción para Anarchy: Anarchy ya quita los errores que permite (solapes); los demás (agua, fuera
            // del mapa...) tienen que seguir parando la construcción.
            if (cfg.RequireNoErrors && !GetAllowApply())
            {
                // Se quitan de la vista previa los que el juego marca en rojo (chocan con algo) y se pide otro Build.
                int removed = RemoveErrored();
                FrontFilled = CountFront();
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
            // La zona construida deja de estar seleccionada: así no queda duda de qué está hecho, y una vista previa
            // nueva no se calcula sobre la rejilla de antes del Build.
            int filled = CountFront(), total = FrontTotal;
            ClearSelection();
            Say(total > 0 ? $"Built {temps.Count} buildings: {filled} of {total} street-front cells filled ({100 * filled / total}%)." : $"Built {temps.Count} buildings.");
            definitionsDirty = true; // el siguiente fotograma borra las definiciones ya aplicadas
            applyMode = ApplyMode.Apply;
            return inputDeps;
        }

        /// <summary>
        /// Con la vista previa ya asentada, quita lo que el juego marca en rojo (sobre todo árboles que tocan un
        /// edificio) sin esperar al Build. Como mucho unas pocas pasadas por vista previa, para no entrar en bucle.
        /// </summary>
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

            // Si aún no hay errores se sigue mirando un rato: el juego puede tardar más en marcarlos con FPS bajos.
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
                Mod.Log.Info($"[vista previa] Limpieza automática (pasada {autoCleanPasses}): quitados {removed} elementos en rojo.");
                definitionsDirty = true;
            }
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

            // Un árbol que toca un edificio deja en rojo a los dos: se quita el árbol y el edificio se queda. Un
            // edificio en rojo solo se quita si no tiene ninguna planta cerca que lo explique (si el error era otro,
            // seguirá en rojo en el siguiente Build y se quitará entonces).
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
                var lot = LotQuad(p, p.Shift, 0f);
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
                Mod.Log.Info($"[build] Quitados por error: {drop.Count(p => !p.IsDecor)} edificios y {drop.Count(p => p.IsDecor)} plantas; " +
                    $"{buildingsKept} edificios en rojo se quedan al quitar las plantas que los tocaban ({string.Join(", ", bad.Take(8).Select(b => $"({b.x:F0},{b.z:F0})"))}).");
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
                lastSidesFull = false;
                if (face != null && cfg.UseVirtualLots && SelectVirtual(face, path, $"manzana de calles ({path.Count} tramos), clic en ({point.x:F1}, {point.z:F1})"))
                {
                    return;
                }

                // Manzana encontrada pero sin una celda libre (ya construida entera): no se cae a las celdas de zona,
                // que dibujaban el contorno por el eje de las calles (cruzando la calzada) sin nada que construir.
                if (face != null && lastSidesFull)
                {
                    Mod.Log.Info($"[selección] clic en ({point.x:F1}, {point.z:F1}): la manzana ya está llena; no se selecciona.");
                    Say("This block is already full.");
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
        /// <summary>La última manzana probada tenía aceras con parcelas, pero ninguna libre.</summary>
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

        /// <summary>Parcelas propias a lo largo de los lados con calle (frontage null = todos) de un contorno.</summary>
        bool SelectVirtualSides(List<List<Pt>> sides, List<bool> frontage, string description)
        {
            var outline = VirtualLots.Outline(sides);
            if (outline.Count < 3)
            {
                return false;
            }

            var obstacles = BuildingFootprints(outline);
            var notes = new List<string>();
            var strips = VirtualLots.Build(sides, obstacles, frontage: frontage, notes: notes);
            Mod.Log.Info($"[selección] contorno: {sides.Count} aceras, {outline.Count} puntos, {obstacles.Count} edificios existentes.\n  " + string.Join("\n  ", notes));
            lastSidesFull = strips.Count > 0 && strips.All(st => st.Grid.FreeCount() == 0);
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
                    var notes = new List<string>();
                    strips.AddRange(VirtualLots.Build(sides, obstacles, clip: clip, notes: notes));
                    Mod.Log.Info($"[selección] polígono, manzana {faces.Count}: {sides.Count} aceras.\n  " + string.Join("\n  ", notes));
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
            var log = new StringBuilder($"[selección] {description}, {strips.Count} tiras.\n");
            foreach (var s in chosen)
            {
                for (int x = 0; x < s.Virtual.Width; x++)
                {
                    if (s.Virtual.Free[x, 0]) freeFrontCells++;
                }

                log.AppendLine($"  tira {s.Virtual.Width}x{s.Virtual.Depth} dir=({s.Block.m_Direction.x:F2},{s.Block.m_Direction.y:F2}) libres={s.Virtual.FreeCount()} esquinas L={s.Virtual.LeftCorner} R={s.Virtual.RightCorner}");
                log.AppendLine($"    {StripInfo(s.Strip)}; fila delantera {Row(s.Virtual, 0)}");
            }

            // Las que se descartan por no tener ni una celda libre (tapadas por edificios existentes o fuera).
            foreach (var st in strips.Where(st => st.Grid.FreeCount() == 0))
            {
                log.AppendLine($"  tira descartada (0 libres) {st.Width}x{st.Depth}: {StripInfo(st)}");
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

        /// <summary>
        /// Radio en planta con el que el juego comprueba si una planta choca (la copa, no el tronco): si es circular,
        /// la mitad de su tamaño; si no, lo más lejos que llega su caja desde el centro.
        /// </summary>
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
        /// <summary>Margen sin solape: dos edificios que solo se tocan no cuentan como solapados.</summary>
        const float StrictOverlap = 0.05f;

        /// <summary>Solape por junta con "Overlap buildings" (Anarchy): más no mejoraba el resultado.</summary>
        const float OverlapOn = 0.5f;


        /// <summary>Eje de la fachada (dirección de x creciente en las celdas del bloque).</summary>
        static float2 FrontAxis(Block b) =>
            math.normalizesafe(ZoneUtils.GetCellPosition(b, new int2(1, 0)).xz - ZoneUtils.GetCellPosition(b, new int2(0, 0)).xz);

        /// <summary>Celdas a la calle ocupadas por los edificios de la vista previa.</summary>
        int CountFront()
        {
            var quads = preview.Where(p => !p.IsDecor).Select(p => LotQuad(p, p.Shift, 0.5f)).ToList();
            return frontCells.Count(c => quads.Any(q => q.Contains(c)));
        }

        static string StripInfo(LotStrip st) => st == null ? "(sin datos)"
            : $"lado {st.SideIndex} tramo {st.ChunkIndex + 1}/{st.ChunkCount}, acera de ({st.ChunkStart.X:F1},{st.ChunkStart.Y:F1}) largo {st.ChunkLength:F2} m, " +
              $"celdas desde {st.Offset:F2} m, sobran {st.Slack:F2} m{(st.Inward > 0 ? $", metida {st.Inward:F2} m por curva" : string.Empty)}";

        /// <summary>Fila de la rejilla como texto: o libre, x ocupada/tapada.</summary>
        static string Row(PlanGrid g, int y)
        {
            var sb = new StringBuilder(g.Width);
            for (int x = 0; x < g.Width; x++)
            {
                sb.Append(g.Free[x, y] ? 'o' : 'x');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Diagnóstico de huecos: recorre la acera real de cada tira (todo su largo, no solo las celdas) 1,5 m hacia
        /// dentro y anota los trozos que no cubre ningún edificio (planificado, con su desplazamiento, o existente).
        /// Así se ven los huecos de esquinas, curvas y diagonales, que el recuento de celdas no ve.
        /// </summary>
        void LogUncovered(List<Quad> existing, StringBuilder log)
        {
            var quads = preview.Where(p => !p.IsDecor).Select(p => LotQuad(p, p.Shift, 0f)).ToList();
            quads.AddRange(existing);
            foreach (var s in selection.Where(x => x.Strip != null))
            {
                var st = s.Strip;
                var n = new Pt(-st.T.Y, st.T.X);
                double depth = st.Inward + 1.5;
                var runs = new List<string>();
                double runStart = -1;
                const double step = 0.25;
                for (double u = 0; u <= st.ChunkLength + 1e-6; u += step)
                {
                    var q = new Pt(st.ChunkStart.X + st.T.X * u + n.X * depth, st.ChunkStart.Y + st.T.Y * u + n.Y * depth);
                    bool covered = Covered(quads, q);
                    if (!covered && runStart < 0)
                    {
                        runStart = u;
                    }

                    bool endRun = runStart >= 0 && (covered || u + step > st.ChunkLength + 1e-6);
                    if (endRun)
                    {
                        double end = covered ? u : st.ChunkLength;
                        if (end - runStart >= 0.5)
                        {
                            string where = runStart < 0.01 ? "al principio" : end > st.ChunkLength - 0.01 ? "al final" : "en medio";
                            runs.Add($"{runStart:F1}-{end:F1} m ({end - runStart:F1} m, {where}){Neighbours(quads, st, n, depth, runStart, end)}");
                        }

                        runStart = -1;
                    }
                }

                log.AppendLine(runs.Count == 0
                    ? $"  acera lado {st.SideIndex} tramo {st.ChunkIndex + 1}/{st.ChunkCount} ({st.ChunkLength:F1} m): cubierta entera."
                    : $"  acera lado {st.SideIndex} tramo {st.ChunkIndex + 1}/{st.ChunkCount} ({st.ChunkLength:F1} m): SIN CUBRIR {string.Join("; ", runs)}");
            }
        }

        static bool Covered(List<Quad> quads, Pt q)
        {
            foreach (var o in quads)
            {
                if (q.X >= o.MinX && q.X <= o.MaxX && q.Y >= o.MinY && q.Y <= o.MaxY && Polygon.Contains(o.P, q))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// En los extremos de la acera, qué hay más allá (la esquina, la calle de al lado): distancia al primer edificio.
        /// Un trozo en medio de la acera tiene edificios pegados a los dos lados.
        /// </summary>
        static string Neighbours(List<Quad> quads, LotStrip st, Pt n, double depth, double a, double b)
        {
            bool atStart = a < 0.01, atEnd = b > st.ChunkLength - 0.01;
            if (!atStart && !atEnd)
            {
                return " [entre dos edificios]";
            }

            Pt At(double u) => new Pt(st.ChunkStart.X + st.T.X * u + n.X * depth, st.ChunkStart.Y + st.T.Y * u + n.Y * depth);
            string Probe(double from, int dir)
            {
                for (double d = 0.25; d <= 12; d += 0.25)
                {
                    var q = At(from + dir * d);
                    if (Covered(quads, q))
                    {
                        return $"{d:F2} m";
                    }
                }

                return "nada a <12 m";
            }

            return $" [{(atStart ? $"antes del principio: {Probe(0, -1)}" : "pegado antes")}, {(atEnd ? $"después del final: {Probe(st.ChunkLength, +1)}" : "pegado después")}]";
        }

        /// <summary>Huella en el suelo de un edificio planificado, corrida "shift" m por la fachada y metida "inset" m.</summary>
        static Quad LotQuad(Planned p, float shift, float inset) => LotQuad(p.Block, p.Min, p.Max, shift, inset);

        static Quad LotQuad(Block block, int2 min, int2 max, float shift, float inset)
        {
            var q = ZoneUtils.CalculateCorners(block, new ValidArea { m_Area = new int4(min.x, max.x, min.y, max.y) });
            var axis = FrontAxis(block);
            var depth = new float2(-axis.y, axis.x);
            var off = axis * shift;
            var c = (q.a + q.b + q.c + q.d) * 0.25f;
            Pt P(float2 v)
            {
                var d = v - c;
                float da = math.dot(d, axis), db = math.dot(d, depth);
                var w = c + off + axis * (da - math.sign(da) * inset) + depth * (db - math.sign(db) * inset);
                return new Pt(w.x, w.y);
            }

            return new Quad(P(q.a), P(q.b), P(q.c), P(q.d));
        }

        /// <summary>
        /// Junta el final de cada calle con lo que tiene delante (el esquinero de la calle de al lado, o un edificio
        /// que ya estaba). Las parcelas van de 8 en 8 m y la calle casi nunca mide un múltiplo de 8: queda un trozo
        /// de celda. Sin solape no hay forma de llenarlo (se deja un único hueco, nunca rendijas repartidas). Con
        /// Anarchy, se ensancha una celda un edificio de la cadena del final y la cadena se aprieta: el solape que
        /// falta se reparte entre todas sus juntas, cada una por debajo del máximo elegido.
        /// </summary>
        int CloseEndGaps(SelectedBlock s, List<Quad> existing, float overlap, List<BuildingInfo> plain,
            Dictionary<string, Entity> prefabs, System.Random rng, StringBuilder log)
        {
            if (s.Virtual == null || areaOutline == null)
            {
                return 0;
            }

            var mine = preview.Where(p => !p.IsDecor && p.Owner == s).OrderBy(p => p.Min.x).ToList();
            if (mine.Count == 0)
            {
                return 0;
            }

            int widened = 0;
            widened += CloseGap(s, mine, existing, overlap, plain, prefabs, rng, +1, log);
            widened += CloseGap(s, mine, existing, overlap, plain, prefabs, rng, -1, log);
            return widened;
        }

        int CloseGap(SelectedBlock s, List<Planned> mine, List<Quad> existing, float overlap, List<BuildingInfo> plain,
            Dictionary<string, Entity> prefabs, System.Random rng, int dir, StringBuilder log)
        {
            // Cadena desde el extremo: edificios seguidos (sin hueco entre ellos), sin el esquinero (va pegado al bordillo).
            var chain = new List<Planned>();
            for (int i = dir > 0 ? mine.Count - 1 : 0; i >= 0 && i < mine.Count; i -= dir)
            {
                var p = mine[i];
                // Ya tocado desde el otro extremo (una calle sin esquinero puede tener una sola cadena): no se toca.
                if (p.Corner || p.Shift != 0 || (chain.Count > 0 && (dir > 0 ? p.Max.x != chain[chain.Count - 1].Min.x : p.Min.x != chain[chain.Count - 1].Max.x)))
                {
                    break;
                }

                chain.Add(p);
            }

            // Solo el esquinero en ese extremo (calle corta): no hay cadena que apretar. Se mide el hueco igualmente y
            // se registra, para saber cuánto queda sin cerrar.
            bool cornerOnly = chain.Count == 0 && mine.Count == 1 && mine[0].Corner && mine[0].Shift == 0;
            if (cornerOnly)
            {
                chain.Add(mine[0]);
            }

            if (chain.Count == 0)
            {
                return 0;
            }

            var otherQuads = preview.Where(p => !p.IsDecor && !chain.Contains(p)).Select(p => LotQuad(p, p.Shift, 0f)).ToList();
            var axis = FrontAxis(s.Block);

            // Un edificio (en min-max, corrido "shift") cabe: no se sale de la manzana por delante en el sentido en que
            // avanza, y no pisa a nadie más de "inset" m. Con lo ya construido igual, a propósito: solo se aprieta con
            // Anarchy, y la junta del final puede pisar lo que había lo mismo que las demás (perJoint, bajo el máximo).
            bool Fits(int2 min, int2 max, float shift, float inset, List<Quad> ignore = null)
            {
                var strict = LotQuad(s.Block, min, max, shift, inset);
                bool Hits(Quad q) => (ignore == null || !ignore.Contains(q)) && Quad.Distance(q, strict) <= 0;
                if (existing.Any(Hits) || otherQuads.Any(Hits))
                {
                    return false;
                }

                // Un hueco que ha dejado la densidad a propósito no se rellena al cerrar otro.
                if (ignore == null && thinned.Any(strict.Contains))
                {
                    return false;
                }

                var probe = LotQuad(s.Block, min, max, shift, 0.5f);
                var center = new Pt(probe.P.Average(v => v.X), probe.P.Average(v => v.Y));
                foreach (var c in probe.P)
                {
                    var d = c - center;
                    if ((d.X * axis.x + d.Y * axis.y) * dir > 0 && !Polygon.Contains(areaOutline, c))
                    {
                        return false;
                    }
                }

                return true;
            }

            // Hueco: hasta dónde llegaría el del extremo sin tocar nada. Lo que ya lo pisa por un lado o por detrás (la
            // calle de al lado, que apretó su cadena contra este esquinero) no cuenta: al correrse a lo largo de la
            // fachada no lo pisa más. Sin esto, el hueco medía 0 y no se cerraba ni se registraba. Lo de delante sí
            // cuenta: empieza a menos de 1 m de la cara del extremo.
            var end = chain[0];
            var endQuad = LotQuad(end, 0f, StrictOverlap);
            float front = Proj(LotQuad(end, 0f, 0f), dir, max: true);
            var beside = existing.Concat(otherQuads)
                .Where(q => Quad.Distance(q, endQuad) <= 0 && Proj(q, dir, max: false) < front - 1f).ToList();
            const float Step = 0.1f;
            float gap = 0;
            while (gap + Step < ZoneUtils.CELL_SIZE && Fits(end.Min, end.Max, dir * (gap + Step), StrictOverlap, beside))
            {
                gap += Step;
            }

            // Proyección de un cuadrilátero sobre el eje de la fachada en el sentido del avance: su punto más adelantado
            // (max) o más atrasado.
            float Proj(Quad q, int sign, bool max)
            {
                var v = q.P.Select(c => (float)(c.X * axis.x + c.Y * axis.y) * sign);
                return max ? v.Max() : v.Min();
            }

            // Menos de 0,3 m: ya está pegado. Una celda entera libre: es un hueco que ha decidido la densidad (o
            // "dejar huecos"), no el sobrante de la calle; no se toca.
            if (gap < 0.3f || gap >= ZoneUtils.CELL_SIZE - Step - 1e-3f)
            {
                return 0;
            }

            float missing = ZoneUtils.CELL_SIZE - gap;
            if (cornerOnly)
            {
                // El esquinero no se mueve (va pegado al bordillo de la otra calle): solo cabe meter uno de una celda
                // que pise lo mismo al esquinero y a lo de delante.
                if (Insert(end, gap, missing, s, overlap, plain, prefabs, rng, dir, log, Fits))
                {
                    return 1;
                }

                log.AppendLine($"  hueco de {gap:F1} m al {(dir > 0 ? "final" : "principio")} junto al esquinero (calle sin más edificios): no cabe uno de una celda; se deja.");
                return 0;
            }

            int joints = chain.Count + 1;
            float perJoint = missing / joints;
            // El hueco se mide en pasos de 0,1 m sumados en float: 6,5 m sale 6,4999995 y pedía "0,50 m" con máximo
            // 0,5, y se rechazaba. Un centímetro de margen absorbe ese error.
            if (overlap <= StrictOverlap || perJoint > overlap + 0.01f)
            {
                log.AppendLine($"  hueco de {gap:F1} m al {(dir > 0 ? "final" : "principio")}: cerrarlo pide {perJoint:F2} m de solape en cada una de {joints} juntas{(overlap <= StrictOverlap ? " (sin solape)" : $" (máximo {overlap:F1} m)")}; se deja.");
                return 0;
            }

            // Se prueba a ensanchar una celda cada edificio de la cadena (al azar), con uno de la colección del mismo
            // fondo (o, si no hay, menos profundo), y a apretar la cadena: el k-ésimo desde el extremo retrocede
            // missing * (n - k) / (n + 1), así cada junta (también con lo de delante y lo de detrás) pisa perJoint.
            int n = chain.Count;
            foreach (int k in Enumerable.Range(0, n).OrderBy(_ => rng.Next()))
            {
                var cur = chain[k];
                int w = cur.Max.x - cur.Min.x, dep = cur.Max.y - cur.Min.y;
                // Nunca más profundo: por detrás podría salirse de una manzana estrecha.
                var options = plain.Where(b => b.LotWidth == w + 1 && b.LotDepth <= dep && prefabs.ContainsKey(b.PrefabName))
                    .OrderBy(b => b.LotDepth == dep ? 0 : 1).ThenBy(_ => rng.Next()).ToList();
                foreach (var b in options)
                {
                    var mins = new int2[n];
                    var maxs = new int2[n];
                    var shifts = new float[n];
                    for (int j = 0; j < n; j++)
                    {
                        var p = chain[j];
                        int move = j < k ? dir : 0; // los que están entre el ensanchado y el extremo se corren una celda
                        mins[j] = new int2(p.Min.x + move, p.Min.y);
                        maxs[j] = new int2(p.Max.x + move, p.Max.y);
                        if (j == k)
                        {
                            if (dir > 0) { maxs[j] = new int2(p.Max.x + 1, p.Min.y + b.LotDepth); }
                            else { mins[j] = new int2(p.Min.x - 1, p.Min.y); maxs[j] = new int2(p.Max.x, p.Min.y + b.LotDepth); }
                        }

                        shifts[j] = -dir * missing * (n - j) / joints;
                    }

                    bool ok = true;
                    for (int j = 0; j < n && ok; j++)
                    {
                        ok = Fits(mins[j], maxs[j], shifts[j], perJoint + StrictOverlap);
                    }

                    if (!ok)
                    {
                        continue;
                    }

                    log.AppendLine($"  hueco de {gap:F1} m al {(dir > 0 ? "final" : "principio")} cerrado: {cur.Name} -> {b.PrefabName} ({w}->{w + 1} de ancho), {n} edificios apretados, {perJoint:F2} m de solape por junta.");
                    cur.Name = b.PrefabName;
                    cur.Prefab = prefabs[b.PrefabName];
                    for (int j = 0; j < n; j++)
                    {
                        chain[j].Min = mins[j];
                        chain[j].Max = maxs[j];
                        chain[j].Shift = shifts[j];
                    }

                    return 1;
                }
            }

            if (Insert(end, gap, missing, s, overlap, plain, prefabs, rng, dir, log, Fits))
            {
                return 1;
            }

            log.AppendLine($"  hueco de {gap:F1} m al {(dir > 0 ? "final" : "principio")}: ningún edificio de la colección una celda más ancho cabe; se deja.");
            return 0;
        }

        /// <summary>
        /// Hueco de casi una celda (falta como mucho el doble del solape): se mete un edificio de una celda delante del
        /// extremo, corrido hacia atrás la mitad de lo que falta, así pisa lo mismo al de detrás y a lo de delante.
        /// </summary>
        bool Insert(Planned end, float gap, float missing, SelectedBlock s, float overlap, List<BuildingInfo> plain,
            Dictionary<string, Entity> prefabs, System.Random rng, int dir, StringBuilder log, Func<int2, int2, float, float, List<Quad>, bool> fits)
        {
            float perJoint = missing / 2;
            if (overlap <= StrictOverlap || perJoint > overlap + 0.01f)
            {
                return false;
            }

            var min = new int2(dir > 0 ? end.Max.x : end.Min.x - 1, end.Min.y);
            if (min.x < s.Area.x || min.x + 1 > s.Area.x + s.Virtual.Width)
            {
                return false;
            }

            int dep = end.Max.y - end.Min.y;
            var options = plain.Where(b => b.LotWidth == 1 && b.LotDepth <= dep && b.LotDepth <= s.Virtual.Depth && prefabs.ContainsKey(b.PrefabName))
                .OrderBy(b => b.LotDepth == dep ? 0 : 1).ThenBy(_ => rng.Next()).ToList();
            float shift = -dir * perJoint;
            foreach (var b in options)
            {
                var max = new int2(min.x + 1, min.y + b.LotDepth);
                if (!fits(min, max, shift, perJoint + StrictOverlap, null))
                {
                    continue;
                }

                preview.Add(new Planned
                {
                    Name = b.PrefabName,
                    Prefab = prefabs[b.PrefabName],
                    Block = s.Block,
                    Min = min,
                    Max = max,
                    Owner = s,
                    Shift = shift,
                });
                log.AppendLine($"  hueco de {gap:F1} m al {(dir > 0 ? "final" : "principio")} cerrado: metido {b.PrefabName} (1x{b.LotDepth}) tras {end.Name}, {perJoint:F2} m de solape por junta.");
                return true;
            }

            return false;
        }

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
