using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace TanShadow.MotionMatchingLab.Editor
{
    public static class MotionLabBuilder
    {
        public const string ScenePath = MotionLibraryBaker.Folder + "/MotionMatchingLab.unity";
        const string DataFolder = MotionLibraryBaker.Folder + "/Data";

        [MenuItem("Tools/Tan Shadow/Motion Matching Lab/Rebuild Pose Database")]
        public static void RebuildDatabase()
        {
            PrepareDataFolder();
            var database = MotionLibraryBaker.Bake(LoadMannequin());
            Debug.Log($"Motion Matching Lab: baked {database.Clips.Length} clips and {database.Frames.Length} pose records.");
        }

        [MenuItem("Tools/Tan Shadow/Motion Matching Lab/Rebuild Aerial Roll")]
        public static void RebuildAerialRoll()
        {
            MotionDatabase database = AssetDatabase.LoadAssetAtPath<MotionDatabase>(MotionLibraryBaker.DatabasePath);
            if (!database)
                throw new InvalidOperationException("Bake the pose database before building the aerial roll.");
            AnimationClip roll = AerialRollBaker.Bake(LoadMannequin(), database);
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
                return;

            bool wasDirty = scene.isDirty;
            foreach (GameObject item in scene.GetRootGameObjects())
            {
                var player = item.GetComponentInChildren<MotionMatchingPlayer>();
                if (!player)
                    continue;
                Undo.RecordObject(player, "Assign Aerial Roll");
                player.SetAerialRoll(roll);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!wasDirty && !EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save the aerial roll assignment.");
                break;
            }
        }

        [MenuItem("Tools/Tan Shadow/Motion Matching Lab/Create Test Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new InvalidOperationException("MotionMatchingLab.unity already exists. Use Rebuild Pose Database to update its clips.");

            PrepareDataFolder();
            GameObject model = LoadMannequin();
            MotionDatabase database = MotionLibraryBaker.Bake(model);
            AnimationClip roll = AerialRollBaker.Bake(model, database);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                Populate(scene, model, database, roll);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Unity did not save " + ScenePath);
            }
            catch
            {
                if (scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                throw;
            }
            Debug.Log($"Motion Matching Lab ready: {database.Clips.Length} clips, {database.Frames.Length} poses. {ScenePath}");
        }

        static void PrepareDataFolder()
        {
            if (!AssetDatabase.IsValidFolder(DataFolder))
                AssetDatabase.CreateFolder(MotionLibraryBaker.Folder, "Data");
        }

        static GameObject LoadMannequin()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(MotionLibraryBaker.RigPath);
            return model ? model : throw new InvalidOperationException("The imported mannequin is missing: " + MotionLibraryBaker.RigPath);
        }

        static void Populate(Scene scene, GameObject model, MotionDatabase database, AnimationClip roll)
        {
            Material asphalt = MaterialFor("Asphalt", new Color(.21f, .245f, .30f));
            Material border = MaterialFor("Slate", new Color(.08f, .12f, .19f));
            Material marking = MaterialFor("Lane Marker", new Color(.41f, .50f, .58f));
            Material body = MaterialFor("Mannequin", new Color(.90f, .79f, .57f), .42f);
            Color[] colors = {
                new Color(.34f, .82f, .74f), new Color(.54f, .69f, .98f),
                new Color(.94f, .66f, .27f), new Color(.91f, .43f, .37f),
                new Color(.77f, .60f, .96f), new Color(.89f, .84f, .43f)
            };
            string[] caps = { "Hurdle", "Vault", "Mantle", "Climb", "Catch", "Gap jump" };
            Material[] accents = new Material[caps.Length];
            for (int i = 0; i < caps.Length; i++)
                accents[i] = MaterialFor(caps[i], colors[i]);

            var course = new GameObject("Test Course");
            var pavement = new GameObject("Floor and lane markings").transform;
            pavement.SetParent(course.transform, false);
            Block("Main floor", pavement, new Vector3(0f, -.24f, 1f), new Vector3(20.2f, .48f, 29f), asphalt);
            Block("Gap approach", pavement, new Vector3(12.35f, -.24f, -6.85f), new Vector3(4.42f, .48f, 13.3f), asphalt);
            Block("Gap landing", pavement, new Vector3(12.35f, -.24f, 8.15f), new Vector3(4.42f, .48f, 12.5f), asphalt);
            Block("Pit floor", pavement, new Vector3(12.35f, -2.65f, .85f), new Vector3(4.42f, .5f, 4.8f), border);
            Block("West barrier", pavement, new Vector3(-10.5f, .48f, 1f), new Vector3(.48f, .96f, 29f), border);
            Block("Back barrier", pavement, new Vector3(2.0f, .44f, 15.6f), new Vector3(24.6f, .88f, .46f), border);
            for (int i = -2; i <= 2; i++)
            {
                float lane = i * 4f + 2f;
                if (lane < 10f)
                    Block("Lane separator", pavement, new Vector3(lane, .006f, .8f), new Vector3(.035f, .01f, 20f), marking, false);
            }

            var stationsRoot = new GameObject("Station Spawn Points").transform;
            stationsRoot.SetParent(course.transform, false);
            var signRoot = new GameObject("Course Labels").transform;
            signRoot.SetParent(course.transform, false);
            var obstacles = new GameObject("Obstacles").transform;
            obstacles.SetParent(course.transform, false);
            float[] lanes = { -8f, -4f, 0f, 4f, 8f, 12.35f };
            var stations = new Transform[lanes.Length];
            var signs = new Transform[lanes.Length];
            var signNames = new string[lanes.Length];
            for (int i = 0; i < lanes.Length; i++)
            {
                float x = lanes[i], floorY = i == 4 ? 2.05f : 0f;
                stations[i] = Anchor(caps[i], stationsRoot, new Vector3(x, floorY + .04f, -5.4f));
                float stripeZ = i == 5 ? -.26f : (i == 4 ? -2.02f : -2.2f);
                Block(caps[i] + " takeoff mark", pavement, new Vector3(x, floorY + .008f, stripeZ),
                    new Vector3(2.9f, .012f, .12f), accents[i], false);
                signNames[i] = (i + 1) + "  " + (i == 5 ? "Gap edge" : caps[i]);
            }

            var hurdle = Obstacle(obstacles, "Hurdle rail", -8f, -.25f, .93f, .30f, accents[0], MotionKind.Hurdle);
            var vault = Obstacle(obstacles, "Vault wall", -4f, -.25f, 1.0f, .39f, accents[1], MotionKind.Vault);
            var mantle = Obstacle(obstacles, "Mantle deck", 0f, .61f, 1.05f, 2.35f, accents[2], MotionKind.Mantle);
            var climb = Obstacle(obstacles, "Climb block", 4f, .72f, 2.50f, 2.55f, accents[3], MotionKind.Climb);
            Block("Catch launch deck", obstacles, new Vector3(8f, 1.025f, -4.35f), new Vector3(3f, 2.05f, 5f), border);
            var catchWall = Obstacle(obstacles, "Airborne catch ledge", 8f, 1.62f, 3.1f, 2.8f, accents[4], MotionKind.Climb, true);
            Block("Gap lip", pavement, new Vector3(12.35f, .008f, 2.0f), new Vector3(4.25f, .012f, .16f), accents[5], false);

            MotionObstacle[] targets = { hurdle, vault, mantle, climb, catchWall };
            for (int i = 0; i < targets.Length; i++)
            {
                BoxCollider volume = targets[i].Volume;
                Vector3 aboveFront = volume.center + new Vector3(0f, volume.size.y * .5f, -volume.size.z * .5f);
                signs[i] = Anchor(signNames[i], signRoot, volume.transform.TransformPoint(aboveFront) + Vector3.up * .42f);
            }
            signs[5] = Anchor(signNames[5], signRoot, new Vector3(lanes[5], 1.25f, 1.5f));

            var player = new GameObject("Motion Matching Player");
            player.transform.position = new Vector3(-1.55f, .04f, -9.5f);
            var motor = player.AddComponent<CharacterController>();
            motor.height = 1.76f;
            motor.center = new Vector3(0f, .88f, 0f);
            motor.radius = .31f;
            motor.stepOffset = .28f;
            motor.slopeLimit = 49f;
            motor.skinWidth = .035f;
            motor.minMoveDistance = 0f;

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            rig.name = "UEFN Mannequin";
            rig.transform.SetParent(player.transform, false);
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                Material[] materials = skin.sharedMaterials;
                Array.Fill(materials, body);
                skin.sharedMaterials = materials;
                skin.updateWhenOffscreen = true;
            }
            var animator = rig.GetComponent<Animator>();
            if (!animator)
                animator = rig.AddComponent<Animator>();
            animator.applyRootMotion = false;
            var controller = player.AddComponent<MotionMatchingPlayer>();
            controller.Configure(database, animator, MotionLibraryBaker.Bone(rig, "root"),
                MotionLibraryBaker.Bone(rig, "pelvis"), MotionLibraryBaker.Bone(rig, "foot_l"), MotionLibraryBaker.Bone(rig, "foot_r"), roll);

            var view = new GameObject("Third-person Camera");
            view.tag = "MainCamera";
            view.transform.SetPositionAndRotation(player.transform.position + new Vector3(.57f, 2.63f, -4.43f),
                Quaternion.Euler(16f, -2f, 0f));
            var camera = view.AddComponent<Camera>();
            camera.fieldOfView = 57f;
            camera.nearClipPlane = .08f;
            camera.farClipPlane = 160f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .135f, .22f);
            view.AddComponent<AudioListener>();
            if (GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)
            {
                var settings = view.AddComponent<HDAdditionalCameraData>();
                settings.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                settings.backgroundColorHDR = camera.backgroundColor;
            }
            var cameraRig = view.AddComponent<MotionLabView>();
            cameraRig.player = controller;
            cameraRig.stations = stations;
            cameraRig.signs = signs;
            cameraRig.signNames = signNames;

            BuildLighting();
        }

        static void BuildLighting()
        {
            bool highDefinition = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset;
            Directional("Key light", new Vector3(43f, -36f, 0f), new Color(1f, .92f, .81f), highDefinition ? 4000f : 1.25f, true);
            Directional("Fill light", new Vector3(24f, 146f, 0f), new Color(.68f, .81f, 1f), highDefinition ? 1050f : .55f, false);
            if (!highDefinition)
                return;

            var profilePath = DataFolder + "/Lab Lighting.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
                var exposure = profile.Add<Exposure>(false);
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(9f);
                AssetDatabase.AddObjectToAsset(exposure, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            var volume = new GameObject("Exposure").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static void Directional(string label, Vector3 euler, Color color, float intensity, bool shadows)
        {
            var light = new GameObject(label).AddComponent<Light>();
            light.type = LightType.Directional;
            if (GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)
            {
                if (!light.GetComponent<HDAdditionalLightData>())
                    light.gameObject.AddComponent<HDAdditionalLightData>();
                light.lightUnit = LightUnit.Lux;
            }
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.transform.rotation = Quaternion.Euler(euler);
        }

        static MotionObstacle Obstacle(Transform parent, string name, float x, float z, float height, float depth,
            Material material, MotionKind kind, bool airOnly = false)
        {
            var block = Block(name, parent, new Vector3(x, height * .5f, z), new Vector3(2.95f, height, depth), material);
            var action = block.AddComponent<MotionObstacle>();
            action.kind = kind;
            action.airCatchOnly = airOnly;
            action.caption = name;
            return action;
        }

        static Transform Anchor(string label, Transform parent, Vector3 position)
        {
            Transform anchor = new GameObject(label).transform;
            anchor.SetParent(parent, false);
            anchor.position = position;
            return anchor;
        }

        static GameObject Block(string label, Transform parent, Vector3 position, Vector3 scale, Material appearance, bool collision = true)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = label;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = appearance;
            if (!collision)
                UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            return block;
        }

        static Material MaterialFor(string label, Color color, float smoothness = .18f)
        {
            string path = DataFolder + "/" + label + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material)
                return material;
            string shader = GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset ? "HDRP/Lit"
                : (GraphicsSettings.currentRenderPipeline ? "Universal Render Pipeline/Lit" : "Standard");
            material = new Material(Shader.Find(shader)) { name = label };
            material.color = color;
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
