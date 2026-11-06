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
        sealed class SelectedBlock
        {
            public Entity Entity;
            public Block Block;
            public int4 Area;
            public bool[] Include;
            public List<(float3 a, float3 b)> Outline = new List<(float3, float3)>();

            public PlanGrid Virtual;

            public LotStrip Strip;
        }

        sealed class Planned
        {
            public PlannedLot Lot;
            public Entity Prefab;
            public Block Block;
            public int2 Min => new int2(Lot.MinX, Lot.MinY);
            public int2 Max => new int2(Lot.MaxX, Lot.MaxY);
            public float Shift => Lot.Shift;
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

        readonly List<(float3 a, float3 b)> areaFill = new List<(float3, float3)>();
        readonly List<(float3 a, float3 b)> areaEdge = new List<(float3, float3)>();
        const float FillStripe = 2f;
        OverlayRenderSystem overlay;
        CityConfigurationSystem city;
        BuiltGuardSystem guard;
        readonly List<float3> polygon = new List<float3>();

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

        List<Pt> areaOutline;

        List<List<Pt>> decorAreas = new List<List<Pt>>();
        List<Pt> decorClip;
        EntityQuery treeQuery;
        string mode = "block";
        float3 hoverPos;
        bool hasHover;
        bool definitionsDirty;
        bool buildRequested;
        int freeFrontCells;
        const int SettleFrames = 10, WatchFrames = 60, MaxAutoCleanPasses = 3;
        int settleFrames = -1;
        int autoCleanPasses;

        public override string toolID => "BuildingCollections.BlockBuilder";

        public bool AreaSelected => selection.Count > 0;

        public bool Previewing => preview.Count > 0;

        public int Lots => Previewing ? preview.Count(p => !p.IsDecor) : freeFrontCells;

        public int FrontFilled { get; private set; }

        public int FrontTotal { get; private set; }

        List<Pt> frontCells = new List<Pt>();

        public int Blocks => selection.Count;

        public string Message { get; private set; } = string.Empty;

        public int MessageSerial { get; private set; }

        public bool MessageIsError { get; private set; }

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
            Mod.Log.Info("BlockBuilderToolSystem created.");
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

            buildRequested = false;
            definitionsDirty = true;

            if (polygon.Count == 0)
            {
                curbs = new List<List<Pt>>();
                curbAge = int.MaxValue;
            }

            applyAction.shouldBeEnabled = true;
            secondaryApplyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            Mod.Log.Info("Builder tool activated.");
        }

        protected override void OnStopRunning()
        {
            preview.Clear();
            applyMode = ApplyMode.Clear;
            base.OnStopRunning();
        }

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
            reselect = null;
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

        public void RequestBuild() => buildRequested = true;

        readonly ThrottledLog errors = new ThrottledLog(TimeSpan.FromSeconds(10));

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                DrawSelection();
            }
            catch (Exception e)
            {
                errors.Error(e);
            }

            try
            {
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
                errors.Error(e);
            }

            applyMode = ApplyMode.None;
            return inputDeps;
        }

        float CurrentOverlap()
        {
            float meters = Math.Min(2f, Math.Max(0f, Mod.Settings?.OverlapMeters ?? OverlapOn));
            return AnarchyBridge.Enabled && meters > StrictOverlap ? meters : StrictOverlap;
        }

        static double CurveOverlap(float overlap) => overlap > StrictOverlap ? overlap : 0;

        Action reselect;

        double stripsOverlap;

        void Say(string message, bool error = false)
        {
            Message = message;
            MessageIsError = error;
            MessageSerial++;
        }
    }
}
