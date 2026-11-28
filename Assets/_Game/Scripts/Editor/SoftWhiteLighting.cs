using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Мягкий белый свет для сцен с космическим небом. Видимое небо тёмное, и рассеянного света от него почти нет —
// тени проваливаются в черноту. Поэтому освещает сцену другое небо: ровное бело-серое градиентное небо в объёме
// на слое SkyLighting (HDRP: skyLightingOverrideLayerMask в ассетах HDRP). Камеры этот слой не видят, поэтому
// на экране остаётся космос, а рассеянный свет и отражения — мягкие белые. Схема одна для космоса и уровней боссов:
//   небо освещения — белое сверху, светло-серое к земле, даёт рассеянный свет долей от солнца;
//   солнце — белое, с размытыми краями теней и ослабленными тенями (яркость солнца не меняется);
//   заливка неба (свет без теней, уже есть в сцене) — белая, долей от солнца;
//   встречная заливка — белая, без теней, с противоположной от солнца стороны.
// Меню «Apply soft white lighting to boss levels» применяет это к сценам боссов; космос делает SpaceSceneBuilder.
// Повторный запуск даёт тот же результат: доли считаются от текущей яркости солнца.
public static class SoftWhiteLighting
{
    // Все доли и углы — [Tbd], подбираются на глаз.
    public const float AmbientShare = 0.5f;         // рассеянный свет неба освещения (освещённость сверху), доля от солнца
    public const float SkyFillShare = 0.2f;         // заливка неба, доля от солнца
    public const float BackFillShare = 0.12f;       // встречная заливка, доля от солнца
    public const float SunAngularDiameter = 6f;     // угловой размер солнца, градусов: чем больше, тем мягче край тени
    public const float ShadowDimmer = 0.7f;         // 1 — тень полной силы, 0 — тени нет
    public const string BackFillName = "Soft back fill (white)";
    public const string SkyLightingLayer = "SkyLighting";
    const string SkyLightingName = "Sky lighting (soft white)";
    const string ProfileDir = "Assets/_Game/Art/Space";
    static readonly Color SkyTop = Color.white, SkyMiddle = new Color(0.92f, 0.92f, 0.92f), SkyBottom = new Color(0.6f, 0.6f, 0.6f);

    const string SceneDir = "Assets/_Game/Scenes";
    static readonly string[] BossScenes =
        { "Spring_Qinglong", "Summer_Zhuque_01_Dry", "Summer_Zhuque_02_Lava", "Autumn_Baihu", "Winter_Xuanwu" };

    [MenuItem("Tools/Tan Shadow/Lighting/Apply soft white lighting to boss levels")]
    public static void ApplyToBossLevels()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Выйди из Play Mode.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var name in BossScenes)
        {
            string path = SceneDir + "/" + name + ".unity";
            if (!File.Exists(path)) { Debug.LogWarning("Soft light: нет сцены " + path); continue; }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Debug.Log("Soft light: " + name + " — " + ApplyToOpenScene(name));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    // Солнце — самый яркий направленный свет с тенями; остальные направленные без теней — заливки неба.
    // Точечные и прожекторы (огни лавы и т. п.) не трогаются.
    static string ApplyToOpenScene(string sceneName)
    {
        Light sun = null;
        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        foreach (var l in lights)
            if (l.type == LightType.Directional && l.shadows != LightShadows.None && l.name != BackFillName &&
                (sun == null || l.intensity > sun.intensity))
                sun = l;
        if (sun == null) return "направленного света с тенями нет, пропуск";

        MakeSoftSun(sun);
        int fills = 0;
        foreach (var l in lights)
        {
            if (l == sun || l.type != LightType.Directional || l.name == BackFillName) continue;
            l.color = Color.white;
            l.intensity = sun.intensity * SkyFillShare;
            l.shadows = LightShadows.None;
            EditorUtility.SetDirty(l);
            fills++;
        }

        Light back = null;
        foreach (var l in lights) if (l.name == BackFillName) back = l;
        if (back == null) back = Fill(sun.transform.parent, BackFillName, 0f, Vector3.down);
        back.intensity = sun.intensity * BackFillShare;
        back.transform.rotation = Quaternion.LookRotation(BackDirection(sun.transform.forward));
        EditorUtility.SetDirty(back);

        GameObject holder = null;
        foreach (var go in sun.gameObject.scene.GetRootGameObjects()) if (go.name == SkyLightingName) holder = go;
        if (holder == null) holder = new GameObject(SkyLightingName);
        AddSkyLighting(holder, sceneName, sun.intensity * AmbientShare);
        return $"солнце «{sun.name}» {sun.intensity:0} лк, заливок неба {fills}, встречная {back.intensity:0} лк, " +
               $"рассеянный {sun.intensity * AmbientShare:0} лк";
    }

    // Объём неба освещения на слое SkyLighting: градиентное небо, яркость — под заданную освещённость сверху.
    public static void AddSkyLighting(GameObject holder, string sceneName, float ambientLux)
    {
        int layer = LayerMask.NameToLayer(SkyLightingLayer);
        if (layer < 0) throw new System.InvalidOperationException("Нет слоя " + SkyLightingLayer + " (Tags and Layers)");
        holder.layer = layer;

        string path = ProfileDir + "/Sky lighting " + sceneName + ".asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        var env = profile.Add<VisualEnvironment>();
        AssetDatabase.AddObjectToAsset(env, profile);
        env.skyType.Override((int)SkyType.Gradient);
        env.cloudType.Override(0);
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var sky = profile.Add<GradientSky>();
        AssetDatabase.AddObjectToAsset(sky, profile);
        sky.top.Override(SkyTop);
        sky.middle.Override(SkyMiddle);
        sky.bottom.Override(SkyBottom);
        // Освещённость горизонтальной поверхности от равномерного неба яркостью L — π·L; верх неба в среднем ≈0.96 белого.
        sky.exposure.Override(Mathf.Log(ambientLux / (Mathf.PI * 0.96f), 2f));
        EditorUtility.SetDirty(profile);

        var volume = holder.GetComponent<Volume>();
        if (volume == null) volume = holder.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.sharedProfile = profile;
        EditorUtility.SetDirty(volume);
    }

    public static void MakeSoftSun(Light sun)
    {
        sun.color = Color.white;
        sun.useColorTemperature = false;
        sun.shadows = LightShadows.Soft;
        var hd = sun.GetComponent<HDAdditionalLightData>();
        if (hd != null)
        {
            hd.angularDiameter = SunAngularDiameter;
            hd.shadowDimmer = ShadowDimmer;
            EditorUtility.SetDirty(hd);
        }
        EditorUtility.SetDirty(sun);
    }

    // Белый направленный свет без теней, светит вдоль forward.
    public static Light Fill(Transform parent, string name, float lux, Vector3 forward)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.rotation = Quaternion.LookRotation(forward.normalized, Mathf.Abs(forward.normalized.y) > 0.99f ? Vector3.forward : Vector3.up);
        var l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        go.AddComponent<HDAdditionalLightData>();
        l.lightUnit = UnityEngine.Rendering.LightUnit.Lux;
        l.intensity = lux;
        l.color = Color.white;
        l.useColorTemperature = false;
        l.shadows = LightShadows.None;
        return l;
    }

    // Встречная заливка: с противоположной от солнца стороны по горизонтали, сверху вниз так же, как солнце.
    public static Vector3 BackDirection(Vector3 sunForward)
    {
        var d = new Vector3(-sunForward.x, Mathf.Min(sunForward.y, -0.3f), -sunForward.z);
        return d.normalized;
    }
}
