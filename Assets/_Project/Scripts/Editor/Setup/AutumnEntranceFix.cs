using System;
using System.Collections.Generic;
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
    public static class AutumnEntranceFix
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string Folder = Generated + "/EntranceAxis";
        const string RootName = "Entrance_Fix";
        const float Floor = 1.2f;
        const float Run = 4.8f;
        const float Ease = 0.75f;
        const float Grade = Floor / (Run - Ease);
        const float Width = 5f;
        const float RampStart = 1.3f;
        const float FacadeZ = -6.9f;
        const float OpeningHalf = 2.5f;
        const float Frame = 0.42f;
        const float WallBack = -4f;

        [MenuItem("ShadowTan/Setup/Art/Fix Autumn Entrance")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Autumn_Gameplay in Edit Mode before fixing its entrance.");

            Transform greybox = Require(RequireRoot(scene, "[Level]"), "Greybox");
            Transform ground = Require(greybox, "Ground");
            Transform walls = Require(greybox, "Walls");
            Transform props = Require(greybox, "Props");
            Transform flow = RequireRoot(scene, "[Flow]");
            Transform gizmos = RequireRoot(scene, "[Gizmos]");
            Transform heroine = RequireRoot(scene, "Heroine");
            Transform entry = Require(flow, "SeasonEntrance");
            Camera entrance = Require(gizmos, "EntranceCamera").GetComponent<Camera>();
            Terrain terrain = Require(ground, "AutumnTerrain").GetComponent<Terrain>();
            int groundLayer = LayerMask.NameToLayer("Ground"), wallLayer = LayerMask.NameToLayer("Wall");
            if (entrance == null || terrain == null || groundLayer < 0 || wallLayer < 0)
                throw new InvalidOperationException("The entrance needs its review camera, terrain and Ground/Wall layers.");

            var materials = new EntranceMaterials();
            EnsureFolder(Folder);
            var origin = P(CenterX, 0f, FacadeZ);
            origin.y = Ground(terrain, origin + Vector3.forward * (RampStart + Run));
            Transform root = Node(greybox, RootName, origin);
            var retained = new HashSet<string>();

            BuildWall(root, materials, retained, wallLayer);
            BuildDeck(root, materials, retained, groundLayer, wallLayer);
            BuildRamp(root, materials, retained, groundLayer, wallLayer);
            BuildLanding(root, terrain, materials, retained, groundLayer);
            SetEntranceLight(root);
            SetCameras(gizmos, entrance, root);

            var spawn = root.TransformPoint(P(0f, Floor + 0.025f, 0.86f));
            Quaternion look = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            SetPose(entry, spawn, look);
            SetPose(heroine, spawn, look);
            Transform exit = flow.Find("SeasonExit");
            if (exit != null)
                SetPose(exit, root.TransformPoint(P(0f, Floor + 0.025f, -3.55f)), Quaternion.identity);

            SetInactive(walls.Find("Wall_Grey"));
            SetInactive(walls.Find("Slope_Ridge"));
            SetInactive(ground.Find("EntranceFloor"));
            SetInactive(ground.Find("Slope"));
            SetInactive(props.Find("SlopeStones"));
            Transform legacy = greybox.Find("Entrance_Root");
            if (legacy != null)
            {
                foreach (string name in new[] { "Entrance_Wall", "Entrance_Structure", "Entrance_Tunnel",
                    "Entrance_Apron", "Entrance_Ramp", "Entrance_ExitTrigger" })
                    SetInactive(legacy.Find(name));
                var clearing = legacy.Find("Entrance_Clearing");
                if (clearing != null)
                {
                    var west = clearing.Find("Entrance_ClearingChordWest");
                    if (west != null) west.gameObject.SetActive(true);
                    SetInactive(clearing.Find("Entrance_ClearingFootSouth"));
                    RemoveDiagonalSandApron(clearing, retained);
                }
            }
            DisableFormerSlopeDressing(props);

            foreach (string path in retained)
                AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadAssetAtPath<Mesh>(path));
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Autumn entrance: portal and ramp on x={CenterX}, threshold {Floor:F2} m above ground, ramp {Run:F2} m.");
        }

        static void DisableFormerSlopeDressing(Transform props)
        {
            Transform dressing = props.Find("TripoProp_Root");
            if (dressing == null) return;

            // These batches were baked in world space against the retired high slope.
            foreach (Transform child in dressing)
                if (child.name.StartsWith("TripoProp_Batch_Mouth_", StringComparison.Ordinal) ||
                    child.name.StartsWith("TripoProp_Batch_Scree_", StringComparison.Ordinal))
                    SetInactive(child);

            Transform instances = dressing.Find("TripoProp_Instances");
            if (instances == null) return;
            foreach (Transform child in instances)
                if (child.name.StartsWith("TripoProp_Mouth_", StringComparison.Ordinal) ||
                    child.name.StartsWith("TripoProp_Scree_", StringComparison.Ordinal))
                    SetInactive(child);
        }

        static void RemoveDiagonalSandApron(Transform clearing, HashSet<string> retained)
        {
            Transform sand = clearing.Find("Entrance_ClearingSand");
            if (sand == null || !sand.TryGetComponent<MeshFilter>(out var filter)) return;
            var source = AssetDatabase.LoadAssetAtPath<Mesh>($"{AutumnEntranceCave.Folder}/Entrance_ClearingSand.asset");
            if (source == null) return;

            Vector3[] vertices = source.vertices;
            int[] triangles = source.GetTriangles(0);
            // BuildClearing emits a fan sharing the first vertex, then a separate apron quad.
            var fan = new List<int>(triangles.Length);
            for (int i = 0; i < triangles.Length; i += 3)
                if (triangles[i] == 0 || triangles[i + 1] == 0 || triangles[i + 2] == 0)
                    for (int j = 0; j < 3; j++)
                        fan.Add(triangles[i + j]);
            if (vertices.Length == 0 || fan.Count == 0 || fan.Count == triangles.Length) return;

            Mesh core = UnityEngine.Object.Instantiate(source);
            core.SetTriangles(fan, 0);
            filter.sharedMesh = null;
            filter.sharedMesh = StoreMesh("ClearingSand", core, retained);
        }

        static void BuildWall(Transform root, EntranceMaterials materials, HashSet<string> retained, int layer)
        {
            Transform wall = Node(root, "Wall_Grey", root.position);
            var masonry = new Geometry(Vector3.zero, 1);
            var facing = new Geometry(Vector3.zero, 1);
            var portal = new Geometry(Vector3.zero, 1);
            const float min = -105f - CenterX, max = 105f - CenterX, wallHeight = 60f;
            const float bottom = Floor, top = Floor + 5f;
            AddWallPart("Left", min, -OpeningHalf, 0f, wallHeight);
            AddWallPart("Right", OpeningHalf, max, 0f, wallHeight);
            AddWallPart("Sill", -OpeningHalf, OpeningHalf, 0f, bottom);
            AddWallPart("Lintel", -OpeningHalf, OpeningHalf, top, wallHeight);

            const float course = 1.66f, block = 3.85f;
            var random = new Random(36841);
            for (int row = 0; row * course < wallHeight; row++)
            {
                float y0 = row * course + 0.017f, y1 = Mathf.Min(wallHeight, (row + 1) * course - 0.019f);
                for (float x = min - block + (row % 2) * block * 0.52f; x < max; x += block)
                {
                    float x0 = Mathf.Max(min, x + 0.025f);
                    float x1 = Mathf.Min(max, x + block - 0.025f);
                    var uv = new Vector2(Range(random, 0f, 1f), Range(random, 0f, 1f));
                    float depth = Range(random, 0.018f, 0.044f);
                    if (x1 <= -OpeningHalf || x0 >= OpeningHalf || y1 <= bottom || y0 >= top)
                    {
                        Tile(x0, x1, y0, y1);
                        continue;
                    }

                    Tile(x0, Mathf.Min(x1, -OpeningHalf), y0, y1);
                    Tile(Mathf.Max(x0, OpeningHalf), x1, y0, y1);
                    Tile(Mathf.Max(x0, -OpeningHalf), Mathf.Min(x1, OpeningHalf), y0, Mathf.Min(y1, bottom));
                    Tile(Mathf.Max(x0, -OpeningHalf), Mathf.Min(x1, OpeningHalf), Mathf.Max(y0, top), y1);

                    void Tile(float l, float r, float b, float t)
                    {
                        if (r - l < 0.008f || t - b < 0.008f) return;
                        Face(facing, P(l, b, depth), P(r, b, depth), P(r, t, depth), P(l, t, depth),
                            Vector3.forward, 4.6f, uv);
                    }
                }
            }

            // Each reveal replaces the corresponding inner face of the wall.
            Face(portal, P(-OpeningHalf, bottom, 0f), P(-OpeningHalf, top, 0f),
                P(-OpeningHalf, top, WallBack), P(-OpeningHalf, bottom, WallBack), Vector3.right, 2.2f);
            Face(portal, P(OpeningHalf, bottom, WallBack), P(OpeningHalf, top, WallBack),
                P(OpeningHalf, top, 0f), P(OpeningHalf, bottom, 0f), Vector3.left, 2.2f);
            Face(portal, P(-OpeningHalf, top, WallBack), P(OpeningHalf, top, WallBack),
                P(OpeningHalf, top, 0f), P(-OpeningHalf, top, 0f), Vector3.down, 2.2f);

            foreach (float face in new[] { 0f, WallBack })
            {
                float direction = face == 0f ? 1f : -1f;
                for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 6; row++)
                {
                    float y = bottom + (row + 0.5f) * 5f / 6f;
                    float depth = row % 2 == 0 ? 0.42f : 0.38f;
                    PortalBlock(P(side * (OpeningHalf + Frame * 0.5f), y, face + direction * 0.16f),
                        P(Frame, 5f / 6f - 0.012f, depth), 0.046f + row % 3 * 0.008f);
                }

                float span = 5f + 2f * Frame;
                for (int i = 0; i < 5; i++)
                {
                    float x = (i - 2) * span / 5f;
                    PortalBlock(P(x, top + Frame * 0.5f, face + direction * (i == 2 ? 0.19f : 0.16f)),
                        P(span / 5f - 0.016f, Frame, 0.42f), 0.055f);
                    PortalBlock(P(x, bottom - Frame * 0.5f, face + direction * 0.16f),
                        P(span / 5f - 0.016f, Frame, 0.38f), 0.05f);
                }
            }

            Bake(wall, "Masonry", masonry, materials.Mortar, retained, true);
            Bake(wall, "Facing", facing, materials.Wall, retained, false);
            Bake(wall, "Portal", portal, materials.Portal, retained, true);

            void AddWallPart(string name, float minX, float maxX, float minY, float maxY)
            {
                var origin = P((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, WallBack * 0.5f);
                var size = P(maxX - minX, maxY - minY, -WallBack);
                if (name == "Sill")
                {
                    origin.y -= 0.08f;
                    size.y -= 0.16f;
                }
                BoxCollision(wall, name, origin, size, layer);
                foreach (float z in new[] { WallBack, 0f })
                {
                    Face(masonry, P(minX, minY, z), P(maxX, minY, z), P(maxX, maxY, z),
                        P(minX, maxY, z), z == 0f ? Vector3.forward : Vector3.back, 4.6f,
                        new Vector2(minX, minY) / 4.6f);
                }

                if (maxY == wallHeight)
                    Face(masonry, P(minX, maxY, 0f), P(maxX, maxY, 0f), P(maxX, maxY, WallBack),
                        P(minX, maxY, WallBack), Vector3.up, 4.6f);
                if (minX == min || maxX == max)
                {
                    float x = minX == min ? minX : maxX;
                    Face(masonry, P(x, minY, 0f), P(x, minY, WallBack), P(x, maxY, WallBack),
                        P(x, maxY, 0f), x < 0f ? Vector3.left : Vector3.right, 4.6f);
                }
            }

            void PortalBlock(Vector3 center, Vector3 size, float bevel)
            {
                BevelBlock(portal, center, size, bevel, 2.2f);
            }
        }

        static void BuildDeck(Transform root, EntranceMaterials materials, HashSet<string> retained,
            int groundLayer, int wallLayer)
        {
            Transform deck = Node(root, "UpperDeck", root.position);
            var slabs = new Geometry(Vector3.zero, 1);
            var backing = new Geometry(Vector3.zero, 1);
            var curbs = new Geometry(Vector3.zero, 1);
            const float approachBack = -6.65f;
            float deckDepth = RampStart - approachBack;
            BoxCollision(deck, "EntranceFloor", P(0f, Floor - 0.16f, RampStart * 0.5f),
                P(Width, 0.32f, RampStart), groundLayer);
            BoxCollision(deck, "Threshold", P(0f, Floor - 0.16f, approachBack * 0.5f),
                P(Width, 0.32f, -approachBack), groundLayer);
            AddWall("ReturnWall", P(0f, Floor + 2.5f, approachBack - 0.12f), P(5.8f, 5.2f, 0.42f));
            AddWall("ReturnCeiling", P(0f, Floor + 5.15f, -5.325f), P(5.8f, 0.30f, 2.65f));
            AddWall("ReturnWest", P(-2.69f, Floor + 2.5f, -5.325f), P(0.38f, 5.2f, 2.65f));
            AddWall("ReturnEast", P(2.69f, Floor + 2.5f, -5.325f), P(0.38f, 5.2f, 2.65f));

            const int rows = 6, columns = 3;
            Face(backing, P(-OpeningHalf, Floor - 0.034f, approachBack),
                P(-OpeningHalf, Floor - 0.034f, RampStart), P(OpeningHalf, Floor - 0.034f, RampStart),
                P(OpeningHalf, Floor - 0.034f, approachBack), Vector3.up, 1.8f);
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
                BevelBlock(slabs, P(-OpeningHalf + (col + 0.5f) * Width / columns,
                        Floor - 0.10f, approachBack + (row + 0.5f) * deckDepth / rows),
                    P(Width / columns - 0.012f, 0.20f, deckDepth / rows - 0.012f), 0.019f, 2.2f,
                    bevelTopOnly: true);

            for (int side = -1; side <= 1; side += 2)
            {
                bool west = side < 0;
                float edge = side * OpeningHalf;
                SideBank(backing, side, 0f, RampStart, Floor, Floor);
                var center = P(edge + side * 0.06f, Floor + 0.11f, RampStart * 0.5f);
                BevelBlock(curbs, center, P(0.35f, 0.46f, RampStart), 0.05f, 1.4f);
                BoxCollision(deck, west ? "WestCurb" : "EastCurb", center + Vector3.up * 0.06f,
                    P(0.35f, 0.34f, RampStart), wallLayer);
            }

            RemoveOwned(deck, "NorthCurb");
            RemoveOwned(deck, "RampShoulder");
            Bake(deck, "Slabs", slabs, materials.Portal, retained, true);
            Bake(deck, "JointsAndSupport", backing, materials.Bank, retained, true);
            Bake(deck, "Curbs", curbs, materials.Curb, retained, true);

            void AddWall(string name, Vector3 center, Vector3 size)
            {
                BevelBlock(slabs, center, size, 0.04f, 2.2f);
                BoxCollision(deck, name, center, size, wallLayer);
            }
        }

        static void BuildRamp(Transform root, EntranceMaterials materials, HashSet<string> retained,
            int groundLayer, int wallLayer)
        {
            Transform ramp = Node(root, "Slope", root.position);
            Transform section = Node(ramp, "Section_00", root.position);
            for (int i = 1; i <= 6; i++)
                RemoveOwned(ramp, $"Section_{i:00}");

            var walk = new Geometry(Vector3.zero, 1);
            var bank = new Geometry(Vector3.zero, 1);
            var bed = new Geometry(Vector3.zero, 1);
            var curbs = new Geometry(Vector3.zero, 1);
            var curbCollision = new Geometry(Vector3.zero, 1);
            var cobbles = new Geometry(Vector3.zero, 2);
            var random = new Random(15731);

            const int segments = 24;
            for (int segment = 0; segment < segments; segment++)
            {
                float a = Run * segment / segments, b = Run * (segment + 1) / segments;
                float az = RampStart + a, bz = RampStart + b;
                float ay = Height(a), by = Height(b);
                RampStrip(walk, az, bz, ay, by, 1.8f);
                RampStrip(bed, az, bz, ay - 0.029f, by - 0.029f, 1.8f);
                for (int side = -1; side <= 1; side += 2)
                {
                    SideBank(bank, side, az, bz, ay, by);
                    float inner = side * (OpeningHalf - 0.115f);
                    float outer = side * (OpeningHalf + 0.235f);
                    var p = P(inner, ay + CurbHeight(a), az);
                    var q = P(inner, by + CurbHeight(b), bz);
                    var r = P(outer, q.y, bz);
                    var s = P(outer, p.y, az);
                    if (CurbHeight(a) <= 0.015f) continue;
                    Face(curbCollision, p, q, r, s, Vector3.up, 1f);
                    Face(curbCollision, P(inner, ay - 0.08f, az), P(inner, by - 0.08f, bz),
                        q, p, Vector3.left * side, 1f);
                    Face(curbCollision, P(outer, ay - 0.08f, az), P(outer, by - 0.08f, bz),
                        r, s, Vector3.right * side, 1f);
                }
            }

            const int rows = 12, columns = 12;
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < columns; col++)
            {
                float distance = Run * (row + Range(random, 0.2f, 0.8f)) / rows;
                float across = Mathf.Lerp(-2.27f, 2.27f,
                    (col + (row % 2 == 0 ? 0.31f : 0.69f) + Range(random, -0.14f, 0.14f)) / columns);
                float diameter = Range(random, 0.29f, 0.48f);
                float blend = Mathf.Clamp01(Mathf.Min(distance / 0.32f, (Run - distance) / 0.75f));
                var point = P(across, Height(distance) - 0.028f, RampStart + distance);
                AddCobble(cobbles, random.Next(4) == 0 ? 1 : 0, point,
                    P(0f, 1f, GradeAt(distance)).normalized, diameter,
                    Range(random, 0.82f, 1.21f), Range(random, 0.075f, 0.133f) * blend, random);
            }

            const int blocks = 7;
            for (int i = 0; i < blocks; i++)
            {
                float a = Run * i / blocks, b = Run * (i + 1f) / blocks, dist = (a + b) * 0.5f;
                float height = CurbHeight(dist);
                float tilt = Mathf.Atan(GradeAt(dist)) * Mathf.Rad2Deg;
                foreach (float x in new[] { -OpeningHalf - 0.06f, OpeningHalf + 0.06f })
                    BevelBlock(curbs, P(x, Height(dist) + (height - 0.11f) * 0.5f, RampStart + dist),
                        P(0.35f, height + 0.11f, (b - a) / Mathf.Cos(tilt * Mathf.Deg2Rad) - 0.014f),
                        Mathf.Min(0.046f, height * 0.135f + 0.019f), 1.4f, tilt);
            }
            Bake(section, "Bank_00", bank, materials.Bank, retained, true);
            Bake(section, "Bed_00", bed, materials.Bed, retained, true);
            Bake(section, "Curbs_00", curbs, materials.Curb, retained, true);
            Bake(section, "Cobbles_00", cobbles, new[] { materials.Rock, materials.WarmRock }, retained, true);
            MeshCollision(ramp, "RampWalk", walk, retained, groundLayer);
            MeshCollision(ramp, "CurbCollision", curbCollision, retained, wallLayer);
        }

        static void SideBank(Geometry geometry, int side, float a, float b, float ay, float by)
        {
            float edge = side * OpeningHalf;
            float aFoot = edge + side * (0.12f + ay * 0.44f);
            float bFoot = edge + side * (0.12f + by * 0.44f);
            Face(geometry, P(edge, ay - 0.025f, a), P(edge, by - 0.025f, b),
                P(bFoot, -0.055f, b), P(aFoot, -0.055f, a), Vector3.right * side, 1.35f,
                new Vector2(a, 0f) / 1.35f);
        }

        static void BuildLanding(Transform root, Terrain terrain, EntranceMaterials materials,
            HashSet<string> retained, int groundLayer)
        {
            Transform landing = Node(root, "Landing", root.position);
            var walking = new Geometry(Vector3.zero, 1);
            var gravel = new Geometry(Vector3.zero, 1);
            const int along = 8, across = 10;
            for (int i = 0; i < along; i++)
            for (int j = 0; j < across; j++)
            {
                float a = i / (float)along, b = (i + 1f) / along;
                float c = j / (float)across, d = (j + 1f) / across;
                var p = Point(a, c);
                var q = Point(a, d);
                var r = Point(b, d);
                var s = Point(b, c);
                Face(walking, p, q, r, s, Vector3.up, 1.8f);
                Face(gravel, Visual(p, a), Visual(q, a), Visual(r, b), Visual(s, b), Vector3.up, 1.8f,
                    projection: point => new Vector2(point.x, point.z) / 1.8f);
            }
            MeshCollision(landing, "LandingWalk", walking, retained, groundLayer);
            Bake(landing, "Gravel", gravel, materials.Gravel, retained, false);

            Vector3 Point(float t, float u)
            {
                float x = Mathf.Lerp(-OpeningHalf, OpeningHalf, u) * (1f - 0.20f * t);
                float z = RampStart + Run + t * (3.05f + Mathf.Sin(u * Mathf.PI) * 0.65f + 0.11f * Mathf.Sin(u * 23f));
                var p = P(x, 0f, z);
                p.y = Mathf.SmoothStep(0f, Ground(terrain, root.TransformPoint(p)) - root.position.y, t);
                return p;
            }

            Vector3 Visual(Vector3 point, float t)
            {
                point.y += Mathf.Lerp(-0.029f, 0.038f, Mathf.SmoothStep(0f, 1f, t * 5f));
                return point;
            }
        }

        static void SetEntranceLight(Transform root)
        {
            WarmPoint("EntranceWarmFill", P(0f, 3.88f, 0.48f), 7f, 240f);
            WarmPoint("EntranceRevealFill", P(0f, 4.18f, -3.2f), 6f, 175f);

            void WarmPoint(string name, Vector3 position, float range, float intensity)
            {
                Transform lantern = Node(root, name, root.TransformPoint(position));
                var light = Component<Light>(lantern.gameObject);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.84f, 0.66f);
                light.range = range;
                light.shadows = LightShadows.None;
                light.lightUnit = LightUnit.Candela;
                light.intensity = intensity;
                Component<HDAdditionalLightData>(lantern.gameObject);
            }
        }

        static void SetCameras(Transform gizmos, Camera entrance, Transform root)
        {
            Transform reviewNode = Node(gizmos, "EntranceReviewCamera", Vector3.zero);
            Camera review = Component<Camera>(reviewNode.gameObject);
            review.CopyFrom(entrance);
            Component<HDAdditionalCameraData>(reviewNode.gameObject);
            review.enabled = false;
            if (reviewNode.TryGetComponent<AudioListener>(out var reviewListener))
                reviewListener.enabled = false;
            Aim(entrance, P(0f, 2.75f, -2.65f), P(0f, 1.35f, 25.9f), 68f);
            Aim(review, P(0f, 3.15f, 15.5f), P(0f, 2.22f, 0f), 66f);

            void Aim(Camera camera, Vector3 position, Vector3 target, float fov)
            {
                camera.transform.SetPositionAndRotation(root.TransformPoint(position),
                    Quaternion.LookRotation(root.TransformDirection(target - position), Vector3.up));
                camera.fieldOfView = fov;
                camera.nearClipPlane = 0.08f;
            }
        }

        static float Height(float distance)
        {
            if (distance < Ease) return Floor - Grade * distance * distance / (2f * Ease);
            float fromEnd = Run - distance;
            if (fromEnd < Ease) return Grade * fromEnd * fromEnd / (2f * Ease);
            return Floor - Grade * (distance - Ease * 0.5f);
        }

        static float GradeAt(float distance) => Grade * Mathf.Clamp01(Mathf.Min(distance, Run - distance) / Ease);

        static float CurbHeight(float distance) => 0.34f * Mathf.SmoothStep(0f, 1f, (Run - distance) / 0.95f);

        static void RampStrip(Geometry geometry, float a, float b, float ay, float by, float period)
        {
            Face(geometry, P(-OpeningHalf, ay, a), P(-OpeningHalf, by, b), P(OpeningHalf, by, b),
                P(OpeningHalf, ay, a), Vector3.up, period,
                projection: point => new Vector2(point.x, (point.z - RampStart) * Mathf.Sqrt(1f + Grade * Grade)) / period);
        }

        static void AddCobble(Geometry geometry, int material, Vector3 center, Vector3 up, float diameter,
            float aspect, float relief, Random random)
        {
            const int sides = 6;
            float spin = Range(random, -0.5f, 0.5f), variation = Range(random, 0f, 5f);
            var along = Vector3.Cross(up, Vector3.forward).normalized;
            var across = Vector3.Cross(along, up).normalized;
            var uvOrigin = new Vector2(Range(random, 0f, 1f), Range(random, 0f, 1f));
            int first = -1;
            for (int ring = 0; ring < 2; ring++)
            for (int i = 0; i < sides; i++)
            {
                float angle = (i + spin + ring * 0.12f) * Mathf.PI * 2f / sides;
                float irregularity = 1f + 0.14f * Mathf.Sin(angle * 3f + variation);
                var radial = along * Mathf.Cos(angle) + across * (Mathf.Sin(angle) * aspect);
                float radius = diameter * (ring == 0 ? 0.5f : 0.34f) * irregularity;
                Vector3 p = center + radial * radius + up * (ring == 0 ? -0.037f : relief * 0.52f);
                Vector3 normal = (radial + up * (ring == 0 ? 0.21f : 1.12f)).normalized;
                int index = geometry.Vertex(p, normal, uvOrigin +
                    new Vector2(Vector3.Dot(p - center, along), Vector3.Dot(p - center, across)) / 0.52f);
                if (first < 0) first = index;
            }
            int top = geometry.Vertex(center + up * relief, up, uvOrigin);
            for (int i = 0; i < sides; i++)
            {
                int a = first + i, b = first + (i + 1) % sides;
                geometry.Triangle(material, a, a + sides, b + sides);
                geometry.Triangle(material, a, b + sides, b);
                geometry.Triangle(material, top, b + sides, a + sides);
            }
        }

        static void BevelBlock(Geometry geometry, Vector3 center, Vector3 size, float bevel, float period,
            float angle = 0f, bool bevelTopOnly = false)
        {
            var half = size * 0.5f;
            var inner = half - Vector3.one * bevel;
            var rotation = Quaternion.AngleAxis(angle, Vector3.right);
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int axis = 0; axis < 3; axis++)
            {
                int j = (axis + 1) % 3, k = (axis + 2) % 3;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = axes[axis] * sign;
                    Vector3 m = n * half[axis];
                    Vector3 u = axes[j] * inner[j], v = axes[k] * inner[k];
                    Add(m - u - v, m + u - v, m + u + v, m - u + v, n);

                    for (int s = -1; s <= 1; s += 2)
                    for (int t = -1; t <= 1; t += 2)
                    {
                        Vector3 h = axes[j] * s, w = axes[k] * t;
                        Vector3 joint = m + h * inner[j] + w * inner[k];
                        if (t < 0)
                            Add(m + h * inner[j] - v, m + h * inner[j] + v,
                                n * inner[axis] + h * half[j] + v, n * inner[axis] + h * half[j] - v, n + h);
                        if (axis == 0)
                            Add(joint, n * inner[axis] + h * half[j] + w * inner[k],
                                n * inner[axis] + h * inner[j] + w * half[k], joint, n + h + w);
                    }
                }
            }

            void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                if (bevelTopOnly && normal.y < -0.1f && Mathf.Abs(normal.y) < 1.01f) return;
                Face(geometry, center + rotation * a, center + rotation * b, center + rotation * c,
                    center + rotation * d, rotation * normal, period,
                    new Vector2(Mathf.Repeat(center.x * 0.39f + center.z * 0.19f, 1f), Mathf.Repeat(center.y * 0.31f, 1f)));
            }
        }

        static void Face(Geometry geometry, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float period,
            Vector2 offset = default, Func<Vector3, Vector2> projection = null)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 u = (b - a).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            if (Vector3.Dot(d - a, v) < -0.00001f) v = -v;
            bool flip = Vector3.Dot(normal, outward) < 0f;
            if (flip) normal = -normal;
            int first = Vertex(a);
            Vertex(b);
            Vertex(c);
            geometry.Triangle(0, first, first + (flip ? 2 : 1), first + (flip ? 1 : 2));
            if (d != a)
            {
                Vertex(d);
                geometry.Triangle(0, first, first + (flip ? 3 : 2), first + (flip ? 2 : 3));
            }

            int Vertex(Vector3 p) => geometry.Vertex(p, normal,
                projection != null ? projection(p) :
                offset + new Vector2(Vector3.Dot(p - a, u), Vector3.Dot(p - a, v)) / period);
        }

        static Vector3 P(float x, float y, float z) => new Vector3(x, y, z);

        static void Bake(Transform parent, string name, Geometry geometry, Material material,
            HashSet<string> retained, bool castShadows) =>
            Bake(parent, name, geometry, new[] { material }, retained, castShadows);

        static void Bake(Transform parent, string name, Geometry geometry, Material[] materials,
            HashSet<string> retained, bool castShadows)
        {
            Mesh mesh = geometry.MakeMesh();
            mesh.RecalculateTangents();
            Transform node = Node(parent, name, parent.position);
            Component<MeshFilter>(node.gameObject).sharedMesh = null;
            Mesh asset = StoreMesh(name, mesh, retained);
            Render(node, asset, materials,
                castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
        }

        static void MeshCollision(Transform parent, string name, Geometry geometry, HashSet<string> retained, int layer)
        {
            var go = Node(parent, name, parent.position).gameObject;
            go.layer = layer;
            var collider = Component<MeshCollider>(go);
            collider.sharedMesh = null;
            collider.sharedMesh = StoreMesh(name, geometry.MakeMesh(), retained);
        }

        static Mesh StoreMesh(string name, Mesh built, HashSet<string> retained)
        {
            string path = $"{Folder}/{name}.asset";
            retained.Add(path);
            Mesh asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (asset == null)
            {
                built.name = name;
                AssetDatabase.CreateAsset(built, path);
                return built;
            }

            asset.Clear();
            asset.indexFormat = built.indexFormat;
            asset.vertices = built.vertices;
            asset.normals = built.normals;
            asset.uv = built.uv;
            asset.tangents = built.tangents;
            asset.subMeshCount = built.subMeshCount;
            for (int i = 0; i < built.subMeshCount; i++)
                asset.SetTriangles(built.GetTriangles(i), i, false);
            asset.RecalculateBounds();
            asset.UploadMeshData(false);
            EditorUtility.SetDirty(asset);
            UnityEngine.Object.DestroyImmediate(built);
            return asset;
        }

        static void BoxCollision(Transform parent, string name, Vector3 position, Vector3 size, int layer)
        {
            var go = Node(parent, name, parent.TransformPoint(position)).gameObject;
            go.layer = layer;
            Component<BoxCollider>(go).size = size;
        }

        static Transform RequireRoot(Scene scene, string name) =>
            Array.Find(scene.GetRootGameObjects(), go => go.name == name)?.transform ??
            throw new InvalidOperationException($"Missing scene root {name}.");

        static Transform Require(Transform parent, string path) =>
            parent.Find(path) ?? throw new InvalidOperationException($"Missing {parent.name}/{path}.");

        static void RemoveOwned(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) Undo.DestroyObjectImmediate(child.gameObject);
        }

        static void SetInactive(Transform node)
        {
            if (node != null && node.gameObject.activeSelf)
                node.gameObject.SetActive(false);
        }

        static void SetPose(Transform node, Vector3 position, Quaternion rotation)
        {
            node.SetPositionAndRotation(position, rotation);
            if (PrefabUtility.IsPartOfPrefabInstance(node))
                PrefabUtility.RecordPrefabInstancePropertyModifications(node);
        }

        sealed class EntranceMaterials
        {
            internal readonly Material Wall = Copy("Wall", "M_Autumn_Wall", "#929EAE");
            internal readonly Material Mortar = Copy("Mortar", "M_Autumn_Wall", "#686F7B");
            internal readonly Material Portal = Copy("Portal", "M_Autumn_EntranceStone", "#B8AEA0");
            internal readonly Material Curb = Copy("Curb", "M_Autumn_CurbStone", "#A09680");
            internal readonly Material Bank = Copy("Bank", "M_Autumn_Path", "#8D7969");
            internal readonly Material Bed = Copy("Bed", "M_Autumn_SlopeStone", "#7A6958");
            internal readonly Material Rock = Copy("Rock", "M_Autumn_SlopeStone", "#A88A6C");
            internal readonly Material WarmRock = Copy("WarmRock", "M_Autumn_Stone", "#9D8066");
            internal readonly Material Gravel = Copy("Gravel", "M_Autumn_Pebbles", "#CBB38E");

            static Material Copy(string name, string source, string tint)
            {
                Material original = AssetDatabase.LoadAssetAtPath<Material>($"{Location}/Materials/{source}.mat");
                if (original == null || original.shader.name != "HDRP/Lit")
                    throw new InvalidOperationException($"Missing HDRP/Lit material {source}.");
                Material result = Material("EntranceFix_" + name, "HDRP/Lit", material =>
                {
                    material.CopyPropertiesFromMaterial(original);
                    material.SetColor("_BaseColor", Linear(tint));
                    foreach (string slot in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap" })
                    {
                        material.SetTextureScale(slot, Vector2.one);
                        material.SetTextureOffset(slot, Vector2.zero);
                    }
                });
                AssetDatabase.SaveAssetIfDirty(result);
                return result;
            }
        }
    }
}
