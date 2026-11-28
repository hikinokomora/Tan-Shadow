using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Небо уровней боссов — космос: туманность и созвездия только своего босса, в зените
// (Assets/_Game/Art/Space/Sky_Boss_<босс>.hdr, генерирует ArtSource/Sky/make_all_skies.sh).
// В сцену добавляется отдельный глобальный Volume «Space sky (constellations)» с приоритетом выше дневного
// профиля. Он заменяет только небо: HDRI вместо дневного, облака выключены; свет, туман, экспозиция — прежние.
// Выключить объект — вернётся прежнее небо. Рассеянный свет от неба (ambient) тоже берётся из нового неба,
// поэтому тени темнеют — это ожидаемо, свет подстраивается отдельно.
// Яркость неба — на SpaceSceneBuilder.SkyOverCameraEV выше фиксированной экспозиции камеры сцены, как в космосе.
public static class BossConstellationSkies
{
    const string SceneDir = "Assets/_Game/Scenes";
    const string ArtDir = "Assets/_Game/Art/Space";
    const string VolumeName = "Space sky (constellations)";
    const float Priority = 100f;
    const float DefaultCameraEV = 10f;

    static readonly string[,] Levels =
    {
        { "Spring_Qinglong", "Qinglong" },
        { "Summer_Zhuque_01_Dry", "Zhuque" },
        { "Summer_Zhuque_02_Lava", "Zhuque" },
        { "Autumn_Baihu", "Baihu" },
        { "Winter_Xuanwu", "Xuanwu" },
    };

    [MenuItem("Tools/Tan Shadow/Space/Apply constellation skies to boss levels")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Выйди из Play Mode.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        for (int i = 0; i < Levels.GetLength(0); i++)
        {
            string sceneName = Levels[i, 0], boss = Levels[i, 1];
            string scenePath = SceneDir + "/" + sceneName + ".unity";
            if (!File.Exists(scenePath)) { Debug.LogWarning("Boss skies: нет сцены " + scenePath); continue; }

            var sky = SpaceSceneBuilder.ImportSky(ArtDir + "/Sky_Boss_" + boss + ".hdr");
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            GameObject holder = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == VolumeName) holder = go;
            float cameraEV = SceneCameraEV(holder);

            var profile = Profile(sceneName, sky, cameraEV + SpaceSceneBuilder.SkyOverCameraEV);
            if (holder == null) holder = new GameObject(VolumeName);
            var volume = holder.GetComponent<Volume>();
            if (volume == null) volume = holder.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Boss skies: {sceneName} — небо {boss}, камера EV {cameraEV}, HDRI EV {cameraEV + SpaceSceneBuilder.SkyOverCameraEV}");
        }
        AssetDatabase.SaveAssets();
    }

    // Фиксированная экспозиция камеры из глобального профиля сцены с самым высоким приоритетом (кроме нашего).
    static float SceneCameraEV(GameObject ours)
    {
        float ev = DefaultCameraEV, best = float.MinValue;
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
        {
            if (!v.isGlobal || v.sharedProfile == null || (ours != null && v.gameObject == ours)) continue;
            if (v.sharedProfile.TryGet<Exposure>(out var exposure) && exposure.active &&
                exposure.mode.overrideState && exposure.mode.value == ExposureMode.Fixed && v.priority > best)
            {
                best = v.priority;
                ev = exposure.fixedExposure.value;
            }
        }
        if (best == float.MinValue) Debug.LogWarning($"Boss skies: фиксированной экспозиции не нашлось, беру EV {DefaultCameraEV}");
        return ev;
    }

    static VolumeProfile Profile(string sceneName, Cubemap sky, float skyEV)
    {
        string path = ArtDir + "/Space sky " + sceneName + ".asset";
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
        env.cloudType.Override(0);   // облака дневного профиля (лето) над космосом не нужны
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var hdri = Add<HDRISky>();
        hdri.hdriSky.Override(sky);
        hdri.exposure.Override(skyEV);
        EditorUtility.SetDirty(profile);
        return profile;
    }
}
