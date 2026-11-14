using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BuildingCollections.Core;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BuildingCollections
{
    public static class MeshFootprints
    {
        const float MinHeight = 1f;

        static readonly Dictionary<Entity, List<FootprintRect>> cache = new Dictionary<Entity, List<FootprintRect>>();

        public static bool TryGet(EntityManager em, PrefabSystem prefabs, Entity prefab, out List<FootprintRect> rects)
        {
            if (!cache.TryGetValue(prefab, out rects))
            {
                rects = Read(em, prefabs, prefab, out string detail);
                cache[prefab] = rects;
                string name = prefabs.TryGetPrefab(prefab, out PrefabBase p) && p != null ? p.name : prefab.ToString();
                Mod.Log.Info(rects == null
                    ? $"[footprint] {name}: none ({detail}); using the lot and mesh box."
                    : string.Format(CultureInfo.InvariantCulture, "[footprint] {0}: {1:F0} m2 in {2} rects ({3})\n  [data] footprint {4}: {5}",
                        name, rects.Sum(r => r.Area), rects.Count, detail, name.Replace(":", "_"), Footprint.Encode(rects)));
            }

            return rects != null;
        }

        static List<FootprintRect> Read(EntityManager em, PrefabSystem prefabs, Entity prefab, out string detail)
        {
            detail = "no sub-meshes";
            if (!em.HasBuffer<SubMesh>(prefab))
            {
                return null;
            }

            try
            {
                var triangles = new List<(Pt, Pt, Pt)>();
                var notes = new List<string>();
                var subMeshes = em.GetBuffer<SubMesh>(prefab, true).ToNativeArray(Unity.Collections.Allocator.Temp).ToArray();
                foreach (var sm in subMeshes)
                {
                    if (!prefabs.TryGetPrefab(sm.m_SubMesh, out RenderPrefab render) || render == null)
                    {
                        notes.Add("sub-mesh without a render prefab");
                        continue;
                    }

                    var meshes = render.ObtainMeshes();
                    try
                    {
                        foreach (var mesh in meshes ?? Array.Empty<Mesh>())
                        {
                            notes.Add(AddMesh(mesh, sm, triangles));
                        }
                    }
                    finally
                    {
                        if (meshes != null)
                        {
                            render.ReleaseMeshes();
                        }
                    }
                }

                detail = string.Join("; ", notes);
                if (triangles.Count == 0)
                {
                    return null;
                }

                var rects = Footprint.FromTriangles(triangles).Rects();
                return rects.Count > 0 ? rects : null;
            }
            catch (Exception e)
            {
                detail = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        static string AddMesh(Mesh mesh, SubMesh sm, List<(Pt, Pt, Pt)> triangles)
        {
            if (mesh == null)
            {
                return "null mesh";
            }

            if (!mesh.isReadable)
            {
                return mesh.name + " not readable";
            }

            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            var raw = RawBounds(vertices);
            bool normalized = raw.min.x >= -0.01f && raw.min.y >= -0.01f && raw.min.z >= -0.01f
                && raw.max.x <= 1.01f && raw.max.y <= 1.01f && raw.max.z <= 1.01f && math.cmax((float3)bounds.size) > 2f;
            var world = new float3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                float3 v = vertices[i];
                if (normalized)
                {
                    v = (float3)bounds.min + v * (float3)bounds.size;
                }

                world[i] = sm.m_Position + math.rotate(sm.m_Rotation, v);
            }

            var indices = mesh.triangles;
            int kept = 0;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                float3 a = world[indices[i]], b = world[indices[i + 1]], c = world[indices[i + 2]];
                if (math.max(a.y, math.max(b.y, c.y)) < MinHeight)
                {
                    continue;
                }

                triangles.Add((new Pt(a.x, a.z), new Pt(b.x, b.z), new Pt(c.x, c.z)));
                kept++;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}: {1} vertices, {2} of {3} triangles above {4} m{5}",
                mesh.name, vertices.Length, kept, indices.Length / 3, MinHeight, normalized ? ", positions decoded from the mesh bounds" : string.Empty);
        }

        static (float3 min, float3 max) RawBounds(Vector3[] vertices)
        {
            float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
            foreach (var v in vertices)
            {
                min = math.min(min, v);
                max = math.max(max, v);
            }

            return (min, max);
        }
    }
}
