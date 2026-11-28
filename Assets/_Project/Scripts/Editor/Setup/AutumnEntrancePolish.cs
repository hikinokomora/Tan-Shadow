using System;
using System.Collections.Generic;
using ShadowTan.Gameplay.Autumn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static class AutumnEntrancePolish
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string Folder = Generated + "/EntrancePolish";
        const string Tripo = Generated + "/Tripo";
        const string ArtName = "Entrance_Polish";
        const string TriggerName = "Entrance_Moments";
        const string AudioFolder = "Assets/_Game/Audio/Placeholder/";

        [MenuItem("ShadowTan/Setup/Art/Polish Autumn Entrance")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Autumn_Gameplay in Edit Mode before polishing the entrance.");

            var greybox = Required(Root(scene, "[Level]"), "Greybox");
            var entrance = Required(greybox, "Entrance_Fix");
            var deck = Required(entrance, "UpperDeck/EntranceFloor").GetComponent<BoxCollider>();
            var ramp = Required(entrance, "Slope/RampWalk").GetComponent<MeshCollider>();
            var landing = Required(entrance, "Landing/LandingWalk").GetComponent<MeshCollider>();
            var terrain = Required(greybox, "Ground/AutumnTerrain").GetComponent<Terrain>();
            var triggers = Required(Root(scene, "[Flow]"), "Triggers");
            var heroine = Root(scene, "Heroine").GetComponent<CharacterController>();
            var gizmos = Root(scene, "[Gizmos]");
            var camera = Required(gizmos, "EntranceCamera").GetComponent<Camera>();
            var playerCamera = Required(gizmos, "Entrance_PlayerCamera").GetComponent<Camera>();
            var warm = Required(entrance, "EntranceWarmFill").GetComponent<Light>();
            var reveal = Required(entrance, "EntranceRevealFill").GetComponent<Light>();
            var layout = new Layout(entrance, deck, ramp, landing, terrain);
            var assets = new EntranceAssets();

            Undo.IncrementCurrentGroup();
            int operation = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Polish Autumn Entrance");
            try
            {
                EnsureFolder(Folder);
                Physics.SyncTransforms();
                var art = PrepareGroup(entrance, ArtName);
                var momentRoot = PrepareGroup(triggers, TriggerName);
                momentRoot.SetPositionAndRotation(entrance.position, entrance.rotation);
                DressWall(art, layout, assets);
                DressGround(art, layout, assets);
                BuildGate(art, layout, assets);
                BuildStoneApproach(art, layout, assets);
                ReplaceEntranceSurface(Required(entrance, "Slope/Section_00/Cobbles_00"));
                ReplaceEntranceSurface(Required(entrance, "Landing/Gravel"));
                ReplaceEntranceSurface(Required(greybox, "Entrance_Root/Entrance_Clearing/Entrance_ClearingSand"));
                BuildMoments(momentRoot, layout, assets, heroine, playerCamera);
                SetLighting(warm, reveal, layout);

                Undo.RecordObjects(new UnityEngine.Object[] { camera, camera.transform }, "Frame autumn entrance");
                Vector3 viewpoint = layout.World(-3.92f, 3.55f, 15.85f);
                camera.transform.SetPositionAndRotation(viewpoint,
                    Quaternion.LookRotation(layout.World(0.40f, 2.90f, 2.38f) - viewpoint));
                camera.fieldOfView = 66f;
                camera.nearClipPlane = 0.08f;
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log("Autumn entrance polish applied. Scene is ready to save.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(operation);
                throw;
            }
            finally
            {
                Undo.CollapseUndoOperations(operation);
            }

            void ReplaceEntranceSurface(Transform node)
            {
                var renderer = node.GetComponent<MeshRenderer>();
                Undo.RecordObject(renderer, "Update entrance paving");
                renderer.enabled = false;
            }
        }

        static void BuildGate(Transform art, Layout layout, EntranceAssets assets)
        {
            var stone = new Geometry(Vector3.zero, 1);
            var iron = new Geometry(Vector3.zero, 1);
            var glow = new Geometry(Vector3.zero, 1);
            var random = new Random(1909);
            float top = layout.Floor + 5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Ashlar(stone, side * 3.28f, 0.37f, 1.55f, 0.70f, 0.79f);
                Ashlar(stone, side * 3.18f, 0.93f, 1.32f, 0.49f, 0.66f);
                Ashlar(stone, side * 3.08f, 1.35f, 1.11f, 0.32f, 0.59f);
                for (int i = 0; i < 5; i++)
                {
                    float y = layout.Floor + 0.73f + i * 0.86f;
                    float width = i % 2 == 0 ? 0.80f : 0.97f;
                    Ashlar(stone, side * (2.50f + width * 0.50f), y, width,
                        0.83f, Range(random, 0.50f, 0.62f));
                }
                Ashlar(stone, side * 3.08f, top - 0.31f, 1.12f, 0.30f, 0.69f);
                Ashlar(stone, side * 3.23f, top - 0.06f, 1.43f, 0.19f, 0.82f);
                Fixture(side * 3.68f, layout.Floor + (side < 0 ? 2.63f : 2.38f));
            }

            for (int course = 0; course < 3; course++)
            {
                int count = course == 1 ? 5 : 4;
                float span = course == 2 ? 7.94f : 7.57f;
                float height = course == 1 ? 0.51f : 0.19f;
                float center = top + (course == 0 ? 0.12f : course == 1 ? 0.48f : 0.85f);
                for (int i = 0; i < count; i++)
                    Ashlar(stone, ((i + 0.5f) / count - 0.5f) * span, center,
                        span / count - 0.017f, height, course == 1 ? 0.65f : 0.88f);
            }

            // A bevelled keystone and three inlaid claw cuts mark the gate.
            Ashlar(stone, 0f, top + 0.47f, 0.84f, 0.74f, 0.97f);
            for (int i = -1; i <= 1; i++)
            {
                Vector3 slash = V(i * 0.14f, top + 0.47f, 0.976f);
                Line(iron, slash + V(-0.085f, -0.15f, 0f), slash + V(0.092f, 0.17f, 0f),
                    0.048f, Vector3.forward);
            }

            const float rearFace = -6.46f;
            for (int row = 0; row < 5; row++)
            for (int column = 0; column < 4; column++)
                Box(stone, V((column - 1.5f) * 1.20f, layout.Floor + 0.44f + row * 0.93f, rearFace),
                    V(1.18f, 0.90f, 0.18f));
            Box(stone, V(0f, top - 0.34f, rearFace + 0.14f), V(4.85f, 0.23f, 0.38f));

            Save(art, "GateMasonry", stone, assets.Portal);
            Save(art, "GateIronwork", iron, assets.Iron);
            Save(art, "LanternGlass", glow, assets.Amber);

            void Fixture(float x, float y)
            {
                Vector3 center = V(x, y, 0.70f);
                Box(iron, center + V(0f, 0.56f, -0.32f), V(0.22f, 0.07f, 0.66f));
                Box(iron, center + V(0f, 0.48f, 0f), V(0.075f, 0.18f, 0.078f));
                Box(iron, center + V(0f, 0.37f, 0f), V(0.47f, 0.085f, 0.36f));
                Box(iron, center + V(0f, -0.37f, 0f), V(0.44f, 0.08f, 0.35f));
                Box(glow, center, V(0.32f, 0.63f, 0.25f));
                foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Box(iron, center + V(sx * 0.172f, 0f, sz * 0.133f), V(0.037f, 0.68f, 0.037f));
                Box(iron, center + V(0f, -0.11f, 0.142f), V(0.38f, 0.034f, 0.036f));
            }
        }

        static void BuildStoneApproach(Transform art, Layout layout, EntranceAssets assets)
        {
            var warm = new Geometry(Vector3.zero, 1);
            var cool = new Geometry(Vector3.zero, 1);
            var random = new Random(711);
            float start = layout.RampHead + 0.044f;
            float end = layout.RampToe;
            for (int row = 0; row < 8; row++)
            {
                float z0 = Mathf.Lerp(start, end, row / 8f);
                float z1 = Mathf.Lerp(start, end, (row + 1f) / 8f);
                float split = Range(random, -0.18f, 0.18f);
                for (int column = 0; column < 4; column++)
                {
                    float x0 = -2.38f + column * 1.19f + (column == 0 ? 0f : split);
                    float x1 = -2.38f + (column + 1) * 1.19f + (column == 3 ? 0f : split);
                    Slab((row + column * 2) % 5 == 1 ? cool : warm, x0 + 0.021f, x1 - 0.021f,
                        z0 + 0.018f, z1 - 0.018f);
                }
            }

            Save(art, "WornStonePaving", warm, assets.Paving);
            Save(art, "PavingInfill", cool, assets.PavingWeathered);

            void Slab(Geometry mesh, float left, float right, float front, float back)
            {
                float a = Range(random, 0.025f, 0.105f), b = Range(random, 0.029f, 0.115f);
                float c = Range(random, 0.026f, 0.094f), d = Range(random, 0.023f, 0.10f);
                var rim = new[] { At(left+a,front), At(left,front+a), At(left,back-b), At(left+b,back),
                    At(right-c,back), At(right,back-c), At(right,front+d), At(right-d,front) };
                Vector3 center = At((left + right) * 0.5f, (front + back) * 0.5f);
                int index = Add(center);
                foreach (Vector3 point in rim) Add(point);
                for (int i = 0; i < rim.Length; i++)
                {
                    int next = (i + 1) % rim.Length;
                    mesh.Triangle(0, index, index + 1 + i, index + 1 + next);
                    Vector3 p = rim[i], q = rim[next];
                    mesh.Quad(0, p - Vector3.up * 0.11f, q - Vector3.up * 0.11f, q, p,
                        new Rect(p.x * 0.72f, p.z * 0.72f, Vector3.Distance(p, q) * 0.72f, 0.08f));
                }

                Vector3 At(float x, float z) => V(x,
                    layout.WalkHeight(x, z) + 0.042f, z);
                int Add(Vector3 point) => mesh.Vertex(point, Vector3.up, new Vector2(point.x, point.z) * 0.83f);
            }
        }

        static void Box(Geometry mesh, Vector3 center, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            var a = center + V(-half.x,-half.y,-half.z);
            var b = center + V(half.x,-half.y,-half.z);
            var c = center + V(half.x,half.y,-half.z);
            var d = center + V(-half.x,half.y,-half.z);
            var z = Vector3.forward * size.z;
            var uv = new Rect(0, 0, size.x, size.y);
            mesh.Quad(0, b, a, d, c, uv);
            mesh.Quad(0, a+z, b+z, c+z, d+z, uv);
            mesh.Quad(0, a, a+z, d+z, d, uv);
            mesh.Quad(0, b+z, b, c, c+z, uv);
            mesh.Quad(0, d+z, c+z, c, d, uv);
            mesh.Quad(0, a, b, b+z, a+z, uv);
        }

        static void DressWall(Transform art, Layout layout, EntranceAssets a)
        {
            var ashlar = new Geometry(Vector3.zero, 1);
            var weathering = new Geometry(Vector3.zero, 1);
            var moss = new Geometry(Vector3.zero, 1);
            var random = new Random(8133);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int row = 0; row < 3; row++)
                for (int i = 0; i < 4; i++)
                {
                    float inner = 3f + i * 1.38f + row * 0.18f;
                    float x = (inner + 0.66f) * side;
                    float height = 0.45f + Range(random, -0.045f, 0.045f);
                    float reach = Range(random, 0.105f, 0.29f) - row * 0.017f;
                    Ashlar(ashlar, x, 0.295f + row * 0.49f, 1.34f, height, reach);
                    if ((i + row) % 3 != 0)
                        Stain(moss, x - 0.64f, 0.10f + row * 0.49f, reach + 0.003f, 0.49f, 0.19f, random);
                }

                for (int i = 0; i < 6; i++)
                {
                    float x = side * (3.16f + i * 0.83f + Range(random, -0.16f, 0.16f));
                    float top = layout.Floor + Range(random, 1.8f, 5.8f);
                    Stain(weathering, x, top, 0.051f, Range(random, 0.09f, 0.30f),
                        -Range(random, 0.38f, 1.64f), random);
                }

                // Two exposed courses sit in the shoulder, outside the central walk line.
                const float shoulder = 2.32f;
                TreadEnd(ashlar, side * shoulder, 2.69f);
                TreadEnd(ashlar, side * shoulder, 4.40f);
            }

            Line(weathering, V(-3.15f, 5.90f, 0.051f), V(-3.25f, 5.35f, 0.051f), 0.025f, Vector3.forward);
            Line(weathering, V(-3.25f, 5.35f, 0.051f), V(-3.56f, 5.08f, 0.051f), 0.015f, Vector3.forward);
            Line(weathering, V(-3.25f, 5.35f, 0.051f), V(-3.20f, 5.13f, 0.051f), 0.010f, Vector3.forward);
            Line(weathering, V(2.74f, 4.72f, 0.374f), V(2.67f, 4.49f, 0.374f), 0.020f, Vector3.forward);
            Line(weathering, V(2.67f, 4.49f, 0.374f), V(2.58f, 4.36f, 0.374f), 0.010f, Vector3.forward);

            // The leaves follow the masonry joints and stop outside the clear opening.
            Vine(-3.95f, 0.92f, 5.30f, 0.23f);
            Vine(-4.32f, 0.54f, 3.37f, 0.33f);
            Vine(3.93f, 1.09f, 3.44f, -0.23f);
            Vine(3.38f, 0.24f, 2.78f, -0.24f);
            Save(art, "RusticatedPlinth", ashlar, a.Portal);
            Save(art, "RunoffAndCracks", weathering, a.Dark);
            Save(art, "WallMossAndVines", moss, a.Green);

            void TreadEnd(Geometry mesh, float x, float z)
            {
                float y0 = layout.WalkHeight(x, z - 0.17f);
                float y1 = layout.WalkHeight(x, z + 0.17f);
                Vector3 p = V(x - 0.16f, y0 + 0.035f, z - 0.17f);
                Vector3 q = V(x - 0.15f, y1 + 0.044f, z + 0.17f);
                Vector3 r = V(x + 0.16f, y1 + 0.044f, z + 0.165f);
                Vector3 s = V(x + 0.15f, y0 + 0.035f, z - 0.16f);
                GroundQuad(mesh, p, q, r, s, 1.25f);
                mesh.Quad(0, q - Vector3.up * 0.14f, r - Vector3.up * 0.14f, r, q,
                    new Rect(x, z, 0.25f, 0.12f));
            }

            void Vine(float x, float baseY, float length, float lean)
            {
                Vector3 last = V(x, baseY, 0.30f);
                for (int i = 1; i <= 22; i++)
                {
                    float t = i / 22f;
                    var next = V(x + lean * t + Mathf.Sin(i * 0.63f) * 0.15f, baseY + t * length,
                        Mathf.Lerp(0.30f, 0.082f, Mathf.InverseLerp(1.1f, 1.68f, baseY + t * length)));
                    Line(moss, last, next, 0.018f * (1f - t * 0.65f), Vector3.forward);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var tip = next + V(side * Range(random, 0.08f, 0.25f), Range(random, 0.015f, 0.13f), 0.022f);
                        Leaf(moss, next, tip, Vector3.forward, Range(random, 0.045f, 0.10f));
                    }
                    last = next;
                }
            }
        }

        static void DressGround(Transform art, Layout layout, EntranceAssets a)
        {
            var reeds = new Geometry(Vector3.zero, 2);
            var greens = new Geometry(Vector3.zero, 1);
            var straw = new Geometry(Vector3.zero, 1);
            var tracks = new Geometry(Vector3.zero, 1);
            var random = new Random(12911);

            // The grove closes around the wall in groups connected to the existing forest.
            Grove(-5.05f, 1.52f, 1.05f, 1.08f, 8, 7.2f, 10.5f);
            Grove(-6.30f, 4.35f, 1.64f, 1.36f, 11, 7.6f, 12.2f);
            Grove(-7.85f, 7.03f, 1.76f, 1.91f, 13, 8.2f, 12.1f);
            Grove(-9.78f, 10.62f, 1.74f, 1.70f, 12, 8.3f, 12.2f);
            Grove(5.22f, 1.64f, 1.02f, 1.08f, 8, 7.1f, 10.6f);
            Grove(6.08f, 4.78f, 1.46f, 1.77f, 11, 7.8f, 11.8f);
            Grove(8.18f, 6.37f, 1.63f, 1.45f, 12, 8.7f, 12.6f);
            Grove(8.73f, 10.46f, 1.91f, 1.73f, 13, 8.1f, 12.7f);

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 14; i++)
                {
                    float z = 0.80f + i * 0.68f + Range(random, -0.22f, 0.21f);
                    float x = side * (3.65f + Mathf.Sin(i * 0.41f) * 0.41f + Range(random, 0f, 0.9f));
                    Vector3 foot = V(x, Surface(x, z) - 0.035f, z);
                    Tuft(straw, foot, Range(random, 0.29f, 0.88f), random);
                    if (i % 3 == 0)
                        Fern(greens, foot, Range(random, 0.35f, 0.74f), random);
                }
            }

            for (int i = 0; i < 5; i++)
            {
                float x = 1.32f + i * 0.35f + (i % 2 == 0 ? 0.12f : -0.13f);
                float z = layout.RampToe + 1.4f + i * 0.95f;
                Paw(tracks, V(x, Surface(x, z) + 0.009f, z), 0.12f + i * 0.009f, 0.48f);
            }

            Save(art, "BambooAtEntrance", reeds.MakeMesh(), new[] { a.Culm, a.BambooLeaves }, true);
            Save(art, "FernsAndBambooLeaves", greens, a.Green);
            Save(art, "DryGrass", straw, a.Straw);
            Save(art, "ClawAndPawMarks", tracks, a.Track);
            foreach (string old in new[] { "GravelApronAndClearingEdge", "BrokenAndLivingBamboo", "LeafLitter", "SeatedBouldersAndScree" })
            {
                Transform node = art.Find(old);
                if (node != null) node.gameObject.SetActive(false);
            }

            var surface = new Geometry(Vector3.zero, 1);
            const int columns = 58, rows = 51;
            const float minX = -12.5f, maxX = 12.5f, minZ = 0.18f, maxZ = 20.5f;
            for (int iz = 0; iz <= rows; iz++)
            for (int ix = 0; ix <= columns; ix++)
            {
                var uv = new Vector2(ix / (float)columns, iz / (float)rows);
                float x = Mathf.Lerp(minX, maxX, uv.x), z = Mathf.Lerp(minZ, maxZ, uv.y);
                int v = surface.Vertex(V(x, Surface(x, z), z), Vector3.up, uv);
                if (ix == columns || iz == rows) continue;
                surface.Triangle(0, v, v + columns + 1, v + columns + 2);
                surface.Triangle(0, v, v + columns + 2, v + 1);
            }
            var ground = surface.MakeMesh();
            ground.RecalculateNormals();
            Save(art, "ForestFloorAtEntrance", ground, a.ForestFloor(SoilTexture()));
            BuildBankCollision();

            float Surface(float x, float z) => layout.GroundHeight(x, z) + 0.036f + BankHeight(x, z);

            void BuildBankCollision()
            {
                const int across = 13, along = 22;
                var collision = new Geometry(Vector3.zero, 1);
                for (int side = -1; side <= 1; side += 2)
                for (int iz = 0; iz <= along; iz++)
                for (int ix = 0; ix <= across; ix++)
                {
                    float x = Mathf.Lerp(3.09f, 9.20f, ix / (float)across) * side;
                    float z = Mathf.Lerp(0.33f, 10.75f, iz / (float)along);
                    float y = layout.GroundHeight(x, z) + BankHeight(x, z) - 0.006f;
                    int v = collision.Vertex(V(x, y, z), Vector3.up, Vector2.zero);
                    if (ix == across || iz == along) continue;
                    int next = v + across + 1;
                    if (side < 0)
                    {
                        collision.Triangle(0, v, next + 1, next);
                        collision.Triangle(0, v, v + 1, next + 1);
                    }
                    else
                    {
                        collision.Triangle(0, v, next, next + 1);
                        collision.Triangle(0, v, next + 1, v + 1);
                    }
                }
                var node = Create(art, "BambooShoulderWalk", Vector3.zero).gameObject;
                node.layer = LayerMask.NameToLayer("Ground");
                var collider = Component<MeshCollider>(node);
                var mesh = SaveMesh(Folder + "/BambooShoulderWalk.asset", collision.MakeMesh(), new HashSet<string>());
                collider.sharedMesh = null;
                collider.sharedMesh = mesh;
                AssetDatabase.SaveAssetIfDirty(mesh);
            }


            float BankHeight(float x, float z)
            {
                float side = x < 0f ? -1f : 1f;
                float absX = Mathf.Abs(x);
                float midX = 5.45f + Mathf.Sin(z * 0.69f + side) * 0.72f + (side < 0f ? 0.11f : 0.48f);
                float h = (side < 0f ? 0.43f : 0.34f) * Mathf.Exp(-Mathf.Pow((absX - midX) / 1.53f, 2f));
                h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.88f, 4.15f, absX));
                h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10.72f, 3.80f, z));
                h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 1.5f, z));
                return h * Mathf.Lerp(0.68f, 1.15f, Mathf.PerlinNoise(x * 0.64f + 18f, z * 0.42f));
            }

            void Grove(float centerX, float centerZ, float radiusX, float radiusZ, int count, float minH, float maxH)
            {
                float phase = Range(random, 0f, Mathf.PI * 2f);
                var outward = new Vector3(Mathf.Sign(centerX), 0f, 0f);
                for (int i = 0; i < count; i++)
                {
                    float angle = phase + i * 2.399963f + Range(random, -0.09f, 0.09f);
                    float spread = Mathf.Sqrt((i + 0.45f) / count);
                    float x = centerX + Mathf.Cos(angle) * radiusX * spread;
                    float z = centerZ + Mathf.Sin(angle) * radiusZ * spread;
                    var foot = V(x, Surface(x, z) - 0.051f, z);
                    var lean = V(Mathf.Cos(angle) * 0.73f, 0f, Mathf.Sin(angle) * 0.42f);
                    AutumnDenseForest.AddEntrancePlant(reeds, foot, outward, lean,
                        Range(random, minH, maxH), Range(random, 0.066f, 0.123f), random);
                }
            }

            Texture2D SoilTexture()
            {
                const int size = 768;
                const string texturePath = Folder + "/ForestFloorBlend.asset";
                var target = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                Texture2D source = a.Soil;
                RenderTexture rt = RenderTexture.GetTemporary(192, 192, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                var readable = new Texture2D(192, 192, TextureFormat.RGB24, false);
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    readable.ReadPixels(new Rect(0f, 0f, 192f, 192f), 0, 0);
                    readable.Apply();
                    var colors = new Color32[size * size];
                    for (int iz = 0; iz < size; iz++)
                    for (int ix = 0; ix < size; ix++)
                    {
                        float x = Mathf.Lerp(minX, maxX, (ix + 0.5f) / size);
                        float z = Mathf.Lerp(minZ, maxZ, (iz + 0.5f) / size);
                        float coarse = Mathf.PerlinNoise(x * 0.36f + 21.55f, z * 0.32f + 5.18f);
                        float detail = Mathf.PerlinNoise(x * 1.98f + 38f, z * 2.67f + 22f);
                        float shoulder = Mathf.Lerp(3.03f, 4.89f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4.35f, 8.1f, z)));
                        shoulder = Mathf.Lerp(shoulder, 1.78f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10.2f, 19.3f, z)));
                        shoulder += (coarse - 0.5f) * 2.71f + (detail - 0.5f) * 0.55f;
                        float shift = Mathf.Sin(z * 0.44f) * 0.39f;
                        float path = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shoulder - 0.75f,
                            shoulder + 1.06f, Mathf.Abs(x - shift)));
                        path *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4.65f, 6.28f, z));
                        path *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17.4f, 20.45f, z));
                        float bank = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.025f, 0.21f, BankHeight(x, z)));
                        float amount = Mathf.Max(path * (0.72f + detail * 0.26f), bank * (0.54f + coarse * 0.40f));
                        Color sample = readable.GetPixelBilinear(Mathf.Repeat(x * 0.74f, 1f), Mathf.Repeat(z * 0.74f, 1f));
                        Color earth = Color.Lerp(new Color(0.49f, 0.39f, 0.27f), sample, 0.61f);
                        earth *= Mathf.Lerp(0.72f, 1.04f, coarse);
                        earth.a = amount;
                        colors[iz * size + ix] = earth;
                    }
                    bool isNew = target == null;
                    if (isNew) target = new Texture2D(size, size, TextureFormat.RGBA32, true)
                    {
                        name = "ForestFloorBlend",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Trilinear,
                        anisoLevel = 4
                    };
                    target.SetPixels32(colors);
                    target.Apply(true);
                    if (isNew) AssetDatabase.CreateAsset(target, texturePath);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                    return target;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                    UnityEngine.Object.DestroyImmediate(readable);
                }
            }
        }

        static void BuildMoments(Transform root, Layout layout, EntranceAssets a, CharacterController heroine,
            Camera playerCamera)
        {
            Transform wind = Trigger("WindAtThreshold", AutumnEntranceMoments.Cue.Wind,
                V(0f, layout.Floor + 1.05f, 1.52f), V(4.94f, 2.16f, 0.32f), a.WindSound, 0.27f);
            var emitter = Component<ParticleSystem>(Create(wind, "LeafGust", V(0f, -0.10f, -0.45f)).gameObject);
            var main = emitter.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 36;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.04f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.065f, 0.19f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = 0.11f;
            var emission = emitter.emission;
            emission.enabled = false;
            var shape = emitter.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.radius = 1.22f;
            var spin = emitter.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-2.4f, 3.9f);
            var leafRenderer = emitter.GetComponent<ParticleSystemRenderer>();
            leafRenderer.sharedMaterial = a.Leaves;
            leafRenderer.shadowCastingMode = ShadowCastingMode.Off;
            leafRenderer.receiveShadows = false;
            leafRenderer.maxParticleSize = 0.14f;
            var windSettings = new SerializedObject(wind.GetComponent<AutumnEntranceMoments>());
            windSettings.FindProperty("particles").objectReferenceValue = emitter;
            windSettings.FindProperty("playerCamera").objectReferenceValue = playerCamera;
            windSettings.ApplyModifiedPropertiesWithoutUndo();

            Transform rockTrigger = Trigger("LooseStonesOnSlope", AutumnEntranceMoments.Cue.LooseStones,
                V(0f, layout.Floor + 0.65f, 2.96f), V(4.90f, 2.24f, 0.32f), a.RockSound, 0.23f);
            var rockSettings = new SerializedObject(rockTrigger.GetComponent<AutumnEntranceMoments>());
            var rockArray = rockSettings.FindProperty("stones");
            rockArray.arraySize = 2;
            for (int i = 0; i < rockArray.arraySize; i++)
            {
                var pivot = Create(root, $"LooseStone_{i + 1}", Vector3.zero);
                var visual = Create(pivot, "Rock", Vector3.zero);
                visual.localScale = ScaleTo(a.Stone, V(0.43f + i * 0.10f, 0.29f + i * 0.06f, 0.36f + i * 0.055f));
                visual.localPosition = -Vector3.Scale(a.Stone.bounds.center, visual.localScale);
                var rockVisual = Render(visual, a.Stone, new[] { a.Rock }, ShadowCastingMode.Off, 0);
                rockVisual.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                float x = 1.54f + i * 0.51f;
                Vector3 start = V(x, layout.WalkHeight(x, 3.04f - i * 0.34f) + 0.10f, 3.04f - i * 0.34f);
                Vector3 middle = V(x + 0.17f, layout.WalkHeight(x + 0.17f, 4.67f) + 0.12f, 4.67f);
                Vector3 end = V(x + 0.21f, layout.FinishHeight(x + 0.21f, layout.RampToe + 0.86f) + 0.063f,
                    layout.RampToe + 0.86f + i * 0.36f);
                pivot.localPosition = start;
                pivot.gameObject.SetActive(false);
                var element = rockArray.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("stone").objectReferenceValue = pivot;
                element.FindPropertyRelative("start").vector3Value = start;
                element.FindPropertyRelative("middle").vector3Value = middle;
                element.FindPropertyRelative("end").vector3Value = end;
                element.FindPropertyRelative("delay").floatValue = i * 0.19f;
            }
            rockSettings.ApplyModifiedPropertiesWithoutUndo();

            Transform eyesTrigger = Trigger("EyesAtClearing", AutumnEntranceMoments.Cue.Eyes,
                V(0f, 1.02f, layout.RampToe + 2.57f), V(6.2f, 2.14f, 0.40f), null, 0f);
            var eyes = Create(root, "BaihuEyes", V(3.15f, 1.42f, layout.RampToe + 10.50f));
            eyes.localRotation = Quaternion.LookRotation(eyesTrigger.localPosition - eyes.localPosition, Vector3.up);
            var glints = new Geometry(Vector3.zero, 1);
            for (int side = -1; side <= 1; side += 2)
            {
                var eye = V(side * 0.135f, 0, 0);
                Leaf(glints, eye - V(0.081f, -side * 0.017f, 0), eye + V(0.081f, -side * 0.017f, 0),
                    Vector3.forward, 0.044f);
            }
            Save(eyes, "EyeGlints", glints, a.Red, false);
            eyes.gameObject.SetActive(false);
            var eyesSettings = new SerializedObject(eyesTrigger.GetComponent<AutumnEntranceMoments>());
            eyesSettings.FindProperty("eyes").objectReferenceValue = eyes;
            eyesSettings.ApplyModifiedPropertiesWithoutUndo();

            Transform Trigger(string name, AutumnEntranceMoments.Cue cue, Vector3 point, Vector3 dimensions,
                AudioClip clip, float volume)
            {
                Transform node = Create(root, name, point);
                node.gameObject.layer = LayerMask.NameToLayer("Trigger");
                var moment = Component<AutumnEntranceMoments>(node.gameObject);
                var box = node.GetComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = dimensions;
                var body = node.GetComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var settings = new SerializedObject(moment);
                settings.FindProperty("cue").enumValueIndex = (int)cue;
                settings.FindProperty("heroine").objectReferenceValue = heroine;
                if (clip != null)
                {
                    var sound = Component<AudioSource>(node.gameObject);
                    sound.clip = clip;
                    sound.playOnAwake = false;
                    sound.spatialBlend = 1f;
                    sound.minDistance = 1.9f;
                    sound.maxDistance = 11f;
                    sound.rolloffMode = AudioRolloffMode.Linear;
                    sound.volume = volume;
                    sound.pitch = cue == AutumnEntranceMoments.Cue.LooseStones ? 0.73f : 0.88f;
                    settings.FindProperty("sound").objectReferenceValue = sound;
                }
                settings.ApplyModifiedPropertiesWithoutUndo();
                return node;
            }
        }

        static void SetLighting(Light warm, Light reveal, Layout layout)
        {
            var hdrp = warm.GetComponent<HDAdditionalLightData>();
            Undo.RecordObjects(new UnityEngine.Object[] { warm, warm.transform, reveal, reveal.transform, hdrp },
                "Relight autumn entrance");
            Vector3 position = layout.World(-0.66f, layout.Floor + 3.34f, -0.48f);
            warm.transform.SetPositionAndRotation(position,
                Quaternion.LookRotation(layout.World(0.48f, 0.12f, layout.RampToe + 1.1f) - position));
            warm.type = LightType.Spot;
            warm.spotAngle = 66f;
            warm.innerSpotAngle = 31f;
            warm.range = 8.9f;
            warm.lightUnit = LightUnit.Candela;
            warm.intensity = 690f;
            warm.color = new Color(1f, 0.83f, 0.63f);
            hdrp.volumetricDimmer = 0.46f;
            reveal.transform.position = layout.World(0.55f, layout.Floor + 2.30f, -4.15f);
            reveal.color = new Color(1f, 0.78f, 0.55f);
            reveal.intensity = 2450f;
            reveal.range = 5.20f;
        }

        static void Ashlar(Geometry mesh, float x, float y, float width, float height, float depth)
        {
            float l = x - width * 0.5f, r = x + width * 0.5f, b = y - height * 0.5f, t = y + height * 0.5f;
            const float bevel = 0.052f;
            Vector3 a = V(l + bevel, b + bevel, depth), c = V(r - bevel, b + bevel, depth);
            Vector3 d = V(r - bevel, t - bevel, depth), e = V(l + bevel, t - bevel, depth);
            Vector3 a0 = V(l, b, 0.049f), c0 = V(r, b, 0.049f), d0 = V(r, t, 0.049f), e0 = V(l, t, 0.049f);
            WallQuad(a, c, d, e);
            WallQuad(a0, c0, c, a);
            WallQuad(c, c0, d0, d);
            WallQuad(e, d, d0, e0);
            WallQuad(a0, a, e, e0);

            void WallQuad(Vector3 p, Vector3 q, Vector3 s, Vector3 u)
            {
                const float repeat = 1.75f;
                mesh.Quad(0, p, q, s, u, new Rect((x + 11f) / repeat, y / repeat,
                    Vector3.Distance(p, q) / repeat, Vector3.Distance(p, u) / repeat));
            }
        }

        static void Stain(Geometry mesh, float x, float y, float z, float width, float height, Random random)
        {
            Vector3 center = V(x, y + height * 0.42f, z);
            int first = mesh.Vertex(center, Vector3.forward, new Vector2(0.5f, 0.5f));
            for (int i = 0; i < 9; i++)
            {
                float angle = i * Mathf.PI * 2f / 9f;
                Vector3 point = center + V(Mathf.Cos(angle) * width * Range(random, 0.35f, 0.60f),
                    Mathf.Sin(angle) * Mathf.Abs(height) * Range(random, 0.24f, 0.54f), 0f);
                mesh.Vertex(point, Vector3.forward, Vector2.zero);
                mesh.Triangle(0, first, first + i + 1, first + (i + 1) % 9 + 1);
            }
        }

        static void GroundQuad(Geometry mesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float period)
        {
            int first = mesh.Vertex(a, Vector3.up, new Vector2(a.x, a.z) / period);
            mesh.Vertex(b, Vector3.up, new Vector2(b.x, b.z) / period);
            mesh.Vertex(c, Vector3.up, new Vector2(c.x, c.z) / period);
            mesh.Vertex(d, Vector3.up, new Vector2(d.x, d.z) / period);
            if (Vector3.Cross(b - a, d - a).y < 0f)
            {
                mesh.Triangle(0, first, first + 2, first + 1);
                mesh.Triangle(0, first, first + 3, first + 2);
            }
            else
            {
                mesh.Triangle(0, first, first + 1, first + 2);
                mesh.Triangle(0, first, first + 2, first + 3);
            }
        }

        static void Line(Geometry mesh, Vector3 a, Vector3 b, float width, Vector3 normal)
        {
            Vector3 side = Vector3.Cross(normal, b - a).normalized * width * 0.5f;
            mesh.Quad(0, a - side, b - side, b + side * 0.31f, a + side, new Rect(0, 0, 1, 1));
        }

        static void Leaf(Geometry mesh, Vector3 a, Vector3 b, Vector3 normal, float width)
        {
            Vector3 side = Vector3.Cross(normal, b - a).normalized * width;
            Vector3 middle = Vector3.Lerp(a, b, 0.47f);
            mesh.Quad(0, a, middle - side, b, middle + side, new Rect(0, 0, 1, 1));
        }

        static void Paw(Geometry mesh, Vector3 center, float width, float angle)
        {
            Quaternion rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
            Print(Vector2.zero, width, width * 0.87f);
            for (int i = 0; i < 4; i++)
                Print(new Vector2((i - 1.5f) * width * 0.48f, width * (i is 1 or 2 ? 1.02f : 0.80f)),
                    width * 0.25f, width * 0.32f);

            void Print(Vector2 offset, float w, float h)
            {
                var origin = center + rotation * V(offset.x, 0f, offset.y);
                int first = mesh.Vertex(origin, Vector3.up, new Vector2(0.5f, 0.5f));
                for (int i = 0; i < 8; i++)
                {
                    float t = i * Mathf.PI * 0.25f;
                    mesh.Vertex(origin + rotation * V(Mathf.Sin(t) * w, 0f, Mathf.Cos(t) * h),
                        Vector3.up, new Vector2(Mathf.Sin(t), Mathf.Cos(t)) * 0.5f + Vector2.one * 0.5f);
                    mesh.Triangle(0, first, first + i + 1, first + (i + 1) % 8 + 1);
                }
            }
        }

        internal static void Fern(Geometry mesh, Vector3 foot, float height, Random random)
        {
            for (int frond = 0; frond < 6; frond++)
            {
                float angle = (frond + Range(random, -0.32f, 0.32f)) * Mathf.PI / 3f;
                Vector3 radial = V(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 side = Vector3.Cross(Vector3.up, radial);
                Vector3 last = foot;
                for (int i = 1; i <= 7; i++)
                {
                    float t = i / 7f;
                    Vector3 p = foot + radial * (t * height * 0.83f) + Vector3.up * (Mathf.Sin(t * 2.13f) * height);
                    Line(mesh, last, p, 0.018f * (1.12f - t), Vector3.up);
                    float length = (0.11f + 0.24f * Mathf.Sin(t * Mathf.PI)) * height;
                    Leaf(mesh, p, p + (side + radial * 0.63f) * length, Vector3.up, length * 0.24f);
                    Leaf(mesh, p, p + (-side + radial * 0.57f) * length, Vector3.up, length * 0.24f);
                    last = p;
                }
            }
        }

        internal static void Tuft(Geometry mesh, Vector3 origin, float height, Random random)
        {
            for (int i = 0; i < 15; i++)
            {
                float angle = Range(random, 0f, Mathf.PI * 2f);
                Vector3 radial = V(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 a = origin + radial * Range(random, 0.016f, 0.12f);
                Vector3 b = a + Vector3.up * (height * Range(random, 0.49f, 1.1f)) + radial * Range(random, 0.08f, 0.31f);
                Leaf(mesh, a, b, radial, Range(random, 0.014f, 0.039f));
            }
        }

        static Vector3 ScaleTo(Mesh mesh, Vector3 size)
        {
            Vector3 bounds = mesh.bounds.size;
            return V(size.x / bounds.x, size.y / bounds.y, size.z / bounds.z);
        }

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        static Transform Root(Scene scene, string name) =>
            Array.Find(scene.GetRootGameObjects(), root => root.name == name)?.transform ??
            throw new InvalidOperationException($"Missing scene root {name}.");

        static Transform Required(Transform parent, string path) => parent.Find(path) ??
            throw new InvalidOperationException($"Missing {parent.name}/{path}.");

        static Transform PrepareGroup(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null) Undo.RegisterFullObjectHierarchyUndo(existing.gameObject, "Update entrance details");
            return Create(parent, name, Vector3.zero);
        }

        static Transform Create(Transform parent, string name, Vector3 position)
        {
            Transform node = parent.Find(name);
            if (node == null)
            {
                var go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create entrance detail");
                node = go.transform;
                node.SetParent(parent, false);
            }
            node.SetLocalPositionAndRotation(position, Quaternion.identity);
            node.localScale = Vector3.one;
            return node;
        }

        static void Save(Transform parent, string name, Geometry geometry, Material material, bool isStatic = true) =>
            Save(parent, name, geometry.MakeMesh(), material, isStatic);

        static void Save(Transform parent, string name, Mesh mesh, Material material, bool isStatic = true) =>
            Save(parent, name, mesh, new[] { material }, isStatic);

        static void Save(Transform parent, string name, Mesh mesh, Material[] materials, bool isStatic)
        {
            mesh.name = name;
            mesh.RecalculateTangents();
            string path = $"{Folder}/{name}.asset";
            var stored = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (stored == null)
            {
                stored = mesh;
                AssetDatabase.CreateAsset(stored, path);
            }
            else
            {
                EditorUtility.CopySerialized(mesh, stored);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(stored);
            }
            AssetDatabase.SaveAssetIfDirty(stored);
            Render(Create(parent, name, Vector3.zero), stored, materials, ShadowCastingMode.Off,
                isStatic ? RendererFlags : 0);
        }

        sealed class Layout
        {
            readonly Transform basis;
            readonly MeshCollider ramp;
            readonly Terrain terrain;
            internal readonly float Floor;
            internal readonly float RampHead;
            internal readonly float RampToe;

            internal Layout(Transform basis, BoxCollider deck, MeshCollider ramp, MeshCollider landing, Terrain terrain)
            {
                this.basis = basis;
                this.ramp = ramp;
                this.terrain = terrain;
                if (!basis.gameObject.activeInHierarchy || !deck.enabled || !ramp.enabled || !landing.enabled)
                    throw new InvalidOperationException("The short autumn entrance and its walk colliders must be active.");
                Floor = basis.InverseTransformPoint(deck.bounds.max).y;
                RampHead = basis.InverseTransformPoint(ramp.bounds.min).z;
                RampToe = basis.InverseTransformPoint(ramp.bounds.max).z;
            }

            internal Vector3 World(float x, float y, float z) => basis.TransformPoint(V(x, y, z));
            internal float GroundHeight(float x, float z) => Ground(terrain, World(x, 0f, z)) - basis.position.y;

            internal float FinishHeight(float x, float z)
            {
                float y = GroundHeight(x, z);
                if (z >= RampToe && Mathf.Abs(x) < 2.8f && z < RampToe + 5.5f)
                    return y + 0.061f;
                return y + 0.035f;
            }

            internal float WalkHeight(float x, float z)
            {
                var ray = new Ray(World(x, 7f, z), -basis.up);
                if (ramp.Raycast(ray, out RaycastHit hit, 15f))
                    return basis.InverseTransformPoint(hit.point).y;
                return FinishHeight(x, z);
            }
        }

        sealed class EntranceAssets
        {
            internal readonly Mesh Stone = Load<Mesh>($"{Tripo}/SM_Autumn_Boulder_LOD1.asset");
            internal readonly Material Rock = Load<Material>($"{Tripo}/M_Autumn_Boulder.mat");
            internal readonly Material Portal = Existing("EntranceFix_Portal");
            internal readonly Material Paving = Existing("EntranceFix_Curb");
            internal readonly Material PavingWeathered = Existing("EntranceFix_Rock");
            internal readonly Material Culm = Existing("ForestBake_Culm");
            internal readonly Material BambooLeaves = Existing("ForestBake_Leaves");
            internal readonly Texture2D Soil = Load<Texture2D>($"{Location}/Textures/T_RockySoil_BaseColor.png");
            internal readonly Material Leaves = Existing("M_Autumn_LeafParticle");
            internal readonly AudioClip WindSound = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + "SFX_Whoosh_01.wav");
            internal readonly AudioClip RockSound = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + "SFX_Hit_01.wav");
            internal Material Green => Tint("Foliage", "#56653E", true);
            internal Material Straw => Tint("Straw", "#8C744B", true);
            internal Material Dark => Tint("Runoff", "#494745", false);
            internal Material Track => Tint("Track", "#6C5440", false);
            internal Material Red => Tint("EyeGlow", "#FF6742", false, true);
            internal Material Amber => Tint("LanternAmber", "#FFA446", false, true);
            internal Material Iron => Tint("LanternIron", "#302C24", false);
            internal Material ForestFloor(Texture2D blend)
            {
                const string path = Folder + "/ForestFloor.mat";
                Material source = Existing("M_Autumn_Path_Clearing");
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(source) { name = "ForestFloor" };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetTexture("_BaseColorMap", blend);
                material.SetTextureScale("_BaseColorMap", Vector2.one);
                material.SetTextureOffset("_BaseColorMap", Vector2.zero);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_SurfaceType", 1f);
                material.SetFloat("_BlendMode", 0f);
                material.SetFloat("_RenderQueueType", 5f);
                material.SetFloat("_EnableBlendModePreserveSpecularLighting", 0f);
                material.SetFloat("_TransparentDepthPrepassEnable", 0f);
                material.SetFloat("_TransparentDepthPostpassEnable", 0f);
                material.SetFloat("_TransparentZWrite", 0f);
                material.SetFloat("_EnableFogOnTransparent", 1f);
                material.SetFloat("_AlphaCutoffEnable", 1f);
                material.SetFloat("_AlphaCutoff", 0.026f);
                material.SetFloat("_SmoothnessRemapMax", 0.26f);
                HDMaterial.ValidateMaterial(material);
                material.renderQueue = (int)RenderQueue.Transparent - 8;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                return material;
            }

            static Material Existing(string name) => Load<Material>($"{Location}/Materials/{name}.mat");
            static T Load<T>(string path) where T : UnityEngine.Object =>
                AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException($"Missing {path}.");

            static Material Tint(string name, string color, bool twoSided, bool glow = false)
            {
                string path = $"{Folder}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(Shader.Find(glow ? "HDRP/Unlit" : "HDRP/Lit")) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                if (glow)
                {
                    material.SetColor("_UnlitColor", Linear(color));
                    material.SetColor("_EmissiveColor", Linear(color) * 6f);
                    material.SetFloat("_EmissiveExposureWeight", 0f);
                }
                else
                {
                    material.SetColor("_BaseColor", Linear(color));
                    material.SetFloat("_Smoothness", 0.15f);
                    material.SetFloat("_DoubleSidedEnable", twoSided ? 1 : 0);
                }
                HDMaterial.ValidateMaterial(material);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                return material;
            }
        }
    }
}
