using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static class AutumnDenseForest
    {
        internal const string RootName = "ForestBake_Root";
        const string Prefix = "ForestBake_";
        const string Folder = Generated + "/Forest";
        const float RadiusMax = 0.119f;
        const float ChunkLength = 10f;
        const float BackdropDepth = 3.72f;
        const int Seed = 91873;
        static readonly Vector2[] RowLimits =
        {
            new Vector2(1.35f, 1.55f), new Vector2(1.75f, 1.95f), new Vector2(2.15f, 2.40f),
            new Vector2(2.60f, 2.85f), new Vector2(3.10f, 3.45f), new Vector2(3.80f, 4.20f)
        };
        static readonly float[] TrailThresholds = { 0.60f, 0.15f, 0.03f };
        static readonly float[] PerimeterThresholds = { 0.60f, 0.27f, 0.195f };

        [MenuItem("ShadowTan/Setup/Art/Build Autumn Dense Forest")]
        public static void Build() => Run("Dense Forest", Build);

        static void Build(Transform props, Terrain terrain)
        {
            EnsureFolder(Folder);
            var culm = VegetationAtlas.Culm($"{Folder}/{Prefix}Culm.png");
            var leaves = VegetationAtlas.Leaves($"{Folder}/{Prefix}Leaves.png");
            var materials = new[] { Lit(Prefix + "Culm", culm, false, 0.27f), Lit(Prefix + "Leaves", leaves, true, 0.18f, 180f) };
            var backdrop = Material(Prefix + "Depth", "HDRP/Unlit", material =>
            {
                material.SetColor("_UnlitColor", Linear("#241910"));
                material.SetFloat("_DoubleSidedEnable", 1f);
                material.SetFloat("_IncludeIndirectLighting", 0f);
            });

            var root = Node(props, RootName, props.position);
            var objects = new HashSet<string>(StringComparer.Ordinal);
            var meshes = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Counts();
            var left = new Span("Left", EndZ - StartZ, distance => new Vector3(CenterX, 0f, StartZ + distance),
                _ => Vector3.left, 0f, true);
            var right = new Span("Right", EndZ - StartZ, distance => new Vector3(CenterX, 0f, StartZ + distance),
                _ => Vector3.right, 0f, true);
            BuildSpan(left, root, terrain, materials, backdrop, objects, meshes, counts);
            int leftCount = counts.Instances;
            BuildSpan(right, root, terrain, materials, backdrop, objects, meshes, counts);
            int trailCount = counts.Instances;
            foreach (Span span in PerimeterSpans())
                BuildSpan(span, root, terrain, materials, backdrop, objects, meshes, counts);

            Transform legacyBamboo = props.Find("Bamboo");
            int grove = BakeGrove(legacyBamboo, root, materials, objects, meshes);
            AddTrail(terrain, root, objects, meshes);
            var volume = Node(root, Prefix + "TrailAtmosphere", new Vector3(CenterX, 4f, (StartZ + EndZ) * 0.5f));
            objects.Add(volume.name);
            ConfigureAtmosphere(volume);
            int removedNodes = RemoveChildren(root, Prefix, objects);
            int removedMeshes = PruneMeshes(Folder, Prefix, meshes);
            int legacy = RemoveChildren(legacyBamboo, "BambooEdge_");
            Debug.Log($"ShadowTan / Dense Forest: {counts} Trail left/right: {leftCount:N0}/{trailCount - leftCount:N0}. " +
                      $"Removed {legacy} BambooEdge_* instances and {removedNodes} obsolete nodes / {removedMeshes} obsolete meshes. " +
                      $"Replaced rendering for {grove:N0} existing grove plants with baked background meshes. " +
                      "Depth panels merged per span. Culm-only shadow proxies use LOD0.");
        }

        static int BakeGrove(Transform legacy, Transform root, Material[] materials, HashSet<string> objects,
            HashSet<string> meshes)
        {
            if (legacy == null) return 0;
            var left = new List<Transform>();
            var right = new List<Transform>();
            foreach (Transform plant in legacy)
            {
                if (!plant.gameObject.activeSelf || !plant.name.StartsWith("Bamboo_Giant_", StringComparison.Ordinal) ||
                    !plant.TryGetComponent<LODGroup>(out _)) continue;

                (plant.position.x < CenterX ? left : right).Add(plant);
            }

            BuildGroveSide("Left", left);
            BuildGroveSide("Right", right);
            foreach (List<Transform> plants in new[] { left, right })
            {
                foreach (Transform plant in plants)
                {
                    var group = plant.GetComponent<LODGroup>();
                    group.enabled = false;
                    if (PrefabUtility.IsPartOfPrefabInstance(group))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(group);

                    foreach (var renderer in plant.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        renderer.enabled = false;
                        if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
            }

            return left.Count + right.Count;

            void BuildGroveSide(string side, List<Transform> plants)
            {
                if (plants.Count == 0) return;

                Vector3 origin = root.position;
                string name = Prefix + "Grove_" + side;
                var geometry = new Geometry(origin, 2);
                var random = new Random(Seed + (side == "Left" ? 14029 : 15737));
                foreach (Transform original in plants)
                    AddGrovePlant(geometry, original, random);

                Mesh mesh = SaveMesh($"{Folder}/{name}.asset", geometry.MakeMesh(), meshes);
                Render(Node(root, name, origin), mesh, materials, ShadowCastingMode.Off);
                objects.Add(name);
            }
        }

        internal static void AddGrovePlant(Geometry geometry, Transform original, Random random)
        {
            Vector3 basePoint = original.position;
            float scale = Mathf.Abs(original.lossyScale.y);
            if (scale < 0.001f) return;
            float height = 15.85f * scale;
            float phase = original.eulerAngles.y * Mathf.Deg2Rad;
            var across = new Vector3(Mathf.Cos(phase), 0f, Mathf.Sin(phase));
            int hue = random.Next(4);
            for (int stem = 0; stem < 2; stem++)
            {
                float side = stem == 0 ? -1f : 1f;
                float length = height * Range(random, 0.78f, 1.02f);
                Vector3 foot = basePoint + across * (side * scale * 0.23f);
                Vector3 lean = across * (side * scale * Range(random, 0.18f, 0.50f));
                var plant = new Plant(foot, across, lean, length, 0.14f * scale, scale * 0.53f,
                    random.NextDouble() >= 0.60, 3, random.Next());
                AddCulm(geometry, plant, 2, hue);
            }

            for (int layer = 0; layer < 4; layer++)
            {
                Vector3 anchor = basePoint + Vector3.up * height * (0.36f + 0.16f * layer);
                for (int card = 0; card < 3; card++)
                {
                    float angle = phase + (card + 0.39f * layer) * 2.094395f;
                    var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    float reach = scale * Range(random, 1.38f, 2.12f);
                    Vector3 tip = anchor + direction * reach + Vector3.up * (0.62f * scale);
                    Vector3 width = Vector3.Cross(direction, Vector3.up) * (reach * 0.66f) + Vector3.up * (0.25f * scale);
                    geometry.Quad(1, anchor - width * 0.38f, anchor + width * 0.38f,
                        tip + width, tip - width, VegetationAtlas.Card(random.Next(4)));
                }
            }
        }

        static void AddTrail(Terrain terrain, Transform parent, HashSet<string> objects, HashSet<string> meshes)
        {
            const string name = Prefix + "TrailSurface";
            var source = AssetDatabase.LoadAssetAtPath<Material>(Location + "/Materials/M_Autumn_Path.mat");
            if (source == null) throw new InvalidOperationException("The original autumn path material is missing.");

            var material = Lit(name, source.GetTexture("_BaseColorMap"), false, 0.13f);
            material.SetColor("_BaseColor", Linear("#D2B183"));
            material.SetTexture("_EmissiveColorMap", source.GetTexture("_BaseColorMap"));
            material.SetColor("_EmissiveColor", Linear("#D2B183") * 5100f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);

            Vector3 center = new Vector3(CenterX, 0f, (StartZ + EndZ) * 0.5f);
            Transform node = Node(parent, name, center);
            var geometry = new Geometry(center, 1);
            int segments = Mathf.CeilToInt((EndZ - StartZ) / 0.5f);
            for (int i = 0; i <= segments; i++)
            {
                float z = Mathf.Lerp(StartZ, EndZ, (float)i / segments);
                float offset = Mathf.Min(1f, (z - StartZ) * 2f, (EndZ - z) * 2f) * 0.014f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float width = 1.275f + 0.063f * Mathf.Sin(z * 2.43f + side) +
                                  0.043f * Mathf.Sin(z * 0.89f + side * 2.16f);
                    var point = new Vector3(CenterX + side * width, 0f, z);
                    point.y = Ground(terrain, point) + offset + 0.003f;
                    geometry.Vertex(point, Vector3.up, new Vector2(point.x / 3.4f, z / 3.4f));
                }

                if (i == 0) continue;
                int a = (i - 1) * 2;
                geometry.Triangle(0, a, a + 2, a + 1);
                geometry.Triangle(0, a + 1, a + 2, a + 3);
            }

            objects.Add(name);
            Mesh mesh = SaveMesh($"{Folder}/{name}.asset", geometry.MakeMesh(), meshes);
            Render(node, mesh, new[] { material }, ShadowCastingMode.Off);
        }

        static IEnumerable<Span> PerimeterSpans()
        {
            yield return Ring(Arena, "Arena", 0f, Arena.Arc);
            float slopeWest = (Mathf.PI - Clearing.Opening - Clearing.HalfOpening) * Clearing.Radius;
            const float slopeHalfOpening = 6.5f;
            yield return Ring(Clearing, "ClearingNorth", 0f, slopeWest - slopeHalfOpening);
            float southStart = slopeWest + slopeHalfOpening;
            yield return Ring(Clearing, "ClearingSouth", southStart, Clearing.Arc - southStart);
        }

        static Span Ring(Perimeter perimeter, string name, float start, float length) =>
            new Span(name, length, d => perimeter.Point(start + d), d => perimeter.Direction(start + d), 1.4f, false);

        static void BuildSpan(Span span, Transform root, Terrain terrain, Material[] materials, Material backdrop,
            HashSet<string> objects, HashSet<string> meshes, Counts counts)
        {
            float[] thresholds = span.IsTrail ? TrailThresholds : PerimeterThresholds;
            int chunkCount = Mathf.CeilToInt(span.Length / ChunkLength);
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                float start = chunkIndex * ChunkLength;
                float length = Mathf.Min(ChunkLength, span.Length - start);
                string name = $"{Prefix}Chunk_{span.Name}_{chunkIndex:00}";
                Vector3 center = span.Point(start + length * 0.5f) + span.Outward(start + length * 0.5f) * 2.6f;
                center.y = Ground(terrain, center);
                Transform chunk = Node(root, name, center);
                objects.Add(name);

                var random = new Random(Seed + counts.Chunks * 17027);
                List<Plant> plants = PlacePlants(span, terrain, start, length, random);
                int hue = random.Next(4);
                var lods = new LOD[3];
                for (int lod = 0; lod < lods.Length; lod++)
                {
                    var geometry = new Geometry(center, 2);
                    foreach (Plant plant in plants)
                    {
                        AddCulm(geometry, plant, lod, hue);
                        AddLeaves(geometry, plant, lod);
                    }

                    string lodName = $"{name}_LOD{lod}";
                    Mesh mesh = SaveMesh($"{Folder}/{lodName}.asset", geometry.MakeMesh(), meshes);
                    counts.Add(lod, mesh);
                    MeshRenderer renderer = Render(Node(chunk, lodName, center), mesh, materials,
                        ShadowCastingMode.Off, RendererFlags | StaticEditorFlags.OccluderStatic);
                    Renderer[] renderers = lod == 0
                        ? new Renderer[] { renderer, AddShadows(chunk, center, plants, hue, materials[0], meshes) }
                        : new Renderer[] { renderer };
                    lods[lod] = new LOD(thresholds[lod], renderers);
                }

                SetLods(chunk, lods);
                RemoveChildren(chunk, chunk.name + "_Depth");
                if (span.IsTrail) AddColliders(span, chunk, start, length, terrain);

                counts.Instances += plants.Count;
                counts.Chunks++;
            }

            Vector3 backdropCenter = span.Point(span.Length * 0.5f);
            string backdropName = Prefix + "Depth_" + span.Name;
            objects.Add(backdropName);
            AddBackdrop(span, terrain, Node(root, backdropName, backdropCenter), backdropCenter, 0f,
                span.Length, backdrop, meshes);
        }

        internal static MeshRenderer AddShadows(Transform chunk, Vector3 center, List<Plant> plants, int hue,
            Material material, HashSet<string> meshes)
        {
            string name = chunk.name + "_Shadow";
            var geometry = new Geometry(center, 1);
            foreach (Plant plant in plants)
            {
                if (plant.Row < 3 || plant.Seed % 3 == 0)
                    AddCulm(geometry, plant, 2, hue);
            }

            Mesh mesh = SaveMesh($"{Folder}/{name}.asset", geometry.MakeMesh(), meshes);
            var renderer = Render(Node(chunk, name, center), mesh, new[] { material }, ShadowCastingMode.ShadowsOnly);
            renderer.receiveShadows = false;
            return renderer;
        }

        static List<Plant> PlacePlants(Span span, Terrain terrain, float start, float length, Random random)
        {
            var plants = new List<Plant>();
            for (int row = 0; row < RowLimits.Length; row++)
            {
                Vector2 limits = RowLimits[row];
                float step = row == 5 ? 0.83f : 0.413f;
                if (!span.IsTrail) step *= 1.32f;
                for (float slot = step * (0.27f + row * 0.11f); slot < length; slot += Range(random, step * 0.88f, step * 1.08f))
                {
                    if (row == 5 && random.NextDouble() < 0.24) continue;
                    float distance = start + Mathf.Clamp(slot + Range(random, -0.12f, 0.12f), 0.04f, length - 0.04f);
                    float depth = Range(random, limits.x, limits.y);
                    AddPlant(plants, span, terrain, random, distance, depth, row);
                    if (row < 5 && random.NextDouble() < 0.30)
                    {
                        float offset = Range(random, 0.08f, 0.18f) * (random.Next(2) == 0 ? -1f : 1f);
                        float partner = Mathf.Clamp(distance + offset, start + 0.025f, start + length - 0.025f);
                        AddPlant(plants, span, terrain, random, partner, Range(random, limits.x, limits.y), row);
                    }
                }
            }

            if (span.IsTrail) CloseEyeLevelGaps(plants, span, terrain, start, length);
            return plants;
        }

        static void CloseEyeLevelGaps(List<Plant> plants, Span span, Terrain terrain, float start, float length)
        {
            const float maxGap = 0.142f;
            const float edgeGap = maxGap * 0.5f;
            float firstZ = StartZ + start;
            float lastZ = firstZ + length;
            var coverage = new List<Vector2>(plants.Count);
            foreach (Plant plant in plants)
            {
                Vector3 foot = plant.Center(0f);
                float eye = Ground(terrain, foot) + 1.6f;
                float height = (eye - foot.y) / plant.Height;
                float z = plant.Center(height).z;
                float radius = plant.Radius * Mathf.Lerp(1f, 0.49f, height) - 0.006f;
                coverage.Add(new Vector2(z - radius, z + radius));
            }

            coverage.Sort((a, b) => a.x.CompareTo(b.x));
            var random = new Random(Seed + 2213 * Mathf.RoundToInt(start) + (span.Name == "Left" ? 17 : 29));
            float covered = firstZ;
            foreach (Vector2 interval in coverage)
            {
                float allowedGap = covered <= firstZ ? edgeGap : maxGap;
                if (interval.x - covered > allowedGap)
                    FillGap(covered, interval.x);
                covered = Mathf.Max(covered, interval.y);
            }

            if (lastZ - covered > edgeGap) FillGap(covered, lastZ);

            void FillGap(float a, float b)
            {
                int stems = Mathf.CeilToInt((b - a) / 0.16f);
                for (int i = 0; i < stems; i++)
                {
                    float z = Mathf.Lerp(a, b, (i + 0.5f) / stems);
                    AddPlant(plants, span, terrain, random, z - StartZ, Range(random, 2.15f, 2.85f), 2);
                }
            }
        }

        static void AddPlant(List<Plant> plants, Span span, Terrain terrain, Random random, float distance, float depth, int row)
        {
            Vector3 outward = span.Outward(distance);
            Vector3 along = Vector3.Cross(Vector3.up, outward);
            Vector3 position = span.Point(distance) + outward * (depth - span.DepthOffset);
            position.y = Ground(terrain, position) - 0.025f;
            float height = Range(random, 10f, 16f);
            float angle = Range(random, 0f, 2f * Mathf.PI);
            Vector3 lean = (outward * Mathf.Cos(angle) + along * Mathf.Sin(angle)) *
                           (height * Mathf.Tan(Range(random, 0f, 4f) * Mathf.Deg2Rad));
            if (row == 0 && Vector3.Dot(lean, outward) < 0f)
                lean = Vector3.Reflect(lean, outward);

            plants.Add(new Plant(position, outward, lean, height, Range(random, 0.061f, RadiusMax),
                Range(random, 0.42f, 0.66f), random.NextDouble() >= 0.60, row, random.Next()));
        }

        internal static void AddEntrancePlant(Geometry geometry, Vector3 foot, Vector3 outward,
            Vector3 lean, float height, float radius, Random random)
        {
            var plant = new Plant(foot, outward, lean, height, radius, Range(random, 0.42f, 0.67f),
                random.NextDouble() > 0.57, 0, random.Next());
            AddCulm(geometry, plant, 1, random.Next(4));
            AddLeaves(geometry, plant, 1);
        }

        internal static void AddCulm(Geometry geometry, Plant plant, int lod, int hue)
        {
            int sides = lod == 0 ? 8 : lod == 1 ? 6 : 4;
            int intervals = lod == 0 ? 9 : lod == 1 ? 5 : 2;
            int variant = hue + (plant.Light ? 4 : 0);
            float u0 = (variant + 0.025f) / 8f;
            float u1 = (variant + 0.975f) / 8f;
            int first = -1;
            for (int ring = 0; ring <= intervals; ring++)
            {
                float t = (float)ring / intervals;
                Vector3 center = plant.Center(t);
                float radius = plant.Radius * Mathf.Lerp(1f, 0.49f, t);
                for (int side = 0; side <= sides; side++)
                {
                    float u = (float)side / sides;
                    float angle = u * 2f * Mathf.PI;
                    var normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    int vertex = geometry.Vertex(center + radius * normal, normal,
                        new Vector2(Mathf.Lerp(u0, u1, u), plant.Seed % 71 / 71f + t * plant.Height / plant.NodeSpacing));
                    if (first < 0) first = vertex;
                }
            }

            int stride = sides + 1;
            for (int ring = 0; ring < intervals; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int a = first + ring * stride + side;
                    geometry.Triangle(0, a, a + stride, a + stride + 1);
                    geometry.Triangle(0, a, a + stride + 1, a + 1);
                }
            }

            if (lod != 0) return;
            Vector3 top = plant.Center(1f);
            int cap = geometry.Vertex(top, Vector3.up, new Vector2((u0 + u1) * 0.5f, 0.30f));
            for (int side = 0; side < sides; side++)
            {
                float angle = side * (2f * Mathf.PI / sides);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                geometry.Vertex(top + direction * (plant.Radius * 0.49f), Vector3.up,
                    new Vector2(Mathf.Lerp(u0, u1, (float)side / sides), 0.33f));
            }

            for (int side = 0; side < sides; side++)
                geometry.Triangle(0, cap, cap + 1 + (side + 1) % sides, cap + 1 + side);
        }

        // minHeight > 0 оставляет пустым нижний ярус листьев (окно для глаз тигра в первых рядах).
        internal static void AddLeaves(Geometry geometry, Plant plant, int lod, float minHeight = 0f)
        {
            var random = new Random(plant.Seed);
            int clusters = lod == 2 ? 2 : 4;
            for (int cluster = 0; cluster < clusters; cluster++)
            {
                float height = cluster == 0
                    ? Range(random, 1.55f, 3.30f)
                    : cluster == 1 ? Range(random, 3.5f, 5.9f)
                    : cluster == 2 ? plant.Height * Range(random, 0.57f, 0.75f)
                    : plant.Height * Range(random, 0.79f, 0.90f);
                int cards = lod == 0 ? 3 : lod == 1 ? 2 : 1;
                float phase = Range(random, 0f, 2f * Mathf.PI);
                Vector3 anchor = plant.Center(height / plant.Height);
                for (int card = 0; card < 3; card++)
                {
                    float a = phase + card * (2f * Mathf.PI / 3f);
                    var direction = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float length = Range(random, 0.64f, 1.40f);
                    if (plant.Row < 2 && height < 6f)
                    {
                        float towardTrail = -Vector3.Dot(direction, plant.Outward);
                        if (towardTrail > 0.18f) length *= Mathf.Lerp(1f, 0.36f, towardTrail);
                    }

                    Vector3 tip = anchor + direction * length + Vector3.up * Range(random, 0.18f, 0.70f);
                    Vector3 across = Vector3.Cross(direction, Vector3.up) * (length * 0.56f) + Vector3.up * (length * 0.28f);
                    Rect uv = VegetationAtlas.Card(random.Next(4));
                    if (card < cards && height >= minHeight)
                        geometry.Quad(1, anchor - across * 0.30f, anchor + across * 0.30f,
                            tip + across, tip - across, uv);
                }
            }
        }

        static void AddBackdrop(Span span, Terrain terrain, Transform chunk, Vector3 origin, float start, float length,
            Material material, HashSet<string> meshes)
        {
            string name = chunk.name;
            var geometry = new Geometry(origin, 1);
            int panels = Mathf.CeilToInt(length);
            for (int i = 0; i < panels; i++)
            {
                float a = start + length * i / panels;
                float b = start + length * (i + 1) / panels;
                Vector3 p = DepthPoint(span, terrain, a);
                Vector3 q = DepthPoint(span, terrain, b);
                geometry.Quad(0, p, q, q + Vector3.up * DepthHeight(b), p + Vector3.up * DepthHeight(a),
                    new Rect(0f, 0f, 1f, 1f));
            }

            Mesh mesh = SaveMesh($"{Folder}/{name}.asset", geometry.MakeMesh(), meshes);
            Render(chunk, mesh, new[] { material }, ShadowCastingMode.Off,
                RendererFlags | StaticEditorFlags.OccluderStatic);
        }

        static Vector3 DepthPoint(Span span, Terrain terrain, float distance)
        {
            float offset = ClearHalfWidth + BackdropDepth - span.DepthOffset + 0.19f * Mathf.Sin(distance * 0.72f);
            Vector3 point = span.Point(distance) + span.Outward(distance) * offset;
            point.y = Ground(terrain, point) - 0.13f;
            return point;
        }

        static float DepthHeight(float distance) => 9.05f + 0.57f * Mathf.Sin(distance * 0.53f);

        static void AddColliders(Span span, Transform chunk, float start, float length, Terrain terrain)
        {
            int wallLayer = LayerMask.NameToLayer("Wall");
            int segments = Mathf.CeilToInt(length / 3.5f);
            for (int i = 0; i < segments; i++)
            {
                float segmentLength = length / segments;
                float distance = start + segmentLength * (i + 0.5f);
                Vector3 point = span.Point(distance) + span.Outward(distance) * 1.34f;
                point.y = Ground(terrain, point) + 1.24f;
                Transform node = Node(chunk, $"{chunk.name}_Collision_{i}", point);
                if (wallLayer >= 0) node.gameObject.layer = wallLayer;
                var collider = Component<BoxCollider>(node.gameObject);
                collider.size = new Vector3(0.34f, 2.60f, segmentLength + 0.015f);
            }
        }

        static void ConfigureAtmosphere(Transform node)
        {
            const string path = Folder + "/" + Prefix + "TrailAtmosphere.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            if (!profile.TryGet(out Fog fog))
            {
                fog = profile.Add<Fog>(false);
                AssetDatabase.AddObjectToAsset(fog, profile);
            }

            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.tint.Override(Color.Lerp(Color.white, Linear("#D8B284"), 0.62f));
            fog.albedo.Override(Linear("#D8B284"));
            fog.meanFreePath.Override(88f);
            fog.baseHeight.Override(-0.5f);
            fog.maximumHeight.Override(16f);
            fog.depthExtent.Override(65f);
            fog.directionalLightsOnly.Override(true);
            fog.enableVolumetricFog.Override(true);
            EditorUtility.SetDirty(fog);

            if (!profile.TryGet(out HDShadowSettings shadows))
            {
                shadows = profile.Add<HDShadowSettings>(false);
                AssetDatabase.AddObjectToAsset(shadows, profile);
            }

            shadows.maxShadowDistance.Override(35f);
            shadows.cascadeShadowSplitCount.Override(1);
            EditorUtility.SetDirty(shadows);
            EditorUtility.SetDirty(profile);

            var collider = Component<BoxCollider>(node.gameObject);
            collider.isTrigger = true;
            collider.size = new Vector3(9f, 13f, EndZ - StartZ + 2f);
            var volume = Component<Volume>(node.gameObject);
            volume.isGlobal = false;
            volume.priority = 10f;
            volume.blendDistance = 4f;
            volume.sharedProfile = profile;
        }

        readonly struct Span
        {
            internal readonly string Name;
            internal readonly float Length;
            internal readonly Func<float, Vector3> Point;
            internal readonly Func<float, Vector3> Outward;
            internal readonly float DepthOffset;
            internal readonly bool IsTrail;

            internal Span(string name, float length, Func<float, Vector3> point, Func<float, Vector3> outward,
                float depthOffset, bool isTrail)
            {
                Name = name;
                Length = length;
                Point = point;
                Outward = outward;
                DepthOffset = depthOffset;
                IsTrail = isTrail;
            }
        }

        internal readonly struct Plant
        {
            readonly Vector3 position;
            readonly Vector3 lean;
            internal readonly Vector3 Outward;
            internal readonly float Height;
            internal readonly float Radius;
            internal readonly float NodeSpacing;
            internal readonly bool Light;
            internal readonly int Row;
            internal readonly int Seed;

            internal Plant(Vector3 position, Vector3 outward, Vector3 lean, float height, float radius, float nodeSpacing,
                bool light, int row, int seed)
            {
                this.position = position;
                this.lean = lean;
                Outward = outward;
                Height = height;
                Radius = radius;
                NodeSpacing = nodeSpacing;
                Light = light;
                Row = row;
                Seed = seed;
            }

            internal Vector3 Center(float t) => position + Vector3.up * (t * Height) + lean * (t * t) +
                Vector3.Cross(Vector3.up, lean) * (0.24f * Mathf.Sin(t * Mathf.PI));
        }
    }
}
