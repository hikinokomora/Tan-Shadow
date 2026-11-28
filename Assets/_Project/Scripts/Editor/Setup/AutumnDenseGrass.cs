using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static class AutumnDenseGrass
    {
        const string RootName = "DenseGrass_Root";
        const string Prefix = "DenseGrass_";
        const string Folder = Generated + "/DenseGrass";
        const float ChunkLength = 6f;
        const float VergeWidth = 0.35f;
        const int Seed = 813551;
        static readonly float[] Thresholds = { 0.096f, 0.036f, 0.025f };

        [MenuItem("ShadowTan/Setup/Art/Build Autumn Dense Grass")]
        public static void Build() => Run("Dense Grass", Build);

        static void Build(Transform props, Terrain terrain)
        {
            EnsureFolder(Folder);
            var texture = VegetationAtlas.Grass($"{Folder}/{Prefix}Straw.png");
            var materials = new[] { Lit(Prefix + "Straw", texture, true, 0.21f, 270f) };
            Transform root = Node(props, RootName, props.position);
            var counts = new Counts();
            var objects = new HashSet<string>(StringComparer.Ordinal);
            var meshes = new HashSet<string>(StringComparer.Ordinal);

            foreach (int side in new[] { -1, 1 })
            {
                string span = side < 0 ? "Left" : "Right";
                Vector3 Point(float distance) => new Vector3(CenterX, 0f, StartZ + distance);
                Vector3 Direction(float _) => Vector3.right * side;
                BuildStrip(span, EndZ - StartZ, Point, Direction, ClearHalfWidth, true,
                    root, terrain, materials, counts, objects, meshes);
            }

            BuildPerimeter(Arena, "Arena", 0f, Arena.Arc, -5.6f, root, terrain, materials, counts, objects, meshes);
            float slopeWest = (Mathf.PI - Clearing.Opening - Clearing.HalfOpening) * Clearing.Radius;
            const float slopeHalfOpening = 6.5f;
            BuildPerimeter(Clearing, "ClearingNorth", 0f, slopeWest - slopeHalfOpening, -3.5f,
                root, terrain, materials, counts, objects, meshes);
            float southStart = slopeWest + slopeHalfOpening;
            BuildPerimeter(Clearing, "ClearingSouth", southStart, Clearing.Arc - southStart, -3.5f,
                root, terrain, materials, counts, objects, meshes);

            int removed = RemoveChildren(root, Prefix, objects);
            int pruned = PruneMeshes(Folder, Prefix, meshes);
            int disabled = DisableLegacyGrass(props.Find("Foliage_DryGrass_Batched"));
            Debug.Log($"ShadowTan / Dense Grass: {counts} Removed {removed} obsolete nodes / {pruned} obsolete meshes. " +
                      $"Disabled {disabled} legacy grass renderers. Existing GrassChunk_* and GrassBatch_* assets were kept; " +
                      "the trail, arena and clearing now use the new LOD meshes.");
        }

        static void BuildPerimeter(Perimeter perimeter, string name, float first, float length, float inner,
            Transform root, Terrain terrain, Material[] materials, Counts counts, HashSet<string> objects,
            HashSet<string> meshes)
        {
            BuildStrip(name, length, d => perimeter.Point(d + first), d => perimeter.Direction(d + first),
                inner, false, root, terrain, materials, counts, objects, meshes);
        }

        static void BuildStrip(string span, float length, Func<float, Vector3> point, Func<float, Vector3> direction,
            float inner, bool trail, Transform root, Terrain terrain, Material[] materials, Counts counts,
            HashSet<string> objects, HashSet<string> meshes)
        {
            int chunks = Mathf.CeilToInt(length / ChunkLength);
            for (int index = 0; index < chunks; index++)
            {
                float start = index * ChunkLength;
                float width = Mathf.Min(ChunkLength, length - start);
                string name = $"{Prefix}Chunk_{span}_{index:00}";
                Vector3 center = point(start + width * 0.5f) + direction(start + width * 0.5f) * (inner + 2.2f);
                center.y = Ground(terrain, center);
                Transform chunk = Node(root, name, center);
                objects.Add(name);
                var random = new Random(Seed + counts.Chunks * 3019);
                var clusters = new List<Cluster>();

                Place(clusters, terrain, point, direction, start, width, inner, VergeWidth, 3.3f, 4, true, trail, random);
                Place(clusters, terrain, point, direction, start, width, inner + VergeWidth, 3f - VergeWidth,
                    trail ? 13.2f : 10.5f, 10, false, trail, random);
                Place(clusters, terrain, point, direction, start, width, inner + 3f, 5f,
                    4.6f, 5, false, trail, random);

                var lods = new LOD[3];
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    var geometry = new Geometry(center, 1);
                    foreach (Cluster cluster in clusters) AddCluster(geometry, cluster, lod);
                    string lodName = $"{name}_LOD{lod}";
                    Mesh mesh = SaveMesh($"{Folder}/{lodName}.asset", geometry.MakeMesh(), meshes);
                    counts.Add(lod, mesh);
                    var renderer = Render(Node(chunk, lodName, center), mesh, materials, ShadowCastingMode.Off);
                    lods[lod] = new LOD(Thresholds[lod], new Renderer[] { renderer });
                }

                SetLods(chunk, lods);
                // Screen coverage uses the authored grass height.
                chunk.GetComponent<LODGroup>().size = 1.23f;
                counts.Instances += clusters.Count;
                counts.Chunks++;
            }
        }

        static void Place(List<Cluster> clusters, Terrain terrain, Func<float, Vector3> point,
            Func<float, Vector3> direction, float start, float length, float inner, float width,
            float density, int cards, bool verge, bool trail, Random random)
        {
            int rows = Mathf.Max(1, Mathf.RoundToInt(width * Mathf.Sqrt(density)));
            int columns = Mathf.Max(1, Mathf.RoundToInt(length * width * density / rows));
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float distance = start + (column + Range(random, 0.07f, 0.93f)) * length / columns;
                    float depth = inner + (row + Range(random, 0.06f, 0.94f)) * width / rows;
                    Vector3 outward = direction(distance);
                    Vector3 position = point(distance) + outward * depth;
                    if (!trail && position.z > StartZ && position.z < EndZ &&
                        Mathf.Abs(position.x - CenterX) < ClearHalfWidth + 0.7f) continue;

                    position.y = Ground(terrain, position) - 0.035f;
                    float height = Range(random, 0.75f, 1.26f);
                    float lean = Mathf.Tan(Range(random, 10f, 25f) * Mathf.Deg2Rad);
                    if (verge) height *= 0.93f;
                    clusters.Add(new Cluster(position, outward, height, lean, cards, random.Next(), trail));
                }
            }
        }

        static void AddCluster(Geometry geometry, Cluster cluster, int lod)
        {
            var random = new Random(cluster.Seed);
            int count = lod == 0 ? cluster.Cards : lod == 1 ? Mathf.CeilToInt(cluster.Cards * 0.55f) : 2;
            float phase = Range(random, 0f, Mathf.PI);
            for (int card = 0; card < count; card++)
            {
                float angle = phase + card * 2.399963f;
                var along = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float height = cluster.Height * Range(random, 0.94f, 1f);
                Vector3 width = along * Range(random, 0.17f, 0.34f);
                Vector3 root = cluster.Point + along * Range(random, -0.09f, 0.09f);
                Vector3 tip = root - cluster.Outward * (height * cluster.Lean) + Vector3.up * height;
                Vector3 bottomLeft = Clip(root - width * 0.72f, cluster);
                Vector3 bottomRight = Clip(root + width * 0.72f, cluster);
                Vector3 topRight = Clip(tip + width, cluster);
                Vector3 topLeft = Clip(tip - width, cluster);
                if ((topRight - topLeft).sqrMagnitude < 0.0001f && (bottomRight - bottomLeft).sqrMagnitude < 0.0001f)
                    continue;

                geometry.Quad(0, bottomLeft, bottomRight, topRight, topLeft, VegetationAtlas.Card(random.Next(4)));
            }
        }

        static Vector3 Clip(Vector3 vertex, Cluster cluster)
        {
            if (cluster.Trail)
            {
                float side = cluster.Outward.x;
                vertex.x = CenterX + side * Mathf.Max(ClearHalfWidth, side * (vertex.x - CenterX));
            }

            return vertex;
        }

        static int DisableLegacyGrass(Transform legacy)
        {
            if (legacy == null) return 0;
            int disabled = 0;
            foreach (Transform child in legacy)
            {
                if (!child.name.StartsWith("GrassChunk_", StringComparison.Ordinal) ||
                    !child.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
                renderer.enabled = false;
                disabled++;
            }

            return disabled;
        }

        readonly struct Cluster
        {
            internal readonly Vector3 Point;
            internal readonly Vector3 Outward;
            internal readonly float Height;
            internal readonly float Lean;
            internal readonly int Cards;
            internal readonly int Seed;
            internal readonly bool Trail;

            internal Cluster(Vector3 point, Vector3 outward, float height, float lean, int cards, int seed, bool trail)
            {
                Point = point;
                Outward = outward;
                Height = height;
                Lean = lean;
                Cards = cards;
                Seed = seed;
                Trail = trail;
            }
        }
    }
}
