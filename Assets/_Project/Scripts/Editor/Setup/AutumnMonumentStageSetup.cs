using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace ShadowTan.EditorTools
{
    static class AutumnMonumentStageSetup
    {
        static readonly Vector2[][] Cracks =
        {
            new[] { new Vector2(.11f, .02f), new Vector2(.21f, .09f), new Vector2(.34f, .12f), new Vector2(.49f, .20f), new Vector2(.56f, .27f) },
            new[] { new Vector2(.33f, .12f), new Vector2(.28f, .20f), new Vector2(.23f, .24f) },
            new[] { new Vector2(.86f, .02f), new Vector2(.68f, .08f), new Vector2(.62f, .13f), new Vector2(.49f, .20f) },
            new[] { new Vector2(.62f, .13f), new Vector2(.71f, .17f), new Vector2(.74f, .25f), new Vector2(.86f, .30f) },
            new[] { new Vector2(.11f, .02f), new Vector2(.42f, .047f), new Vector2(.68f, .08f), new Vector2(.89f, .12f) },
            new[] { new Vector2(.49f, .20f), new Vector2(.48f, .27f), new Vector2(.41f, .33f), new Vector2(.44f, .36f) },
            new[] { new Vector2(.24f, .056f), new Vector2(.29f, .028f), new Vector2(.32f, .005f) }
        };

        public static void Apply(Component monument, string path)
        {
            var original = new SerializedObject(monument);
            var shaft = (Collider)original.FindProperty("shaftCollider").objectReferenceValue;
            var shape = shaft.GetComponent<MeshFilter>();
            var renderer = shaft.GetComponent<MeshRenderer>();
            if (!shape || !renderer)
            {
                Debug.LogWarning("AU-10 / AU-12 require the authored square shaft mesh and renderer.");
                return;
            }
            var stages = new Material[4];
            for (int i = 0; i < stages.Length; i++)
                stages[i] = Stage(path, renderer.sharedMaterial, i);

            var chipped = LoadOrBakeShaft(path, shape.sharedMesh.bounds);
            AutumnVfxSetup.Set(monument, so =>
            {
                var list = so.FindProperty("crackStages");
                list.arraySize = stages.Length;
                for (int i = 0; i < stages.Length; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
                AutumnVfxSetup.Reference(so, "damagedShaft", chipped);
            });
            Undo.RecordObject(renderer, "Assign pre-cracked monument stage");
            renderer.sharedMaterial = stages[0];
        }

        static Material Stage(string root, Material original, int stage)
        {
            string path = $"{root}/M_Autumn_MonumentCrack_{stage}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            const int size = 256;
            var height = new float[size * size];
            int strokes = stage == 0 ? 2 : stage == 1 ? 4 : stage == 2 ? 5 : Cracks.Length;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var uv = new Vector2(x, y) / (size - 1);
                float cut = 0;
                for (int branch = 0; branch < strokes; branch++)
                {
                    var points = Cracks[branch];
                    for (int i = 1; i < points.Length; i++)
                    {
                        var ab = points[i] - points[i - 1];
                        var delta = uv - points[i - 1];
                        float t = Mathf.Clamp01(Vector2.Dot(delta, ab) / ab.sqrMagnitude);
                        float distance = (delta - ab * t).magnitude;
                        float width = (.006f + stage * .002f) * Mathf.Lerp(.71f, 1.28f,
                            Mathf.PerlinNoise(uv.x * 74f, uv.y * 54f));
                        cut = Mathf.Max(cut, Mathf.SmoothStep(1, 0, Mathf.Clamp01(distance / width)));
                    }
                }
                height[y * size + x] = cut;
            }
            var color = new Color32[height.Length];
            var normals = new Color32[height.Length];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;
                float shade = 1f - .86f * height[index];
                color[index] = new Color(shade, shade, shade, 1f);
                float dx = height[y * size + Mathf.Min(x + 1, size - 1)] - height[y * size + Mathf.Max(x - 1, 0)];
                float dy = height[Mathf.Min(y + 1, size - 1) * size + x] - height[Mathf.Max(y - 1, 0) * size + x];
                var n = new Vector3(dx * .95f, dy * .95f, 1).normalized * .5f + Vector3.one * .5f;
                normals[index] = new Color(n.x, n.y, n.z, 1);
            }
            var albedo = WriteMap($"{root}/T_Autumn_MonumentCrack_{stage}_Mask.asset", color, size, false);
            var normal = WriteMap($"{root}/T_Autumn_MonumentCrack_{stage}_Normal.asset", normals, size, true);
            material = new Material(original);
            material.SetTexture("_BaseColorMap", albedo);
            material.SetTexture("_NormalMap", normal);
            material.SetFloat("_NormalScale", .8f);
            HDMaterial.ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static Texture2D WriteMap(string path, Color32[] data, int size, bool linear)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture) return texture;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, true, linear);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(data);
            texture.Apply(true, true);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        static Mesh LoadOrBakeShaft(string root, Bounds original)
        {
            string path = $"{root}/SM_Autumn_MonumentChippedBase.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) return mesh;
            var heights = new[] { 0f, .055f, .14f, .24f, 1f };
            var corners = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            var cuts = new[]
            {
                new[] { .26f, .04f, .19f, .05f },
                new[] { .34f, .12f, .12f, .04f },
                new[] { .12f, .04f, .072f, .09f },
                new[] { 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f }
            };
            Vector3 Vertex(int level, int corner)
            {
                Vector2 point = corners[corner] * (1f - cuts[level][corner]);
                return new Vector3(original.center.x + point.x * original.extents.x,
                    Mathf.Lerp(original.min.y, original.max.y, heights[level]),
                    original.center.z + point.y * original.extents.z);
            }
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float v0, float v1)
            {
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                uv.AddRange(new[] { new Vector2(0, v0), new Vector2(1, v0), new Vector2(0, v1), new Vector2(1, v1) });
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 2, start + 3, start + 1 });
            }
            for (int face = 0; face < 4; face++)
            {
                int next = (face + 1) % 4;
                for (int i = 0; i < heights.Length - 1; i++)
                    Quad(Vertex(i, face), Vertex(i, next), Vertex(i + 1, face), Vertex(i + 1, next), heights[i], heights[i + 1]);
            }
            int top = heights.Length - 1;
            Quad(Vertex(top, 0), Vertex(top, 1), Vertex(top, 3), Vertex(top, 2), .6f, .96f);
            Quad(Vertex(0, 1), Vertex(0, 0), Vertex(0, 2), Vertex(0, 3), .6f, .96f);
            mesh = new Mesh { name = "Monument chipped base" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.bounds = original;
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
