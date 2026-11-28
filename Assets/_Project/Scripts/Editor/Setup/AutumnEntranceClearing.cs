using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnEntranceCave
    {
        static void BuildClearing(Transform root, Terrain terrain, Materials materials, HashSet<string> meshes,
            HashSet<string> objects)
        {
            Transform clearing = Section(root, "Clearing", objects);
            const int segments = 64;
            var sand = new Geometry(Vector3.zero, 1);
            Vector3 center = new Vector3(CenterX, 0.024f, ClearingZ);
            int origin = SandVertex(center);
            Vector3 right = Edge(0f), left = Edge(Mathf.PI);
            int prior = SandVertex(right);
            for (int i = 1; i <= segments; i++)
            {
                int current = SandVertex(Edge(i * Mathf.PI / segments));
                sand.Triangle(0, origin, current, prior);
                prior = current;
            }

            Vector3 a = RampPoint(1f, -1f), b = RampPoint(1f, 1f);
            Vector3 outside = new Vector3(CenterX + 3.25f, 0f, a.z - 0.24f);
            Surface(sand, a + Vector3.up * 0.026f, b + Vector3.up * 0.026f,
                new Vector3(CenterX + 3.25f, 0.024f, ClearingZ + 0.65f), outside + Vector3.up * 0.024f, 3.4f);
            Bake(clearing, "ClearingSand", sand, materials.Sand, ShadowCastingMode.Off, meshes);

            var trunks = new Geometry(Vector3.zero, 1);
            var leaves = new Geometry(Vector3.zero, 1);
            var straw = new Geometry(Vector3.zero, 1);
            var random = new Random(Seed + 849);
            int guards = 0;
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * (i + 0.5f) / segments;
                if (Mathf.Abs(Mathf.Cos(angle) * ClearingRadius) < 1.36f) continue;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 tangent = new Vector3(-radial.z, 0f, radial.x);
                Vector3 p = Edge(angle);
                if (i % 2 == 0)
                {
                    Vector3 q = Edge(Mathf.PI * i / segments);
                    Vector3 r = Edge(Mathf.PI * (i + 2) / segments);
                    if (Mathf.Sign(q.x - CenterX) == Mathf.Sign(r.x - CenterX) &&
                        Mathf.Min(Mathf.Abs(q.x - CenterX), Mathf.Abs(r.x - CenterX)) > 1.14f)
                        Guard(clearing, $"{Prefix}ClearingEdge_{guards++:00}", q, r, 2.35f);
                }

                for (int depth = 0; depth < 4; depth++)
                {
                    Vector3 at = p + radial * (0.28f + depth * 0.58f) + tangent * Range(random, -0.22f, 0.22f);
                    if (NearRamp(at, 0.4f)) continue;
                    at.y = Ground(terrain, at);
                    float h = Range(random, 9f, 14f);
                    float radius = Range(random, 0.072f, 0.114f);
                    Vector3 tip = at + Vector3.up * h - radial * Range(random, 0f, 0.48f);
                    Culm(trunks, at, tip, radius, random.Next(8));
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 branch = Vector3.Lerp(at, tip, k == 0 ? 0.25f : k == 1 ? 0.49f : 0.82f);
                        Vector3 side = k % 2 == 0 ? tangent : radial;
                        Card(leaves, branch, branch + side * Range(random, 0.64f, 1.4f) + Vector3.up * 0.47f,
                            Vector3.Cross(side, Vector3.up) * 0.47f + Vector3.up * 0.21f, random.Next(4));
                    }
                }

                for (int bush = 0; bush < 13; bush++)
                {
                    Vector3 foot = p + radial * Range(random, -0.48f, 1.82f) + tangent * Range(random, -0.27f, 0.27f);
                    if (NearRamp(foot, 0.15f)) continue;
                    foot.y = Ground(terrain, foot) - 0.025f;
                    for (int card = 0; card < 5; card++)
                    {
                        float turn = Range(random, 0f, Mathf.PI);
                        Vector3 span = new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn)) * Range(random, 0.20f, 0.33f);
                        Vector3 tip = foot + Vector3.up * Range(random, 1.52f, 1.71f) - radial * Range(random, 0.16f, 0.43f);
                        Card(straw, foot, tip, span, random.Next(4));
                    }
                }
            }

            AddEntranceCanopy(trunks, leaves, terrain);
            Bake(clearing, "ClearingCulm", trunks, materials.Culm, ShadowCastingMode.Off, meshes);
            Bake(clearing, "ClearingLeaves", leaves, materials.Leaves, ShadowCastingMode.Off, meshes);
            Bake(clearing, "ClearingStraw", straw, materials.Grass, ShadowCastingMode.Off, meshes);
            Guard(clearing, Prefix + "ClearingChordWest", left, new Vector3(CenterX - 2.7f, 0f, ClearingZ), 2.1f);
            Guard(clearing, Prefix + "ClearingChordEast", new Vector3(CenterX + 3.25f, 0f, ClearingZ), right, 2.1f);
            Guard(clearing, Prefix + "ClearingFootSouth", a, outside, 2.1f);
            Guard(clearing, Prefix + "ClearingFootEast", outside, new Vector3(CenterX + 3.25f, 0f, ClearingZ), 2.1f);

            Vector3 Edge(float angle) =>
                new Vector3(CenterX + Mathf.Cos(angle) * ClearingRadius, 0.024f, ClearingZ + Mathf.Sin(angle) * ClearingRadius);
            int SandVertex(Vector3 point) => sand.Vertex(point, Vector3.up, new Vector2(point.x / 3.4f, point.z / 3.4f));
        }

        static void AddEntranceCanopy(Geometry stems, Geometry leaves, Terrain terrain)
        {
            var random = new Random(Seed + 7131);
            for (float z = MouthZ + 1.3f; z <= 114f; z += 2.3f)
            {
                float left = -43f + 7f * Mathf.Sin(z * 0.047f);
                float right = Mathf.Lerp(113f, 141f, Mathf.InverseLerp(-13f, 25f, z));
                for (float x = left; x <= right; x += 2.55f)
                {
                    var at = new Vector3(x + Range(random, -1.10f, 1.10f), 0f, z + Range(random, -0.9f, 0.9f));
                    if (NearRamp(at, 1.25f)) continue;
                    if (Vector3.SqrMagnitude(at - new Vector3(CenterX, 0f, ClearingZ)) < 13.6f * 13.6f ||
                        (at.z > StartZ - 1f && Mathf.Abs(at.x - CenterX) < 8.5f))
                        continue;

                    at.y = Ground(terrain, at) - 0.02f;
                    float h = Range(random, 10.2f, 16.5f);
                    float spin = Range(random, 0f, 6.28318f);
                    var direction = new Vector3(Mathf.Cos(spin), 0f, Mathf.Sin(spin));
                    Vector3 tip = at + Vector3.up * h + direction * Range(random, 0.18f, 0.91f);
                    Culm(stems, at, tip, Range(random, 0.064f, 0.113f), random.Next(8), 4);
                    Vector3 branch = Vector3.Lerp(at, tip, Range(random, 0.62f, 0.86f));
                    Card(leaves, branch, branch + direction * Range(random, 1.70f, 2.45f) + Vector3.up * 0.78f,
                        Vector3.Cross(direction, Vector3.up) * 0.91f + Vector3.up * 0.34f, random.Next(4));
                }
            }

        }

        static bool NearRamp(Vector3 point, float margin)
        {
            Vector3 offset = point - new Vector3(RampTop.x, 0f, RampTop.z);
            float t = Vector3.Dot(offset, Along) / RampRun;
            return t > -0.08f && t < 1.08f &&
                   Mathf.Abs(Vector3.Dot(offset, Across)) < RampWidth(Mathf.Clamp01(t)) * 0.50f + margin;
        }

        static void Culm(Geometry geometry, Vector3 bottom, Vector3 top, float radius, int variant, int sides = 6)
        {
            int first = -1;
            for (int ring = 0; ring < 2; ring++)
            {
                for (int edge = 0; edge <= sides; edge++)
                {
                    float t = (float)edge / sides;
                    float angle = t * 2f * Mathf.PI;
                    var normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 p = (ring == 0 ? bottom : top) + normal * radius * (ring == 0 ? 1f : 0.55f);
                    float u = (variant + Mathf.Lerp(0.025f, 0.975f, t)) / 8f;
                    int index = geometry.Vertex(p, normal, new Vector2(u, ring == 0 ? 0f : (top.y - bottom.y) / 0.51f));
                    if (first < 0) first = index;
                }
            }

            for (int edge = 0; edge < sides; edge++)
            {
                int p = first + edge;
                geometry.Triangle(0, p, p + sides + 1, p + sides + 2);
                geometry.Triangle(0, p, p + sides + 2, p + 1);
            }
        }

        static void Card(Geometry geometry, Vector3 bottom, Vector3 top, Vector3 width, int tile)
        {
            geometry.Quad(0, bottom - width * 0.55f, bottom + width * 0.55f, top + width, top - width, VegetationAtlas.Card(tile));
        }

        static Mesh Bake(Transform parent, string name, Geometry geometry, Material material, ShadowCastingMode mode,
            HashSet<string> meshes, StaticEditorFlags flags = RendererFlags)
        {
            string label = Prefix + name;
            Mesh mesh = SaveMesh($"{Folder}/{label}.asset", Finish(geometry), meshes);
            Render(Node(parent, label, Vector3.zero), mesh, new[] { material }, mode, flags);
            return mesh;
        }

        static Mesh Finish(Geometry geometry)
        {
            Mesh mesh = geometry.MakeMesh();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void MeshCollision(Transform parent, string name, Mesh mesh, string layer)
        {
            Transform node = Node(parent, name, Vector3.zero);
            node.gameObject.layer = LayerMask.NameToLayer(layer);
            Component<MeshCollider>(node.gameObject).sharedMesh = mesh;
        }

        static void ColliderBox(Transform parent, string name, Vector3 position, Vector3 size, string layer)
        {
            Transform node = Node(parent, name, position);
            node.gameObject.layer = LayerMask.NameToLayer(layer);
            Component<BoxCollider>(node.gameObject).size = size;
        }

        static void Guard(Transform parent, string name, Vector3 a, Vector3 b, float height)
        {
            Vector3 v = b - a;
            ColliderBox(parent, name, (a + b) * 0.5f + Vector3.up * (height * 0.45f),
                new Vector3(0.23f, height, v.magnitude + 0.19f), "Wall");
            parent.Find(name).rotation = Quaternion.LookRotation(v);
        }

        static void Surface(Geometry geometry, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float tile)
        {
            bool triangle = c == d;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            bool invert = normal.y < 0f;
            if (invert) normal = -normal;
            int first = Add(a);
            int second = Add(b);
            int third = Add(c);
            geometry.Triangle(0, first, invert ? third : second, invert ? second : third);
            if (!triangle)
            {
                int fourth = Add(d);
                geometry.Triangle(0, first, invert ? fourth : third, invert ? third : fourth);
            }

            int Add(Vector3 p) => geometry.Vertex(p, normal, new Vector2(p.x / tile, (p.z + p.y * 0.62f) / tile));
        }

        static void Box(Geometry geometry, Vector3 center, Vector3 size, float tile)
        {
            Vector3 min = center - size * 0.5f, max = center + size * 0.5f;
            Vector3 a = new Vector3(min.x, min.y, min.z), b = new Vector3(max.x, min.y, min.z);
            Vector3 c = new Vector3(max.x, max.y, min.z), d = new Vector3(min.x, max.y, min.z);
            Vector3 e = new Vector3(min.x, min.y, max.z), f = new Vector3(max.x, min.y, max.z);
            Vector3 g = new Vector3(max.x, max.y, max.z), h = new Vector3(min.x, max.y, max.z);
            Face(e, f, g, h);
            Face(b, a, d, c);
            Face(a, e, h, d);
            Face(f, b, c, g);
            Face(d, h, g, c);
            Face(a, b, f, e);
            void Face(Vector3 p, Vector3 q, Vector3 r, Vector3 s) =>
                geometry.Quad(0, p, q, r, s, new Rect(0f, 0f, Vector3.Distance(p, q) / tile, Vector3.Distance(p, s) / tile));
        }
    }
}
