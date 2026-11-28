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

// Четыре космические сцены — по одной после каждого босса: МКС в центре, небо — туманность и созвездия
// 28 стоянок ещё живых боссов (Sky_Space_After<босс>.hdr, ArtSource/Sky/make_all_skies.sh), полёт от первого
// лица (SpaceFlyer), белая заливка-загрузка при спавне и при подлёте к станции. МКС — модель NASA
// «ISS stationary» в реальном масштабе (см. Iss()).
// Цепочка: сезон → (победа) → Space_After<босс> → (подлёт к МКС) → Slideshow_<следующий> → следующий сезон.
// Пересобирает сцены целиком; SpaceTuning, если уже есть, не трогает.
public static class SpaceSceneBuilder
{
    const string SceneDir = "Assets/_Game/Scenes";
    const string ArtDir = "Assets/_Game/Art/Space";
    const string DataDir = "Assets/_Game/Data/Space";
    // Небо после каждого босса: остаются сектора ещё живых (весна → лето → осень → зима); после последнего — пустота.
    static readonly string[] SkyPaths =
    {
        ArtDir + "/Sky_Space_AfterQinglong.hdr",   // юг, запад, север
        ArtDir + "/Sky_Space_AfterZhuque.hdr",     // запад, север
        ArtDir + "/Sky_Space_AfterBaihu.hdr",      // север
        ArtDir + "/Sky_Space_Nebula.hdr",          // пустота
    };
    // HDRI неба ярче камеры на столько EV (звёзды — около белого, туманность — слабая); то же и на уровнях боссов.
    public const float SkyOverCameraEV = 1.5f;
    const float CameraEV = 15f;
    const string LoadingConfigPath = "Assets/_Project/Data/Narrative/Slideshows/LoadingCoverConfig.asset";

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
        for (int i = 0; i < 4; i++) BuildScene((Season)i, tuning, loading, ImportSky(SkyPaths[i]));

        var paths = new List<string>();
        foreach (var n in SpaceScenes) paths.Add(SceneDir + "/" + n + ".unity");
        AddToBuildSettings(paths);
        AssetDatabase.DeleteAsset(SceneDir + "/Space_ISS.unity");   // первая версия, одна сцена на всё
        // прежние звёзды-меши (четыре сектора со случайными звёздами) и общий профиль — заменены небом-HDRI
        foreach (var old in new[] { "Stars_East", "Stars_South", "Stars_West", "Stars_North" })
            AssetDatabase.DeleteAsset(ArtDir + "/" + old + ".asset");
        AssetDatabase.DeleteAsset(ArtDir + "/M_SpaceStars.mat");
        AssetDatabase.DeleteAsset(ArtDir + "/Space atmosphere.asset");
        // прежняя МКС-грейбокс из примитивов — заменена моделью NASA (Art/Space/ISS)
        foreach (var old in new[] { "ISS hull MLI.mat", "ISS hull Russian segment.mat", "ISS truss.mat", "ISS joints.mat",
                                    "ISS radiator.mat", "ISS array mast.mat", "ISS solar blanket.mat", "ISS solar cells.png" })
            AssetDatabase.DeleteAsset(ArtDir + "/" + old);
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

    static void BuildScene(Season defeated, SpaceTuning tuning, LoadingCoverConfig loading, Cubemap sky)
    {
        int i = (int)defeated;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Space").transform;

        Lighting(root, sky, SpaceScenes[i]);
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

        var hub = new GameObject("Space hub").AddComponent<SpaceHub>();
        hub.transform.SetParent(root, false);
        hub.tuning = tuning;
        hub.flyer = flyer;
        hub.station = station;
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

    // ---------------- Небо и свет ----------------

    // HDR-развёртка → кубмапа для HDRI Sky (общая для космоса и уровней боссов).
    public static Cubemap ImportSky(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new FileNotFoundException("Нет неба: сгенерируй ArtSource/Sky/make_all_skies.sh → " + path);
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
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    const float SunLux = 120000f;   // солнце без атмосферы

    static Volume Lighting(Transform root, Cubemap sky, string sceneName)
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
        // Мягкий белый свет (SoftWhiteLighting): белое солнце с мягкими тенями, белая заливка снизу и встречная.
        Light("Sun", SunLux, Color.white, SunEuler, true);
        var sun = group.Find("Sun").GetComponent<Light>();
        SoftWhiteLighting.MakeSoftSun(sun);
        Light("Fill from below", SunLux * SoftWhiteLighting.SkyFillShare, Color.white, new Vector3(-80f, 20f, 0f), false);
        SoftWhiteLighting.Fill(group, SoftWhiteLighting.BackFillName, SunLux * SoftWhiteLighting.BackFillShare,
                               SoftWhiteLighting.BackDirection(sun.transform.forward));
        var skyLighting = new GameObject("Sky lighting (soft white)");
        skyLighting.transform.SetParent(group, false);
        SoftWhiteLighting.AddSkyLighting(skyLighting, sceneName, SunLux * SoftWhiteLighting.AmbientShare);

        string path = ArtDir + "/Space atmosphere " + sceneName + ".asset";
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
        hdri.exposure.Override(CameraEV + SkyOverCameraEV);
        var exposure = Add<Exposure>();
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(CameraEV);
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

    // ---------------- МКС ----------------
    // Модель NASA «ISS stationary» (glTF, импорт — пакет glTFast), реальный масштаб: ферма ≈110 м вдоль локальной X,
    // модули вдоль Z, нос (Harmony) — локальная +Z: левый борт (панели *_p) на −X. Станция ставится центром
    // в начало координат и поворачивается на StationYaw. Материалы glTF переводятся в HDRP/Lit
    // (Art/Space/ISS/Materials) с единой гладкостью UnifySmoothness.Value; на каждую деталь — выпуклый
    // коллайдер: по ним SpaceHub считает подлёт (ClosestPoint не работает с невыпуклыми).
    const string IssModel = ArtDir + "/ISS/ISS_stationary.glb";
    const string IssMaterialDir = ArtDir + "/ISS/Materials";

    static Transform Iss(Transform root)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(IssModel);
        if (model == null) throw new FileNotFoundException("Нет модели МКС " + IssModel + " (нужен пакет glTFast)");

        var iss = Group("ISS", root);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "ISS (NASA model)";
        instance.transform.SetParent(iss, false);

        var renderers = instance.GetComponentsInChildren<Renderer>();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        instance.transform.localPosition = -bounds.center;
        iss.localRotation = Quaternion.Euler(0f, StationYaw, 0f);

        Directory.CreateDirectory(IssMaterialDir);
        var converted = new Dictionary<Material, Material>();
        foreach (var r in renderers)
        {
            var mats = r.sharedMaterials;
            for (int k = 0; k < mats.Length; k++)
            {
                if (mats[k] == null) continue;
                if (!converted.TryGetValue(mats[k], out var lit)) converted[mats[k]] = lit = LitFromGltf(mats[k]);
                mats[k] = lit;
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;

            var filter = r.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var collider = r.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
            }
        }
        return iss;
    }

    // glTF pbrMetallicRoughness → HDRP/Lit: цвет и его текстура, нормали, металличность; гладкость — единая.
    static Material LitFromGltf(Material src)
    {
        string safe = string.Join("_", src.name.Split(Path.GetInvalidFileNameChars())).Replace("*", "").Trim();
        string path = IssMaterialDir + "/M_ISS_" + safe.Replace(' ', '_') + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("HDRP/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        if (src.HasProperty("baseColorFactor")) m.SetColor("_BaseColor", src.GetColor("baseColorFactor"));
        m.SetTexture("_BaseColorMap", src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : null);
        var normal = src.HasProperty("normalTexture") ? src.GetTexture("normalTexture") : null;
        m.SetTexture("_NormalMap", normal);
        if (normal != null && src.HasProperty("normalTexture_scale")) m.SetFloat("_NormalScale", src.GetFloat("normalTexture_scale"));
        m.SetFloat("_Metallic", src.HasProperty("metallicFactor") ? src.GetFloat("metallicFactor") : 0f);
        m.SetFloat("_Smoothness", UnifySmoothness.Value);
        HDMaterial.ValidateMaterial(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }
}
