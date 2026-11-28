using System;
using System.Linq;
using ShadowTan.Gameplay.Autumn;
using ShadowTan.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    public static class AutumnBaihuPort
    {
        const string VfxAssets = "Assets/_Project/Art/Environment/Autumn/Location/Generated/VFX";
        const string Output = "Assets/_Project/Art/Environment/Autumn/Location/Generated/ArenaPort";

        [MenuItem("ShadowTan/Setup/Art/Port Autumn Baihu")]
        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != AutumnVfxSetup.ArtScene)
                throw new InvalidOperationException("Open Autumn_Gameplay in Edit Mode.");

            var roots = scene.GetRootGameObjects();
            var all = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var player = all.Select(t => t.GetComponent<PlayerController>()).Single(x => x);
            var view = all.Single(t => t.name == "Entrance_PlayerCamera").GetComponent<Camera>();
            var targets = roots.Single(r => r.name == "[Encounter]").transform;
            var center = targets.Find("Targets/Monument");
            var respawn = roots.Single(r => r.name == "[Flow]").transform.Find("RespawnPoint_Entrance");
            var monumentRoot = all.Single(t => t.name == "SM_Monument");
            var shaft = monumentRoot.GetComponent<BoxCollider>();
            var art = monumentRoot.GetComponentInChildren<MeshRenderer>();
            var vfx = all.Select(t => t.GetComponent<AutumnVfxDirector>()).Single(c => c);
            var windAudio = all.Select(t => t.GetComponent<AutumnWindAudio>()).Single(c => c);
            if (!center || !respawn || !shaft || !art || !art.sharedMaterial)
                throw new InvalidOperationException("Autumn art geometry or marker is missing.");

            var source = EditorSceneManager.OpenPreviewScene(AutumnVfxSetup.BattleScene);
            try
            {
                var sourceBehaviours = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
                var original = sourceBehaviours.Single(c => c && c.GetType().FullName == "TanShadow.Autumn.BaihuEncounter");
                var originalData = new SerializedObject(original);
                var oldTiger = Ref<Transform>(originalData, "tigerBody");
                var oldPeek = Ref<Component>(originalData, "peek");
                var oldMonument = Ref<Component>(originalData, "monument");
                if (!oldTiger || !oldPeek || !oldMonument || !Ref<Object>(originalData, "tuning"))
                    throw new InvalidOperationException("Source encounter has missing presentation or tuning.");

                var parent = AutumnVfxSetup.FindOrCreate(targets, "Baihu");
                var trigger = AutumnVfxSetup.FindOrCreate(parent, "Trail activation");
                trigger.position = new Vector3(center.position.x, 0f, 24f);
                var clone = CloneOnce(oldTiger, parent, "Baihu body");
                clone.SetPositionAndRotation(center.position + Vector3.forward * 28f, Quaternion.identity);
                var bodyBounds = clone.GetComponentInChildren<Renderer>(true).bounds;
                int colossus = LayerMask.NameToLayer("Colossus");
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = colossus;
                var proxy = AutumnVfxSetup.Component<BoxCollider>(clone);
                proxy.center = clone.InverseTransformPoint(bodyBounds.center);
                proxy.size = bodyBounds.size;
                proxy.isTrigger = true;
                clone.gameObject.SetActive(false);

                var peek = CloneOnce(oldPeek.transform, parent, "Baihu peek").GetComponent(oldPeek.GetType());
                var cover = CopyOnce(Ref<Component>(originalData, "cover"), parent);
                var world = CopyOnce(Ref<Component>(originalData, "world"), parent);
                var monument = CopyOnce(oldMonument, monumentRoot);
                var encounter = GetOrAdd(parent, original.GetType());
                var hero = GetOrAdd(player.transform, FindType("TanShadow.Seasons.BaihuHeroBridge"));
                var orbit = BindCamera(view, player, all);
                AutumnVfxSetup.Set(hero, so => Set(so, "cameraRig", orbit));

                BindMonument(monument, shaft, art);
                AutumnVfxSetup.Set(encounter, so =>
                {
                    Set(so, "tuning", Ref<Object>(originalData, "tuning"));
                    Set(so, "hero", hero); Set(so, "heroSpawn", respawn);
                    Set(so, "trailActivation", trigger); Set(so, "arenaCenter", center);
                    Set(so, "tigerBody", clone); Set(so, "peek", peek);
                    Set(so, "monument", monument); Set(so, "cover", cover); Set(so, "world", world);
                    so.FindProperty("arenaRadius").floatValue = 26f;
                    so.FindProperty("arenaEntranceAngle").floatValue = -90f;
                    so.FindProperty("tigerBodyYawOffset").floatValue = originalData.FindProperty("tigerBodyYawOffset").floatValue;
                    so.FindProperty("showDebug").boolValue = originalData.FindProperty("showDebug").boolValue;
                    so.FindProperty("eyesOnlyInEyesPhase").boolValue = true;
                });

                var sounds = CopyOnce(original.GetComponent(FindType("TanShadow.Autumn.BaihuSounds")), parent);
                AutumnVfxSetup.Set(sounds, so =>
                {
                    Set(so, "encounter", encounter); Set(so, "cover", cover); Set(so, "sceneAudio", windAudio);
                });
                var feedback = CopyOnce(original.GetComponent(FindType("TanShadow.Autumn.BaihuFeedback")), parent);
                AutumnVfxSetup.Set(feedback, so => { Set(so, "encounter", encounter); Set(so, "view", view); });
                AutumnVfxSetup.BindBattle(encounter, vfx, false);
                AutumnVfxSetup.Set(world, so =>
                {
                    Array(so, "worldRoots", new Object[] { roots.Single(r => r.name == "[Level]").transform });
                    Array(so, "exclude", new Object[] { player.transform });
                    Array(so, "aliveOnly", new Object[] { clone.gameObject, peek.gameObject });
                });
            }
            finally { EditorSceneManager.ClosePreviewScene(source); }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        static AutumnPlayerCamera BindCamera(Camera view, PlayerController player, Transform[] all)
        {
            foreach (var camera in all.Select(t => t.GetComponent<Camera>()).Where(c => c))
            {
                Undo.RecordObject(camera, "Autumn camera range");
                camera.farClipPlane = 600f;
            }
            var entrance = view.GetComponents<MonoBehaviour>().Single(c => c && c.GetType().Name == "SeasonEntranceView");
            Undo.RecordObject(entrance, "Use autumn orbit");
            entrance.enabled = false;
            var orbit = AutumnVfxSetup.Component<AutumnPlayerCamera>(view.transform);
            var input = player.GetComponent<PlayerInputReader>();
            AutumnVfxSetup.Set(orbit, so =>
            {
                Set(so, "target", player.transform); Set(so, "input", input);
                so.FindProperty("obstacles").intValue = LayerMask.GetMask("Default", "Ground", "Wall");
                Array(so, "reviewCameras", all.Select(t => t.GetComponent<Camera>()).Where(c => c && c != view).ToArray());
            });
            AutumnVfxSetup.Set(input, so => Set(so, "cameraTransform", view.transform));
            return orbit;
        }

        static void BindMonument(Component monument, BoxCollider shaft, MeshRenderer art)
        {
            EnsureOutput();
            foreach (var t in monument.GetComponentsInChildren<Transform>(true))
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(t.gameObject);
                var moving = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI;
                if ((flags & moving) == 0) continue;
                Undo.RecordObject(t.gameObject, "Allow monument movement");
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags & ~moving);
            }
            var stages = new Material[4];
            for (int i = 0; i < stages.Length; i++)
            {
                string path = $"{Output}/M_Autumn_ArtMonument_{i}.mat";
                stages[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (stages[i]) continue;
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>($"{VfxAssets}/T_Autumn_MonumentCrack_{i}_Mask.asset");
                if (!mask) throw new InvalidOperationException($"Missing monument stage {i} mask.");
                var finish = new Material(art.sharedMaterial);
                finish.SetTexture("_BaseColorMap", mask);
                HDMaterial.ValidateMaterial(finish);
                AssetDatabase.CreateAsset(finish, path);
                stages[i] = finish;
            }
            AutumnVfxSetup.Set(monument, so =>
            {
                Set(so, "shaftCollider", shaft); Set(so, "artShaft", art);
                Array(so, "crackStages", stages);
                Set(so, "damagedShaft", null);
            });
            Undo.RecordObject(art, "Set authored monument visual stage");
            art.sharedMaterial = stages[0];
        }

        internal static void EnsureOutput()
        {
            string parent = System.IO.Path.GetDirectoryName(Output).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(Output)) AssetDatabase.CreateFolder(parent, "ArenaPort");
        }

        static Transform CloneOnce(Transform source, Transform parent, string name)
        {
            var target = parent.Find(name);
            if (target) return target;
            target = Object.Instantiate(source, parent, false);
            target.name = name;
            Undo.RegisterCreatedObjectUndo(target.gameObject, "Port " + name);
            return target;
        }

        static Component CopyOnce(Component source, Transform parent)
        {
            if (!source) throw new InvalidOperationException($"Source component missing on {parent.name}.");
            var target = parent.GetComponent(source.GetType());
            if (target) return target;
            target = Undo.AddComponent(parent.gameObject, source.GetType());
            EditorUtility.CopySerialized(source, target);
            return target;
        }

        static Component GetOrAdd(Transform parent, Type type)
        {
            var target = parent.GetComponent(type);
            return target ? target : Undo.AddComponent(parent.gameObject, type);
        }

        static Type FindType(string name) => TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Single(t => t.FullName == name);
        static T Ref<T>(SerializedObject value, string name) where T : Object => value.FindProperty(name).objectReferenceValue as T;
        static void Set(SerializedObject value, string name, Object component) => value.FindProperty(name).objectReferenceValue = component;
        static void Array(SerializedObject value, string name, Object[] objects)
        {
            var list = value.FindProperty(name);
            list.arraySize = objects.Length;
            for (int i = 0; i < objects.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = objects[i];
        }
    }
}
