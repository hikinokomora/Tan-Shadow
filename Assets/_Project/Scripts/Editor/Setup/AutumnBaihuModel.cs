using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    // Скелетный Байху из ArtSource/Colossi/Baihu/Baihu.blend (экспорт — export_baihu.py рядом с .blend).
    // Настраивает импорт SK_Baihu.fbx, материал, AC_Baihu и префаб Prefabs/Colossi/Baihu, затем в открытой
    // сцене с BaihuEncounter (Autumn_Baihu или Autumn_Gameplay) меняет болванку BaihuScale.obj на этот префаб.
    // Идемпотентен: повторный запуск перезаписывает свои ассеты и не плодит объекты в сцене.
    public static class AutumnBaihuModel
    {
        const string Root = "Assets/_Project/Art/Colossi/Baihu";
        const string FbxPath = Root + "/Models/SK_Baihu.fbx";
        const string MaterialPath = Root + "/Materials/M_Baihu_Fur.mat";
        const string ControllerPath = Root + "/Animations/AC_Baihu.controller";
        const string PrefabPath = "Assets/_Project/Prefabs/Colossi/Baihu.prefab";
        const string TuningPath = "Assets/_Game/Data/Autumn/BaihuTuning.asset";
        const string ModelName = "Baihu";
        static readonly string[] Clips = { "AttackJump", "LandMiss", "MonumentHit", "ReturnToBamboo" };

        // Как у болванки BaihuScale.obj (10,4 м с хвостом): колосс «значительно крупнее обычного тигра»,
        // точные размеры не утверждены (GDD-AU §06).
        const float BodyLength = 10.4f;
        // Шерсть фарфоровая (GDD-AU §03, #E0E1DD): светлые волоски атласа подгоняются к этому цвету.
        const string PeltHex = "#E0E1DD";
        // Глаза светятся в глубине бамбука; экспозиция дневная, поэтому число большое.
        const float EyeGlowNits = 9000f;
        // Высота дуги кода поверх прыжка из клипа (в клипе таз поднимается на ≈2,4 м).
        const float CodeLeapHeight = 0.6f;

        [MenuItem("ShadowTan/Setup/Art/Autumn Baihu Model")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
            var report = new System.Collections.Generic.List<string>();
            ConfigureTextures();
            var material = BuildMaterial(report);
            ConfigureModel(material, report);
            var controller = BuildController();
            var prefab = BuildPrefab(controller, report);
            Tuning(report);
            WireScene(prefab, report);
            AssetDatabase.SaveAssets();
            Debug.Log("ShadowTan / Autumn Baihu Model:\n" + string.Join("\n", report));
        }

        // ------------------------------------------------------------------ textures + material

        static void ConfigureTextures()
        {
            Texture("T_Baihu_BaseColor", true, false);
            Texture("T_Baihu_Normal", false, true);
            Texture("T_Baihu_Mask", false, false);
            Texture("T_Baihu_Emission", true, false);
        }

        static void Texture(string name, bool srgb, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath($"{Root}/Textures/{name}.png")
                           ?? throw new InvalidOperationException($"Missing {name}.png — run export_baihu.py.");
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.sRGBTexture == srgb && importer.maxTextureSize == 4096) return;
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = 4096;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }

        static Material BuildMaterial(System.Collections.Generic.List<string> report)
        {
            EnsureFolder(Path.GetDirectoryName(MaterialPath));
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Lit")) { name = "M_Baihu_Fur" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            Texture2D Load(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/{n}.png");
            material.SetTexture("_BaseColorMap", Load("T_Baihu_BaseColor"));
            material.SetTexture("_NormalMap", Load("T_Baihu_Normal"));
            material.SetTexture("_MaskMap", Load("T_Baihu_Mask"));
            material.SetTexture("_EmissiveColorMap", Load("T_Baihu_Emission"));
            Color tint = PeltTint();
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_NormalScale", 1f);
            material.SetFloat("_MetallicRemapMax", 0f);
            material.SetFloat("_SmoothnessRemapMin", 0f);
            material.SetFloat("_SmoothnessRemapMax", 0.35f);
            material.SetFloat("_UseEmissiveIntensity", 0f);
            material.SetColor("_EmissiveColor", Color.white * EyeGlowNits);
            material.SetFloat("_EmissiveExposureWeight", 1f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            report.Add($"Material: M_Baihu_Fur, pelt tint {ColorUtility.ToHtmlStringRGB(tint)} → light fur ≈ {PeltHex}; eyes emissive {EyeGlowNits} nits.");
            return material;
        }

        // Светлая шерсть атласа (верхние 35 % яркости, без полос) приводится к #E0E1DD. Тинт не ярче белого.
        static Color PeltTint()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes($"{Root}/Textures/T_Baihu_BaseColor.png"));
            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            var luminance = new float[(pixels.Length + 15) / 16];
            var linear = new Vector3[luminance.Length];
            for (int i = 0, k = 0; i < pixels.Length; i += 16, k++)
            {
                Color c = ((Color)pixels[i]).linear;
                linear[k] = new Vector3(c.r, c.g, c.b);
                luminance[k] = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            }
            float threshold = luminance.OrderBy(v => v).ElementAt((int)(luminance.Length * 0.65f));
            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int k = 0; k < linear.Length; k++)
                if (luminance[k] >= threshold) { sum += linear[k]; count++; }
            Vector3 light = sum / Mathf.Max(1, count);
            ColorUtility.TryParseHtmlString(PeltHex, out Color target);
            target = target.linear;
            var tint = new Vector3(target.r / Mathf.Max(0.001f, light.x), target.g / Mathf.Max(0.001f, light.y), target.b / Mathf.Max(0.001f, light.z));
            tint /= Mathf.Max(1f, Mathf.Max(tint.x, Mathf.Max(tint.y, tint.z)));
            return new Color(tint.x, tint.y, tint.z, 1f).gamma;
        }

        // ------------------------------------------------------------------ model import

        static void ConfigureModel(Material material, System.Collections.Generic.List<string> report)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath)
                           ?? throw new InvalidOperationException("Missing SK_Baihu.fbx — run export_baihu.py.");
            const string signature = "baihu-import-v2";
            if (importer.userData == signature && importer.clipAnimations.Length == Clips.Length)
            {
                report.Add("Model import: up to date.");
                return;
            }

            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.motionNodeName = "";
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Baihu_Fur"), material);

            // Без root motion: смещение и разворот кости Master гасит BaihuBody.KeepInPlace (у Master «вверх» — локальная Z).
            importer.clipAnimations = importer.importedTakeInfos.Select(take =>
            {
                string action = take.name.Substring(take.name.IndexOf('|') + 1).Replace("Baihu_", "");
                if (!Clips.Contains(action)) throw new InvalidOperationException($"Unexpected take {take.name}.");
                return new ModelImporterClipAnimation
                {
                    name = "A_Baihu_" + action,
                    takeName = take.name,
                    firstFrame = take.startTime * take.sampleRate,
                    lastFrame = take.stopTime * take.sampleRate,
                    loopTime = false,
                };
            }).ToArray();
            importer.userData = signature;
            importer.SaveAndReimport();
            report.Add($"Model import: Generic, no root motion node, clips {string.Join(", ", Clips.Select(c => "A_Baihu_" + c))}.");
        }

        static AnimationClip Clip(string action) =>
            AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>().SingleOrDefault(c => c.name == "A_Baihu_" + action)
            ?? throw new InvalidOperationException($"Clip A_Baihu_{action} is missing in SK_Baihu.fbx.");

        // ------------------------------------------------------------------ controller + prefab

        static AnimatorController BuildController()
        {
            EnsureFolder(Path.GetDirectoryName(ControllerPath));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var child in machine.states.Where(s => !Clips.Contains(s.state.name)).ToArray())
                machine.RemoveState(child.state);
            for (int i = 0; i < Clips.Length; i++)
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == Clips[i])
                            ?? machine.AddState(Clips[i], new Vector3(260f, 60f * i, 0f));
                state.motion = Clip(Clips[i]);
                state.writeDefaultValues = true;
                state.speed = 1f;
            }
            // Стоит на лапах — первый кадр разворота.
            machine.defaultState = machine.states.Select(s => s.state).Single(s => s.name == "ReturnToBamboo");
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static GameObject BuildPrefab(AnimatorController controller, System.Collections.Generic.List<string> report)
        {
            EnsureFolder(Path.GetDirectoryName(PrefabPath));
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            var root = new GameObject(ModelName);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.name = "SK_Baihu";

                // Габарит в позе привязки: длина по Z (с хвостом) и низ лап.
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                var world = baked.vertices.Select(v => skin.transform.TransformPoint(v)).ToArray();
                Object.DestroyImmediate(baked);
                float length = world.Max(v => v.z) - world.Min(v => v.z);
                float feet = world.Min(v => v.y);
                float scale = BodyLength / length;
                model.transform.localScale = Vector3.one * scale;
                model.transform.localPosition = new Vector3(0f, -feet * scale, 0f);

                var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                skin.updateWhenOffscreen = true;
                skin.shadowCastingMode = ShadowCastingMode.On;
                skin.skinnedMotionVectors = true;

                int layer = LayerMask.NameToLayer("Colossus");
                if (layer >= 0) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                var bodyType = FindType("TanShadow.Autumn.BaihuBody");
                var body = root.AddComponent(bodyType);
                var so = new SerializedObject(body);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                report.Add($"Prefab {PrefabPath}: model ×{scale:F2} → {BodyLength} m long, feet lifted {(-feet * scale):F2} m; AC_Baihu, root motion off.");
                return prefab;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static void Tuning(System.Collections.Generic.List<string> report)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<ScriptableObject>(TuningPath);
            if (tuning == null) return;
            var so = new SerializedObject(tuning);
            so.FindProperty("pounceLeapHeight").floatValue = CodeLeapHeight;
            so.FindProperty("retreatLeapHeight").floatValue = CodeLeapHeight;
            if (so.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(tuning);
            report.Add($"BaihuTuning: pounce/retreat leap height {CodeLeapHeight} m on top of the clip leap.");
        }

        // ------------------------------------------------------------------ scene

        static void WireScene(GameObject prefab, System.Collections.Generic.List<string> report)
        {
            var scene = SceneManager.GetActiveScene();
            var encounterType = FindType("TanShadow.Autumn.BaihuEncounter");
            var encounter = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren(encounterType, true)).FirstOrDefault();
            if (encounter == null)
            {
                report.Add($"Scene {scene.name}: no BaihuEncounter, scene untouched.");
                return;
            }

            var so = new SerializedObject(encounter);
            var tigerBody = so.FindProperty("tigerBody").objectReferenceValue as Transform
                            ?? throw new InvalidOperationException("BaihuEncounter.tigerBody is empty.");
            int removed = 0;
            for (int i = tigerBody.childCount - 1; i >= 0; i--)
            {
                var child = tigerBody.GetChild(i);
                var filter = child.GetComponent<MeshFilter>();
                bool blockout = filter != null && AssetDatabase.GetAssetPath(filter.sharedMesh).EndsWith("BaihuScale.obj", StringComparison.Ordinal);
                if (!blockout) continue;
                Undo.DestroyObjectImmediate(child.gameObject);
                removed++;
            }

            var model = tigerBody.Find(ModelName);
            if (model == null)
            {
                model = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, tigerBody)).transform;
                Undo.RegisterCreatedObjectUndo(model.gameObject, "Baihu model");
            }
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = tigerBody.gameObject.layer;

            // Прокси-триггер тела (камера, VFX) — по габариту новой модели в позе привязки.
            var proxy = tigerBody.GetComponent<BoxCollider>();
            if (proxy != null)
            {
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                var local = baked.vertices.Select(v => tigerBody.InverseTransformPoint(skin.transform.TransformPoint(v))).ToArray();
                Object.DestroyImmediate(baked);
                var bounds = new Bounds(local[0], Vector3.zero);
                foreach (var v in local) bounds.Encapsulate(v);
                Undo.RecordObject(proxy, "Baihu proxy");
                proxy.center = bounds.center;
                proxy.size = bounds.size;
            }

            so.FindProperty("body").objectReferenceValue = model.GetComponent(FindType("TanShadow.Autumn.BaihuBody"));
            so.FindProperty("tigerBodyYawOffset").floatValue = 0f;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException($"Could not save {scene.path}.");
            report.Add($"Scene {scene.name}: blockout children removed {removed}; {tigerBody.name}/{ModelName} wired to BaihuEncounter.body; proxy {(proxy != null ? "refit" : "none")}.");
        }

        static Type FindType(string name) =>
            TypeCache.GetTypesDerivedFrom<MonoBehaviour>().SingleOrDefault(t => t.FullName == name)
            ?? throw new InvalidOperationException($"Type {name} not found.");

        static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            EnsureFolder(Path.GetDirectoryName(path));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
