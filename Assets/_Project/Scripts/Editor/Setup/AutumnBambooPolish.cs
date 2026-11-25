using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    // Полировка осенней бамбуковой рощи: густая кромка вдоль тропы и вокруг арены,
    // тёплые цвета листвы и чистка дублей каменного декора.
    //
    // Запускать после Build Autumn Location / Dress Autumn Location / Build Autumn Barriers.
    // Скрипт не пересобирает сцену: добавляет бамбук в существующий контейнер,
    // правит только _BaseColor материалов бамбука и удаляет дубли StoneDecor*.
    //
    // Допущения:
    //  - Земля плоская, y=0 (Terrain). Бамбук ставится пивотом на y=0.
    //  - Префабы Bamboo_Giant_A/B/C уже содержат LOD-меши и материалы; скрипт их не трогает.
    //  - Цвета _BaseColor заданы в линейном пространстве: HDRP/Lit читает свойство
    //    как линейный цвет, поэтому конверсия .linear здесь не применяется.
    //  - Повторный запуск удаляет бамбук прошлого прогона по префиксу имени BambooEdge_,
    //    поэтому сцена не накапливает дубли. Чужой бамбук не затрагивается.
    //  - Барьеры, трава, Terrain и декали не изменяются.
    public static class AutumnBambooPolish
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string BambooPrefabRoot = "Assets/_Project/Prefabs/Environment/Autumn";
        const string BambooMaterialRoot = "Assets/_Project/Art/Environment/Autumn/Bamboo/Materials";

        const string LevelRootName = "[Level]";
        const string PropsPath = "Greybox/Props";
        const string BambooRootName = "Bamboo";

        // Геометрия из AutumnSceneBuilder: менять только вместе с ним.
        static readonly Vector3 ArenaCenter = new Vector3(70f, 0f, 188f);
        static readonly Vector3 ClearingCenter = new Vector3(70f, 0f, 0f);
        const float TrailCenterX = 70f;
        const float TrailStartZ = 12f;
        const float TrailEndZ = 162f;
        const float GroundY = 0f;

        // Кромка вдоль тропы: пять регулярных рядов с каждой стороны.
        // Шаг 4 м по Z совпадает с шагом взрослого бамбука в существующем лесу.
        static readonly float[] EdgeOffsets = { 12.5f, 15.0f, 18.0f, 21.5f, 25.5f };
        const float RowStepZ = 4.0f;
        const float RowJitterZ = 1.5f;
        const float RowJitterX = 0.5f;

        // Защитные радиусы: внутрь арены и поляны не сажаем.
        const float ArenaBlockRadius = 29f;
        const float ClearingBlockRadius = 18f;
        const float CorridorHalfWidth = 3.2f;

        // Два внешних кольца вокруг арены; вход с тропы (270°) остаётся открытым.
        static readonly float[] ArenaOuterRadii = { 38f, 41f };
        const int ArenaRingSlots = 60;
        const float ArenaEntranceAngle = 270f;
        const float ArenaEntranceHalfAngle = 24f;

        const float PlantScaleMin = 1.0f;
        const float PlantScaleMax = 1.4f;
        const string PlantNamePrefix = "BambooEdge_";
        const int Seed = 83;

        const string StoneDecorPrefix = "StoneDecor";
        const string StoneDecorContainerName = "StoneDecor_Batched";
        const string BaseColorProperty = "_BaseColor";

        [MenuItem("ShadowTan/Setup/Art/Polish Autumn Bamboo")]
        public static void Polish()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError($"ShadowTan: не найдена сцена {ScenePath}. Полировка бамбука не выполнена.");
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // Не теряем несохранённые правки в текущей сцене молча.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("ShadowTan: открытие Autumn_Gameplay отменено — полировка бамбука не выполнена.");
                    return;
                }

                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"ShadowTan: не удалось открыть {ScenePath}.");
                return;
            }

            SceneManager.SetActiveScene(scene);

            Transform level = FindRoot(scene, LevelRootName);
            if (level == null)
            {
                Debug.LogError($"ShadowTan: в сцене нет корня {LevelRootName}. Похоже, это не Autumn_Gameplay.");
                return;
            }

            var props = level.Find(PropsPath);
            if (props == null)
            {
                Debug.LogError($"ShadowTan: не найден {LevelRootName}/{PropsPath}. Пересобери грейбокс.");
                return;
            }

            var report = new PolishReport();
            bool saved = false;

            try
            {
                var bambooRoot = EnsureBambooRoot(props, report);
                var prefabs = LoadBambooPrefabs(report);

                WarmUpBambooMaterials(report);

                if (bambooRoot != null && prefabs.Length > 0)
                    PlantEdgeForest(bambooRoot, prefabs, scene, report);
                else if (prefabs.Length == 0)
                    Debug.LogWarning("ShadowTan: ни один префаб Bamboo_Giant_A/B/C не найден — новый бамбук не расставлен.");

                FixStoneDecor(props, report);
            }
            catch (Exception e)
            {
                Debug.LogError($"ShadowTan: полировка бамбука прервана исключением, сцена будет сохранена как есть.\n{e}");
            }
            finally
            {
                try
                {
                    saved = EditorSceneManager.SaveScene(scene, ScenePath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"ShadowTan: не удалось сохранить {ScenePath}.\n{e}");
                }

                try
                {
                    AssetDatabase.SaveAssets();
                }
                catch (Exception e)
                {
                    Debug.LogError($"ShadowTan: не удалось сохранить ассеты материалов бамбука.\n{e}");
                }

                report.Log(saved);
            }
        }

        // ---------------------------------------------------------------- bamboo placement

        static void PlantEdgeForest(Transform bambooRoot, GameObject[] prefabs, Scene scene, PolishReport report)
        {
            var rnd = new System.Random(Seed);

            int removed = RemovePreviousPlants(bambooRoot);
            if (removed > 0)
            {
                report.Note($"прошлый прогон: удалено {removed} растений с префиксом {PlantNamePrefix}.");
                Debug.Log($"ShadowTan: удалено {removed} растений бамбука прошлого прогона.");
            }

            int rows = 0;

            // Регулярная кромка: пять рядов на сторону, z 12..162 шагом 4 м.
            // 38 слотов по Z × 10 точек = 380 кандидатов, минус углы поляны
            // и арены. Кольца арены добавляют ещё ~102 растения.
            for (float z = TrailStartZ; z <= TrailEndZ + 0.001f; z += RowStepZ)
            {
                foreach (float offset in EdgeOffsets)
                {
                    for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                    {
                        float side = sideIndex == 0 ? -1f : 1f;
                        float x = TrailCenterX + side * (offset + Range(rnd, -RowJitterX, RowJitterX));
                        float zz = Mathf.Clamp(z + Range(rnd, -RowJitterZ, RowJitterZ), TrailStartZ, TrailEndZ);
                        var position = new Vector3(x, GroundY, zz);
                        if (TryPlant(bambooRoot, prefabs, scene, rnd, position, report))
                            rows++;
                    }
                }
            }

            int rings = 0;

            // Кольца 38 и 41 м: плотная внешняя опушка арены.
            foreach (float radius in ArenaOuterRadii)
            {
                for (int slot = 0; slot < ArenaRingSlots; slot++)
                {
                    float angle = slot * (360f / ArenaRingSlots);
                    if (Mathf.Abs(Mathf.DeltaAngle(angle, ArenaEntranceAngle)) < ArenaEntranceHalfAngle) continue;

                    Vector3 position = Polar(ArenaCenter, radius, angle);
                    if (TryPlant(bambooRoot, prefabs, scene, rnd, position, report))
                        rings++;
                }
            }

            report.EdgeRows = rows;
            report.ArenaRings = rings;

            Debug.Log(
                $"ShadowTan: новый бамбук — тропа {rows}, кольца арены {rings}, " +
                $"всего {report.NewBamboo} растений.");
        }

        static bool TryPlant(
            Transform parent,
            GameObject[] prefabs,
            Scene scene,
            System.Random rnd,
            Vector3 position,
            PolishReport report)
        {
            if (IsBlocked(position)) return false;

            var prefab = prefabs[rnd.Next(prefabs.Length)];
            var go = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (go == null)
            {
                Debug.LogWarning($"ShadowTan: не удалось инстанцировать {prefab.name}.");
                return false;
            }

            go.name = $"{PlantNamePrefix}{report.NewBamboo:0000}";
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f);
            go.transform.localScale = Vector3.one * Range(rnd, PlantScaleMin, PlantScaleMax);

            report.NewBamboo++;
            return true;
        }

        static bool IsBlocked(Vector3 p)
        {
            // Коридор тропы: бамбук не должен стоять в проходе.
            if (Mathf.Abs(p.x - TrailCenterX) < CorridorHalfWidth) return true;

            // Арена и её внешнее кольцо.
            if (XZDistance(p, ArenaCenter) < ArenaBlockRadius) return true;

            // Поляна: устье тропы и настил насыпи остаются открытыми.
            if (XZDistance(p, ClearingCenter) < ClearingBlockRadius) return true;

            return false;
        }

        static int RemovePreviousPlants(Transform bambooRoot)
        {
            // Повторный запуск не должен удваивать лес: удаляем только собственные
            // растения прошлого прогона, опознаваемые по префиксу.
            var doomed = new List<GameObject>();
            foreach (Transform child in bambooRoot)
            {
                if (child.name.StartsWith(PlantNamePrefix, StringComparison.Ordinal))
                    doomed.Add(child.gameObject);
            }

            foreach (var go in doomed)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            return doomed.Count;
        }

        static Transform EnsureBambooRoot(Transform props, PolishReport report)
        {
            var existing = props.Find(BambooRootName);
            if (existing != null) return existing;

            var go = new GameObject(BambooRootName);
            go.transform.SetParent(props, false);
            go.transform.localPosition = Vector3.zero;
            report.Note($"контейнер {PropsPath}/{BambooRootName} отсутствовал — создан.");
            return go.transform;
        }

        static GameObject[] LoadBambooPrefabs(PolishReport report)
        {
            var result = new List<GameObject>(3);
            foreach (string variant in new[] { "A", "B", "C" })
            {
                string path = $"{BambooPrefabRoot}/Bamboo_Giant_{variant}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"ShadowTan: не найден префаб бамбука {path} — вариант {variant} пропущен.");
                    report.AddMissing(path);
                    continue;
                }

                result.Add(prefab);
            }

            return result.ToArray();
        }

        // ---------------------------------------------------------------- materials

        static readonly BambooTint[] BambooTints =
        {
            new BambooTint("M_Bamboo_Leaf_Green", 0.30f, 0.38f, 0.16f),
            new BambooTint("M_Bamboo_Leaf_Olive", 0.55f, 0.50f, 0.20f),
            new BambooTint("M_Bamboo_Leaf_Yellow", 0.78f, 0.60f, 0.25f),
            new BambooTint("M_Bamboo_Culm_Dark", 0.33f, 0.24f, 0.14f),
            new BambooTint("M_Bamboo_Culm_Light", 0.60f, 0.44f, 0.24f)
        };

        static void WarmUpBambooMaterials(PolishReport report)
        {
            int updated = 0;

            foreach (var tint in BambooTints)
            {
                string path = $"{BambooMaterialRoot}/{tint.FileName}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    Debug.LogWarning($"ShadowTan: не найден материал {path} — цвет {tint.FileName} не изменён.");
                    report.AddMissing(path);
                    continue;
                }

                if (!material.HasProperty(BaseColorProperty))
                {
                    Debug.LogWarning($"ShadowTan: у {path} нет свойства {BaseColorProperty} — пропущено.");
                    report.Note($"{tint.FileName}: нет {BaseColorProperty}, цвет не задан.");
                    continue;
                }

                // Значения из задачи уже линейные: HDRP/Lit трактует _BaseColor как линейный,
                // поэтому .linear не применяется (иначе оттенок уехал бы в тёмный).
                material.SetColor(BaseColorProperty, new Color(tint.R, tint.G, tint.B, 1f));
                EditorUtility.SetDirty(material);
                updated++;
            }

            report.MaterialsUpdated = updated;
            Debug.Log($"ShadowTan: цвета бамбука обновлены у {updated}/{BambooTints.Length} материалов.");
        }

        // ---------------------------------------------------------------- stone decor

        static void FixStoneDecor(Transform props, PolishReport report)
        {
            var containers = new List<Transform>();
            foreach (Transform child in props)
            {
                if (child.name.StartsWith(StoneDecorPrefix, StringComparison.Ordinal))
                    containers.Add(child);
            }

            var sb = new StringBuilder();
            foreach (var container in containers)
                sb.Append($"'{container.name}' ({container.childCount}) ");
            Debug.Log($"ShadowTan: {StoneDecorPrefix}* до чистки: {containers.Count} контейнер(ов) — {sb.ToString().Trim()}");

            if (containers.Count == 0)
            {
                report.Note($"{StoneDecorPrefix}* не найдено — чистка камней пропущена.");
                return;
            }

            // Оставляем контейнер с наибольшим числом прямых детей; при равенстве — первый
            // по иерархии, чтобы выбор был детерминированным.
            Transform kept = containers[0];
            for (int i = 1; i < containers.Count; i++)
            {
                if (containers[i].childCount > kept.childCount) kept = containers[i];
            }

            if (kept.name != StoneDecorContainerName)
            {
                report.Note($"контейнер '{kept.name}' переименован в {StoneDecorContainerName}.");
                kept.name = StoneDecorContainerName;
            }

            foreach (var container in containers)
            {
                if (container == kept) continue;

                report.RemovedStoneContainers++;
                report.Note($"удалён дубль '{container.name}' ({container.childCount} детей).");
                Debug.Log($"ShadowTan: удалён дубль контейнера '{container.name}' ({container.childCount} детей).");
                Object.DestroyImmediate(container.gameObject);
            }

            int removedChunks = DedupeStoneChunks(kept, report);
            report.RemovedStoneChunks = removedChunks;
            report.FinalStoneChunks = kept.childCount;

            Debug.Log(
                $"ShadowTan: камни — оставлен '{kept.name}', удалено контейнеров " +
                $"{report.RemovedStoneContainers}, дублей чанков {removedChunks}, " +
                $"итог {kept.childCount} чанков.");
        }

        static int DedupeStoneChunks(Transform container, PolishReport report)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var doomed = new List<Transform>();

            for (int i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                string baseName = StripCopySuffix(child.name);

                // Имена с "(1)" считаются копией даже без исходного соседа.
                if (child.name.Contains("(1)") || seen.Contains(baseName))
                {
                    doomed.Add(child);
                    continue;
                }

                seen.Add(baseName);
            }

            foreach (var child in doomed)
            {
                report.Note($"удалён дубль камня '{child.name}'.");
                Debug.Log($"ShadowTan: удалён дубль камня '{child.name}'.");
                Object.DestroyImmediate(child.gameObject);
            }

            return doomed.Count;
        }

        static string StripCopySuffix(string name)
        {
            int open = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (open < 0 || !name.EndsWith(")", StringComparison.Ordinal)) return name;

            string digits = name.Substring(open + 2, name.Length - open - 3);
            if (digits.Length == 0) return name;

            for (int i = 0; i < digits.Length; i++)
            {
                if (!char.IsDigit(digits[i])) return name;
            }

            return name.Substring(0, open);
        }

        // ---------------------------------------------------------------- helpers

        static Transform FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root.transform;
            }

            return null;
        }

        static Vector3 Polar(Vector3 center, float radius, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(
                center.x + Mathf.Cos(radians) * radius,
                GroundY,
                center.z + Mathf.Sin(radians) * radius);
        }

        static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float Range(System.Random rnd, float min, float max)
        {
            return min + (float)rnd.NextDouble() * (max - min);
        }

        // ---------------------------------------------------------------- report

        sealed class BambooTint
        {
            public readonly string FileName;
            public readonly float R;
            public readonly float G;
            public readonly float B;

            public BambooTint(string fileName, float r, float g, float b)
            {
                FileName = fileName;
                R = r;
                G = g;
                B = b;
            }
        }

        sealed class PolishReport
        {
            public int NewBamboo;
            public int EdgeRows;
            public int ArenaRings;
            public int MaterialsUpdated;
            public int RemovedStoneContainers;
            public int RemovedStoneChunks;
            public int FinalStoneChunks = -1;

            readonly List<string> missing = new List<string>();
            readonly List<string> notes = new List<string>();

            public void AddMissing(string path)
            {
                if (!missing.Contains(path)) missing.Add(path);
            }

            public void Note(string text)
            {
                notes.Add(text);
            }

            public void Log(bool saved)
            {
                var sb = new StringBuilder();
                sb.AppendLine(saved
                    ? "ShadowTan: бамбук Autumn дополнен, сцена сохранена."
                    : "ShadowTan: бамбук Autumn дополнен, но сцена НЕ сохранена (см. ошибки выше).");
                sb.AppendLine($"  Сцена: {ScenePath}");
                string stoneFinal = FinalStoneChunks >= 0 ? FinalStoneChunks.ToString() : "n/a";
                sb.AppendLine($"  Новый бамбук: {NewBamboo} (тропа {EdgeRows}, кольца арены {ArenaRings})");
                sb.AppendLine($"  Цвета бамбука: обновлено {MaterialsUpdated}/{BambooTints.Length} материалов");
                sb.AppendLine(
                    $"  Камни: удалено контейнеров {RemovedStoneContainers}, " +
                    $"дублей чанков {RemovedStoneChunks}, осталось чанков {stoneFinal}");
                sb.AppendLine(
                    $"  Кромка тропы: x = 70 ± 12.5/15/18/21.5/25.5, z {TrailStartZ:0}..{TrailEndZ:0} шагом {RowStepZ:0.0}; " +
                    $"кольца арены r = 38 и 41, без входа 270° ± 24°.");

                if (missing.Count > 0)
                {
                    sb.AppendLine("  Missing (skipped):");
                    foreach (string path in missing) sb.AppendLine("    - " + path);
                }

                if (notes.Count > 0)
                {
                    sb.AppendLine("  Notes:");
                    foreach (string note in notes) sb.AppendLine("    - " + note);
                }

                Debug.Log(sb.ToString());
            }
        }
    }
}
