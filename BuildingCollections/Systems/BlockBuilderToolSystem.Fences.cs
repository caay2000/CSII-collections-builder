using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Simulation;
using Game.Tools;
using Unity.Entities;
using Unity.Mathematics;

namespace BuildingCollections.Systems
{
    public partial class BlockBuilderToolSystem
    {
        const float FencePieceLength = 24f;

        EntityQuery containerQuery;
        ParcelFenceSystem parcelFences;

        bool ParcelLimitsActive => Mod.Settings?.UpdateParcelLimits == true && BetterBulldozerBridge.Available;

        public void RefreshParcelLimits()
        {
            if (preview.Count > 0)
            {
                definitionsDirty = true;
            }
        }

        void CreateFenceDefinitions(ref TerrainHeightData heights)
        {
            if (!ParcelLimitsActive)
            {
                return;
            }

            var buildings = preview.Where(p => !p.IsDecor).ToList();
            var fence = DominantFence(buildings);
            if (fence == Entity.Null)
            {
                Mod.Log.Info("[fences] None of the previewed buildings has a fence: no parcel limits drawn.");
                return;
            }

            if (!GetContainers(containerQuery, out Entity container, out _) || container == Entity.Null)
            {
                Mod.Log.Warn("[fences] The game's lane container was not found: no parcel limits drawn.");
                return;
            }

            var segments = ParcelFences.Plan(buildings.Select(p => p.Lot).ToList(), areaOutline);
            var commands = m_ToolOutputBarrier.CreateCommandBuffer();
            foreach (var segment in segments)
            {
                EmitFence(commands, container, fence, segment, ref heights);
            }

            Mod.Log.Info($"[fences] {segments.Count} parcel lines, {segments.Sum(s => s.Length):F0} m in total.");
        }

        Entity DominantFence(IEnumerable<Planned> buildings) => buildings
            .SelectMany(p => FenceLanes.OfPrefab(EntityManager, p.Prefab))
            .GroupBy(f => f)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Index)
            .Select(g => g.Key)
            .FirstOrDefault();

        void EmitFence(EntityCommandBuffer commands, Entity container, Entity fence, FenceSegment segment, ref TerrainHeightData heights)
        {
            int pieces = math.max(1, (int)math.ceil((float)segment.Length / FencePieceLength));
            var previous = OnTerrain(segment.A, ref heights);
            for (int i = 1; i <= pieces; i++)
            {
                var along = segment.A + (segment.B - segment.A) * ((double)i / pieces);
                var next = OnTerrain(along, ref heights);
                var curve = NetUtils.StraightCurve(previous, next);
                var start = CourseEnd(curve, previous, 0f, i == 1 ? CoursePosFlags.IsFirst : 0);
                var end = CourseEnd(curve, next, 1f, i == pieces ? CoursePosFlags.IsLast : 0);
                var entity = commands.CreateEntity();
                commands.AddComponent(entity, new CreationDefinition
                {
                    m_Prefab = container,
                    m_SubPrefab = fence,
                    m_RandomSeed = RandomSeed.Next().GetRandom(i).NextInt(),
                    m_Flags = CreationFlags.SubElevation,
                });
                commands.AddComponent(entity, default(Updated));
                commands.AddComponent(entity, new NetCourse
                {
                    m_Curve = curve,
                    m_StartPosition = start,
                    m_EndPosition = end,
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                });
                previous = next;
            }
        }

        static CoursePos CourseEnd(Bezier4x3 curve, float3 position, float delta, CoursePosFlags flags) => new CoursePos
        {
            m_Position = position,
            m_Rotation = NetUtils.GetNodeRotation(delta == 0f ? MathUtils.StartTangent(curve) : MathUtils.EndTangent(curve)),
            m_CourseDelta = delta,
            m_Flags = flags | CoursePosFlags.IsLeft | CoursePosFlags.IsRight,
            m_ParentMesh = -1,
        };

        static float3 OnTerrain(Pt point, ref TerrainHeightData heights)
        {
            var position = new float3((float)point.X, 0f, (float)point.Y);
            position.y = TerrainUtils.SampleHeight(ref heights, position);
            return position;
        }
    }
}
