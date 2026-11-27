using System.Collections.Generic;
using System.IO;
using ShadowTan.Narrative;
using TanShadow.Seasons;
using TanShadow.Cosmos;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Четыре космические сцены — по одной после каждого босса: МКС в центре, небо без звёзд и звёзды в четырёх
// секторах по сторонам света (сектор убитого босса гаснет), полёт от первого лица (SpaceFlyer), белая
// заливка-загрузка при спавне и при подлёте к станции. МКС — грейбокс из примитивов в реальном масштабе.
// Цепочка: сезон → (победа) → Space_After<босс> → (подлёт к МКС) → Slideshow_<следующий> → следующий сезон.
// Пересобирает сцены целиком; SpaceTuning, если уже есть, не трогает.
public static class SpaceSceneBuilder
{
    const string SceneDir = "Assets/_Game/Scenes";
    const string ArtDir = "Assets/_Game/Art/Space";
    const string DataDir = "Assets/_Game/Data/Space";
    const string SkyPath = ArtDir + "/Sky_Space_Nebula.hdr";
    const string LoadingConfigPath = "Assets/_Project/Data/Narrative/Slideshows/LoadingCoverConfig.asset";
    const float StarDistance = 2000f;

    // Босс → космическая сцена после него → слайд-шоу после подлёта к МКС; и сцены сезона, где этот босс живёт.
    static readonly string[] SpaceScenes = { "Space_AfterQinglong", "Space_AfterZhuque", "Space_AfterBaihu", "Space_AfterXuanwu" };
    static readonly string[] NextSlideshows = { "Slideshow_Zhuque", "Slideshow_Baihu", "Slideshow_Xuanwu", "Slideshow_Finale" };
    static readonly string[][] SeasonScenes =
    {
        new[] { "Spring_Qinglong" },
        new[] { "Summer_Zhuque_01_Dry", "Summer_Zhuque_02_Lava" },
        new[] { "Autumn_Baihu" },
        new[] { "Winter_Xuanwu" },
    };

    static Material hull, russianHull, truss, joint, radiator, solar, mast, starMaterial;
    // Солнце высоко: освещает станцию с любой стороны света. Станция повёрнута на 45°, чтобы ни с одной
    // стороны не быть видной торцом фермы.
    static readonly Vector3 SunEuler = new Vector3(62f, 20f, 0f);
    const float StationYaw = 45f;

    [MenuItem("Tools/Tan Shadow/Space/Build 4 space scenes")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Выйди из Play Mode.");
        Directory.CreateDirectory(ArtDir);
        Directory.CreateDirectory(DataDir);
        AssetDatabase.Refresh();

        var tuning = Tuning();
        var loading = AssetDatabase.LoadAssetAtPath<LoadingCoverConfig>(LoadingConfigPath);
        if (loading == null) throw new FileNotFoundException("Нет " + LoadingConfigPath + " (слайд-шоу из main)");
        var sky = ImportSky();
        Materials();
        var starMeshes = StarMeshes(tuning);

        for (int i = 0; i < 4; i++) BuildScene((Season)i, tuning, loading, sky, starMeshes);

        var paths = new List<string>();
        foreach (var n in SpaceScenes) paths.Add(SceneDir + "/" + n + ".unity");
        AddToBuildSettings(paths);
        AssetDatabase.DeleteAsset(SceneDir + "/Space_ISS.unity");   // первая версия, одна сцена на всё
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(paths[0]);
        Debug.Log("Space: собраны " + string.Join(", ", SpaceScenes));
    }

    // Во все сцены сезонов — VictoryToSpace: после победы загрузка соответствующего космоса.
    [MenuItem("Tools/Tan Shadow/Space/Wire season victories to space")]
    public static void WireSeasons()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Выйди из Play Mode.");
        var loading = AssetDatabase.LoadAssetAtPath<LoadingCoverConfig>(LoadingConfigPath);
        for (int s = 0; s < 4; s++)
            foreach (var name in SeasonScenes[s])
            {
                string path = SceneDir + "/" + name + ".unity";
                if (!File.Exists(path)) { Debug.LogWarning("Space: нет сцены " + path); continue; }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                VictoryToSpace link = null;
                foreach (var go in scene.GetRootGameObjects())
                    if (go.name == "Victory → space") link = go.GetComponent<VictoryToSpace>();
                if (link == null) link = new GameObject("Victory → space").AddComponent<VictoryToSpace>();
                link.season = (Season)s;
                link.spaceScene = SpaceScenes[s];
                link.loadingConfig = loading;
                EditorUtility.SetDirty(link);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
    }

    static void BuildScene(Season defeated, SpaceTuning tuning, LoadingCoverConfig loading, Cubemap sky, Mesh[] starMeshes)
    {
        int i = (int)defeated;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Space").transform;

        Lighting(root, sky);
        var station = Iss(root);

        var cam = new GameObject("Player camera (SpaceFlyer)");
        cam.tag = "MainCamera";
        cam.transform.SetParent(root, false);
        var camera = cam.AddComponent<Camera>();
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 3000f;
        camera.fieldOfView = 60f;
        var hdCam = cam.AddComponent<HDAdditionalCameraData>();
        hdCam.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
        hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cam.AddComponent<AudioListener>();
        var flyer = cam.AddComponent<SpaceFlyer>();
        flyer.tuning = tuning;
        flyer.center = station;
        flyer.enabled = false;   // включает SpaceHub, когда спадёт заливка
        Vector3 spawn = SeasonProgress.Direction(defeated) * tuning.spawnDistance + Vector3.up * tuning.spawnHeight;
        cam.transform.SetPositionAndRotation(spawn, Quaternion.LookRotation(-spawn, Vector3.up));
        Heroine(root, cam.transform, tuning);

        var stars = new GameObject("Stars (4 sectors)").AddComponent<StarSectors>();
        stars.transform.SetParent(root, false);
        stars.tuning = tuning;
        stars.follow = cam.transform;
        string[] sectorNames = { "East · spring", "South · summer", "West · autumn", "North · winter" };
        for (int s = 0; s < 4; s++)
        {
            var go = new GameObject("Stars " + sectorNames[s]);
            go.transform.SetParent(stars.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = starMeshes[s];
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = starMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            stars.sectors[s] = r;
        }

        var hub = new GameObject("Space hub").AddComponent<SpaceHub>();
        hub.transform.SetParent(root, false);
        hub.tuning = tuning;
        hub.flyer = flyer;
        hub.station = station;
        hub.stars = stars;
        hub.defeated = defeated;
        hub.nextScene = NextSlideshows[i];
        hub.loadingConfig = loading;

        EditorSceneManager.SaveScene(scene, SceneDir + "/" + SpaceScenes[i] + ".unity");
    }

    // Героиня (визуальная модель из Player, без боевого контроллера): камера в её голове, тело видно снизу.
    // Поза — стоячий idle: в невесомости он читается как парение. [Tbd] отдельная поза полёта.
    const string HeroineModel = "Assets/_Game/Prefabs/Characters/Yinniang_Model.prefab";
    const string HeroineIdle = "Assets/_Project/Art/Characters/Heroine/_Placeholder/Yinniang/Animations/Locomotion/idle.fbx";

    static void Heroine(Transform root, Transform view, SpaceTuning tuning)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeroineModel);
        if (prefab == null) { Debug.LogWarning("Space: нет модели героини " + HeroineModel); return; }
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.name = "Heroine (first-person body)";
        model.transform.SetParent(root, false);
        model.transform.SetPositionAndRotation(view.position - Vector3.up * 1.6f, Quaternion.Euler(0f, view.eulerAngles.y, 0f));

        var animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = SpaceController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var body = model.AddComponent<SpaceBody>();
        body.tuning = tuning;
        body.view = view;
        body.body = animator;
    }

    static RuntimeAnimatorController SpaceController()
    {
        string path = ArtDir + "/AC_Heroine_Space.controller";
        var existing = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
        if (existing != null) return existing;
        AnimationClip idle = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(HeroineIdle))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) idle = c;
        return UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPathWithClip(path, idle);
    }

    static void AddToBuildSettings(List<string> paths)
    {
        var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var p in paths)
            if (!list.Exists(s => s.path == p)) list.Add(new EditorBuildSettingsScene(p, true));
        list.RemoveAll(s => s.path == SceneDir + "/Space_ISS.unity");
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ---------------- Данные ----------------

    static SpaceTuning Tuning()
    {
        string path = DataDir + "/SpaceTuning.asset";
        var tuning = AssetDatabase.LoadAssetAtPath<SpaceTuning>(path);
        if (tuning == null)
        {
            tuning = ScriptableObject.CreateInstance<SpaceTuning>();
            AssetDatabase.CreateAsset(tuning, path);
        }
        return tuning;
    }

    // ---------------- Звёзды ----------------
    // Сектор — дуга горизонта шириной sectorSpan вокруг своей стороны света. Звёзды — квадраты к центру на
    // расстоянии StarDistance; объект едет за камерой. Яркость по степенному закону: много слабых, мало ярких.

    static Mesh[] StarMeshes(SpaceTuning tuning)
    {
        var meshes = new Mesh[4];
        string[] files = { "East", "South", "West", "North" };
        float[] azimuth = { 90f, 180f, 270f, 0f };   // от севера (+Z) по часовой: восток — +X
        for (int s = 0; s < 4; s++)
        {
            var rng = new System.Random(1000 + s);
            int n = tuning.starsPerSector;
            var v = new Vector3[n * 4];
            var uv = new Vector2[n * 4];
            var col = new Color[n * 4];
            var tri = new int[n * 6];
            float sinLo = Mathf.Sin(tuning.starElevation.x * Mathf.Deg2Rad), sinHi = Mathf.Sin(tuning.starElevation.y * Mathf.Deg2Rad);
            for (int k = 0; k < n; k++)
            {
                float az = (azimuth[s] + ((float)rng.NextDouble() - 0.5f) * tuning.sectorSpan) * Mathf.Deg2Rad;
                float el = Mathf.Asin(Mathf.Lerp(sinLo, sinHi, (float)rng.NextDouble()));
                var dir = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
                float b = Mathf.Pow((float)rng.NextDouble(), 3.5f);
                float bright = Mathf.Lerp(0.04f, 1f, b);
                float size = StarDistance * Mathf.Tan(Mathf.Lerp(tuning.starAngularSize.x, tuning.starAngularSize.y, Mathf.Sqrt(b)) * Mathf.Deg2Rad);
                var right = Vector3.Cross(Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up, dir).normalized * size * 0.5f;
                var up = Vector3.Cross(dir, right).normalized * size * 0.5f;
                var c = dir * StarDistance;
                float temp = (float)rng.NextDouble();   // голубые — белые — жёлтые
                var tint = temp < 0.5f ? Color.Lerp(new Color(0.75f, 0.85f, 1f), Color.white, temp * 2f)
                                       : Color.Lerp(Color.white, new Color(1f, 0.86f, 0.66f), (temp - 0.5f) * 2f);
                int o = k * 4;
                v[o] = c - right - up; v[o + 1] = c + right - up; v[o + 2] = c + right + up; v[o + 3] = c - right + up;
                uv[o] = new Vector2(0, 0); uv[o + 1] = new Vector2(1, 0); uv[o + 2] = new Vector2(1, 1); uv[o + 3] = new Vector2(0, 1);
                for (int q = 0; q < 4; q++) col[o + q] = tint * bright;
                int t = k * 6;
                tri[t] = o; tri[t + 1] = o + 2; tri[t + 2] = o + 1; tri[t + 3] = o; tri[t + 4] = o + 3; tri[t + 5] = o + 2;
            }
            string path = ArtDir + "/Stars_" + files[s] + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.name = "Stars_" + files[s];
            mesh.vertices = v; mesh.uv = uv; mesh.colors = col; mesh.triangles = tri;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            meshes[s] = mesh;
        }

        string matPath = ArtDir + "/M_SpaceStars.mat";
        var shader = Shader.Find("TanShadow/VfxUnlit");
        starMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (starMaterial == null) { starMaterial = new Material(shader); AssetDatabase.CreateAsset(starMaterial, matPath); }
        starMaterial.shader = shader;
        starMaterial.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Game/Art/Vfx/T_VfxSoftDot.png"));
        starMaterial.SetColor("_Color", Color.white);
        starMaterial.SetFloat("_Nits", tuning.starNits);
        starMaterial.SetFloat("_Mode", 1);
        starMaterial.SetFloat("_SrcBlend", (float)BlendMode.One);
        starMaterial.SetFloat("_DstBlend", (float)BlendMode.One);
        starMaterial.renderQueue = 3000;
        EditorUtility.SetDirty(starMaterial);
        return meshes;
    }

    // ---------------- Небо и свет ----------------

    static Cubemap ImportSky()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(SkyPath);
        if (importer == null) throw new FileNotFoundException("Нет неба: сгенерируй ArtSource/Sky/make_space_sky.py → " + SkyPath);
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.textureShape = TextureImporterShape.TextureCube;
        settings.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        settings.cubemapConvolution = TextureImporterCubemapConvolution.None;
        settings.mipmapEnabled = true;
        importer.SetTextureSettings(settings);
        importer.sRGBTexture = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Cubemap>(SkyPath);
    }

    static Volume Lighting(Transform root, Cubemap sky)
    {
        var group = new GameObject("Lighting").transform;
        group.SetParent(root, false);

        void Light(string name, float lux, Color color, Vector3 euler, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(group, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            go.AddComponent<HDAdditionalLightData>();
            l.lightUnit = LightUnit.Lux;
            l.intensity = lux;
            l.color = color;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }
        // Солнце без атмосферы: жёсткий белый свет; снизу — слабый голубой отсвет Земли.
        Light("Sun", 120000f, new Color(1f, 0.97f, 0.92f), SunEuler, true);
        Light("Earthshine", 20000f, new Color(0.55f, 0.68f, 1f), new Vector3(-80f, 20f, 0f), false);

        string path = ArtDir + "/Space atmosphere.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        T Add<T>() where T : VolumeComponent
        {
            var c = profile.Add<T>();
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }
        var env = Add<VisualEnvironment>();
        env.skyType.Override((int)SkyType.HDRI);
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var hdri = Add<HDRISky>();
        hdri.hdriSky.Override(sky);
        hdri.exposure.Override(16.5f);
        var exposure = Add<Exposure>();
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(15f);
        var fog = Add<Fog>();
        fog.enabled.Override(false);
        var blur = Add<MotionBlur>();
        blur.intensity.Override(0f);
        var bloom = Add<Bloom>();
        bloom.intensity.Override(0.15f);
        EditorUtility.SetDirty(profile);

        var volume = group.gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 50;
        volume.sharedProfile = profile;
        return volume;
    }

    // ---------------- Материалы ----------------

    static void Materials()
    {
        hull = Mat("ISS hull MLI", new Color(0.80f, 0.78f, 0.72f), 0f, 0.25f);
        russianHull = Mat("ISS hull Russian segment", new Color(0.70f, 0.72f, 0.66f), 0f, 0.3f);
        truss = Mat("ISS truss", new Color(0.56f, 0.57f, 0.59f), 0.8f, 0.45f);
        joint = Mat("ISS joints", new Color(0.18f, 0.18f, 0.19f), 0.6f, 0.4f);
        radiator = Mat("ISS radiator", new Color(0.92f, 0.92f, 0.90f), 0f, 0.6f);
        mast = Mat("ISS array mast", new Color(0.35f, 0.33f, 0.30f), 0.7f, 0.5f);
        solar = Mat("ISS solar blanket", Color.white, 0.6f, 0.75f);
        solar.SetTexture("_BaseColorMap", SolarTexture());
        solar.SetTextureScale("_BaseColorMap", new Vector2(2f, 14f));
        EditorUtility.SetDirty(solar);
    }

    static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        string path = ArtDir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("HDRP/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    // Ячейки солнечной батареи: тёмно-янтарные, со светлыми швами.
    static Texture2D SolarTexture()
    {
        string path = ArtDir + "/ISS solar cells.png";
        const int size = 256, cell = 32;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool seam = x % cell < 2 || y % cell < 2;
            float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.06f;
            Color c = seam ? new Color(0.62f, 0.52f, 0.36f) : new Color(0.30f + n, 0.17f + n * 0.6f, 0.06f);
            t.SetPixel(x, y, c);
        }
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- МКС ----------------
    // Ферма — вдоль X, модули — вдоль Z под фермой. Размеры — по реальной станции, округлённо.

    static Transform Iss(Transform root)
    {
        var iss = Group("ISS", root);
        iss.localRotation = Quaternion.Euler(0f, StationYaw, 0f);
        // Поворот блоков батарей вокруг фермы (альфа-шарнир), чтобы полотна смотрели на солнце.
        Vector3 toSun = Quaternion.Inverse(iss.localRotation) * (Quaternion.Euler(SunEuler) * Vector3.back);
        float alpha = Mathf.Atan2(toSun.z, toSun.y) * Mathf.Rad2Deg;
        const float y = -4.4f;   // ось модулей под фермой

        var modules = Group("Pressurized modules", iss);
        ModuleZ("PMA-2", modules, 1.0f, 2.0f, new Vector3(0, y, 15.45f), joint);
        ModuleZ("Harmony", modules, 2.2f, 7.2f, new Vector3(0, y, 10.85f), hull);
        ModuleZ("Destiny", modules, 2.15f, 8.5f, new Vector3(0, y, 3.0f), hull);
        ModuleZ("Unity", modules, 2.25f, 5.5f, new Vector3(0, y, -4.0f), hull);
        ModuleZ("PMA-1", modules, 1.4f, 2.0f, new Vector3(0, y, -7.75f), joint);
        ModuleZ("Zarya", modules, 2.05f, 12.6f, new Vector3(0, y, -15.05f), russianHull);
        ModuleZ("Zvezda", modules, 2.1f, 9.1f, new Vector3(0, y, -25.9f), russianHull);
        ModuleZ("Zvezda aft", modules, 2.7f, 4.0f, new Vector3(0, y, -32.45f), russianHull);
        ModuleZ("Progress", modules, 1.35f, 7.0f, new Vector3(0, y, -37.95f), russianHull);
        ModuleX("Columbus", modules, 2.2f, 7.0f, new Vector3(5.7f, y, 10.85f), hull);
        ModuleX("Kibo", modules, 2.2f, 11.2f, new Vector3(-7.8f, y, 10.85f), hull);
        Box("Kibo exposed facility", modules, new Vector3(-16f, y, 10.85f), new Vector3(5f, 1f, 5.6f), truss);
        ModuleX("Quest airlock", modules, 2.0f, 5.5f, new Vector3(5.0f, y, -4.0f), hull);
        ModuleX("Tranquility", modules, 2.25f, 6.7f, new Vector3(-5.6f, y, -4.0f), hull);
        ModuleX("Cupola", modules, 1.5f, 1.5f, new Vector3(-9.7f, y, -4.0f), joint);
        ModuleY("Nauka", modules, 2.1f, 13f, new Vector3(0, y - 8.6f, -22f), russianHull);
        // Батареи «Звезды».
        Wing("Zvezda array +X", modules, new Vector3(2.3f, y, -25f), Vector3.right, 13f, 3.3f);
        Wing("Zvezda array -X", modules, new Vector3(-2.3f, y, -25f), Vector3.left, 13f, 3.3f);

        Truss(Group("Integrated truss", iss), 109f, 4.5f);

        // Радиаторы: два больших крыла вниз от фермы.
        var radiators = Group("Radiators", iss);
        for (int s = -1; s <= 1; s += 2)
        {
            Box("Radiator wing " + (s > 0 ? "S1" : "P1"), radiators, new Vector3(22f * s, -14.3f, 0f), new Vector3(11f, 23f, 0.15f), radiator);
            Box("Radiator boom " + (s > 0 ? "S1" : "P1"), radiators, new Vector3(22f * s, -2.8f, 0f), new Vector3(0.5f, 1.5f, 0.5f), joint);
        }

        // Четыре блока солнечных батарей: на каждом два крыла в разные стороны, блок повёрнут к солнцу.
        var arrays = Group("Solar arrays", iss);
        foreach (float x in new[] { -49.5f, -37.5f, 37.5f, 49.5f })
        {
            var pvm = Group("Solar module " + (x > 0 ? "S" : "P") + Mathf.Abs(x).ToString("0"), arrays);
            pvm.localPosition = new Vector3(x, 0, 0);
            pvm.localRotation = Quaternion.Euler(alpha, 0, 0);
            Box("Beta gimbal", pvm, Vector3.zero, new Vector3(1.6f, 1.6f, 6f), joint);
            Wing("Wing +Z", pvm, new Vector3(0, 0, 3f), Vector3.forward, 35f, 11.6f);
            Wing("Wing -Z", pvm, new Vector3(0, 0, -3f), Vector3.back, 35f, 11.6f);
            Box("PV radiator", pvm, new Vector3(0, 8.5f, 0), new Vector3(2.6f, 13f, 0.15f), radiator);
        }

        // Манипулятор Canadarm2 на ферме.
        var arm = Group("Canadarm2", iss);
        arm.localPosition = new Vector3(8f, 2.6f, 0.5f);
        Cylinder("Boom 1", arm, new Vector3(0, 3.2f, 1.2f), Quaternion.Euler(-20, 0, 0), 0.2f, 8.5f, truss);
        Cylinder("Boom 2", arm, new Vector3(0, 6.5f, 5.8f), Quaternion.Euler(-75, 0, 0), 0.2f, 8.5f, truss);
        return iss;
    }

    static void Truss(Transform group, float length, float side)
    {
        // Решётчатая ферма: четыре пояса, рамы через секцию и диагонали в боковых гранях.
        float h = side * 0.5f;
        foreach (var c in new[] { new Vector2(h, h), new Vector2(-h, h), new Vector2(h, -h), new Vector2(-h, -h) })
            Box("Longeron", group, new Vector3(0, c.x, c.y), new Vector3(length, 0.3f, 0.3f), truss);
        int bays = Mathf.RoundToInt(length / side);
        float step = length / bays;
        for (int i = 0; i <= bays; i++)
        {
            float x = -length * 0.5f + i * step;
            Box("Frame", group, new Vector3(x, h, 0), new Vector3(0.25f, 0.25f, side), truss);
            Box("Frame", group, new Vector3(x, -h, 0), new Vector3(0.25f, 0.25f, side), truss);
            Box("Frame", group, new Vector3(x, 0, h), new Vector3(0.25f, side, 0.25f), truss);
            Box("Frame", group, new Vector3(x, 0, -h), new Vector3(0.25f, side, 0.25f), truss);
            if (i == bays) break;
            float mid = x + step * 0.5f;
            float diag = Mathf.Sqrt(step * step + side * side);
            float angle = Mathf.Atan2(side, step) * Mathf.Rad2Deg * (i % 2 == 0 ? 1 : -1);
            Box("Diagonal", group, new Vector3(mid, 0, h), new Vector3(diag, 0.18f, 0.18f), truss, Quaternion.Euler(0, 0, angle));
            Box("Diagonal", group, new Vector3(mid, 0, -h), new Vector3(diag, 0.18f, 0.18f), truss, Quaternion.Euler(0, 0, -angle));
        }
        // Сердцевина с кабелями и оборудованием — чтобы ферма читалась издалека.
        Box("Core", group, Vector3.zero, new Vector3(length * 0.96f, side * 0.55f, side * 0.55f), joint);
        foreach (float x in new[] { -30f, 30f })
            Box("Alpha joint", group, new Vector3(x, 0, 0), new Vector3(2.2f, side + 0.6f, side + 0.6f), joint);
    }

    // Крыло батареи: мачта и два полотна по бокам. along — куда крыло растёт от основания.
    static void Wing(string name, Transform parent, Vector3 basePos, Vector3 along, float length, float width)
    {
        var w = Group(name, parent);
        w.localPosition = basePos;
        w.localRotation = Quaternion.LookRotation(along, Mathf.Abs(along.y) > 0.9f ? Vector3.forward : Vector3.up);
        float blanket = width * 0.4f;
        Box("Canister", w, new Vector3(0, 0, 0.6f), new Vector3(width, 0.5f, 1.2f), mast);
        Box("Mast", w, new Vector3(0, 0, length * 0.5f), new Vector3(0.35f, 0.35f, length), mast);
        Box("Blanket L", w, new Vector3(-width * 0.5f + blanket * 0.5f, 0, length * 0.5f + 0.6f), new Vector3(blanket, 0.05f, length - 1.2f), solar);
        Box("Blanket R", w, new Vector3(width * 0.5f - blanket * 0.5f, 0, length * 0.5f + 0.6f), new Vector3(blanket, 0.05f, length - 1.2f), solar);
        Box("Tip", w, new Vector3(0, 0, length), new Vector3(width, 0.3f, 0.4f), mast);
    }

    static Transform ModuleZ(string n, Transform p, float r, float len, Vector3 pos, Material m) =>
        Cylinder(n, p, pos, Quaternion.Euler(90, 0, 0), r, len, m);
    static Transform ModuleX(string n, Transform p, float r, float len, Vector3 pos, Material m) =>
        Cylinder(n, p, pos, Quaternion.Euler(0, 0, 90), r, len, m);
    static Transform ModuleY(string n, Transform p, float r, float len, Vector3 pos, Material m) =>
        Cylinder(n, p, pos, Quaternion.identity, r, len, m);

    static Transform Cylinder(string name, Transform parent, Vector3 pos, Quaternion rot, float radius, float length, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    static Transform Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion? rot = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot ?? Quaternion.identity;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go.transform;
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }
}
