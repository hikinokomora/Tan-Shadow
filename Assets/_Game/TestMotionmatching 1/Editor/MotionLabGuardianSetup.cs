using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TanShadow.MotionMatchingLab.Editor
{
    public static class MotionLabGuardianSetup
    {
        const string Folder = MotionLibraryBaker.Folder + "/Guardian";
        const string VisualName = "Guardian Visual";
        const string StationName = "Sword Hang Station";

        [MenuItem("Tools/Tan Shadow/Motion Matching Lab/Set Up Guardian")]
        public static void BuildInCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MotionLabBuilder.ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open MotionMatchingLab in Edit Mode.");

            MotionMatchingPlayer legacy = UnityEngine.Object.FindFirstObjectByType<MotionMatchingPlayer>();
            MotionLabGuardian controller = UnityEngine.Object.FindFirstObjectByType<MotionLabGuardian>();
            Transform player = legacy ? legacy.transform : controller ? controller.transform : null;
            Camera camera = Camera.main;
            if (!player || !camera)
                throw new InvalidOperationException("Missing Guardian player or scene camera.");

            AnimationClip[] motions = ReadClips();
            Material body = CreateArmorMaterial("Guardian Armor", "Guardian_texture_0.png", "Guardian_normal.png",
                "Guardian_texture_0_metallic_roughness.png");
            Material sword = CreateArmorMaterial("Guardian Sword", "Sword_texture_0_002.png", "Sword_normal_002.png",
                "Sword_texture_0_metallic_roughness_001.png");
            Material slate = MaterialFor("Station Slate", new Color(.075f, .12f, .165f), .35f);
            Material marker = MaterialFor("Station Ochre", new Color(.95f, .69f, .27f), .16f);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClipPath(MotionLabGuardian.Clip.Idle));
            if (!prefab)
                throw new InvalidOperationException("Guardian/Clips/Idle.fbx is missing.");

            // Remove the mannequin and the old visual instance from this scene.
            foreach (Transform child in player.Cast<Transform>().ToArray())
                if (child.name == VisualName || child.GetComponentInChildren<Animator>(true))
                    Undo.DestroyObjectImmediate(child.gameObject);

            var previousStation = scene.GetRootGameObjects().FirstOrDefault(g => g.name == StationName);
            if (previousStation)
                Undo.DestroyObjectImmediate(previousStation);

            var holder = new GameObject(VisualName);
            Undo.RegisterCreatedObjectUndo(holder, "Create Guardian visual");
            holder.transform.SetParent(player, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder.transform);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = model.GetComponent<Animator>();
            if (!animator)
                animator = Undo.AddComponent<Animator>(model);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform root = FindBone(model, "root");
            Transform hand = FindBone(model, "DEF-hand.L");
            AnimationClip grab = motions[(int)MotionLabGuardian.Clip.JumpToBar];

            var raw = SampleTravel(model, root, holder.transform, grab);
            Vector3 advance = raw.samples[^1];
            float planar = new Vector2(advance.x, advance.z).magnitude;
            if (planar < .35f || planar > .70f || Mathf.Abs(advance.y - .205f) > .065f)
                throw new InvalidOperationException("Guardian jump root scale or axis is unexpected: " + advance);
            holder.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(advance.x, advance.z) * Mathf.Rad2Deg, 0f);
            var jump = SampleTravel(model, root, player, grab);
            var dive = SampleTravel(model, root, player, motions[(int)MotionLabGuardian.Clip.Dive]);
            var rise = SampleTravel(model, root, player, motions[(int)MotionLabGuardian.Clip.Rise]);
            grab.SampleAnimation(model, grab.length);
            Vector3 wrist = player.InverseTransformPoint(hand.position);
            float left = Mathf.Sign(wrist.x);
            Vector3 bar = wrist + new Vector3(.06192f * left, .06931f, .02795f);
            Transform station = BuildStation(bar, left, slate, marker);
            motions[(int)MotionLabGuardian.Clip.Idle].SampleAnimation(model, 0f);

            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Undo.RecordObject(skin, "Assign Guardian materials");
                Material material = skin.name.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0 ? sword : body;
                skin.sharedMaterials = Enumerable.Repeat(material, Math.Max(1, skin.sharedMaterials.Length)).ToArray();
                skin.updateWhenOffscreen = true;
            }
            if (!model.GetComponent<SwordRopePhysics>())
                Undo.AddComponent<SwordRopePhysics>(model);
            if (!model.GetComponent<GuardianSecondaryMotion>())
                Undo.AddComponent<GuardianSecondaryMotion>(model);

            RemoveLegacyCourse(scene);
            if (legacy)
                Undo.DestroyObjectImmediate(legacy);
            var oldView = camera.GetComponent<MotionLabView>();
            if (oldView)
                Undo.DestroyObjectImmediate(oldView);
            if (!controller)
                controller = Undo.AddComponent<MotionLabGuardian>(player.gameObject);
            Undo.RecordObject(controller, "Configure Guardian animations");
            controller.Configure(animator, holder.transform, root, station, motions, jump, dive, rise);
            Undo.RecordObject(player.gameObject, "Rename Guardian player");
            player.name = "Guardian Player";
            Undo.RecordObject(player, "Put Guardian at the grab station");
            player.SetPositionAndRotation(station.position - station.forward * .49f, station.rotation);

            var follow = camera.GetComponent<GuardianLabCamera>();
            if (!follow)
                follow = Undo.AddComponent<GuardianLabCamera>(camera.gameObject);
            Undo.RecordObject(follow, "Connect Guardian camera");
            Undo.RecordObject(camera, "Frame Guardian and the grab station");
            camera.fieldOfView = 46f;
            follow.Configure(player);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save MotionMatchingLab.");
            Selection.activeGameObject = player.gameObject;
            Debug.Log($"Guardian scene ready with {motions.Length} clips and one grab station.");
        }

        public static void ConfigureCurrentClips()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != MotionLabBuilder.ScenePath || EditorApplication.isPlaying)
                throw new InvalidOperationException("Open MotionMatchingLab in Edit Mode.");
            var controller = UnityEngine.Object.FindFirstObjectByType<MotionLabGuardian>();
            if (!controller || !controller.Takeoff)
                throw new InvalidOperationException("Guardian and the takeoff marker must already be in the scene.");
            Transform holder = controller.transform.Find(VisualName);
            var animator = holder ? holder.GetComponentInChildren<Animator>() : null;
            if (!animator)
                throw new InvalidOperationException("Guardian visual is missing.");
            Transform root = FindBone(animator.gameObject, "root");
            AnimationClip[] motions = ReadClips();
            GameObject sample = UnityEngine.Object.Instantiate(animator.gameObject, holder, false);
            sample.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Transform sampleRoot = FindBone(sample, "root");
                var jump = SampleTravel(sample, sampleRoot, controller.transform, motions[(int)MotionLabGuardian.Clip.JumpToBar]);
                var dive = SampleTravel(sample, sampleRoot, controller.transform, motions[(int)MotionLabGuardian.Clip.Dive]);
                var rise = SampleTravel(sample, sampleRoot, controller.transform, motions[(int)MotionLabGuardian.Clip.Rise]);
                Undo.RecordObject(controller, "Assign Guardian animation clips");
                controller.Configure(animator, holder, root, controller.Takeoff, motions, jump, dive, rise);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sample);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save MotionMatchingLab.");
        }

        static void RemoveLegacyCourse(Scene scene)
        {
            GameObject course = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Test Course");
            if (!course)
                return;
            Transform floor = course.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Main floor");
            if (!floor)
                throw new InvalidOperationException("The scene floor could not be separated from the old course.");
            Undo.SetTransformParent(floor, null, "Keep the floor");
            Undo.RecordObject(floor.gameObject, "Rename the floor");
            floor.name = "Ground";
            Undo.RecordObject(floor, "Expand the flat floor");
            floor.localScale = new Vector3(43f, .48f, 43f);
            Undo.DestroyObjectImmediate(course);
        }
        static AnimationClip[] ReadClips()
        {
            var all = (MotionLabGuardian.Clip[])Enum.GetValues(typeof(MotionLabGuardian.Clip));
            var result = new AnimationClip[all.Length];
            foreach (var slot in all)
            {
                string file = ClipPath(slot);
                var importer = AssetImporter.GetAtPath(file) as ModelImporter;
                if (!importer)
                    throw new InvalidOperationException("Missing animation " + file);
                bool loop = slot is MotionLabGuardian.Clip.Idle or MotionLabGuardian.Clip.Walk
                    or MotionLabGuardian.Clip.Run or MotionLabGuardian.Clip.Hang;
                var entries = importer.clipAnimations;
                if (entries.Length == 0)
                    entries = importer.defaultClipAnimations;
                if (entries.Length != 1)
                    throw new InvalidOperationException("Expected one baked take in " + file);
                bool changed = importer.animationType != ModelImporterAnimationType.Generic || importer.optimizeGameObjects
                    || importer.animationCompression != ModelImporterAnimationCompression.Off || !importer.importAnimation
                    || importer.materialImportMode != ModelImporterMaterialImportMode.None
                    || entries[0].loopTime != loop || entries[0].name != slot.ToString();
                if (changed)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    importer.optimizeGameObjects = false;
                    importer.importAnimation = true;
                    importer.animationCompression = ModelImporterAnimationCompression.Off;
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    entries[0].loopTime = loop;
                    entries[0].loopPose = false;
                    entries[0].name = slot.ToString();
                    importer.clipAnimations = entries;
                    importer.SaveAndReimport();
                }
                result[(int)slot] = AssetDatabase.LoadAllAssetsAtPath(file).OfType<AnimationClip>()
                    .Single(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal));
            }
            return result;
        }

        static string ClipPath(MotionLabGuardian.Clip clip)
        {
            string file = clip switch
            {
                MotionLabGuardian.Clip.LeftSlash => "Left_Slash",
                MotionLabGuardian.Clip.RightSlash => "Right_Hand_Sword_Slash",
                MotionLabGuardian.Clip.JumpToBar => "Jump_and_Hang_on_Bar",
                MotionLabGuardian.Clip.Hang => "Bar_Hang_Idle",
                MotionLabGuardian.Clip.Stab => "Hang_Stab_R",
                MotionLabGuardian.Clip.Dive => "Dive_Forward",
                MotionLabGuardian.Clip.Rise => "Get_Up",
                _ => clip.ToString()
            };
            return $"{Folder}/Clips/{file}.fbx";
        }

        static Transform FindBone(GameObject go, string name)
        {
            return go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)
                ?? throw new InvalidOperationException("The Guardian FBX is missing " + name);
        }

        static MotionLabGuardian.Travel SampleTravel(GameObject model, Transform root, Transform relativeTo, AnimationClip clip)
        {
            int count = Mathf.RoundToInt(clip.length * 60f) + 1;
            Vector3[] travel = new Vector3[count];
            clip.SampleAnimation(model, 0f);
            Vector3 origin = relativeTo.InverseTransformPoint(root.position);
            for (int i = 0; i < count; i++)
            {
                clip.SampleAnimation(model, clip.length * i / (count - 1));
                travel[i] = relativeTo.InverseTransformPoint(root.position) - origin;
            }
            return new MotionLabGuardian.Travel { samples = travel, duration = clip.length };
        }

        static Transform BuildStation(Vector3 center, float leftSign, Material frame, Material accent)
        {
            var root = new GameObject(StationName);
            Undo.RegisterCreatedObjectUndo(root, "Create sword hang station");
            root.transform.position = new Vector3(-1.55f, 0f, -6.75f);
            var takeoff = new GameObject("Takeoff · E").transform;
            takeoff.SetParent(root.transform, false);
            takeoff.localPosition = Vector3.up * .035f;

            const float radius = .0225f;
            Vector3 barPosition = center + Vector3.up * takeoff.localPosition.y;
            barPosition.x -= .033f * leftSign;
            Block(root.transform, "Grab bar", PrimitiveType.Cylinder, barPosition,
                new Vector3(radius * 2f, .55f, radius * 2f), Quaternion.Euler(0f, 0f, 90f), accent);
            foreach (float side in new[] { -1f, 1f })
            {
                float x = barPosition.x + side * .72f;
                float z = barPosition.z + .25f;
                Block(root.transform, "Stand", PrimitiveType.Cube, new Vector3(x, barPosition.y * .5f, z),
                    new Vector3(.085f, barPosition.y, .092f), Quaternion.identity, frame);
                Block(root.transform, "Bracket", PrimitiveType.Cube, new Vector3(x - side * .10f, barPosition.y, z - .125f),
                    new Vector3(.29f, .058f, .31f), Quaternion.identity, frame);
                Block(root.transform, "Stand foot", PrimitiveType.Cube, new Vector3(x, .011f, z),
                    new Vector3(.21f, .022f, .36f), Quaternion.identity, frame);
                Block(root.transform, "Takeoff side", PrimitiveType.Cube, new Vector3(side * .40f, .008f, -.02f),
                    new Vector3(.035f, .016f, .74f), Quaternion.identity, accent);
            }
            Block(root.transform, "Takeoff line", PrimitiveType.Cube, new Vector3(0, .008f, .35f),
                new Vector3(.83f, .016f, .041f), Quaternion.identity, accent);
            return takeoff;
        }

        static void Block(Transform parent, string name, PrimitiveType primitive, Vector3 position,
            Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject item = GameObject.CreatePrimitive(primitive);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.SetLocalPositionAndRotation(position, rotation);
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
        }

        static Material CreateArmorMaterial(string name, string color, string normal, string mask)
        {
            Material material = MaterialFor(name, Color.white, .4f);
            Texture2D albedo = ImportTexture(color, true, false);
            Texture2D normals = ImportTexture(normal, false, true);
            Texture2D packed = ImportTexture(mask, false, false);
            if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTexture("_BaseColorMap", albedo);
                material.SetTexture("_NormalMap", normals);
                material.SetTexture("_MaskMap", packed);
                material.EnableKeyword("_NORMALMAP");
                material.EnableKeyword("_TANGENT_SPACE_NORMALMAP");
                material.EnableKeyword("_MASKMAP");
            }
            else
            {
                material.SetTexture("_MainTex", albedo);
                material.SetTexture("_BumpMap", normals);
                material.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D ImportTexture(string file, bool srgb, bool normal)
        {
            string path = Folder + "/Textures/" + file;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type || importer.sRGBTexture != srgb)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material MaterialFor(string name, Color color, float smoothness)
        {
            string path = $"{Folder}/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var shader = Shader.Find(GraphicsSettings.currentRenderPipeline ? "HDRP/Lit" : "Standard");
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", color);
            else
                m.color = color;
            if (m.HasProperty("_Smoothness"))
                m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}

