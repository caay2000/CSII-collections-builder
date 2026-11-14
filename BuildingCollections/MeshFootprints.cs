using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BuildingCollections.Core;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BuildingCollections
{
    public static class MeshFootprints
    {
        const float MinHeight = 1f;
        const double MinCoverOfMeshBox = 0.6;

        static readonly FieldInfo InstancesField = typeof(GeometryAsset).GetField("m_Instances", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo RequestsField = typeof(GeometryAsset).GetField("mRequestCounts", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly Dictionary<string, List<FootprintRect>> cache = new Dictionary<string, List<FootprintRect>>(StringComparer.Ordinal);

        sealed class Skip : Exception
        {
            public Skip(string message) : base(message) { }
        }

        public static bool TryGet(EntityManager em, PrefabSystem prefabs, Entity prefab, out List<FootprintRect> rects)
        {
            rects = null;
            if (!prefabs.TryGetPrefab(prefab, out PrefabBase p) || p == null)
            {
                return false;
            }

            if (cache.TryGetValue(p.name, out rects))
            {
                return rects != null;
            }

            try
            {
                rects = Read(em, prefabs, prefab, out string detail);
                cache[p.name] = rects;
                Mod.Log.Info(rects == null
                    ? $"[footprint] {p.name}: none ({detail}); using the lot and mesh box."
                    : string.Format(CultureInfo.InvariantCulture, "[footprint] {0}: {1:F0} m2 in {2} rects ({3})\n  [data] footprint {4}: {5}",
                        p.name, rects.Sum(r => r.Area), rects.Count, detail, p.name.Replace(":", "_"), Footprint.Encode(rects)));
            }
            catch (Skip e)
            {
                rects = null;
                Mod.Log.Info($"[footprint] {p.name}: not now ({e.Message}); using the lot and mesh box.");
            }
            catch (Exception e)
            {
                rects = null;
                Mod.Log.Warn($"[footprint] {p.name}: {e.GetType().Name}: {e.Message}; using the lot and mesh box.");
            }

            return rects != null;
        }

        static List<FootprintRect> Read(EntityManager em, PrefabSystem prefabs, Entity prefab, out string detail)
        {
            if (!em.HasBuffer<SubMesh>(prefab))
            {
                detail = "no sub-meshes";
                return null;
            }

            var buffer = em.GetBuffer<SubMesh>(prefab, true);
            var subMeshes = new List<SubMesh>(buffer.Length);
            for (int i = 0; i < buffer.Length; i++)
            {
                subMeshes.Add(buffer[i]);
            }

            var triangles = new List<(Pt, Pt, Pt)>();
            var notes = new List<string>();
            foreach (var sm in subMeshes)
            {
                if (!prefabs.TryGetPrefab(sm.m_SubMesh, out RenderPrefab render) || render == null || render.geometryAsset == null)
                {
                    detail = "a sub-mesh has no geometry";
                    return null;
                }

                EnsureSafeToLoad(render.geometryAsset);
                var meshes = render.ObtainMeshes();
                try
                {
                    if (meshes == null || meshes.Length == 0)
                    {
                        detail = render.name + " has no meshes";
                        return null;
                    }

                    foreach (var mesh in meshes)
                    {
                        if (mesh == null || !mesh.isReadable)
                        {
                            detail = render.name + " is not readable";
                            return null;
                        }

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
                detail += "; nothing above " + MinHeight.ToString(CultureInfo.InvariantCulture) + " m";
                return null;
            }

            var rects = Footprint.FromTriangles(triangles).Rects();
            if (PrefabCatalog.TryMeshBounds(em, prefab, out var min, out var max) && !CoversMeshBox(rects, min, max))
            {
                detail += "; the outline covers too little of the mesh box";
                return null;
            }

            return rects;
        }

        static void EnsureSafeToLoad(GeometryAsset geometry)
        {
            bool gameHoldsMeshes = InstancesField?.GetValue(geometry) != null;
            if (gameHoldsMeshes)
            {
                return;
            }

            if (InstancesField == null || RequestsField == null)
            {
                throw new Skip("the game's geometry loader has changed");
            }

            if ((int)RequestsField.GetValue(geometry) > 0 || geometry.data.IsValid)
            {
                throw new Skip("the game is loading this mesh");
            }
        }

        static bool CoversMeshBox(List<FootprintRect> rects, float2 min, float2 max)
        {
            double fx = (rects.Max(r => r.MaxX) - rects.Min(r => r.MinX)) / Math.Max(1e-3, max.x - min.x);
            double fz = (rects.Max(r => r.MaxZ) - rects.Min(r => r.MinZ)) / Math.Max(1e-3, max.y - min.y);
            return fx >= MinCoverOfMeshBox && fz >= MinCoverOfMeshBox;
        }

        static string AddMesh(Mesh mesh, SubMesh sm, List<(Pt, Pt, Pt)> triangles)
        {
            var vertices = mesh.vertices;
            var world = new float3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                world[i] = sm.m_Position + math.rotate(sm.m_Rotation, (float3)vertices[i]);
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

            return string.Format(CultureInfo.InvariantCulture, "{0}: {1} vertices ({2}), {3} of {4} triangles above {5} m",
                mesh.name, vertices.Length, mesh.GetVertexAttributeFormat(UnityEngine.Rendering.VertexAttribute.Position), kept, indices.Length / 3, MinHeight);
        }
    }
}
