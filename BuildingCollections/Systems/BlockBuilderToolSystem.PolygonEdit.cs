using System.Collections.Generic;
using System.Linq;
using BuildingCollections.Core;
using Game.Simulation;
using Game.Tools;
using Unity.Mathematics;

namespace BuildingCollections.Systems
{
    public partial class BlockBuilderToolSystem : ObjectToolBaseSystem
    {
        const float HandleRadius = 6f;
        const float DragThreshold = 1.5f;

        float3 rawHover;
        PolygonHandle hoverHandle = PolygonHandle.None;
        PolygonHandle pressedHandle = PolygonHandle.None;
        float3 pressPos;
        bool pressing;
        bool dragging;
        int dragIndex = -1;
        (List<float3> points, List<int> curb, List<double> arc) snapshot;

        bool PolygonClosed => selection.Count > 0;

        bool PolygonEditable => mode == "polygon" && polygon.Count > 0;

        PolygonHandle HandleAt(float3 p)
        {
            if (!PolygonEditable)
            {
                return PolygonHandle.None;
            }

            var verts = polygon.Select(v => new Pt(v.x, v.z)).ToList();
            return PolygonHandles.Find(verts, polyCurb, polyArc, curbs, PolygonClosed, new Pt(p.x, p.z), HandleRadius);
        }

        List<PolygonHandle> PolygonMidpoints()
        {
            var verts = polygon.Select(v => new Pt(v.x, v.z)).ToList();
            return PolygonHandles.Midpoints(verts, polyCurb, polyArc, curbs);
        }

        bool TryPressHandle()
        {
            if (hoverHandle.Kind == HandleKind.None)
            {
                return false;
            }

            pressedHandle = hoverHandle;
            pressPos = rawHover;
            pressing = true;
            dragging = false;
            return true;
        }

        void UpdatePress()
        {
            if (!applyAction.IsPressed() || applyAction.WasReleasedThisFrame())
            {
                EndPress();
                return;
            }

            if (!hasHover)
            {
                return;
            }

            if (!dragging)
            {
                if (math.distance(rawHover.xz, pressPos.xz) < DragThreshold)
                {
                    return;
                }

                BeginDrag();
            }

            hoverPos = rawHover;
            SnapHover();
            polygon[dragIndex] = hoverPos;
            polyCurb[dragIndex] = hoverSnapped ? hoverHit.Curb : -1;
            polyArc[dragIndex] = hoverSnapped ? hoverHit.Arc : 0;
        }

        void BeginDrag()
        {
            TakeSnapshot();
            dragging = true;
            if (pressedHandle.Kind == HandleKind.Midpoint)
            {
                dragIndex = pressedHandle.Index + 1;
                polygon.Insert(dragIndex, Ground(pressedHandle.Point));
                polyCurb.Insert(dragIndex, pressedHandle.Curb);
                polyArc.Insert(dragIndex, pressedHandle.Arc);
            }
            else
            {
                dragIndex = pressedHandle.Index;
            }
        }

        void ResetPress()
        {
            pressing = false;
            dragging = false;
            dragIndex = -1;
            hoverHandle = PolygonHandle.None;
        }

        void EndPress()
        {
            bool moved = dragging;
            ResetPress();
            if (moved)
            {
                FinishEdit("Point moved.");
                return;
            }

            if (pressedHandle.Kind == HandleKind.Vertex && pressedHandle.Index == 0 && !PolygonClosed && polygon.Count >= 3)
            {
                SelectPolygon();
            }
        }

        bool TryCancelOnPolygon()
        {
            if (pressing)
            {
                if (dragging)
                {
                    RestoreSnapshot();
                }

                ResetPress();
                Say("Edit cancelled.");
                return true;
            }

            if (hoverHandle.Kind != HandleKind.Vertex)
            {
                return false;
            }

            if (PolygonClosed && polygon.Count <= 3)
            {
                Say("A polygon needs at least 3 points.", error: true);
                return true;
            }

            int index = hoverHandle.Index;
            TakeSnapshot();
            polygon.RemoveAt(index);
            polyCurb.RemoveAt(index);
            polyArc.RemoveAt(index);
            hoverHandle = PolygonHandle.None;
            FinishEdit("Point removed.");
            return true;
        }

        void FinishEdit(string message)
        {
            if (!PolygonClosed)
            {
                Say(polygon.Count == 0 ? "Polygon cleared." : $"{message} {polygon.Count} points.");
                return;
            }

            if (SelectPolygon())
            {
                return;
            }

            RestoreSnapshot();
            SelectPolygon();
            Say("That change leaves nothing to build, so it was undone.", error: true);
        }

        void TakeSnapshot() => snapshot = (new List<float3>(polygon), new List<int>(polyCurb), new List<double>(polyArc));

        void RestoreSnapshot()
        {
            if (snapshot.points == null)
            {
                return;
            }

            polygon.Clear();
            polygon.AddRange(snapshot.points);
            polyCurb.Clear();
            polyCurb.AddRange(snapshot.curb);
            polyArc.Clear();
            polyArc.AddRange(snapshot.arc);
        }

        float3 Ground(Pt p)
        {
            var heights = m_TerrainSystem.GetHeightData();
            var q = new float3((float)p.X, 0, (float)p.Y);
            q.y = TerrainUtils.SampleHeight(ref heights, q);
            return q;
        }
    }
}
