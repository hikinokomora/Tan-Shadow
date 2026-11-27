using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    // Правки осенней сцены после ревью. Каждый шаг идемпотентен: читает исходные данные (оригинальные меши,
    // GroundDecals, легаси-бамбук, формулу террейна из AutumnPolishPass) и перезаписывает только свой результат.
    public static class AutumnFixPass
    {
        const string Menu = "ShadowTan/Setup/Art/Autumn Fix Pass/";
        const string Folder = Generated + "/FixPass";
        const string ForestFolder = Generated + "/Forest";

        // Измерено до правок: коридор между коллайдерами и кромкой травы 2.3 м. Новая тропа — ровно ×3 от оси.
        internal const float OldHalfWidth = 1.15f;
        internal const float HalfWidth = OldHalfWidth * 3f;
        const float Shift = HalfWidth - OldHalfWidth;
        // Глаза тигра — у новой кромки, в первых редких рядах: морда видна с тропы, тело за плотным слоем.
        const float EyesOffset = HalfWidth + 0.95f;

        static readonly Vector3 TerrainOrigin = new Vector3(20f, 0f, -40f);
        static readonly Vector3 TerrainSize = new Vector3(100f, 1f, 290f);

        // Чёрный задник: граница видимой зоны леса (поляна, тропа, арена).
        const float BackdropClearingRadius = 25.5f;
        const float BackdropTrailHalf = 10.55f;
        const float BackdropArenaRadius = 35f;
        const float WallZ = -6.9f;

        [MenuItem(Menu + "Run All")]
        public static void RunAll()
        {
            Run("All", c =>
            {
                Terrain(c);
                WidenTrail(c);
                PruneBamboo(c);
                Backdrop(c);
                StoneMound(c);
                Decals(c);
                GrassStreaming(c);
                Palette(c);
            });
        }

        [MenuItem(Menu + "1 Terrain (trail x3, shrink)")] public static void MenuTerrain() => Run("Terrain", Terrain);
        [MenuItem(Menu + "2 Widen Trail + Bamboo Rows")] public static void MenuWiden() => Run("Widen Trail", WidenTrail);
        [MenuItem(Menu + "3 Prune Unused Bamboo")] public static void MenuPrune() => Run("Prune Bamboo", PruneBamboo);
        [MenuItem(Menu + "4 Black Backdrop")] public static void MenuBackdrop() => Run("Backdrop", Backdrop);
        [MenuItem(Menu + "5 Start Stone Mound + Hole")] public static void MenuMound() => Run("Stone Mound", StoneMound);
        [MenuItem(Menu + "6 Trail Decals")] public static void MenuDecals() => Run("Decals", Decals);
        [MenuItem(Menu + "7 Arena Grass Streaming")] public static void MenuGrass() => Run("Grass Streaming", GrassStreaming);
        [MenuItem(Menu + "8 Palette")] public static void MenuPalette() => Run("Palette", Palette);

        // ------------------------------------------------------------------ context

        sealed class Context
        {
            internal Scene Scene;
            internal Transform Level, Greybox, Props, Forest, Grass, Polish, Clearing, EntranceFix;
            internal Terrain Terrain;
            internal readonly List<string> Report = new List<string>();
            internal Transform Find(Transform root, string path)
            {
                var t = root.Find(path);
                if (t == null) throw new InvalidOperationException($"Missing {root.name}/{path}");
                return t;
            }
        }

        static void Run(string label, Action<Context> step)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
                throw new InvalidOperationException("Open Autumn_Gameplay in Edit Mode.");

            EnsureFolder(Folder);
            var c = new Context { Scene = scene };
            c.Level = scene.GetRootGameObjects().Single(r => r.name == "[Level]").transform;
            c.Greybox = c.Find(c.Level, "Greybox");
            c.Props = c.Find(c.Greybox, "Props");
            c.Forest = c.Find(c.Props, AutumnDenseForest.RootName);
            c.Grass = c.Find(c.Props, "DenseGrass_Root");
            c.Polish = c.Find(c.Props, "Autumn_ScenePolish");
            c.Clearing = c.Find(c.Greybox, "Entrance_Root/Entrance_Clearing");
            c.EntranceFix = c.Find(c.Greybox, "Entrance_Fix");
            c.Terrain = c.Find(c.Greybox, "Ground/AutumnTerrain").GetComponent<Terrain>();

            var timer = System.Diagnostics.Stopwatch.StartNew();
            step(c);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {scene.path}.");
            Debug.Log($"ShadowTan / Autumn Fix Pass / {label}: {timer.Elapsed.TotalSeconds:F1} s.\n" + string.Join("\n", c.Report));
        }

        // ------------------------------------------------------------------ 1. terrain

        static void Terrain(Context c)
        {
            var terrain = c.Terrain;
            var data = terrain.terrainData;
            terrain.transform.position = TerrainOrigin;
            if (data.size != TerrainSize) data.size = TerrainSize;

            int width = data.alphamapWidth, height = data.alphamapHeight;
            var weights = new float[height, width, 4];
            for (int iz = 0; iz < height; iz++)
            {
                float z = TerrainOrigin.z + TerrainSize.z * iz / (height - 1f);
                for (int ix = 0; ix < width; ix++)
                {
                    float x = TerrainOrigin.x + TerrainSize.x * ix / (width - 1f);
                    Vector4 w = Splat(x, z);
                    weights[iz, ix, 0] = w.x;
                    weights[iz, ix, 1] = w.y;
                    weights[iz, ix, 2] = w.z;
                    weights[iz, ix, 3] = w.w;
                }
            }

            data.SetAlphamaps(0, 0, weights);
            EditorUtility.SetDirty(data);
            terrain.Flush();

            // Земля за пределами террейна закрыта чёрным задником и лесом — лишний 490-метровый плейн не нужен.
            var floor = c.Props.Find("Arena Polish/Forest depth/Forest floor beyond terrain");
            if (floor != null) floor.gameObject.SetActive(false);
            c.Report.Add($"Terrain: {TerrainSize.x}x{TerrainSize.z} m at {TerrainOrigin}; trail tread x3 from z>=4.");
        }

        // Формула AutumnPolishPass.BakeTerrain (проверено: совпадает с текущим alphamap до 0.002),
        // ширина утоптанной полосы ×3 на тропе. На поляне (z<4) рисунок прежний.
        internal static Vector4 Splat(float x, float z)
        {
            float offset = x - CenterX;
            float patch = Mathf.PerlinNoise(x * 0.13f + 14f, z * 0.15f + 23f);
            float broadPatch = Mathf.PerlinNoise(x * 0.052f + 31f, z * 0.055f + 17f);
            float widen = Mathf.Lerp(1f, HalfWidth / OldHalfWidth, Fade(4f, 11f, z));
            float trailCenter = Mathf.Sin(z * 0.27f) * 0.13f * widen;
            float tread = 1f - Fade(0.57f * widen, (1.56f + 0.44f * (patch - 0.5f)) * widen, Mathf.Abs(offset - trailCenter));
            tread *= Fade(-7.2f, -3.4f, z) * (1f - Fade(158f, 168f, z));
            tread *= Mathf.Lerp(0.70f, 0.96f, patch);
            float clearing = Mathf.Sqrt(Mathf.Pow((offset + 0.42f) / 6.1f, 2f) + Mathf.Pow((z - 0.6f) / 9.5f, 2f));
            float wear = (1f - Fade(0.17f, 1.15f, clearing + (broadPatch - 0.5f) * 0.33f)) * Mathf.Lerp(0.28f, 0.64f, patch);
            tread = Mathf.Max(tread, wear);

            float distance = Mathf.Sqrt(offset * offset + (z - 188f) * (z - 188f));
            float sand = 1f - Fade(24.1f, 27.0f, distance + (patch - 0.5f) * 0.85f);
            float sandDetail = sand * Mathf.Lerp(0.09f, 0.21f, broadPatch);
            float grassWeight = (1f - sand) * (1f - tread);
            float dryBlend = Mathf.Lerp(0.16f, 0.72f, broadPatch * 0.74f + patch * 0.26f);
            var w = new Vector4(grassWeight * (1f - dryBlend), grassWeight * dryBlend, tread * (1f - sand) + sandDetail, sand - sandDetail);
            float sum = w.x + w.y + w.z + w.w;
            return sum > 0.0001f ? w / sum : new Vector4(1f, 0f, 0f, 0f);
        }

        static float Fade(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ------------------------------------------------------------------ 2. trail x3 + bamboo rows

        struct Row
        {
            internal float Min, Max, Step, Keep;
            internal int Band;
            internal Row(float min, float max, float step, float keep, int band) { Min = min; Max = max; Step = step; Keep = keep; Band = band; }
        }

        // Глубина ряда — от новой кромки тропы. Band 0: первые три ряда, редко и без нижнего яруса листьев
        // (окно для морды тигра). Band 1: средняя плотность. Band 2: плотная стена, щели на уровне глаз закрыты.
        static readonly Row[] Rows =
        {
            new Row(0.25f, 0.45f, 0.413f, 0.30f, 0), new Row(0.65f, 0.85f, 0.413f, 0.30f, 0), new Row(1.05f, 1.30f, 0.413f, 0.30f, 0),
            new Row(1.50f, 1.75f, 0.413f, 0.70f, 1), new Row(2.00f, 2.35f, 0.413f, 0.70f, 1), new Row(2.65f, 3.00f, 0.413f, 0.70f, 1),
            new Row(3.25f, 3.55f, 0.413f, 1.00f, 2), new Row(3.80f, 4.15f, 0.413f, 1.00f, 2), new Row(4.40f, 4.80f, 0.413f, 1.00f, 2),
            new Row(5.05f, 5.50f, 0.830f, 0.85f, 2)
        };
        const float WallEnd = 5.6f;
        const float FrontLeafFloor = 3.4f;
        static readonly float[] TrailThresholds = { 0.60f, 0.15f, 0.03f };

        static void WidenTrail(Context c)
        {
            var points = c.Scene.GetRootGameObjects().Single(r => r.name == "[Encounter]").transform.Find("Points");
            var ambush = new List<Vector3>();
            foreach (Transform p in points)
            {
                if (!p.name.StartsWith("AmbushPoint_Trail_", StringComparison.Ordinal)) continue;
                float side = Mathf.Sign(p.position.x - CenterX);
                p.position = new Vector3(CenterX + side * EyesOffset, p.position.y, p.position.z);
                ambush.Add(p.position);
            }

            var meshes = new HashSet<string>(StringComparer.Ordinal);
            int plants = 0;
            foreach (int side in new[] { -1, 1 })
            for (int index = 0; index < 15; index++)
                plants += BuildTrailChunk(c, side, index, ambush, meshes);
            c.Report.Add($"Trail bamboo: 30 chunks, {plants} plants (rows: 3 sparse, 3 medium, 4 wall).");

            ShiftTrailGrass(c);
            int legacyWalls = 0;
            foreach (var (name, x) in new[] { ("BambooWall_L", 67.25f - Shift), ("BambooWall_R", 72.75f + Shift) })
            {
                var wall = c.Find(c.Greybox, "Walls/BambooWalls/" + name);
                wall.position = new Vector3(x, wall.position.y, wall.position.z);
                legacyWalls++;
            }

            foreach (var edge in new[] { "Entrance_ClearingEdge_13", "Entrance_ClearingEdge_14" })
                c.Find(c.Clearing, edge).gameObject.SetActive(false);

            Canopy(c);
            ShiftTripoTrail(c);
            CutCorridor(c);

            var volume = c.Forest.Find("ForestBake_TrailAtmosphere")?.GetComponent<BoxCollider>();
            if (volume != null) volume.size = new Vector3(2f * (HalfWidth + 4f), volume.size.y, volume.size.z);
            var triggers = c.Scene.GetRootGameObjects().Single(r => r.name == "[Flow]").transform.Find("Triggers");
            foreach (var name in new[] { "AutumnZoneTrigger_Trail", "TrailActivationTrigger" })
            {
                var box = triggers.Find(name)?.GetComponent<BoxCollider>();
                if (box != null && box.size.x < 2f * HalfWidth + 2f) box.size = new Vector3(2f * HalfWidth + 2f, box.size.y, box.size.z);
            }

            var tuning = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Game/Data/Autumn/BaihuTuning.asset");
            if (tuning != null)
            {
                var so = new SerializedObject(tuning);
                so.FindProperty("trailEyesSideOffset").floatValue = EyesOffset;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(tuning);
            }

            c.Report.Add($"Trail width {2f * OldHalfWidth:F2} -> {2f * HalfWidth:F2} m; ambush/eyes offset {EyesOffset:F2} m; walls moved {legacyWalls}.");
        }

        static int BuildTrailChunk(Context c, int side, int index, List<Vector3> ambush, HashSet<string> meshes)
        {
            string sideName = side < 0 ? "Left" : "Right";
            string name = $"ForestBake_Chunk_{sideName}_{index:00}";
            Transform chunk = c.Find(c.Forest, name);
            var source = c.Find(chunk, name + "_LOD0").GetComponent<MeshRenderer>();
            Material[] materials = source.sharedMaterials;

            const float length = 10f;
            float z0 = StartZ + index * length;
            var outward = new Vector3(side, 0f, 0f);
            Vector3 center = new Vector3(CenterX + side * (HalfWidth + 2.9f), 0f, z0 + length * 0.5f);
            center.y = Ground(c.Terrain, center);
            chunk.SetPositionAndRotation(center, Quaternion.identity);

            var random = new Random(70411 + index * 17027 + (side < 0 ? 0 : 913));
            var list = new List<AutumnDenseForest.Plant>();
            var bands = new List<int>();
            for (int r = 0; r < Rows.Length; r++)
            {
                Row row = Rows[r];
                for (float slot = row.Step * (0.27f + r * 0.11f % 0.66f); slot < length; slot += Range(random, row.Step * 0.88f, row.Step * 1.08f))
                {
                    float z = z0 + Mathf.Clamp(slot + Range(random, -0.12f, 0.12f), 0.04f, length - 0.04f);
                    float depth = Range(random, row.Min, row.Max);
                    bool keep = random.NextDouble() < row.Keep;
                    bool partner = row.Step < 0.5f && random.NextDouble() < 0.30;
                    float partnerZ = Mathf.Clamp(z + Range(random, 0.08f, 0.18f) * (random.Next(2) == 0 ? -1f : 1f), z0 + 0.025f, z0 + length - 0.025f);
                    float partnerDepth = Range(random, row.Min, row.Max);
                    if (keep) TryAdd(z, depth);
                    if (keep && partner) TryAdd(partnerZ, partnerDepth);

                    void TryAdd(float pz, float pd)
                    {
                        var plant = MakePlant(c.Terrain, random, side, pz, pd, row.Band);
                        if (row.Band < 2 && BlocksSight(plant, ambush, side)) return;
                        list.Add(plant);
                        bands.Add(row.Band);
                    }
                }
            }

            CloseWallGaps(c.Terrain, list, bands, side, z0, length, random);

            int hue = random.Next(4);
            var lods = new LOD[3];
            for (int lod = 0; lod < 3; lod++)
            {
                var geometry = new Geometry(center, 2);
                for (int i = 0; i < list.Count; i++)
                {
                    AutumnDenseForest.AddCulm(geometry, list[i], lod, hue);
                    AutumnDenseForest.AddLeaves(geometry, list[i], lod, bands[i] == 0 ? FrontLeafFloor : 0f);
                }

                string lodName = $"{name}_LOD{lod}";
                Mesh mesh = SaveMesh($"{ForestFolder}/{lodName}.asset", geometry.MakeMesh(), meshes);
                MeshRenderer renderer = Render(Node(chunk, lodName, center), mesh, materials,
                    ShadowCastingMode.Off, RendererFlags | StaticEditorFlags.OccluderStatic);
                Renderer[] renderers = lod == 0
                    ? new Renderer[] { renderer, AutumnDenseForest.AddShadows(chunk, center, list, hue, materials[0], meshes) }
                    : new Renderer[] { renderer };
                lods[lod] = new LOD(TrailThresholds[lod], renderers);
            }

            SetLods(chunk, lods);

            int wallLayer = LayerMask.NameToLayer("Wall");
            const int segments = 3;
            for (int i = 0; i < segments; i++)
            {
                float segment = length / segments;
                var point = new Vector3(CenterX + side * (HalfWidth + 0.17f), 0f, z0 + segment * (i + 0.5f));
                point.y = Ground(c.Terrain, point) + 1.24f;
                Transform node = Node(chunk, $"{name}_Collision_{i}", point);
                node.gameObject.layer = wallLayer;
                Component<BoxCollider>(node.gameObject).size = new Vector3(0.34f, 2.6f, segment + 0.015f);
            }

            return list.Count;
        }

        static AutumnDenseForest.Plant MakePlant(Terrain terrain, Random random, int side, float z, float depth, int band)
        {
            var outward = new Vector3(side, 0f, 0f);
            var along = Vector3.forward;
            var position = new Vector3(CenterX + side * (HalfWidth + depth), 0f, z);
            position.y = Ground(terrain, position) - 0.025f;
            float height = Range(random, 10f, 16f);
            float angle = Range(random, 0f, 2f * Mathf.PI);
            Vector3 lean = (outward * Mathf.Cos(angle) + along * Mathf.Sin(angle)) *
                           (height * Mathf.Tan(Range(random, 0f, 4f) * Mathf.Deg2Rad));
            if (band == 0 && Vector3.Dot(lean, outward) < 0f) lean = Vector3.Reflect(lean, outward);
            int row = band == 0 ? 0 : band == 1 ? 1 : 3;
            return new AutumnDenseForest.Plant(position, outward, lean, height, Range(random, 0.061f, 0.119f),
                Range(random, 0.42f, 0.66f), random.NextDouble() >= 0.60, row, random.Next());
        }

        // Линия взгляда с тропы на глаза у каждой AmbushPoint_Trail: стебли первых рядов на высоте морды её не перекрывают.
        static bool BlocksSight(AutumnDenseForest.Plant plant, List<Vector3> ambush, int side)
        {
            Vector3 foot = plant.Center(0f);
            float t = Mathf.Clamp01((1.35f) / plant.Height);
            Vector3 p = plant.Center(t);
            var flat = new Vector2(p.x, p.z);
            foreach (Vector3 eye in ambush)
            {
                if (Mathf.Sign(eye.x - CenterX) != side) continue;
                var e = new Vector2(eye.x, eye.z);
                if (DistanceToSegment(flat, new Vector2(CenterX, eye.z - 8f), e) < 0.5f) return true;
                if (DistanceToSegment(flat, new Vector2(CenterX, eye.z - 2f), e) < 0.5f) return true;
                if (DistanceToSegment(flat, new Vector2(CenterX - side * 1.5f, eye.z + 3f), e) < 0.45f) return true;
            }
            return false;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        // Как CloseEyeLevelGaps в AutumnDenseForest, но только для плотной стены (band 2).
        static void CloseWallGaps(Terrain terrain, List<AutumnDenseForest.Plant> plants, List<int> bands, int side,
            float z0, float length, Random random)
        {
            const float maxGap = 0.142f;
            var coverage = new List<Vector2>();
            for (int i = 0; i < plants.Count; i++)
            {
                if (bands[i] != 2) continue;
                var plant = plants[i];
                Vector3 foot = plant.Center(0f);
                float eye = Ground(terrain, foot) + 1.6f;
                float height = (eye - foot.y) / plant.Height;
                float z = plant.Center(height).z;
                float radius = plant.Radius * Mathf.Lerp(1f, 0.49f, height) - 0.006f;
                coverage.Add(new Vector2(z - radius, z + radius));
            }

            coverage.Sort((a, b) => a.x.CompareTo(b.x));
            float covered = z0;
            var gaps = new List<Vector2>();
            foreach (Vector2 interval in coverage)
            {
                if (interval.x - covered > (covered <= z0 ? maxGap * 0.5f : maxGap)) gaps.Add(new Vector2(covered, interval.x));
                covered = Mathf.Max(covered, interval.y);
            }
            if (z0 + length - covered > maxGap * 0.5f) gaps.Add(new Vector2(covered, z0 + length));

            foreach (Vector2 gap in gaps)
            {
                int stems = Mathf.CeilToInt((gap.y - gap.x) / 0.16f);
                for (int i = 0; i < stems; i++)
                {
                    float z = Mathf.Lerp(gap.x, gap.y, (i + 0.5f) / stems);
                    plants.Add(MakePlant(terrain, random, side, z, Range(random, 3.45f, 4.1f), 2));
                    bands.Add(2);
                }
            }
        }

        static void ShiftTrailGrass(Context c)
        {
            int moved = 0;
            foreach (Transform chunk in c.Grass)
            {
                bool left = chunk.name.StartsWith("DenseGrass_Chunk_Left_", StringComparison.Ordinal);
                bool right = chunk.name.StartsWith("DenseGrass_Chunk_Right_", StringComparison.Ordinal);
                if (!left && !right) continue;
                var renderer = chunk.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name.EndsWith("_LOD0", StringComparison.Ordinal));
                if (renderer == null) continue;
                foreach (var filter in chunk.GetComponentsInChildren<MeshFilter>(true))
                    filter.sharedMesh = Original(filter, $"{Generated}/DenseGrass/{filter.name}.asset");
                renderer.ResetBounds();
                float edge = left ? renderer.bounds.max.x : renderer.bounds.min.x;
                float target = CenterX + (left ? -HalfWidth : HalfWidth);
                chunk.position += new Vector3(target - edge, 0f, 0f);

                // Живая кромка: в полосе глаз тигра (до EyesOffset) трава ~0.8 м, дальше плавно дорастает до полной высоты.
                // Морда на высоте 1.2–1.6 м у новой кромки остаётся над травой, тело — в высокой траве и бамбуке за ней.
                foreach (var filter in chunk.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh original = filter.sharedMesh;
                    var vertices = original.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 world = filter.transform.TransformPoint(vertices[i]);
                        float depth = Mathf.Abs(world.x - CenterX) - HalfWidth;
                        float scale = Mathf.Lerp(0.45f, 1f, Fade(EyesOffset - HalfWidth + 0.25f, EyesOffset - HalfWidth + 1.3f, depth));
                        float ground = Ground(c.Terrain, world);
                        world.y = ground + (world.y - ground) * scale;
                        vertices[i] = filter.transform.InverseTransformPoint(world);
                    }
                    var mesh = Object.Instantiate(original);
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    filter.sharedMesh = SaveOwnMesh($"{Folder}/DenseGrass/{filter.name}.asset", mesh);
                }
                moved++;
            }
            c.Report.Add($"Trail grass: {moved} chunks moved to the new edge (x = 70 ± {HalfWidth:F2}); edge grass 45% height in the eyes band, 100% from {EyesOffset - HalfWidth + 1.3f:F2} m.");
        }

        static void Canopy(Context c)
        {
            for (int start = 0; start < 3; start++)
            {
                Transform node = c.Find(c.Polish, $"Canopy_{start}");
                Material[] materials = node.GetComponent<MeshRenderer>().sharedMaterials;
                float z0 = 12.1f + start * 46f;
                var origin = new Vector3(CenterX, 0f, z0 + 24f);
                var random = new Random(55117 + start * 977);
                var geometry = new Geometry(origin, 2);
                for (int i = 0; i < 12; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    float z = z0 + i * 3.9f + Range(random, 0.1f, 2f);
                    var foot = new Vector3(CenterX + side * (HalfWidth + Range(random, 0.15f, 0.7f)), 0f, z);
                    foot.y = Ground(c.Terrain, foot) - 0.09f;
                    AutumnDenseForest.AddEntrancePlant(geometry, foot, new Vector3(-side, 0f, 0f),
                        new Vector3(-side * Range(random, 5.2f, 7.4f), 0f, Range(random, -0.69f, 0.67f)),
                        Range(random, 10.5f, 14f), Range(random, 0.045f, 0.075f), random);
                }

                node.SetPositionAndRotation(origin, Quaternion.identity);
                node.GetComponent<MeshFilter>().sharedMesh = SaveOwnMesh($"{Folder}/Canopy_{start}.asset", geometry.MakeMesh());
                node.GetComponent<MeshRenderer>().sharedMaterials = materials;
            }
        }

        static void ShiftTripoTrail(Context c)
        {
            var root = c.Find(c.Props, "TripoProp_Root");
            int batches = 0, instances = 0;
            foreach (Transform batch in root)
            {
                if (!batch.name.StartsWith("TripoProp_Batch_Trail_", StringComparison.Ordinal)) continue;
                foreach (var filter in batch.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh original = Original(filter, $"{Generated}/Tripo/Chunks/{filter.name}.asset");
                    var vertices = original.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 world = filter.transform.TransformPoint(vertices[i]);
                        world.x += Mathf.Sign(world.x - CenterX) * Shift;
                        vertices[i] = filter.transform.InverseTransformPoint(world);
                    }
                    var mesh = Object.Instantiate(original);
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    filter.sharedMesh = SaveOwnMesh($"{Folder}/Tripo/{filter.name}.asset", mesh);
                }
                batch.GetComponent<LODGroup>()?.RecalculateBounds();
                batches++;
            }

            foreach (Transform instance in c.Find(root, "TripoProp_Instances"))
            {
                if (!instance.name.StartsWith("TripoProp_Trail_", StringComparison.Ordinal)) continue;
                float dx = instance.position.x - CenterX;
                if (Mathf.Abs(dx) >= HalfWidth) continue;
                instance.position += new Vector3(Mathf.Sign(dx) * Shift, 0f, 0f);
                instances++;
            }
            c.Report.Add($"Tripo trail props: {batches} batches and {instances} collider instances moved to the new edge.");
        }

        static bool InCorridor(Vector3 p, float margin = 0.2f) =>
            p.z > 9.3f && p.z < 162f && Mathf.Abs(p.x - CenterX) < HalfWidth + margin;

        // Всё, что после расширения оказалось внутри тропы на входе с поляны: подлесок, солома, Tripo-пучки поляны.
        static void CutCorridor(Context c)
        {
            int removed = 0;
            removed += CutPlants(c.Find(c.Polish, "EntranceUnderstory"), "ScenePolish/EntranceUnderstory.asset", 0, null, null, -1,
                foot => InCorridor(foot, 0.35f));
            foreach (var name in new[] { "EntranceFerns", "EntranceStraw" })
                removed += CutSmall(c.Find(c.Polish, name), $"ScenePolish/{name}.asset", p => InCorridor(p));
            removed += CutSmall(c.Find(c.Clearing, "Entrance_ClearingStraw"), "Entrance/Entrance_ClearingStraw.asset", p => InCorridor(p));

            var root = c.Find(c.Props, "TripoProp_Root");
            foreach (Transform batch in root)
            {
                if (!batch.name.StartsWith("TripoProp_Batch_Clearing_", StringComparison.Ordinal)) continue;
                foreach (var filter in batch.GetComponentsInChildren<MeshFilter>(true))
                    removed += CutSmall(filter.transform, $"Tripo/Chunks/{filter.name}.asset", p => InCorridor(p, 0.4f), "Tripo/");
                batch.GetComponent<LODGroup>()?.RecalculateBounds();
            }
            foreach (Transform instance in c.Find(root, "TripoProp_Instances"))
                if (instance.name.StartsWith("TripoProp_Clearing_", StringComparison.Ordinal) && InCorridor(instance.position, 0.4f) &&
                    instance.GetComponent<Collider>() == null)
                    instance.gameObject.SetActive(false);
            c.Report.Add($"Corridor cleanup: {removed} plant/tuft parts removed from the new trail mouth.");
        }

        // ------------------------------------------------------------------ 3. unused bamboo

        internal static bool InsideBackdrop(Vector3 p, float margin)
        {
            if (p.z > WallZ && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(CenterX, 0f)) <= BackdropClearingRadius - margin) return true;
            if (p.z > 0f && p.z < 170f && Mathf.Abs(p.x - CenterX) <= BackdropTrailHalf - margin) return true;
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(CenterX, 188f)) <= BackdropArenaRadius - margin;
        }

        static void PruneBamboo(Context c)
        {
            // Легаси Bamboo_Giant — только источник для ForestBake_Grove. Убираем то, что за чёрным задником (не видно)
            // и то, что попало в первые редкие ряды у новой тропы. Пользовательские выключенные не трогаем.
            Transform legacy = c.Find(c.Props, "Bamboo");
            var left = new List<Transform>();
            var right = new List<Transform>();
            int disabledFar = 0, disabledFront = 0, alreadyOff = 0;
            foreach (Transform plant in legacy)
            {
                if (!plant.name.StartsWith("Bamboo_Giant_", StringComparison.Ordinal) || !plant.TryGetComponent<LODGroup>(out _)) continue;
                if (!plant.gameObject.activeSelf) { alreadyOff++; continue; }
                Vector3 p = plant.position;
                bool front = p.z > 9f && p.z < 162f && Mathf.Abs(p.x - CenterX) < HalfWidth + 1.4f;
                bool far = !InsideBackdrop(p, 0.3f);
                if (front || far)
                {
                    plant.gameObject.SetActive(false);
                    if (front) disabledFront++; else disabledFar++;
                    continue;
                }
                (p.x < CenterX ? left : right).Add(plant);
            }

            foreach (var (sideName, plants, seed) in new[] { ("Left", left, 91873 + 14029), ("Right", right, 91873 + 15737) })
            {
                var node = c.Find(c.Forest, "ForestBake_Grove_" + sideName);
                Material[] materials = node.GetComponent<MeshRenderer>().sharedMaterials;
                var geometry = new Geometry(c.Forest.position, 2);
                var random = new Random(seed);
                foreach (Transform plant in plants) AutumnDenseForest.AddGrovePlant(geometry, plant, random);
                node.GetComponent<MeshFilter>().sharedMesh = SaveOwnMesh($"{Folder}/ForestBake_Grove_{sideName}.asset", geometry.MakeMesh());
                node.GetComponent<MeshRenderer>().sharedMaterials = materials;
            }

            int culms = CutPlants(c.Find(c.Clearing, "Entrance_ClearingCulm"), "Entrance/Entrance_ClearingCulm.asset", 0,
                c.Find(c.Clearing, "Entrance_ClearingLeaves"), "Entrance/Entrance_ClearingLeaves.asset", 0,
                foot => !InsideBackdrop(foot, -1.5f) || InCorridor(foot, 0.35f));

            c.Report.Add($"Legacy bamboo: grove {left.Count}+{right.Count} kept; disabled {disabledFar} behind the backdrop and " +
                         $"{disabledFront} in the sparse front rows; {alreadyOff} user-disabled untouched. Clearing culm parts removed: {culms}.");
        }

        // ------------------------------------------------------------------ 4. black backdrop

        static void Backdrop(Context c)
        {
            Transform root = c.Find(c.Level, "Atmosphere/BambooBackdrop_Black");
            var material = root.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).First(m => m != null);
            material.SetFloat("_DoubleSidedEnable", 1f);
            material.SetFloat("_CullMode", 0f);
            material.SetFloat("_CullModeForward", 0f);
            material.SetColor("_UnlitColor", new Color(0.008f, 0.008f, 0.008f, 1f));
            material.SetColor("_EmissiveColor", Color.black);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            foreach (Transform child in root)
                if (child.name != "Loop") child.gameObject.SetActive(false);

            var path = new List<Vector3>();
            void Arc(Vector3 center, float radius, float fromDeg, float toDeg)
            {
                int steps = Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) * Mathf.Deg2Rad * radius / 1.5f);
                for (int i = path.Count > 0 ? 1 : 0; i <= steps; i++)
                {
                    float a = Mathf.Lerp(fromDeg, toDeg, (float)i / steps) * Mathf.Deg2Rad;
                    path.Add(center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
                }
            }
            void Line(Vector3 a, Vector3 b)
            {
                int steps = Mathf.CeilToInt(Vector3.Distance(a, b) / 1.5f);
                for (int i = 1; i <= steps; i++) path.Add(Vector3.Lerp(a, b, (float)i / steps));
            }

            var clearing = new Vector3(CenterX, 0f, 0f);
            var arena = new Vector3(CenterX, 0f, 188f);
            float wallAngle = Mathf.Asin(-WallZ / BackdropClearingRadius) * Mathf.Rad2Deg;
            float clearingJoin = Mathf.Acos(BackdropTrailHalf / BackdropClearingRadius) * Mathf.Rad2Deg;
            float joinZ = Mathf.Sqrt(BackdropClearingRadius * BackdropClearingRadius - BackdropTrailHalf * BackdropTrailHalf);
            float arenaZ = 188f - Mathf.Sqrt(BackdropArenaRadius * BackdropArenaRadius - BackdropTrailHalf * BackdropTrailHalf);
            float arenaJoin = Mathf.Atan2(arenaZ - 188f, -BackdropTrailHalf) * Mathf.Rad2Deg + 360f;

            Arc(clearing, BackdropClearingRadius, 180f + wallAngle, 180f - clearingJoin);
            Line(path[path.Count - 1], new Vector3(CenterX - BackdropTrailHalf, 0f, arenaZ));
            Arc(arena, BackdropArenaRadius, arenaJoin, 540f - arenaJoin - 360f + 0f);
            Line(path[path.Count - 1], new Vector3(CenterX + BackdropTrailHalf, 0f, joinZ));
            Arc(clearing, BackdropClearingRadius, clearingJoin, -wallAngle);

            var geometry = new Geometry(Vector3.zero, 1);
            float travelled = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                float ta = travelled, tb = travelled + Vector3.Distance(a, b);
                travelled = tb;
                float ha = 9.6f + 1.3f * Mathf.Sin(ta * 0.37f) + 0.6f * Mathf.Sin(ta * 1.13f);
                float hb = 9.6f + 1.3f * Mathf.Sin(tb * 0.37f) + 0.6f * Mathf.Sin(tb * 1.13f);
                a.y = Ground(c.Terrain, a) - 0.6f;
                b.y = Ground(c.Terrain, b) - 0.6f;
                geometry.Quad(0, a, b, b + Vector3.up * hb, a + Vector3.up * ha, new Rect(0f, 0f, 1f, 1f));
            }

            Transform loop = root.Find("Loop");
            if (loop == null)
            {
                loop = new GameObject("Loop").transform;
                loop.SetParent(root, false);
            }
            loop.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            loop.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            var renderer = Render(loop, SaveOwnMesh($"{Folder}/BambooBackdrop_Loop.asset", geometry.MakeMesh()),
                new[] { material }, ShadowCastingMode.Off, StaticEditorFlags.BatchingStatic);
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.allowOcclusionWhenDynamic = true;
            c.Report.Add($"Black backdrop: one loop, {path.Count - 1} panels, {travelled:F0} m; clearing r{BackdropClearingRadius}, " +
                         $"trail ±{BackdropTrailHalf}, arena r{BackdropArenaRadius}, height 8.3..11.5 m.");
        }

        // ------------------------------------------------------------------ 5. stone mound + hole

        const float DeckY = 1.2f, DeckFrontZ = -5.6f, PathEndZ = 2.6f, PathHalf = 2.6f, ShoulderWidth = 1.6f;
        const float MoundMinX = 55.5f, MoundMaxX = 84.5f, MoundMaxZ = 5.4f;

        static float PathY(float z) =>
            z <= DeckFrontZ ? DeckY : DeckY * (1f - Fade(0f, 1f, (z - DeckFrontZ) / (PathEndZ - DeckFrontZ)));

        static float HeapY(float x, float z)
        {
            float rx = (x - CenterX) / 13.5f, rz = (z - WallZ) / 11.8f;
            float r = Mathf.Sqrt(rx * rx + rz * rz);
            if (r >= 1f) return 0f;
            float h = 3.3f * Mathf.Pow(1f - r, 1.2f);
            h += (Mathf.PerlinNoise(x * 0.33f + 3.1f, z * 0.33f + 9.7f) - 0.5f) * 0.45f * Mathf.Clamp01(h * 1.5f);
            return Mathf.Max(0f, h);
        }

        internal static float MoundY(float x, float z)
        {
            if (z < WallZ - 0.2f) return 0f;
            float path = PathY(z);
            float dx = Mathf.Abs(x - CenterX);
            if (dx >= PathHalf + ShoulderWidth) return HeapY(x, z);
            float heap = Mathf.Max(HeapY(x, z), path);
            float s = Fade(0f, 1f, (dx - PathHalf) / ShoulderWidth);
            return Mathf.Lerp(path, heap, s);
        }

        static Vector3 MoundNormal(float x, float z)
        {
            const float e = 0.15f;
            float dx = MoundY(x + e, z) - MoundY(x - e, z);
            float dz = MoundY(x, z + e) - MoundY(x, z - e);
            return new Vector3(-dx, 2f * e, -dz).normalized;
        }

        static void StoneMound(Context c)
        {
            // Насыпь из круглых камней вместо лестницы (силуэт грейбокса: конус от портала к земле).
            c.Find(c.EntranceFix, "Slope").gameObject.SetActive(false);
            foreach (var name in new[] { "WornStonePaving", "PavingInfill", "RusticatedPlinth" })
                c.Find(c.EntranceFix, "Entrance_Polish/" + name).gameObject.SetActive(false);

            Transform root = c.Props.Find("Start_StoneMound");
            if (root == null)
            {
                root = new GameObject("Start_StoneMound").transform;
                root.SetParent(c.Props, false);
            }
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var stoneMaterial = AssetDatabase.LoadAssetAtPath<Material>(Location + "/Materials/M_Autumn_Stone.mat");
            var pebbleMaterial = AssetDatabase.LoadAssetAtPath<Material>(Location + "/Materials/M_Autumn_Pebbles.mat");
            // Оба материала используются только насыпью: бурые камни насыпи (GDD-AU §03, «Камни» #78583D).
            pebbleMaterial.SetColor("_BaseColor", new Color(0.56f, 0.45f, 0.33f, 1f));
            pebbleMaterial.SetFloat("_NormalScale", 1.3f);
            stoneMaterial.SetColor("_BaseColor", new Color(0.42f, 0.35f, 0.27f, 1f));
            EditorUtility.SetDirty(pebbleMaterial);
            EditorUtility.SetDirty(stoneMaterial);

            // Подложка и коллайдер ходьбы из одной высотной сетки.
            const float step = 0.35f;
            int nx = Mathf.CeilToInt((MoundMaxX - MoundMinX) / step) + 1;
            int nz = Mathf.CeilToInt((MoundMaxZ - WallZ) / step) + 1;
            Mesh Grid(float lift)
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = Mathf.Min(MoundMinX + ix * step, MoundMaxX);
                    float z = Mathf.Min(WallZ + iz * step, MoundMaxZ);
                    float y = MoundY(x, z);
                    verts.Add(new Vector3(x, y > 0.001f ? y + lift : -0.15f, z));
                    uvs.Add(new Vector2(x / 0.75f, z / 0.75f));
                    if (ix > 0 && iz > 0)
                    {
                        int a = iz * nx + ix, b = a - 1, d = a - nx, e = d - 1;
                        tris.AddRange(new[] { e, b, a, e, a, d });
                    }
                }
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }

            var bed = Node(root, "Mound_Bed", Vector3.zero);
            var bedRenderer = Render(bed, SaveOwnMesh($"{Folder}/Mound_Bed.asset", Grid(-0.07f)), new[] { pebbleMaterial },
                ShadowCastingMode.Off, RendererFlags);
            var walk = Node(root, "Mound_Walk", Vector3.zero);
            walk.gameObject.layer = LayerMask.NameToLayer("Ground");
            Component<MeshCollider>(walk.gameObject).sharedMesh = SaveOwnMesh($"{Folder}/Mound_Walk.asset", Grid(0f));

            Mesh[] cobbles = AssetDatabase.LoadAllAssetsAtPath(Location + "/Models/SM_RoundedCobblePack_Fixed.fbx").OfType<Mesh>().ToArray();
            Mesh[] pebbles = AssetDatabase.LoadAllAssetsAtPath(Location + "/Models/SM_StonesPebblesPack_Fixed.fbx").OfType<Mesh>().ToArray();
            if (cobbles.Length == 0 || pebbles.Length == 0) throw new InvalidOperationException("Stone packs are missing.");

            var random = new Random(4242);
            var placed = new List<Vector4>();
            var large = new List<CombineInstance>();
            var medium = new List<CombineInstance>();
            var small = new List<CombineInstance>();
            var boulders = new List<(Vector3 center, float radius)>();

            // Меши паков тяжёлые (~1100 треугольников на камень): мелочь даёт текстура подложки, а камней — сотни, не тысячи.
            Scatter(cobbles, large, 45, 0.60f, 1.10f, 0.38f, flankOnly: true, minHeight: 0.3f, boulders);
            Scatter(cobbles, medium, 210, 0.28f, 0.52f, 0.42f, flankOnly: false, minHeight: 0.04f, null);
            Scatter(pebbles, small, 24, 0.08f, 0.15f, 0.35f, flankOnly: false, minHeight: 0.0f, null);

            void Scatter(Mesh[] source, List<CombineInstance> output, int count, float minSize, float maxSize, float sink,
                bool flankOnly, float minHeight, List<(Vector3, float)> colliders)
            {
                int attempts = 0;
                while (output.Count < count && attempts++ < count * 40)
                {
                    float x = Range(random, MoundMinX + 0.5f, MoundMaxX - 0.5f);
                    float z = Range(random, WallZ + 0.25f, MoundMaxZ - 0.3f);
                    float y = MoundY(x, z);
                    bool onPath = Mathf.Abs(x - CenterX) < PathHalf + 0.3f;
                    if (y < minHeight || (flankOnly && onPath) || (y < 0.02f && random.NextDouble() < 0.6)) continue;
                    if (Mathf.Abs(x - CenterX) < 2.6f && z < DeckFrontZ) continue;
                    float size = Range(random, minSize, maxSize) * (onPath ? 0.8f : 1f);
                    if (placed.Any(p => (new Vector2(p.x - x, p.z - z)).magnitude < (p.w + size) * 0.42f)) continue;

                    Mesh mesh = source[random.Next(source.Length)];
                    Vector3 extent = mesh.bounds.size;
                    float scale = size / Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
                    Vector3 normal = MoundNormal(x, z);
                    Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal) *
                        Quaternion.Euler(Range(random, -12f, 12f), Range(random, 0f, 360f), Range(random, -12f, 12f));
                    if (onPath) rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);

                    var matrix = Matrix4x4.TRS(Vector3.zero, rotation, Vector3.one * scale);
                    float lowest = float.MaxValue, highest = float.MinValue;
                    Bounds b = mesh.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        var corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                        float cy = matrix.MultiplyPoint3x4(corner).y;
                        lowest = Mathf.Min(lowest, cy);
                        highest = Mathf.Max(highest, cy);
                    }
                    float tall = highest - lowest;
                    float embed = onPath ? 0.55f : sink;
                    var position = new Vector3(x, y - lowest - tall * embed, z);
                    output.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, rotation, Vector3.one * scale) });
                    placed.Add(new Vector4(x, y, z, size));
                    colliders?.Add((new Vector3(x, y + tall * (0.5f - embed) * 0.8f, z), size * 0.42f));
                }
            }

            Mesh Combine(List<CombineInstance> list)
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(list.ToArray(), true, true);
                mesh.RecalculateBounds();
                return mesh;
            }

            var largeRenderer = Render(Node(root, "Mound_Stones_Large", Vector3.zero), SaveOwnMesh($"{Folder}/Mound_Stones_Large.asset", Combine(large)),
                new[] { stoneMaterial }, ShadowCastingMode.On, RendererFlags);
            var mediumRenderer = Render(Node(root, "Mound_Stones_Medium", Vector3.zero), SaveOwnMesh($"{Folder}/Mound_Stones_Medium.asset", Combine(medium)),
                new[] { stoneMaterial }, ShadowCastingMode.On, RendererFlags);
            var smallRenderer = Render(Node(root, "Mound_Pebbles", Vector3.zero), SaveOwnMesh($"{Folder}/Mound_Pebbles.asset", Combine(small)),
                new[] { pebbleMaterial }, ShadowCastingMode.Off, RendererFlags);
            // Издалека (с середины тропы) остаются подложка и крупные камни.
            SetLods(root, new[]
            {
                new LOD(0.30f, new Renderer[] { bedRenderer, largeRenderer, mediumRenderer, smallRenderer }),
                new LOD(0.02f, new Renderer[] { bedRenderer, largeRenderer })
            });
            FillClearingGap(c);

            Transform colliders = Node(root, "Mound_BoulderColliders", Vector3.zero);
            for (int i = colliders.childCount - 1; i >= 0; i--) Object.DestroyImmediate(colliders.GetChild(i).gameObject);
            for (int i = 0; i < boulders.Count; i++)
            {
                var node = Node(colliders, $"Boulder_{i:00}", boulders[i].center);
                node.gameObject.layer = LayerMask.NameToLayer("Wall");
                Component<SphereCollider>(node.gameObject).radius = boulders[i].radius;
            }

            // Дырка слева: у FootEast не было пары, со ступеней можно было уйти вдоль стены на −X.
            Transform east = c.Find(c.Clearing, "Entrance_ClearingFootEast");
            Transform west = c.Clearing.Find("Entrance_ClearingFootWest");
            if (west == null)
            {
                west = Object.Instantiate(east.gameObject, c.Clearing).transform;
                west.name = "Entrance_ClearingFootWest";
            }
            foreach (Transform foot in new[] { east, west })
            {
                var box = foot.GetComponent<BoxCollider>();
                box.size = new Vector3(box.size.x, 4.6f, box.size.z);
                float x = foot == east ? east.position.x : 2f * CenterX - east.position.x;
                foot.SetPositionAndRotation(new Vector3(x, 2.2f, east.position.z), east.rotation);
            }

            int verts = large.Concat(medium).Concat(small).Sum(i => i.mesh.vertexCount);
            c.Report.Add($"Stone mound: {large.Count} large (+{boulders.Count} colliders), {medium.Count} head-sized, {small.Count} pebbles, " +
                         $"{verts:N0} verts; peak {HeapY(CenterX + 4f, WallZ + 0.3f):F2} m, path {DeckY} -> 0 m over {PathEndZ - DeckFrontZ:F1} m. " +
                         "Stairs disabled; Entrance_ClearingFootWest added.");
        }

        // Кольцо бамбука поляны было разорвано на западе (≈162°…198°, проём под склон старой схемы) — это и есть
        // дырка слева от старта. Закрываем её чанками того же кольца, которые стояли за 60-метровой стеной и не были видны.
        static void FillClearingGap(Context c)
        {
            var pivot = new Vector3(CenterX, 0f, 0f);
            int moved = 0, hidden = 0;
            void Move(Transform root, string name, float yaw)
            {
                Transform t = c.Find(root, name);
                Quaternion target = Quaternion.Euler(0f, yaw, 0f);
                if (Quaternion.Angle(t.rotation, target) < 0.01f) return;
                if (Quaternion.Angle(t.rotation, Quaternion.identity) > 0.01f)
                    throw new InvalidOperationException($"{name} has an unexpected rotation.");
                t.SetPositionAndRotation(pivot + target * (t.position - pivot), target);
                t.gameObject.SetActive(true);
                moved++;
            }
            Move(c.Forest, "ForestBake_Chunk_ClearingSouth_01", 68f);
            Move(c.Forest, "ForestBake_Chunk_ClearingSouth_02", 78f);
            Move(c.Grass, "DenseGrass_Chunk_ClearingSouth_02", 68f);
            Move(c.Grass, "DenseGrass_Chunk_ClearingSouth_03", 68f);

            foreach (var name in new[] { "ForestBake_Chunk_ClearingSouth_03", "ForestBake_Chunk_ClearingSouth_04" })
            { c.Find(c.Forest, name).gameObject.SetActive(false); hidden++; }
            foreach (var name in new[] { "01", "04", "05", "06", "07" })
            { c.Find(c.Grass, "DenseGrass_Chunk_ClearingSouth_" + name).gameObject.SetActive(false); hidden++; }
            c.Report.Add($"Clearing gap: {moved} ring chunks moved from behind the wall into 162..204°; {hidden} hidden chunks disabled.");
        }

        // ------------------------------------------------------------------ 6. trail decals

        static void Decals(Context c)
        {
            // Декали вдоль тропы: разнесены по новой ширине (×3 от оси) и развёрнуты на 180° вокруг своего центра.
            // Размер и порядок материалов прежние. Декали поляны и арены не трогаются.
            Transform source = c.Find(c.Props, "GroundDecals");
            var groups = source.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>()?.sharedMaterial != null)
                .GroupBy(f => Mathf.FloorToInt(f.transform.position.z / 18f));
            int flipped = 0;
            foreach (var group in groups)
            {
                var origin = new Vector3(CenterX, 0f, group.Key * 18f + 9f);
                var subsets = group.GroupBy(f => f.GetComponent<MeshRenderer>().sharedMaterial)
                    .OrderBy(s => s.Key.name, StringComparer.Ordinal).ToArray();
                var geometry = new Geometry(origin, subsets.Length);
                for (int slot = 0; slot < subsets.Length; slot++)
                foreach (MeshFilter filter in subsets[slot])
                {
                    Matrix4x4 matrix = filter.transform.localToWorldMatrix;
                    Vector3 p = filter.transform.position;
                    if (p.z > 8f && p.z < 165f)
                    {
                        float dx = Mathf.Clamp((p.x - CenterX) * 3f, -(HalfWidth - 0.35f), HalfWidth - 0.35f);
                        var target = new Vector3(CenterX + dx, p.y, p.z);
                        matrix = Matrix4x4.Translate(target) * Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f)) *
                                 Matrix4x4.Translate(-p) * matrix;
                        flipped++;
                    }

                    Mesh mesh = filter.sharedMesh;
                    Vector3[] positions = mesh.vertices;
                    Vector2[] uv = mesh.uv;
                    var indices = new int[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 point = matrix.MultiplyPoint3x4(positions[i]);
                        point.y = Ground(c.Terrain, point) + 0.014f;
                        indices[i] = geometry.Vertex(point, Vector3.up, uv[i]);
                    }
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                        geometry.Triangle(slot, indices[triangles[i]], indices[triangles[i + 1]], indices[triangles[i + 2]]);
                }

                Transform node = c.Polish.Find($"Litter_{group.Key:00}");
                if (node == null) continue;
                var batch = geometry.MakeMesh();
                batch.RecalculateTangents();
                node.SetPositionAndRotation(origin, Quaternion.identity);
                node.GetComponent<MeshFilter>().sharedMesh = SaveOwnMesh($"{Folder}/Litter_{group.Key:00}.asset", batch);
                node.GetComponent<MeshRenderer>().sharedMaterials = subsets.Select(s => s.Key).ToArray();
            }
            c.Report.Add($"Trail decals: {flipped} flipped 180° and spread to ±{HalfWidth - 0.35f:F2} m; ground offset 0.014 m.");
        }

        // ------------------------------------------------------------------ 7. arena grass streaming

        static void GrassStreaming(Context c)
        {
            // LODGroup.size травы был 0.9 м вместо ~9 м: чанки отсекались уже на ~30 м, со входа дальнее кольцо пропадало.
            int grass = 0;
            foreach (Transform chunk in c.Grass)
            {
                var group = chunk.GetComponent<LODGroup>();
                if (group == null) continue;
                group.RecalculateBounds();
                var lods = group.GetLODs();
                if (lods.Length == 3)
                {
                    lods[0].screenRelativeTransitionHeight = 0.50f;
                    lods[1].screenRelativeTransitionHeight = 0.20f;
                    lods[2].screenRelativeTransitionHeight = 0.012f;
                    group.SetLODs(lods);
                }
                grass++;
            }

            int arena = 0;
            foreach (Transform chunk in c.Forest)
            {
                if (!chunk.name.StartsWith("ForestBake_Chunk_Arena_", StringComparison.Ordinal)) continue;
                var group = chunk.GetComponent<LODGroup>();
                var lods = group.GetLODs();
                lods[lods.Length - 1].screenRelativeTransitionHeight = 0.05f;
                group.SetLODs(lods);
                arena++;
            }
            c.Report.Add($"Grass streaming: {grass} grass LODGroups recalculated (LOD0 <~16 m, LOD1 <~40 m, cull >~650 m); " +
                         $"{arena} arena bamboo chunks cull at 0.05 instead of 0.195.");
        }

        // ------------------------------------------------------------------ 8. palette

        static void Palette(Context c)
        {
            AutumnFixPalette.Apply(c.Scene, c.Report);
        }

        // ------------------------------------------------------------------ mesh helpers

        static Mesh SaveOwnMesh(string path, Mesh mesh)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
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

        // Исходный меш: если фильтр уже смотрит на результат fix pass, берём оригинал по известному пути.
        static Mesh Original(MeshFilter filter, string originalPath)
        {
            string current = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (!current.StartsWith(Folder, StringComparison.Ordinal)) return filter.sharedMesh;
            var original = AssetDatabase.LoadAssetAtPath<Mesh>(originalPath);
            if (original == null) throw new InvalidOperationException($"Original mesh {originalPath} is missing.");
            return original;
        }

        sealed class Parts
        {
            internal Mesh Mesh;
            internal Transform Transform;
            internal Vector3[] World;
            internal int[] Component;
            internal int Count;
            internal int[] Size;
            internal Vector3[] Foot, Centroid;
            internal int[][] Triangles;
            internal List<int>[] Members;

            internal Parts(Mesh mesh, Transform transform)
            {
                Mesh = mesh;
                Transform = transform;
                Vector3[] local = mesh.vertices;
                World = local.Select(transform.TransformPoint).ToArray();
                Triangles = Enumerable.Range(0, mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
                var parent = Enumerable.Range(0, local.Length).ToArray();
                int Find(int a)
                {
                    while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
                    return a;
                }
                foreach (int[] t in Triangles)
                for (int i = 0; i < t.Length; i += 3)
                {
                    int a = Find(t[i]), b = Find(t[i + 1]), d = Find(t[i + 2]);
                    parent[b] = a;
                    parent[Find(d)] = a;
                }
                var ids = new Dictionary<int, int>();
                Component = new int[local.Length];
                for (int i = 0; i < local.Length; i++)
                {
                    int r = Find(i);
                    if (!ids.TryGetValue(r, out int id)) { id = ids.Count; ids[r] = id; }
                    Component[i] = id;
                }
                Count = ids.Count;
                Size = new int[Count];
                Foot = Enumerable.Repeat(new Vector3(0f, float.MaxValue, 0f), Count).ToArray();
                Centroid = new Vector3[Count];
                Members = new List<int>[Count];
                for (int i = 0; i < Count; i++) Members[i] = new List<int>();
                for (int i = 0; i < local.Length; i++)
                {
                    int id = Component[i];
                    Members[id].Add(i);
                    Size[id]++;
                    Centroid[id] += World[i];
                    if (World[i].y < Foot[id].y) Foot[id] = World[i];
                }
                for (int i = 0; i < Count; i++) Centroid[i] /= Mathf.Max(1, Size[i]);
            }

            internal Mesh Without(bool[] remove)
            {
                var map = new int[World.Length];
                for (int i = 0; i < map.Length; i++) map[i] = -1;
                var keep = new List<int>();
                var subs = new List<int>[Triangles.Length];
                for (int s = 0; s < Triangles.Length; s++)
                {
                    subs[s] = new List<int>();
                    int[] t = Triangles[s];
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        if (remove[Component[t[i]]]) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            int v = t[i + k];
                            if (map[v] < 0) { map[v] = keep.Count; keep.Add(v); }
                            subs[s].Add(map[v]);
                        }
                    }
                }

                var mesh = new Mesh { indexFormat = keep.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                T[] Pick<T>(T[] array) => array != null && array.Length == World.Length ? keep.Select(i => array[i]).ToArray() : null;
                mesh.vertices = Pick(Mesh.vertices);
                var normals = Pick(Mesh.normals); if (normals != null) mesh.normals = normals;
                var tangents = Pick(Mesh.tangents); if (tangents != null) mesh.tangents = tangents;
                var colors = Pick(Mesh.colors32); if (colors != null) mesh.colors32 = colors;
                var uv = Pick(Mesh.uv); if (uv != null) mesh.uv = uv;
                var uv2 = Pick(Mesh.uv2); if (uv2 != null) mesh.uv2 = uv2;
                mesh.subMeshCount = subs.Length;
                for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // Удаляет стебли (связные трубки с основанием, где removeFoot) вместе с их листьями и шапками:
        // мелкие части приписываются ближайшему стеблю, чтобы не оставалось парящих листьев.
        static int CutPlants(Transform culmNode, string culmOriginal, int culmSub, Transform leafNode, string leafOriginal, int leafSub,
            Func<Vector3, bool> removeFoot)
        {
            var culmFilter = culmNode.GetComponent<MeshFilter>();
            var culms = new Parts(Original(culmFilter, $"{Generated}/{culmOriginal}"), culmNode);
            Parts leaves = null;
            MeshFilter leafFilter = null;
            if (leafNode != null)
            {
                leafFilter = leafNode.GetComponent<MeshFilter>();
                leaves = new Parts(Original(leafFilter, $"{Generated}/{leafOriginal}"), leafNode);
            }

            var inSub = new bool[culms.Count];
            foreach (int v in culms.Triangles[culmSub]) inSub[culms.Component[v]] = true;
            bool IsStem(int id) => inSub[id] && culms.Size[id] >= 20;

            var removeCulm = new bool[culms.Count];
            var grid = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < culms.World.Length; i += 2)
            {
                int id = culms.Component[i];
                if (!IsStem(id)) continue;
                var cell = Vector3Int.FloorToInt(culms.World[i]);
                if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
                list.Add(i);
            }
            int removed = 0;
            for (int id = 0; id < culms.Count; id++)
                if (IsStem(id) && removeFoot(culms.Foot[id])) { removeCulm[id] = true; removed++; }

            int Owner(Parts parts, int id)
            {
                int best = -1;
                float bestDistance = 2.5f;
                foreach (int v in parts.Members[id])
                {
                    Vector3 p = parts.World[v];
                    var cell = Vector3Int.FloorToInt(p);
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!grid.TryGetValue(cell + new Vector3Int(dx, dy, dz), out var list)) continue;
                        foreach (int i in list)
                        {
                            float d = (culms.World[i] - p).sqrMagnitude;
                            if (d < bestDistance * bestDistance) { bestDistance = Mathf.Sqrt(d); best = culms.Component[i]; }
                        }
                    }
                }
                return best;
            }

            for (int id = 0; id < culms.Count; id++)
            {
                if (IsStem(id)) continue;
                int owner = Owner(culms, id);
                removeCulm[id] = owner >= 0 ? removeCulm[owner] : removeFoot(culms.Centroid[id]);
                if (owner < 0 && removeCulm[id]) removed++;
            }
            culmFilter.sharedMesh = SaveOwnMesh($"{Folder}/{Path.GetFileName(culmOriginal)}", culms.Without(removeCulm));

            if (leaves != null)
            {
                var removeLeaf = new bool[leaves.Count];
                for (int id = 0; id < leaves.Count; id++)
                {
                    int owner = Owner(leaves, id);
                    removeLeaf[id] = owner >= 0 ? removeCulm[owner] : removeFoot(leaves.Centroid[id]);
                }
                leafFilter.sharedMesh = SaveOwnMesh($"{Folder}/{Path.GetFileName(leafOriginal)}", leaves.Without(removeLeaf));
            }
            return removed;
        }

        // Мелкие пучки (солома, папоротник, Tripo-пучки): удаляются целиком, если центр внутри заданной зоны.
        static int CutSmall(Transform node, string original, Func<Vector3, bool> remove, string subFolder = "")
        {
            var filter = node.GetComponent<MeshFilter>();
            var parts = new Parts(Original(filter, $"{Generated}/{original}"), node);
            var mask = new bool[parts.Count];
            int removed = 0;
            for (int id = 0; id < parts.Count; id++)
                if (remove(parts.Centroid[id])) { mask[id] = true; removed++; }
            var mesh = parts.Without(mask);
            filter.sharedMesh = SaveOwnMesh($"{Folder}/{subFolder}{Path.GetFileName(original)}", mesh);
            return removed;
        }
    }
}
