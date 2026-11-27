using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace ShadowTan.EditorTools
{
    // Осенний пост-процессинг: тёплый жёлто-коричневый look без оранжевого заката.
    // Neutral — чистый вариант, Warm — с тёплой дымкой и коричневыми тенями, ACES — кинематографичная альтернатива.
    public static class AutumnPostProcessingSetup
    {
        const string Generated = "Assets/_Project/Art/Environment/Autumn/Location/Generated";
        const string Folder = Generated + "/Post";
        const string NeutralPath = Folder + "/PP_Autumn_Gameplay_Neutral.asset";
        const string WarmPath = Folder + "/PP_Autumn_Gameplay_Warm.asset";
        const string AcesPath = Folder + "/PP_Autumn_Gameplay_ACES.asset";

        [MenuItem("ShadowTan/Setup/Art/Setup Autumn Post Processing")]
        public static void Apply()
        {
            EnsureFolder();
            RemoveTrailFogOverride();
            Build(NeutralPath, TonemappingMode.Neutral, false);
            var warm = Build(WarmPath, TonemappingMode.Neutral, true);
            Build(AcesPath, TonemappingMode.ACES, false);

            var global = FindVolume("GlobalVolume");
            if (global != null) global.sharedProfile = warm;

            var tripo = FindVolume("TripoProp_Finish");
            if (tripo != null) tripo.weight = 0f;

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Autumn PP: assigned PP_Autumn_Gameplay_Warm; TripoProp_Finish vignette disabled.");
        }

        [MenuItem("ShadowTan/Setup/Art/Capture Autumn PP Comparison")]
        public static void CaptureComparison()
        {
            var global = FindVolume("GlobalVolume");
            var tripo = FindVolume("TripoProp_Finish");
            if (global == null)
            {
                Debug.LogError("Autumn PP: GlobalVolume not found.");
                return;
            }

            var original = global.sharedProfile;
            var tripoWeight = tripo != null ? tripo.weight : 0f;

            global.sharedProfile = original;
            if (tripo != null) tripo.weight = tripoWeight;
            Capture("pp_before_arena.png", ArenaPosition, ArenaTarget);
            Capture("pp_before_trail.png", TrailPosition, ArenaTarget);

            RemoveTrailFogOverride();
            EnsureFolder();
            var neutral = Build(NeutralPath, TonemappingMode.Neutral, false);
            if (tripo != null) tripo.weight = 0f;
            global.sharedProfile = neutral;
            Capture("pp_neutral_arena.png", ArenaPosition, ArenaTarget);
            Capture("pp_neutral_trail.png", TrailPosition, ArenaTarget);

            var aces = Build(AcesPath, TonemappingMode.ACES, false);
            global.sharedProfile = aces;
            Capture("pp_aces_arena.png", ArenaPosition, ArenaTarget);

            var warm = Build(WarmPath, TonemappingMode.Neutral, true);
            global.sharedProfile = warm;
            Capture("pp_warm_arena.png", ArenaPosition, ArenaTarget);
            Capture("pp_warm_trail.png", TrailPosition, ArenaTarget);

            global.sharedProfile = warm;
            if (tripo != null) tripo.weight = 0f;

            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Autumn PP: captured before / neutral / aces / warm.");
        }

        static readonly Vector3 ArenaPosition = new Vector3(70f, 2.0f, 166f);
        static readonly Vector3 TrailPosition = new Vector3(70f, 2.4f, 152f);
        static readonly Vector3 ArenaTarget = new Vector3(70f, 1.2f, 188f);

        static Volume FindVolume(string name)
        {
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (var volume in volumes)
                if (volume != null && volume.gameObject.name == name)
                    return volume;
            return null;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Generated)) return;
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder(Generated, "Post");
        }

        static VolumeProfile Build(string path, TonemappingMode tonemapping, bool warm)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            else
            {
                for (int i = profile.components.Count - 1; i >= 0; i--)
                {
                    var component = profile.components[i];
                    if (component != null) Object.DestroyImmediate(component, true);
                }
                profile.components.Clear();
            }

            var exposure = Add<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(warm ? 11.6f : 12.4f);
            exposure.compensation.Override(0f);

            var tone = Add<Tonemapping>(profile);
            tone.mode.Override(tonemapping);

            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(warm ? 1.05f : 1.0f);
            bloom.intensity.Override(warm ? 0.10f : 0.18f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(warm ? new Color(1.0f, 0.92f, 0.80f) : new Color(1.0f, 0.94f, 0.84f));
            bloom.dirtIntensity.Override(0f);

            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0f);
            color.contrast.Override(warm ? 5f : 8f);
            color.colorFilter.Override(warm ? new Color(1.0f, 0.92f, 0.80f) : new Color(1.0f, 0.97f, 0.90f));
            color.hueShift.Override(warm ? -3f : 0f);
            color.saturation.Override(warm ? 2f : 6f);

            var whiteBalance = Add<WhiteBalance>(profile);
            whiteBalance.temperature.Override(warm ? 14f : 6f);
            whiteBalance.tint.Override(0f);

            var smh = Add<ShadowsMidtonesHighlights>(profile);
            smh.shadows.Override(warm ? new Vector4(0.90f, 0.84f, 0.74f, 1f) : new Vector4(0.98f, 0.95f, 0.88f, 1f));
            smh.midtones.Override(warm ? new Vector4(0.98f, 0.93f, 0.82f, 1f) : new Vector4(1.02f, 1.00f, 0.93f, 1f));
            smh.highlights.Override(warm ? new Vector4(1.00f, 0.95f, 0.86f, 1f) : new Vector4(1.00f, 0.97f, 0.90f, 1f));
            smh.shadowsStart.Override(0f);
            smh.shadowsEnd.Override(0.25f);
            smh.highlightsStart.Override(0.75f);
            smh.highlightsEnd.Override(1f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(warm ? 0.17f : 0.14f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(new Color(0.05f, 0.03f, 0.02f));
            vignette.rounded.Override(true);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(warm ? 0.07f : 0.10f);
            grain.response.Override(0.8f);

            var fog = Add<Fog>(profile);
            fog.enabled.Override(true);
            fog.colorMode.Override(warm ? FogColorMode.ConstantColor : FogColorMode.SkyColor);
            fog.color.Override(warm ? new Color(2.2f, 1.8f, 1.3f) : Color.black);
            fog.tint.Override(warm ? new Color(0.95f, 0.90f, 0.80f) : new Color(0.95f, 0.90f, 0.80f));
            fog.maxFogDistance.Override(warm ? 250f : 600f);
            fog.baseHeight.Override(-0.5f);
            fog.maximumHeight.Override(22f);
            fog.meanFreePath.Override(warm ? 140f : 180f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(warm ? new Color(0.50f, 0.44f, 0.33f) : new Color(0.62f, 0.56f, 0.44f));
            fog.depthExtent.Override(warm ? 90f : 100f);
            fog.anisotropy.Override(warm ? 0.15f : 0.10f);
            fog.directionalLightsOnly.Override(true);

            var motion = Add<MotionBlur>(profile);
            motion.intensity.Override(0f);
            motion.cameraMotionBlur.Override(false);

            var ssao = Add<ScreenSpaceAmbientOcclusion>(profile);
            ssao.intensity.Override(0.45f);
            ssao.directLightingStrength.Override(0.15f);
            ssao.radius.Override(1.2f);
            ssao.spatialBilateralAggressiveness.Override(0.65f);
            ssao.temporalAccumulation.Override(true);
            ssao.ghostingReduction.Override(0.5f);
            ssao.blurSharpness.Override(0.5f);
            ssao.specularOcclusion.Override(0.5f);
            ssao.occluderMotionRejection.Override(true);
            ssao.receiverMotionRejection.Override(true);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void RemoveTrailFogOverride()
        {
            const string path = "Assets/_Project/Art/Environment/Autumn/Location/Generated/Forest/ForestBake_TrailAtmosphere.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) return;

            Fog fog;
            if (!profile.TryGet(out fog) || fog == null) return;

            profile.components.Remove(fog);
            Object.DestroyImmediate(fog, true);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void Capture(string fileName, Vector3 position, Vector3 target)
        {
            var go = new GameObject("PP_CompareCamera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 800f;
            cam.fieldOfView = 55f;
            cam.transform.position = position;
            cam.transform.LookAt(target, Vector3.up);
            go.AddComponent<HDAdditionalCameraData>();

            const int width = 1280;
            const int height = 720;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            File.WriteAllBytes("Assets/Screenshots/" + fileName, tex.EncodeToPNG());
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            AssetDatabase.Refresh();
        }
    }
}
