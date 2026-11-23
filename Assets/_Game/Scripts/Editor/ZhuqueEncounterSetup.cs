using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TanShadow.Seasons;
using TanShadow.Spring;
using TanShadow.Summer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TanShadow.EditorTools
{
    // Расставляет испытание Чжуцюэ в сцене Summer_Zhuque_01_Dry поверх грейбокса SummerCalderaBuilder (KaVQ).
    // Лавовые реки переносятся из Summer_Zhuque_02_Lava в эту же сцену выключенной группой: одна сцена,
    // два состояния арены. Сцену 02_Lava скрипт не меняет.
    // Можно запускать повторно: объект «Zhuque encounter» пересоздаётся, ассеты настроек не перезаписываются.
    public static class ZhuqueEncounterSetup
    {
        const string ScenePath = "Assets/_Game/Scenes/Summer_Zhuque_01_Dry.unity";
        const string LavaScenePath = "Assets/_Game/Scenes/Summer_Zhuque_02_Lava.unity";
        const string DataDir = "Assets/_Game/Data/Summer";
        const string ArtDir = "Assets/_Game/Art/SummerBlockout/";
        const string RootName = "Summer - Zhuque";
        const string LavaName = "07 - Open lava rivers and heat";
        const string EncounterName = "Zhuque encounter";

        [MenuItem("Tools/Tan Shadow/Summer/Setup Zhuque Encounter")]
        public static void SetupFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply();
        }

        // Для запуска без интерфейса: Unity -batchmode -executeMethod TanShadow.EditorTools.ZhuqueEncounterSetup.Apply -quit
        public static void Apply()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName)?.transform;
            if (root == null) throw new System.InvalidOperationException("Не найден корень «" + RootName + "» в " + ScenePath);

            var tuning = LoadOrCreate<ZhuqueTuning>(DataDir + "/ZhuqueTuning.asset");
            var bank = LoadOrCreate<ZhuqueSoundBank>(DataDir + "/ZhuqueSoundBank.asset");

            var lavaGroup = root.Find(LavaName) ?? BringLavaFrom(LavaScenePath, root);
            var rivers = lavaGroup.GetComponentsInChildren<MeshCollider>(true)
                .Where(c => c.name.StartsWith("Molten river")).OrderBy(c => c.name).ToArray();
            var dryBeds = root.Find("01 - Ash ring and volcanic rim/Dry ash across the five future channels");
            if (rivers.Length == 0 || dryBeds == null) throw new System.InvalidOperationException("Не найдены реки или сухие русла");
            lavaGroup.gameObject.SetActive(false);
            dryBeds.gameObject.SetActive(true);
            FillCrossingGaps(root.Find("04 - Crossing stones"));

            // Пересоздаём объект испытания.
            foreach (var old in root.Cast<Transform>().Where(t => t.name == EncounterName).ToArray())
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(EncounterName);
            go.transform.SetParent(root, false);

            // Героиня и камера.
            var heroGo = root.GetComponentInChildren<TanShadow.Player.PlayerController>(true).gameObject;
            var bridge = heroGo.GetComponent<LegacyHeroBridge>();
            if (bridge == null) bridge = heroGo.AddComponent<LegacyHeroBridge>();
            // Прыжков нет ни в одном сезоне (GDD-00 §03, I-1); летом рывок — главный способ сократить время на виду.
            var player = heroGo.GetComponent<TanShadow.Player.PlayerController>();
            if (player.jumpSpeed != 0f)
            {
                player.jumpSpeed = 0f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            }
            var view = root.GetComponentInChildren<SpringFollowCamera>(true).GetComponent<Camera>();

            // Птица: оснастка поверх прокси из билдера.
            var birdRoot = root.Find("05 - Perched Zhuque - scale proxy");
            var bird = birdRoot.GetComponent<ZhuqueBird>();
            if (bird == null) bird = birdRoot.gameObject.AddComponent<ZhuqueBird>();

            var lava = go.AddComponent<ZhuqueLava>();
            lava.tuning = tuning;
            lava.hero = bridge;
            lava.dryBeds = dryBeds.gameObject;
            lava.lavaRoot = lavaGroup.gameObject;
            lava.rivers = rivers;

            var cover = go.AddComponent<YinYangCover>();

            var world = go.AddComponent<DeadWorld>();
            world.worldRoots = new[] { root };
            world.exclude = new[] { heroGo.transform, view.transform, go.transform };
            world.greySource = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "Ash sand.mat");
            world.aliveOnly = lavaGroup.GetComponentsInChildren<ParticleSystem>(true).Select(p => p.gameObject).ToArray();

            var perchRoot = root.Find("03 - Basalt cover and perches");
            var encounter = go.AddComponent<ZhuqueEncounter>();
            encounter.tuning = tuning;
            encounter.hero = bridge;
            encounter.bird = bird;
            encounter.lava = lava;
            encounter.perchRoot = perchRoot;
            encounter.firstPerch = null;   // стартовый валун выбирается по ZhuqueTuning.startPerchDistance
            encounter.flightRing = BuildFlightRing();
            encounter.sun = root.Find("06 - Caldera daylight/Afternoon sun")?.GetComponent<Light>();
            encounter.cover = cover;
            encounter.world = world;

            var sounds = go.AddComponent<ZhuqueSounds>();
            sounds.encounter = encounter;
            sounds.cover = cover;
            sounds.bank = bank;

            var feedback = go.AddComponent<ZhuqueFeedback>();
            feedback.encounter = encounter;
            feedback.view = view;

            var flightCamera = go.AddComponent<ZhuqueFlightCamera>();
            flightCamera.encounter = encounter;
            flightCamera.view = view;

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("Не удалось сохранить " + ScenePath);
            Debug.Log("[ZhuqueEncounterSetup] Испытание Чжуцюэ расставлено в " + ScenePath);
        }

        // Переносит группу рек из сцены с лавой в сухую сцену. Сцена с лавой закрывается без сохранения.
        static Transform BringLavaFrom(string path, Transform dryRoot)
        {
            var lavaScene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var lavaRoot = lavaScene.GetRootGameObjects().First(g => g.name == RootName).transform;
            var group = lavaRoot.Find(LavaName);
            if (group == null) throw new System.InvalidOperationException("Не найдена группа «" + LavaName + "» в " + path);
            // Опасность лавы теперь ведёт ZhuqueLava: телепорт SummerLava здесь не нужен.
            foreach (var old in group.GetComponents<SummerLava>()) Object.DestroyImmediate(old);
            group.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(group.gameObject, dryRoot.gameObject.scene);
            group.SetParent(dryRoot, true);
            EditorSceneManager.CloseScene(lavaScene, true);
            return group;
        }

        // Камни-переправы билдера стоят с зазором ≈ 1 м: в лавовой фазе в зазоре лава, и без прыжка (а прыжков нет,
        // GDD-00 §03) героиня проваливается. Между соседними камнями ставим такой же камень — получается сплошная полоса.
        // Повторный запуск сначала убирает добавленные камни.
        static void FillCrossingGaps(Transform crossings)
        {
            if (crossings == null) return;
            foreach (Transform crossing in crossings)
            {
                foreach (var added in crossing.Cast<Transform>().Where(t => t.name.StartsWith("Foothold gap")).ToArray())
                    Object.DestroyImmediate(added.gameObject);
                var stones = crossing.Cast<Transform>().OrderBy(t => t.name).ToArray();
                for (int i = 0; i + 1 < stones.Length; i++)
                {
                    Transform a = stones[i], b = stones[i + 1];
                    var gap = Object.Instantiate(a.gameObject, crossing).transform;
                    gap.name = "Foothold gap " + (i + 1);
                    gap.position = Vector3.Lerp(a.position, b.position, 0.5f);
                    // Шире соседей: полоса прощает неточный шаг (проверено проходом со смещением ±0,5 м от оси).
                    gap.localScale = Vector3.Scale(Vector3.Lerp(a.localScale, b.localScale, 0.5f), new Vector3(1.45f, 0.95f, 1.45f));
                    gap.rotation = Quaternion.Euler(0f, a.eulerAngles.y + 61f, 0f);
                }
            }
        }

        // Кольцо полёта — по середине дна долины. Формы берём у билдера KaVQ, чтобы кольцо совпало с геометрией.
        static Vector3[] BuildFlightRing()
        {
            var builder = typeof(SummerCalderaBuilder);
            const BindingFlags any = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            var inner = builder.GetMethod("Inner", any);
            var outer = builder.GetMethod("Outer", any);
            var ground = builder.GetMethod("Ground", any);
            if (inner == null || outer == null || ground == null) return null;

            var points = new List<Vector3>();
            for (int i = 0; i < 72; i++)
            {
                float a = i * 5f;
                float r = ((float)inner.Invoke(null, new object[] { a }) + (float)outer.Invoke(null, new object[] { a })) * 0.5f;
                float x = r * Mathf.Cos(a * Mathf.Deg2Rad), z = r * Mathf.Sin(a * Mathf.Deg2Rad);
                points.Add(new Vector3(x, (float)ground.Invoke(null, new object[] { x, z }), z));
            }
            return points.ToArray();
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
