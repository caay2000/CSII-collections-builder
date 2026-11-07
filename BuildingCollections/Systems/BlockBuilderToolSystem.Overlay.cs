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
        void DrawSelection()
        {
            bool snapMarker = mode == "polygon" && (selection.Count == 0 || dragging) && hasHover && hoverSnapped;
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
                var hotColor = new UnityEngine.Color(1f, 1f, 1f, 1f);
                void Path(List<float3> pts, float width, UnityEngine.Color color)
                {
                    for (int k = 1; k < pts.Count; k++)
                    {
                        buffer.DrawLine(color, new Line3.Segment(pts[k - 1], pts[k]), width);
                    }
                }

                bool closed = selection.Count > 0;
                if (!closed)
                {
                    for (int i = 1; i < polygon.Count; i++)
                    {
                        Path(PolygonSegment(polygon[i - 1], polyCurb[i - 1], polyArc[i - 1], polygon[i], polyCurb[i], polyArc[i]), 1f, polyCurb[i] >= 0 && polyCurb[i] == polyCurb[i - 1] ? snapColor : lineColor);
                    }

                    if (hasHover && !pressing)
                    {
                        int last = polygon.Count - 1;
                        bool follows = hoverSnapped && polyCurb[last] >= 0 && polyCurb[last] == hoverHit.Curb;
                        Path(PolygonSegment(polygon[last], polyCurb[last], polyArc[last], hoverPos, hoverSnapped ? hoverHit.Curb : -1, hoverHit.Arc), 0.6f, follows ? snapColor : lineColor);
                    }
                }

                else
                {
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        int j = (i + 1) % polygon.Count;
                        Path(PolygonSegment(polygon[i], polyCurb[i], polyArc[i], polygon[j], polyCurb[j], polyArc[j]), 0.6f, polyCurb[i] >= 0 && polyCurb[i] == polyCurb[j] ? snapColor : lineColor);
                    }

                    if (!dragging)
                    {
                        foreach (var m in PolygonMidpoints())
                        {
                            bool hot = hoverHandle.Kind == HandleKind.Midpoint && hoverHandle.Index == m.Index;
                            buffer.DrawCircle(hot ? hotColor : lineColor, Ground(m.Point), hot ? 4f : 2f);
                        }
                    }
                }

                bool nearFirst = !closed && !dragging && hasHover && polygon.Count >= 3 && math.distance(hoverPos.xz, polygon[0].xz) <= 12f;
                for (int i = 0; i < polygon.Count; i++)
                {
                    bool hot = dragging ? i == dragIndex : hoverHandle.Kind == HandleKind.Vertex && hoverHandle.Index == i;
                    if (i == 0 && nearFirst)
                    {
                        buffer.DrawCircle(new UnityEngine.Color(0.3f, 0.9f, 0.4f, 1f), polygon[0], 8f);
                    }
                    else
                    {
                        float size = i == 0 && !closed ? 5f : 3f;
                        buffer.DrawCircle(hot ? hotColor : lineColor, polygon[i], hot ? size + 2f : size);
                    }
                }
            }

            overlay.AddBufferWriter(default(JobHandle));
        }
    }
}
