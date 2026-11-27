using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    static class AutumnVegetationBake
    {
        internal const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        internal const string Location = "Assets/_Project/Art/Environment/Autumn/Location";
        internal const string Generated = Location + "/Generated";
        internal const float CenterX = 70f;
        internal const float StartZ = 12f;
        internal const float EndZ = 162f;
        internal const float ClearHalfWidth = 1.15f;
        internal const StaticEditorFlags RendererFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;
        internal static readonly Perimeter Arena = new Perimeter(188f, 30f, 1.5f * Mathf.PI, 0.145f);
        internal static readonly Perimeter Clearing = new Perimeter(0f, 20.5f, 0.5f * Mathf.PI, 0.47f);

        internal static void Run(string label, Action<Transform, Terrain> build)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Run vegetation baking in Edit Mode.");

                var scene = SceneManager.GetSceneByPath(ScenePath);
                if (!scene.isLoaded)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }

                Transform level = Array.Find(scene.GetRootGameObjects(), root => root.name == "[Level]")?.transform;
                Transform props = level?.Find("Greybox/Props");
                Terrain terrain = level?.Find("Greybox/Ground/AutumnTerrain")?.GetComponent<Terrain>();
                if (props == null || terrain == null)
                    throw new InvalidOperationException("Autumn_Gameplay is missing Greybox/Props or AutumnTerrain.");

                build(props, terrain);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException($"Could not save {ScenePath}.");

                Debug.Log($"ShadowTan / {label}: saved {ScenePath} in {timer.Elapsed.TotalSeconds:F2} s.");
            }
            catch (Exception error)
            {
                Debug.LogError($"ShadowTan / {label}: {error}");
            }
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException($"Could not create {path}.");
        }

        internal static Transform Node(Transform parent, string name, Vector3 worldPosition)
        {
            var node = parent.Find(name);
            if (node == null)
            {
                node = new GameObject(name).transform;
                node.SetParent(parent, false);
            }

            node.SetPositionAndRotation(worldPosition, Quaternion.identity);
            node.localScale = Vector3.one;
            return node;
        }

        internal static T Component<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent(out T component) ? component : go.AddComponent<T>();
        }

        internal static int RemoveChildren(Transform parent, string prefix, HashSet<string> retained = null)
        {
            if (parent == null) return 0;
            int removed = 0;
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Transform child = parent.GetChild(index);
                if (!child.name.StartsWith(prefix, StringComparison.Ordinal) || (retained != null && retained.Contains(child.name))) continue;
                Object.DestroyImmediate(child.gameObject);
                removed++;
            }

            return removed;
        }

        internal static int PruneMeshes(string folder, string prefix, HashSet<string> retained)
        {
            int removed = 0;
            foreach (string path in Directory.EnumerateFiles(folder, prefix + "*.asset"))
            {
                string projectPath = path.Replace('\\', '/');
                if (retained.Contains(projectPath)) continue;
                if (AssetDatabase.LoadAssetAtPath<Mesh>(projectPath) != null && AssetDatabase.DeleteAsset(projectPath)) removed++;
            }

            return removed;
        }

        internal static float Range(Random random, float min, float max) =>
            Mathf.LerpUnclamped(min, max, (float)random.NextDouble());

        internal static float Ground(Terrain terrain, Vector3 point) =>
            terrain.SampleHeight(point) + terrain.transform.position.y;

        internal static Color Linear(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out Color color)) throw new ArgumentException(hex);
            return color.linear;
        }

        internal static Mesh SaveMesh(string path, Mesh mesh, HashSet<string> retained)
        {
            retained.Add(path);
            mesh.name = Path.GetFileNameWithoutExtension(path);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        internal static Material Material(string name, string shaderName, Action<Material> configure)
        {
            string path = $"{Location}/Materials/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"Shader {shaderName} is unavailable.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            configure(material);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Material Lit(string name, Texture map, bool cutout, float smoothness, float emissive = 0f)
        {
            return Material(name, "HDRP/Lit", material =>
            {
                material.SetTexture("_BaseColorMap", map);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_SurfaceType", 0f);
                material.SetFloat("_AlphaCutoffEnable", cutout ? 1f : 0f);
                material.SetFloat("_AlphaCutoff", VegetationAtlas.Cutoff);
                material.SetFloat("_DoubleSidedEnable", cutout ? 1f : 0f);
                material.SetFloat("_DoubleSidedNormalMode", 1f);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_SmoothnessRemapMin", 0.04f);
                material.SetFloat("_SmoothnessRemapMax", smoothness);
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_EnableGeometricSpecularAA", 1f);
                material.SetTexture("_EmissiveColorMap", emissive > 0f ? map : null);
                material.SetColor("_EmissiveColor", new Color(emissive, emissive, emissive, 1f));
                material.doubleSidedGI = cutout;
            });
        }

        internal static MeshRenderer Render(Transform node, Mesh mesh, Material[] materials,
            ShadowCastingMode shadows, StaticEditorFlags flags = RendererFlags)
        {
            Component<MeshFilter>(node.gameObject).sharedMesh = mesh;
            MeshRenderer renderer = Component<MeshRenderer>(node.gameObject);
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            GameObjectUtility.SetStaticEditorFlags(node.gameObject, flags);
            return renderer;
        }

        internal static void SetLods(Transform chunk, LOD[] lods)
        {
            var group = Component<LODGroup>(chunk.gameObject);
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(lods);
            group.RecalculateBounds();
        }

        internal readonly struct Perimeter
        {
            readonly float centerZ;
            internal readonly float Radius;
            internal readonly float Opening;
            internal readonly float HalfOpening;

            internal Perimeter(float centerZ, float radius, float opening, float halfOpening)
            {
                this.centerZ = centerZ;
                Radius = radius;
                Opening = opening;
                HalfOpening = halfOpening;
            }

            internal float Arc => (2f * Mathf.PI - 2f * HalfOpening) * Radius;

            internal Vector3 Direction(float distance)
            {
                float angle = Opening + HalfOpening + distance / Radius;
                return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            }

            internal Vector3 Point(float distance) => new Vector3(CenterX, 0f, centerZ) + Direction(distance) * Radius;
        }

        internal sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int>[] triangles;
            readonly Vector3 origin;

            internal Geometry(Vector3 origin, int submeshes)
            {
                this.origin = origin;
                triangles = new List<int>[submeshes];
                for (int index = 0; index < submeshes; index++) triangles[index] = new List<int>();
            }

            internal int Vertex(Vector3 position, Vector3 normal, Vector2 texCoord)
            {
                int index = vertices.Count;
                vertices.Add(position - origin);
                normals.Add(normal);
                uv.Add(texCoord);
                return index;
            }

            internal void Triangle(int submesh, int a, int b, int c)
            {
                triangles[submesh].Add(a);
                triangles[submesh].Add(b);
                triangles[submesh].Add(c);
            }

            internal void Quad(int submesh, Vector3 bottomLeft, Vector3 bottomRight, Vector3 topRight,
                Vector3 topLeft, Rect area)
            {
                Vector3 normal = Vector3.Cross(bottomRight - bottomLeft, topLeft - bottomLeft).normalized;
                int first = Vertex(bottomLeft, normal, new Vector2(area.xMin, area.yMin));
                Vertex(bottomRight, normal, new Vector2(area.xMax, area.yMin));
                Vertex(topRight, normal, new Vector2(area.xMax, area.yMax));
                Vertex(topLeft, normal, new Vector2(area.xMin, area.yMax));
                Triangle(submesh, first, first + 1, first + 2);
                Triangle(submesh, first, first + 2, first + 3);
            }

            internal Mesh MakeMesh()
            {
                var mesh = new Mesh
                {
                    indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = triangles.Length;
                for (int index = 0; index < triangles.Length; index++) mesh.SetTriangles(triangles[index], index, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        internal sealed class Counts
        {
            readonly long[] verts = new long[3];
            readonly long[] tris = new long[3];
            readonly int[] submeshes = new int[3];
            internal int Chunks;
            internal int Instances;

            internal void Add(int lod, Mesh mesh)
            {
                verts[lod] += mesh.vertexCount;
                submeshes[lod] += mesh.subMeshCount;
                for (int index = 0; index < mesh.subMeshCount; index++) tris[lod] += (long)mesh.GetIndexCount(index) / 3;
            }

            public override string ToString()
            {
                return $"{Chunks:N0} chunks, {Instances:N0} instances. Whole bake, each tier in isolation: " +
                       $"LOD0 {verts[0]:N0} verts / {tris[0]:N0} tris / {submeshes[0]} material slots; " +
                       $"LOD1 {verts[1]:N0} verts / {tris[1]:N0} tris / {submeshes[1]} material slots; " +
                       $"LOD2 {verts[2]:N0} verts / {tris[2]:N0} tris / {submeshes[2]} material slots.";
            }
        }
    }
}
