using System.IO;
using System.Linq;
using TanShadow.Autumn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Расставляет испытание Байху в сцене Autumn_Baihu поверх грейбокса AutumnBlockoutBuilder.
    // Можно запускать повторно: старый объект «Baihu encounter» пересоздаётся, ассеты настроек не перезаписываются.
    public static class BaihuEncounterSetup
    {
        const string ScenePath = "Assets/_Game/Scenes/Autumn_Baihu.unity";
        const string DataDir = "Assets/_Game/Data/Autumn";
        const string ArtDir = "Assets/_Game/Art/AutumnBlockout/";
        const string EncounterName = "Baihu encounter";

        [MenuItem("Tools/Tan Shadow/Autumn/Setup Baihu Encounter")]
        public static void SetupFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply();
        }

        // Для запуска без интерфейса: Unity -batchmode -executeMethod TanShadow.EditorTools.BaihuEncounterSetup.Apply -quit
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var trial = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<AutumnTrial>(true)).FirstOrDefault(t => t);
            if (trial == null) throw new System.InvalidOperationException("AutumnTrial не найден в " + ScenePath);
            var root = trial.transform;

            var tuning = LoadOrCreate<BaihuTuning>(DataDir + "/BaihuTuning.asset");
            var bank = LoadOrCreate<BaihuSoundBank>(DataDir + "/BaihuSoundBank.asset");

            // Пересоздаём объект испытания.
            foreach (var old in root.Cast<Transform>().Where(t => t.name == EncounterName).ToArray())
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(EncounterName);
            go.transform.SetParent(root, false);

            // Мост к героине.
            var heroGo = trial.hero.gameObject;
            var bridge = heroGo.GetComponent<LegacyHeroBridge>();
            if (bridge == null) bridge = heroGo.AddComponent<LegacyHeroBridge>();

            // Арена и столб.
            var arena = root.Find("04 · Sand arena and iron monument");
            if (arena == null) throw new System.InvalidOperationException("Не найдена арена «04 · Sand arena and iron monument»");
            var shaft = arena.GetComponentInChildren<BoxCollider>(true);
            var monumentRoot = shaft.transform.parent;
            var monument = monumentRoot.GetComponent<BaihuMonument>();
            if (monument == null) monument = monumentRoot.gameObject.AddComponent<BaihuMonument>();
            monument.shaftCollider = shaft;
            monument.crackMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Basal fractures.mat");
            monument.stages = tuning.requiredHits;

            // Маркер начала охоты: героиня сбежала с насыпи и немного прошла по тропе.
            var activation = new GameObject("Trail activation (hunt starts here)").transform;
            activation.SetParent(go.transform, false);
            activation.position = new Vector3(arena.position.x, 0f, 36f);

            var cover = go.AddComponent<YinYangCover>();

            var world = go.AddComponent<AutumnWorldState>();
            world.worldRoots = new[] { root };
            world.exclude = new[] { heroGo.transform, trial.view.transform, go.transform };
            world.greySource = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Ash concrete.mat");

            var encounter = go.AddComponent<BaihuEncounter>();
            encounter.tuning = tuning;
            encounter.hero = bridge;
            encounter.heroSpawn = trial.spawn;
            encounter.trailActivation = activation;
            encounter.arenaCenter = arena;
            encounter.arenaRadius = 36f;
            encounter.arenaEntranceAngle = -90f;
            encounter.tigerBody = trial.tiger;

            // Часть морды с глазами, выглядывающая из травы.
            var peekGo = new GameObject("Baihu peek (muzzle + eyes)");
            peekGo.transform.SetParent(go.transform, false);
            var peek = peekGo.AddComponent<BaihuPeek>();
            peek.peltMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Porcelain pelt.mat");
            peek.stripeMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Basal fractures.mat");
            peek.eyesMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Red eyes.mat");
            peek.grassMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Dry grass.mat");
            // Тёмная масса позади морды: тот же тёмный материал, что у полос, чтобы тело «тонуло» в тени зарослей.
            peek.shadowMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Basal fractures.mat");
            peek.eyesHeightAboveGround = tuning.eyesHeight;
            encounter.peek = peek;
            encounter.monument = monument;
            encounter.cover = cover;
            encounter.world = world;

            var sounds = go.AddComponent<BaihuSounds>();
            sounds.encounter = encounter;
            sounds.cover = cover;
            sounds.bank = bank;

            var feedback = go.AddComponent<BaihuFeedback>();
            feedback.encounter = encounter;
            feedback.view = trial.view;

            EditorUtility.SetDirty(trial);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("Не удалось сохранить " + ScenePath);
            Debug.Log("[BaihuEncounterSetup] Испытание Байху расставлено в " + ScenePath);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.Refresh();
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
