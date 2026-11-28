using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ShadowTan.EditorTools.AutumnVegetationBake;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        static Mesh Assemble(Vector3[] vertices, Vector3[] normals, Vector2[] uv, int[] indices)
        {
            var mesh = new Mesh { indexFormat = vertices.Length > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static Mesh Reduce(Mesh source, float fraction)
        {
            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] tex = source.uv;
            int[] indices = source.triangles;
            Bounds bounds = source.bounds;
            float scale = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            int target = Mathf.RoundToInt(indices.Length / 3f * fraction);
            MeshData best = ReduceTo(2);
            int low = 3, high = 55;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                MeshData result = ReduceTo(middle);
                if (result.Triangles.Count / 3 <= target)
                {
                    if (result.Triangles.Count > best.Triangles.Count) best = result;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return Assemble(best.Positions.ToArray(), best.Normals.ToArray(), best.Uv.ToArray(), best.Triangles.ToArray());

            MeshData ReduceTo(int divisions)
            {
                var output = new MeshData();
                var bins = new Dictionary<(int, int, int, int, int), int>();
                var counts = new List<int>();
                var remap = new int[positions.Length];
                float density = divisions / Mathf.Max(0.001f, scale);
                int uvBins = Mathf.Max(2, divisions / 6);
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 location = (positions[i] - bounds.min) * density;
                    Vector2 uv = tex[i] * uvBins;
                    var key = (Mathf.FloorToInt(location.x), Mathf.FloorToInt(location.y), Mathf.FloorToInt(location.z),
                        Mathf.FloorToInt(uv.x), Mathf.FloorToInt(uv.y));
                    if (!bins.TryGetValue(key, out int index))
                    {
                        index = output.Positions.Count;
                        bins.Add(key, index);
                        output.Positions.Add(positions[i]);
                        output.Normals.Add(normals[i]);
                        output.Uv.Add(tex[i]);
                        counts.Add(1);
                    }
                    else
                    {
                        output.Positions[index] += positions[i];
                        output.Normals[index] += normals[i];
                        output.Uv[index] += tex[i];
                        counts[index]++;
                    }

                    remap[i] = index;
                }

                for (int i = 0; i < counts.Count; i++)
                {
                    output.Positions[i] /= counts[i];
                    output.Normals[i] = output.Normals[i].normalized;
                    output.Uv[i] /= counts[i];
                }

                var seen = new HashSet<(int, int, int)>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = remap[indices[i]], b = remap[indices[i + 1]], c = remap[indices[i + 2]];
                    if (a == b || a == c || b == c) continue;
                    int min = Mathf.Min(a, b, c);
                    int max = Mathf.Max(a, b, c);
                    if (!seen.Add((min, a + b + c - min - max, max))) continue;
                    Vector3 normal = Vector3.Cross(output.Positions[b] - output.Positions[a], output.Positions[c] - output.Positions[a]);
                    if (normal.sqrMagnitude < 0.000000000001f) continue;
                    output.Triangles.Add(a);
                    output.Triangles.Add(b);
                    output.Triangles.Add(c);
                }

                return output;
            }
        }

        static Mesh CrossedCards(Bounds b, Rect[] atlas)
        {
            var geometry = new Geometry(Vector3.zero, 1);
            float y0 = b.min.y, y1 = b.max.y;
            Vector3 p = b.min, q = b.max, c = b.center;
            geometry.Quad(0, new Vector3(p.x, y0, c.z), new Vector3(q.x, y0, c.z),
                new Vector3(q.x, y1, c.z), new Vector3(p.x, y1, c.z), atlas[0]);
            geometry.Quad(0, new Vector3(c.x, y0, p.z), new Vector3(c.x, y0, q.z),
                new Vector3(c.x, y1, q.z), new Vector3(c.x, y1, p.z), atlas[1]);
            return geometry.MakeMesh();
        }

        static void SavePrefab(Prop asset)
        {
            var root = new GameObject("SM_" + asset.Name);
            try
            {
                Vector3 size = StandardSize(asset.Kind);
                root.transform.localScale = ScaleFor(asset, size);
                var group = root.AddComponent<LODGroup>();
                var lods = new LOD[3];
                for (int i = 0; i < lods.Length; i++)
                {
                    var item = new GameObject($"LOD{i}");
                    item.transform.SetParent(root.transform, false);
                    var renderer = Render(item.transform, asset.Meshes[i],
                        new[] { i == 2 ? asset.CardMaterial : asset.Material }, CastShadows(asset.Kind), 0);
                    lods[i] = new LOD(i == 0 ? 0.30f : i == 1 ? 0.10f : 0.045f, new Renderer[] { renderer });
                }

                group.SetLODs(lods);
                group.RecalculateBounds();
                AddCollider(root, asset);
                if (PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/SM_{asset.Name}.prefab") == null)
                    throw new InvalidOperationException($"Failed to save prefab {asset.Name}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static ShadowCastingMode CastShadows(Kind kind) =>
            kind == Kind.DryGrassClump ? ShadowCastingMode.Off : ShadowCastingMode.On;

        static void AddCollider(GameObject target, Prop asset)
        {
            Bounds b = asset.Meshes[0].bounds;
            switch (asset.Kind)
            {
                case Kind.DryGrassClump:
                    return;
                case Kind.BambooLog:
                    var capsule = Component<CapsuleCollider>(target);
                    capsule.direction = 0;
                    capsule.center = b.center;
                    capsule.radius = Mathf.Min(b.size.y, b.size.z) * 0.31f;
                    capsule.height = b.size.x * 0.88f;
                    break;
                default:
                    var box = Component<BoxCollider>(target);
                    box.center = b.center;
                    box.size = b.size * 0.83f;
                    break;
            }

            int layer = LayerMask.NameToLayer("Wall");
            if (layer >= 0) target.layer = layer;
        }

        static Vector3 StandardSize(Kind kind)
        {
            switch (kind)
            {
                case Kind.BambooLog: return new Vector3(2.0f, 0.52f, 0.54f);
                case Kind.Boulder: return Vector3.one * 0.47f;
                case Kind.BambooStump: return new Vector3(1.1f, 0.83f, 1.1f);
                case Kind.WallChunk: return Vector3.one * 1.55f;
                default: return Vector3.one * 1.55f;
            }
        }

        static Vector3 ScaleFor(Prop asset, Vector3 meters)
        {
            Vector3 bounds = asset.Meshes[0].bounds.size;
            if (asset.Kind == Kind.BambooLog || asset.Kind == Kind.BambooStump)
                return new Vector3(meters.x / bounds.x, meters.y / bounds.y, meters.z / bounds.z);

            float longest = asset.Kind == Kind.DryGrassClump ? bounds.y : Mathf.Max(bounds.x, bounds.y, bounds.z);
            return Vector3.one * (meters.x / longest);
        }

        sealed class MeshData
        {
            internal readonly List<Vector3> Positions = new List<Vector3>();
            internal readonly List<Vector3> Normals = new List<Vector3>();
            internal readonly List<Vector2> Uv = new List<Vector2>();
            internal readonly List<int> Triangles = new List<int>();

            internal void Add(Mesh mesh, Matrix4x4 transform, Rect? atlas)
            {
                int first = Positions.Count;
                Matrix4x4 normal = transform.inverse.transpose;
                Vector3[] sourcePoints = mesh.vertices;
                Vector3[] sourceNormals = mesh.normals;
                Vector2[] sourceUv = mesh.uv;
                for (int i = 0; i < sourcePoints.Length; i++)
                {
                    Positions.Add(transform.MultiplyPoint3x4(sourcePoints[i]));
                    Normals.Add(normal.MultiplyVector(sourceNormals[i]).normalized);
                    Vector2 u = sourceUv[i];
                    if (atlas.HasValue)
                    {
                        Rect r = atlas.Value;
                        u = new Vector2(r.x + Mathf.Clamp01(u.x) * r.width, r.y + Mathf.Clamp01(u.y) * r.height);
                    }

                    Uv.Add(u);
                }

                foreach (int index in mesh.triangles) Triangles.Add(first + index);
            }

            internal Mesh ToMesh() => Assemble(Positions.ToArray(), Normals.ToArray(), Uv.ToArray(), Triangles.ToArray());
        }
    }
}
