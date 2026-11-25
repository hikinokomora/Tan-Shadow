using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowTan.EditorTools
{
    // Грейбокс осенней локации «Байху»: серая стена с проёмом на ~50 м, каменная насыпь,
    // полукруглая поляна, прямая бамбуковая тропа и круглая арена с монументом.
    // Источники: GDD-AU §04–05, ARCHITECTURE §7.3 и §13.3.1.
    //
    // Сцена собирается в новой аддитивной сцене, сохраняется и закрывается. Открытая
    // до запуска сцена не перезаписывается. Это ревью-грейбокс: примитивы + готовые
    // префабы бамбука, без финального арта.
    public static class AutumnSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string SceneFolder = "Assets/_Project/Scenes/Seasons/Autumn";
        const string MaterialRoot = "Assets/_Project/Art/Environment/Autumn/Location/Materials";
        const string BambooPrefabRoot = "Assets/_Project/Prefabs/Environment/Autumn";
        const string HeroinePrefabPath = "Assets/_Project/Prefabs/Player/Heroine.prefab";

        // Стена: передняя плоскость на z=0, толщина уходит в -Z. Так задняя половина
        // поляны (z<0) оказывается за стеной, как в GDD.
        const float WallHalfWidth = 100f;
        const float WallHeight = 60f;
        const float WallThickness = 4f;
        const float OpeningWidth = 5f;
        const float OpeningBottom = 47.5f;   // квадратный проём 5×5 вокруг отметки ~50 м
        const float OpeningTop = 52.5f;

        // Насыпь: верх (0,50,4), низ (65,0,4). Длина и наклон считаются из этих точек.
        // Первые 3 м по X заходят под площадку 6x6 — игрок выходит на z≈4 и только
        // потом поворачивает на +X. Для грейбокса это допустимое упрощение.
        static readonly Vector3 RampStart = new Vector3(0f, 50f, 4f);
        static readonly Vector3 RampEnd = new Vector3(65f, 0f, 4f);
        const float RampWidth = 8f;
        const float RampThickness = 0.4f;

        // Ширина тропы и радиус арены в GDD не заданы (ARCHITECTURE §13.3.1: TBD).
        // Это плейсхолдеры для ревью-прохождения.
        static readonly Vector3 ClearingCenter = new Vector3(70f, 0f, 0f);
        const float ClearingRadius = 16f;

        static readonly Vector3 TrailCenter = new Vector3(70f, 0f, 87f);
        const float TrailWidth = 3.6f;
        const float TrailLength = 150f;
        const float TrailStartZ = 12f;
        const float TrailEndZ = 162f;

        static readonly Vector3 ArenaCenter = new Vector3(70f, 0f, 188f);
        const float ArenaRadius = 26f;
        const int RingSlots = 20;
        const float RingRadius = 27f;

        // Плоские песчаные площадки: верх на y=0, низ на y=-0.2.
        // Лес — плоскость на y=-0.2, чтобы площадки читались как настил.
        const float PadCenterY = -0.1f;
        const float PadThickness = 0.2f;
        const float ForestFloorY = -0.2f;

        [MenuItem("ShadowTan/Setup/Art/Build Autumn Location")]
        public static void Build()
        {
            var alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
            if (alreadyOpen.IsValid() && alreadyOpen.isLoaded)
            {
                Debug.LogError($"ShadowTan: {ScenePath} уже открыта. Закрой её и запусти сборку снова.");
                return;
            }

            EnsureFolder(MaterialRoot);
            EnsureFolder(SceneFolder);

            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("ShadowTan: не найден шейдер HDRP/Lit. Проверь render pipeline.");
                return;
            }

            AssetDatabase.Refresh();

            var ctx = new BuildContext
            {
                WallMaterial = CreateMaterial(shader, "M_Autumn_Wall", "#6B6B6B", 0.10f, 0f),
                SandMaterial = CreateMaterial(shader, "M_Autumn_Sand", "#D8C18E", 0.05f, 0f),
                GrassMaterial = CreateMaterial(shader, "M_Autumn_Grass", "#5C392A", 0.05f, 0f),
                StoneMaterial = CreateMaterial(shader, "M_Autumn_Stone", "#78583D", 0.08f, 0f),
                MetalMaterial = CreateMaterial(shader, "M_Autumn_Metal", "#727B80", 0.55f, 0.9f),
                BambooWallMaterial = CreateMaterial(shader, "M_Autumn_BambooWall", "#3E4A2A", 0.05f, 0f)
            };
            ctx.GroundLayer = ResolveLayer("Ground");
            ctx.WallLayer = ResolveLayer("Wall");
            ctx.TriggerLayer = ResolveLayer("Trigger");
            ctx.BambooPrefabs = LoadBambooPrefabs();
            AssetDatabase.SaveAssets();

            var heroinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeroinePrefabPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            bool saved = false;
            string boundsReport = string.Empty;
            try
            {
                CreateHierarchy(ctx);
                BuildGround(ctx);
                BuildWalls(ctx);
                BuildProps(ctx);
                BuildFlow(ctx);
                BuildEncounter(ctx);
                BuildAtmosphere(ctx);
                BuildGizmos(ctx);
                ScatterBamboo(ctx, scene);
                PlaceHeroinePreview(scene, heroinePrefab);

                boundsReport = BuildBoundsReport(ctx);

                saved = EditorSceneManager.SaveScene(scene, ScenePath);
                if (!saved)
                    Debug.LogError($"ShadowTan: не удалось сохранить сцену {ScenePath}.");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!saved) return;

            Debug.Log(
                "ShadowTan: Autumn_Gameplay собран и сохранён.\n" +
                $"  Сцена: {ScenePath}\n" +
                $"  Бамбук: {ctx.BambooCount} растений (Bamboo_Giant_A/B/C, seed 17)\n" +
                $"  Камни насыпи: {ctx.StoneCount}\n" +
                $"  Сегменты кольца арены: {ctx.RingSegments}\n" +
                "  Точки засады: 11 (тропа) + 8 (арена)\n" +
                $"  Материалы: {MaterialRoot}\n" +
                "  Ключевые габариты:\n" + boundsReport);
        }

        // ---------------------------------------------------------------- hierarchy

        static void CreateHierarchy(BuildContext ctx)
        {
            ctx.LevelRoot = CreateEmpty(null, "[Level]", Vector3.zero).transform;

            ctx.Greybox = CreateEmpty(ctx.LevelRoot, "Greybox", Vector3.zero).transform;
            ctx.GroundRoot = CreateEmpty(ctx.Greybox, "Ground", Vector3.zero).transform;
            ctx.WallsRoot = CreateEmpty(ctx.Greybox, "Walls", Vector3.zero).transform;
            ctx.PropsRoot = CreateEmpty(ctx.Greybox, "Props", Vector3.zero).transform;
            ctx.BambooRoot = CreateEmpty(ctx.PropsRoot, "Bamboo", Vector3.zero).transform;

            ctx.FlowRoot = CreateEmpty(null, "[Flow]", Vector3.zero).transform;
            ctx.EncounterRoot = CreateEmpty(null, "[Encounter]", Vector3.zero).transform;
            ctx.AtmosphereRoot = CreateEmpty(null, "[Atmosphere]", Vector3.zero).transform;
            ctx.GizmosRoot = CreateEmpty(null, "[Gizmos]", Vector3.zero).transform;
        }

        // ---------------------------------------------------------------- ground

        static void BuildGround(BuildContext ctx)
        {
            var forest = CreatePlane(ctx.GroundRoot, "ForestFloor", new Vector3(35f, ForestFloorY, 100f), 400f, ctx.GrassMaterial, ctx.GroundLayer);
            ctx.KeyObjects.Add(forest);

            var entrance = CreateBox(ctx.GroundRoot, "EntranceFloor", new Vector3(0f, 49.8f, 0f), new Vector3(6f, 0.4f, 6f), Quaternion.identity, ctx.StoneMaterial, ctx.GroundLayer);
            ctx.KeyObjects.Add(entrance);

            var ramp = BuildRamp(ctx);
            ctx.KeyObjects.Add(ramp);

            var clearing = CreateCylinder(ctx.GroundRoot, "Clearing", new Vector3(ClearingCenter.x, PadCenterY, ClearingCenter.z), ClearingRadius, PadThickness, ctx.SandMaterial, ctx.GroundLayer);
            ctx.KeyObjects.Add(clearing);

            var trail = CreateBox(ctx.GroundRoot, "Trail", new Vector3(TrailCenter.x, PadCenterY, TrailCenter.z), new Vector3(TrailWidth, PadThickness, TrailLength), Quaternion.identity, ctx.SandMaterial, ctx.GroundLayer);
            ctx.KeyObjects.Add(trail);

            var arena = CreateCylinder(ctx.GroundRoot, "Arena", new Vector3(ArenaCenter.x, PadCenterY, ArenaCenter.z), ArenaRadius, PadThickness, ctx.SandMaterial, ctx.GroundLayer);
            ctx.KeyObjects.Add(arena);
        }

        static GameObject BuildRamp(BuildContext ctx)
        {
            Vector3 delta = RampEnd - RampStart;
            float length = delta.magnitude;
            float tilt = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            var rotation = Quaternion.Euler(0f, 0f, tilt);
            Vector3 normal = rotation * Vector3.up;

            // Верхняя плоскость проходит через RampStart/RampEnd, поэтому центр смещён
            // вниз по нормали на половину толщины.
            Vector3 center = (RampStart + RampEnd) * 0.5f - normal * (RampThickness * 0.5f);

            var ramp = CreateBox(ctx.GroundRoot, "Slope", center, new Vector3(length, RampThickness, RampWidth), rotation, ctx.StoneMaterial, ctx.GroundLayer);

            // Низкий бортик по +Z краю, чтобы с насыпи нельзя было свалиться.
            const float ridgeHeight = 1f;
            const float ridgeThickness = 0.5f;
            Vector3 ridgeLocal = new Vector3(0f, RampThickness * 0.5f + ridgeHeight * 0.5f, RampWidth * 0.5f - ridgeThickness * 0.5f);
            CreateBox(ctx.WallsRoot, "Slope_Ridge", center + rotation * ridgeLocal, new Vector3(length, ridgeHeight, ridgeThickness), rotation, ctx.WallMaterial, ctx.WallLayer);

            // Круглые камни по краям; центр шириной ~5 м остаётся проходимым.
            var stones = CreateEmpty(ctx.PropsRoot, "SlopeStones", Vector3.zero).transform;
            var rnd = new System.Random(41);
            const int stoneCount = 14;
            for (int i = 0; i < stoneCount; i++)
            {
                float t = (i + 0.5f) / stoneCount;
                float localX = (t - 0.5f) * length;
                float side = (i % 2 == 0) ? -1f : 1f;
                float localZ = side * 3.1f + Range(rnd, -0.15f, 0.15f);
                float diameter = Range(rnd, 0.5f, 1.0f);
                Vector3 p = center + rotation * new Vector3(localX, RampThickness * 0.5f + diameter * 0.5f, localZ);
                var stone = CreateSphere(stones, $"SlopeStone_{i:00}", p, diameter, ctx.StoneMaterial, ctx.WallLayer);
                stone.transform.rotation = Quaternion.Euler(Range(rnd, -12f, 12f), Range(rnd, 0f, 360f), Range(rnd, -12f, 12f));
            }
            ctx.StoneCount = stoneCount;

            return ramp;
        }

        // ---------------------------------------------------------------- walls

        static void BuildWalls(BuildContext ctx)
        {
            var wall = CreateEmpty(ctx.WallsRoot, "Wall_Grey", Vector3.zero).transform;

            float halfOpening = OpeningWidth * 0.5f;
            float sideWidth = WallHalfWidth - halfOpening;
            float sideX = halfOpening + sideWidth * 0.5f;
            float wallZ = -WallThickness * 0.5f;

            CreateBox(wall, "Wall_Left", new Vector3(-sideX, WallHeight * 0.5f, wallZ), new Vector3(sideWidth, WallHeight, WallThickness), Quaternion.identity, ctx.WallMaterial, ctx.WallLayer);
            CreateBox(wall, "Wall_Right", new Vector3(sideX, WallHeight * 0.5f, wallZ), new Vector3(sideWidth, WallHeight, WallThickness), Quaternion.identity, ctx.WallMaterial, ctx.WallLayer);

            // Подоконник (низ) и перемычка (верх) проёма 5 x 7.
            CreateBox(wall, "Wall_Sill", new Vector3(0f, OpeningBottom * 0.5f, wallZ), new Vector3(OpeningWidth, OpeningBottom, WallThickness), Quaternion.identity, ctx.WallMaterial, ctx.WallLayer);
            float lintelHeight = WallHeight - OpeningTop;
            CreateBox(wall, "Wall_Lintel", new Vector3(0f, OpeningTop + lintelHeight * 0.5f, wallZ), new Vector3(OpeningWidth, lintelHeight, WallThickness), Quaternion.identity, ctx.WallMaterial, ctx.WallLayer);
            ctx.KeyObjects.Add(wall.gameObject);

            // Непроходимые «стены» бамбука вдоль тропы.
            var bambooWalls = CreateEmpty(ctx.WallsRoot, "BambooWalls", Vector3.zero).transform;
            const float bambooWallHeight = 5f;
            const float bambooWallThickness = 1.5f;
            // Коллизия бамбука стоит за физическими барьерами (x=±2.0),
            // чтобы игрок упирался именно в забор, а не в невидимую стену.
            CreateBox(bambooWalls, "BambooWall_L", new Vector3(70f - 2.75f, bambooWallHeight * 0.5f, TrailCenter.z), new Vector3(bambooWallThickness, bambooWallHeight, TrailLength), Quaternion.identity, ctx.BambooWallMaterial, ctx.WallLayer);
            CreateBox(bambooWalls, "BambooWall_R", new Vector3(70f + 2.75f, bambooWallHeight * 0.5f, TrailCenter.z), new Vector3(bambooWallThickness, bambooWallHeight, TrailLength), Quaternion.identity, ctx.BambooWallMaterial, ctx.WallLayer);
            ctx.KeyObjects.Add(bambooWalls.gameObject);

            // Кольцо арены: 20 слотов, слот на -Z (вход с тропы) пропускается.
            var ring = CreateEmpty(ctx.WallsRoot, "BambooRing", Vector3.zero).transform;
            float segmentLength = 2f * (RingRadius + 1f) * Mathf.Tan(Mathf.PI / RingSlots) * 1.1f;
            int placed = 0;
            for (int i = 0; i < RingSlots; i++)
            {
                float angle = i * (360f / RingSlots);
                if (Mathf.Abs(Mathf.DeltaAngle(angle, 270f)) < 16f) continue;

                float rad = angle * Mathf.Deg2Rad;
                var position = new Vector3(
                    ArenaCenter.x + Mathf.Cos(rad) * RingRadius,
                    2.5f,
                    ArenaCenter.z + Mathf.Sin(rad) * RingRadius);
                // Длинная ось сегмента — локальный Z, поэтому поворот по -angle даёт касательную.
                var rotation = Quaternion.Euler(0f, -angle, 0f);
                CreateBox(ring, $"BambooRing_{i:00}", position, new Vector3(2f, 5f, segmentLength), rotation, ctx.BambooWallMaterial, ctx.WallLayer);
                placed++;
            }
            ctx.RingSegments = placed;
            ctx.KeyObjects.Add(ring.gameObject);

            // Коллизии бамбуковых стен остаются, но их серые коробки не рисуем:
            // визуальную стену даёт сам бамбук, а тропа не тонет в тени коробок.
            foreach (var r in bambooWalls.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var r in ring.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        // ---------------------------------------------------------------- props

        static void BuildProps(BuildContext ctx)
        {
            var monument = CreateBox(
                ctx.PropsRoot,
                "Monument_Base",
                new Vector3(ArenaCenter.x, 2.5f, ArenaCenter.z),
                new Vector3(1.8f, 5f, 1.8f),
                Quaternion.identity,
                ctx.MetalMaterial,
                ctx.WallLayer);
            ctx.KeyObjects.Add(monument);
        }

        // ---------------------------------------------------------------- flow

        static void BuildFlow(BuildContext ctx)
        {
            var entrance = new Vector3(0f, 50.1f, 0f);
            CreateEmpty(ctx.FlowRoot, "SeasonEntrance", entrance);
            CreateEmpty(ctx.FlowRoot, "SeasonExit", entrance);
            CreateEmpty(ctx.FlowRoot, "RespawnPoint_Entrance", entrance);

            var triggers = CreateEmpty(ctx.FlowRoot, "Triggers", Vector3.zero).transform;

            // Зона тропы: маркер у входа, объём на всю тропу (z 12..162).
            CreateTriggerVolume(triggers, "AutumnZoneTrigger_Trail", new Vector3(70f, 0f, 20f), new Vector3(12f, 6f, TrailLength), new Vector3(0f, 3f, TrailCenter.z - 20f), ctx.TriggerLayer);

            // Зона арены: грубый куб вокруг круглой опушки (z 158..218).
            CreateTriggerVolume(triggers, "AutumnZoneTrigger_Arena", new Vector3(70f, 0f, 170f), new Vector3(60f, 6f, 60f), new Vector3(0f, 3f, 18f), ctx.TriggerLayer);

            // Активация преследования после безопасного начала тропы (trailSafeDistance TBD).
            CreateTriggerVolume(triggers, "TrailActivationTrigger", new Vector3(70f, 0f, 30f), new Vector3(8f, 6f, 8f), new Vector3(0f, 3f, 0f), ctx.TriggerLayer);
        }

        // ---------------------------------------------------------------- encounter

        static void BuildEncounter(BuildContext ctx)
        {
            var targets = CreateEmpty(ctx.EncounterRoot, "Targets", Vector3.zero).transform;
            CreateEmpty(targets, "Monument", new Vector3(ArenaCenter.x, 0f, ArenaCenter.z));

            var points = CreateEmpty(ctx.EncounterRoot, "Points", Vector3.zero).transform;

            // 11 точек вдоль тропы, поочерёдно слева/справа, z = 30..150 с шагом 12.
            for (int i = 0; i <= 10; i++)
            {
                float z = 30f + i * 12f;
                float x = 70f + ((i % 2 == 0) ? -5f : 5f);
                CreateEmpty(points, $"AmbushPoint_Trail_{i:00}", new Vector3(x, 1.5f, z));
            }

            // 8 точек по кромке арены; смещение 22.5° не ставит точку в проём тропы.
            for (int i = 0; i < 8; i++)
            {
                float angle = 22.5f + i * 45f;
                float rad = angle * Mathf.Deg2Rad;
                var p = new Vector3(
                    ArenaCenter.x + Mathf.Cos(rad) * 24f,
                    1.5f,
                    ArenaCenter.z + Mathf.Sin(rad) * 24f);
                CreateEmpty(points, $"AmbushPoint_Arena_{i:00}", p);
            }
        }

        // ---------------------------------------------------------------- atmosphere

        static void BuildAtmosphere(BuildContext ctx)
        {
            var sunGo = CreateEmpty(ctx.AtmosphereRoot, "Sun", Vector3.zero);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 30000f;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.useColorTemperature = true;
            sun.colorTemperature = 5200f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
        }

        // ---------------------------------------------------------------- gizmos

        static void BuildGizmos(BuildContext ctx)
        {
            CreateCamera(ctx.GizmosRoot, "OverviewCamera", new Vector3(35f, 70f, -70f), new Vector3(30f, 10f, 110f), 50f, false);
            CreateCamera(ctx.GizmosRoot, "EntranceCamera", new Vector3(0f, 52f, -4f), new Vector3(0f, 30f, 80f), 60f, true);
        }

        static Camera CreateCamera(Transform parent, string name, Vector3 position, Vector3 target, float fov, bool main)
        {
            var go = CreateEmpty(parent, name, position);
            go.tag = main ? "MainCamera" : "Untagged";

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.58f, 0.68f, 0.78f);
            // Overview — справочная камера планировки: существует в сцене, но не перебивает
            // Game view. Включается вручную при проверке общего вида.
            cam.enabled = main;
            go.transform.LookAt(target, Vector3.up);

            // Один слушатель на ревью-сцену; в игре его даёт CameraRig из 00_Boot.
            if (main) go.AddComponent<AudioListener>();

            return cam;
        }

        // ---------------------------------------------------------------- bamboo

        static void ScatterBamboo(BuildContext ctx, Scene scene)
        {
            if (ctx.BambooPrefabs == null || ctx.BambooPrefabs.Length == 0)
            {
                Debug.LogWarning("ShadowTan: префабы Bamboo_Giant_A/B/C не найдены — бамбук не расставлен.");
                return;
            }

            var rnd = new System.Random(17);

            // Густой бамбуковый лес вдоль тропы: восемь рядов с каждой стороны.
            // Крона взрослого бамбука ~4.5 м в диаметре, поэтому шаг 4 м по z
            // даёт сплошную стену без просветов на уровне глаз.
            float[] trailOffsets = { 3.9f, 5.0f, 6.3f, 7.9f, 9.9f };
            for (float z = 13f; z <= 165f; z += 4f)
            {
                for (int s = 0; s < 2; s++)
                {
                    float side = (s == 0) ? -1f : 1f;
                    for (int r = 0; r < trailOffsets.Length; r++)
                    {
                        float x = 70f + side * (trailOffsets[r] + Range(rnd, -0.5f, 0.5f));
                        float zz = z + Range(rnd, -1.8f, 1.8f);
                        PlantBamboo(ctx, scene, rnd, new Vector3(x, ForestFloorY, zz));
                    }
                }
            }

            // Промежуточные и внешние ряды: тотально плотный бамбук без
            // случайных одиночек. Внутренний ряд 2.9 м стоит сразу за травой,
            // внешние 13.5/17.5 дают глубину леса.
            float[] denseOffsets = { 3.5f, 4.5f, 6.9f, 10.5f, 13.5f, 17.5f };
            float[] denseSteps = { 3.5f, 3.5f, 3.5f, 3.5f, 4.5f, 4.5f };
            float[] denseStarts = { 13.5f, 14.2f, 13.0f, 14.0f, 13.5f, 15.0f };
            for (int s = 0; s < 2; s++)
            {
                float side = (s == 0) ? -1f : 1f;
                for (int r = 0; r < denseOffsets.Length; r++)
                {
                    for (float z = denseStarts[r]; z <= 165f; z += denseSteps[r])
                    {
                        float x = 70f + side * (denseOffsets[r] + Range(rnd, -0.4f, 0.4f));
                        float zz = z + Range(rnd, -1.2f, 1.2f);
                        PlantBamboo(ctx, scene, rnd, new Vector3(x, ForestFloorY, zz));
                    }
                }
            }

            // Дуга вокруг поляны снаружи r=19..24, только видимая (+Z) половина:
            // сторона насыпи и устье тропы остаются открытыми.
            float[] clearingAngles = { 10f, 22f, 34f, 46f, 58f, 70f, 110f, 122f, 134f, 146f, 158f, 170f };
            for (int i = 0; i < clearingAngles.Length; i++)
            {
                for (int ring = 0; ring < 2; ring++)
                {
                    float radius = (ring == 0) ? Range(rnd, 19f, 21f) : Range(rnd, 21.5f, 24f);
                    PlantBamboo(ctx, scene, rnd, Polar(ClearingCenter, radius, clearingAngles[i] + Range(rnd, -2f, 2f)));
                }
            }

            // Три плотных кольца вокруг арены; проём на -Z остаётся входом с тропы.
            // Внутреннее кольцо стоит сразу за коллизией (её внешняя грань ~28 м).
            for (int ring = 0; ring < 4; ring++)
            {
                float baseRadius = 28.8f + ring * 2.5f;
                for (int i = 0; i < 80; i++)
                {
                    float angle = i * 4.5f + ring * 1.5f;
                    if (Mathf.Abs(Mathf.DeltaAngle(angle, 270f)) < 24f) continue;
                    float radius = baseRadius + Range(rnd, -0.9f, 0.9f);
                    PlantBamboo(ctx, scene, rnd, Polar(ArenaCenter, radius, angle + Range(rnd, -2.5f, 2.5f)));
                }
            }

            // Свободно стоящий бамбук не сажаем: лес держится на регулярных
            // рядах вдоль тропы и кольцах арены, без случайных одиночек.
        }

        static void PlantBamboo(BuildContext ctx, Scene scene, System.Random rnd, Vector3 position)
        {
            if (IsBlocked(position)) return;

            var prefab = ctx.BambooPrefabs[rnd.Next(ctx.BambooPrefabs.Length)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetParent(ctx.BambooRoot, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f);
            go.transform.localScale = Vector3.one * Range(rnd, 1.10f, 1.55f);
            ctx.BambooCount++;
        }

        static bool IsBlocked(Vector3 p)
        {
            // Стена и её тыльная сторона.
            if (p.z > -6f && p.z < 2.5f && p.x > -WallHalfWidth - 5f && p.x < WallHalfWidth + 5f) return true;

            // Насыпь вместе с её тенью (x<67, z 0..8) — бамбук не должен протыкать настил.
            if (p.x > -2f && p.x < 67f && p.z > -2f && p.z < 11f) return true;

            // Поляна (запас 1 м: первые растения коридора стоят в 17.5 м от центра).
            if (XZDistance(p, ClearingCenter) < ClearingRadius + 1f) return true;

            // Тропа: не сажаем внутрь прохода. Ряд 2.9 м стоит уже за
            // невидимой коллизией (её внутренняя грань 1.5 м), поэтому разрешён.
            if (p.z > TrailStartZ - 1f && p.z < TrailEndZ + 1f && Mathf.Abs(p.x - 70f) < 2.4f) return true;

            // Арена и её бамбуковое кольцо (внешняя грань сегментов ~28 м).
            if (XZDistance(p, ArenaCenter) < RingRadius + 0.4f) return true;

            return false;
        }

        // ---------------------------------------------------------------- misc

        static void PlaceHeroinePreview(Scene scene, GameObject prefab)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"ShadowTan: не найден {HeroinePrefabPath} — превью героини пропущено.");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.transform.SetPositionAndRotation(new Vector3(0f, 50.1f, -2f), Quaternion.identity);
        }

        static GameObject[] LoadBambooPrefabs()
        {
            var variants = new[] { "A", "B", "C" };
            var result = new List<GameObject>();
            for (int i = 0; i < variants.Length; i++)
            {
                var path = $"{BambooPrefabRoot}/Bamboo_Giant_{variants[i]}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"ShadowTan: не найден префаб бамбука {path}.");
                    continue;
                }
                result.Add(prefab);
            }
            return result.ToArray();
        }

        static string BuildBoundsReport(BuildContext ctx)
        {
            var sb = new StringBuilder();
            foreach (var go in ctx.KeyObjects)
            {
                if (go == null) continue;

                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    sb.AppendLine($"  {go.name}: нет рендереров");
                    continue;
                }

                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                sb.AppendLine($"  {go.name}: center={bounds.center} size={bounds.size}");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- primitives

        static GameObject CreateEmpty(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go;
        }

        static GameObject CreateBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation);
            go.transform.localScale = size;
            go.layer = layer;
            ApplyMaterial(go, material);
            return go;
        }

        static GameObject CreateCylinder(Transform parent, string name, Vector3 center, float radius, float height, Material material, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            go.layer = layer;
            ApplyMaterial(go, material);

            // У примитива-цилиндра капсульный коллайдер: для плоской площадки он врёт.
            // Снимаем любой готовый коллайдер и ставим мешевый по фактической форме.
            var existing = go.GetComponent<Collider>();
            if (existing != null) Object.DestroyImmediate(existing);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var meshCollider = go.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;
            return go;
        }

        static GameObject CreateSphere(Transform parent, string name, Vector3 center, float diameter, Material material, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = Vector3.one * diameter;
            go.layer = layer;
            ApplyMaterial(go, material);
            return go;
        }

        static GameObject CreatePlane(Transform parent, string name, Vector3 center, float size, Material material, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(size / 10f, 1f, size / 10f); // Plane = 10x10 при scale 1
            go.layer = layer;
            ApplyMaterial(go, material);
            return go;
        }

        static GameObject CreateTriggerVolume(Transform parent, string name, Vector3 position, Vector3 size, Vector3 center, int layer)
        {
            var go = CreateEmpty(parent, name, position);
            go.layer = layer;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = center;
            box.size = size;
            return go;
        }

        static void ApplyMaterial(GameObject go, Material material)
        {
            if (material == null) return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        // ---------------------------------------------------------------- helpers

        static Material CreateMaterial(Shader shader, string name, string hex, float smoothness, float metallic)
        {
            var path = $"{MaterialRoot}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = shader;

            if (!ColorUtility.TryParseHtmlString(hex, out var color))
            {
                Debug.LogError($"ShadowTan: не разобрал цвет {hex} для {name}.");
                color = Color.magenta;
            }
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 0f);
            if (mat.HasProperty("_DoubleSidedNormalMode")) mat.SetFloat("_DoubleSidedNormalMode", 0f);
            if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 2f);

            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static int ResolveLayer(string name)
        {
            int index = LayerMask.NameToLayer(name);
            if (index < 0)
            {
                Debug.LogWarning($"ShadowTan: слой \"{name}\" не найден, объекты уйдут в Default. Запусти ShadowTan/Setup/Configure Layers.");
                return 0;
            }
            return index;
        }

        static Vector3 Polar(Vector3 center, float radius, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector3(center.x + Mathf.Cos(rad) * radius, ForestFloorY, center.z + Mathf.Sin(rad) * radius);
        }

        static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float Range(System.Random rnd, float min, float max)
        {
            return min + (float)rnd.NextDouble() * (max - min);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        sealed class BuildContext
        {
            public Transform LevelRoot;
            public Transform Greybox;
            public Transform GroundRoot;
            public Transform WallsRoot;
            public Transform PropsRoot;
            public Transform FlowRoot;
            public Transform EncounterRoot;
            public Transform AtmosphereRoot;
            public Transform GizmosRoot;
            public Transform BambooRoot;

            public Material WallMaterial;
            public Material SandMaterial;
            public Material GrassMaterial;
            public Material StoneMaterial;
            public Material MetalMaterial;
            public Material BambooWallMaterial;

            public GameObject[] BambooPrefabs;

            public int GroundLayer;
            public int WallLayer;
            public int TriggerLayer;

            public int BambooCount;
            public int StoneCount;
            public int RingSegments;

            public readonly List<GameObject> KeyObjects = new List<GameObject>();
        }
    }
}
