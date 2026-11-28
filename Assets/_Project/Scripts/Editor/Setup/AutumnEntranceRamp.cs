using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnEntranceCave
    {
        static void BuildRamp(Transform root, Materials materials, HashSet<string> meshes, HashSet<string> objects, Counts counts, Geometry soil)
        {
            Transform ramp = Section(root, "Ramp", objects);
            const int chunks = 7, longitudinal = 42;
            var walk = new Geometry(Vector3.zero, 1);
            var sides = new Geometry(Vector3.zero, 1);
            float[] cross = { -1.72f, -1.35f, -1f, -0.55f, 0f, 0.55f, 1f, 1.35f, 1.72f };
            for (int i = 0; i < longitudinal; i++)
            {
                float a = i / (float)longitudinal, b = (i + 1) / (float)longitudinal;
                for (int j = 0; j + 1 < cross.Length; j++)
                    Surface(soil, BankPoint(a, cross[j]), BankPoint(a, cross[j + 1]),
                        BankPoint(b, cross[j + 1]), BankPoint(b, cross[j]), 3.8f);

                Surface(walk, RampPoint(a, -1f), RampPoint(a, 1f), RampPoint(b, 1f), RampPoint(b, -1f), 1f);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 p = RampPoint(a, side), q = RampPoint(b, side);
                    if (side > 0) (p, q) = (q, p);
                    sides.Quad(0, new Vector3(p.x, -0.18f, p.z), new Vector3(q.x, -0.18f, q.z), q + Vector3.up * 2f,
                        p + Vector3.up * 2f, new Rect(0f, 0f, 1f, 1f));
                }
            }

            Bake(ramp, "RampBed", soil, materials.Soil, ShadowCastingMode.Off, meshes);
            MeshCollision(ramp, Prefix + "RampWalk", SaveMesh($"{Folder}/{Prefix}RampWalk.asset", walk.MakeMesh(), meshes), "Ground");
            MeshCollision(ramp, Prefix + "RampEdges", SaveMesh($"{Folder}/{Prefix}RampEdges.asset", sides.MakeMesh(), meshes), "Wall");

            var rampNormal = (Vector3.up + Along * (EntranceY / RampRun)).normalized;
            for (int index = 0; index < chunks; index++)
            {
                float a = index / (float)chunks, b = (index + 1) / (float)chunks;
                Vector3 center = RampPoint((a + b) * 0.5f, 0f);
                Transform chunk = Node(ramp, $"{Prefix}Rubble_{index:00}", center);
                var random = new Random(Seed + index * 193);
                var detail = new Geometry(center, 1);
                var low = new Geometry(center, 1);
                int rows = Mathf.CeilToInt(RampRun * (b - a) / 0.425f);
                for (int row = 0; row < rows; row++)
                {
                    float t = Mathf.Lerp(a, b, (row + 0.5f) / rows);
                    int columns = Mathf.CeilToInt(RampWidth(t) * 1.36f / 0.46f);
                    for (int col = 0; col < columns; col++)
                    {
                        float stagger = row % 2 == 0 ? 0.34f : 0.66f;
                        float across = ((col + stagger + Range(random, -0.28f, 0.28f)) / columns * 2f - 1f) * 1.56f;
                        float at = Mathf.Clamp(t + Range(random, -0.16f, 0.16f) / RampRun, a, b);
                        float lane = Mathf.Min(Mathf.Abs(across + 0.42f), Mathf.Abs(across), Mathf.Abs(across - 0.42f));
                        float diameter = lane < 0.075f ? Range(random, 0.30f, 0.38f) : Range(random, 0.34f, 0.50f);
                        Vector3 point = Mathf.Abs(across) <= 1f ? RampPoint(at, across) : BankPoint(at, across);
                        if (point.y < -0.025f) continue;
                        if (Mathf.Abs(across) > 1f) point.y -= 0.07f;
                        int variant = random.Next(5);
                        float spin = Range(random, 0f, 360f);
                        float squash = Range(random, 0.72f, 0.98f), aspect = Range(random, 0.76f, 1.10f);
                        Vector3 normal = Mathf.Abs(across) < 1f ? rampNormal : Vector3.up;
                        AddRock(detail, point, normal, diameter, squash, aspect, spin, variant, false);
                        if ((col + row) % 2 != 0) AddRock(low, point, normal, diameter, squash, aspect, spin, variant, true);
                        counts.Instances++;
                    }
                }

                var lods = new LOD[2];
                Mesh shadowMesh = null;
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    string name = $"{chunk.name}_LOD{lod}";
                    Mesh mesh = SaveMesh($"{Folder}/{name}.asset", Finish(lod == 0 ? detail : low), meshes);
                    counts.Add(lod, mesh);
                    if (lod == 1) shadowMesh = mesh;
                    var renderer = Render(Node(chunk, name, center), mesh, new[] { materials.Rock },
                        ShadowCastingMode.Off);
                    lods[lod] = new LOD(lod == 0 ? 0.44f : 0.018f, new Renderer[] { renderer });
                }

                var shadow = Render(Node(chunk, chunk.name + "_Shadow", center), shadowMesh,
                    new[] { materials.Rock }, ShadowCastingMode.ShadowsOnly);
                shadow.receiveShadows = false;
                lods[0] = new LOD(lods[0].screenRelativeTransitionHeight, new Renderer[] { lods[0].renderers[0], shadow });
                SetLods(chunk, lods);
                chunk.GetComponent<LODGroup>().size = 15.5f;
                counts.Chunks++;
            }
        }

        static void AddRock(Geometry geometry, Vector3 surface, Vector3 up, float diameter, float squash, float aspect,
            float spin, int variant, bool low)
        {
            int sectors = low ? 4 : 7;
            const int rings = 2;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, spin, 0f);
            float height = diameter * squash;
            Vector3 center = surface - up * (height * 0.35f);
            int first = -1;
            for (int row = 0; row < rings; row++)
            {
                float t = (float)row / (rings - 1);
                float ringY = (0.06f + 0.76f * t) * height;
                float ringWidth = Mathf.Lerp(0.49f, low ? 0.31f : 0.34f, t);
                for (int step = 0; step < sectors; step++)
                {
                    float angle = step * Mathf.PI * 2f / sectors;
                    float noise = 1f + 0.16f * Mathf.Sin(angle * 3f + variant * 1.19f + row * 0.65f);
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle) * aspect);
                    Vector3 vertex = center + rotation * (direction * (diameter * ringWidth * noise) +
                        Vector3.up * (ringY + diameter * 0.055f * Mathf.Cos(2f * angle + variant)));
                    Vector3 normal = rotation * (direction + Vector3.up * (t * 1.25f - 0.36f)).normalized;
                    int id = geometry.Vertex(vertex, normal, StoneUv(vertex));
                    if (first < 0) first = id;
                }
            }

            for (int ring = 0; ring + 1 < rings; ring++)
            {
                for (int side = 0; side < sectors; side++)
                {
                    int a = first + ring * sectors + side;
                    int b = first + ring * sectors + (side + 1) % sectors;
                    geometry.Triangle(0, a, a + sectors, b + sectors);
                    geometry.Triangle(0, a, b + sectors, b);
                }
            }

            Vector3 cap = center + up * (height * 0.99f);
            int top = geometry.Vertex(cap, up, StoneUv(cap));
            int last = first + (rings - 1) * sectors;
            for (int side = 0; side < sectors; side++)
                geometry.Triangle(0, top, last + (side + 1) % sectors, last + side);

            Vector2 StoneUv(Vector3 p) => new Vector2(p.x * 1.28f + variant * 0.16f, (p.y + p.z) * 1.39f);
        }

        static float RampWidth(float t) => Mathf.Lerp(9f, 15.5f, t);

        static Vector3 RampPoint(float t, float side) =>
            Vector3.Lerp(RampTop, RampFoot, t) + Across * (side * RampWidth(t) * 0.5f);

        static Vector3 BankPoint(float t, float side)
        {
            Vector3 p = RampPoint(t, side);
            float edge = Mathf.Max(0f, Mathf.Abs(side) - 1f);
            p.y = Mathf.Lerp(p.y - 0.032f, -0.11f, Mathf.Clamp01(edge / 0.67f));
            p.z = Mathf.Max(p.z, MouthZ + 0.04f);
            return p;
        }
    }
}
