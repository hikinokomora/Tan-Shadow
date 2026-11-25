using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    // Собирает LOD-префабы придорожных барьеров Autumn из Tripo-FBX SM_Barrier_A/B/C
    // и расставляет их вдоль прямой тропы (x = 70 ± 2.0, z = 12..162) вместо
    // плейсхолдера [Level]/Greybox/Props/Barriers.
    //
    // Допущения:
    //  - В каждом FBX четыре ребёнка <Variant>_LOD0..3. Если каких-то LOD нет,
    //    берутся существующие меши по порядку, а пороги — первые N из таблицы.
    //  - Меши FBX лежат в единичном масштабе и без поворотов на узлах: размер
    //    берётся из mesh.bounds LOD0, как в контракте ассетов. При нестандартном
    //    импорте высота 0.9 м может не совпасть — скрипт пишет фактические
    //    числа в лог, проверяй их после прогона.
    //  - Пивот меша совпадает с центром сегмента вдоль длинной оси, а основание
    //    по Y лежит в пивоте: позиция ставится по z + length/2, y=0, без
    //    коррекции пивота.
    //  - Барьеры — визуальный декор без коллайдеров. Слои объекта не меняются
    //    (остаётся Default).
    //  - Повторный запуск пересобирает материалы, префабы и контейнер
    //    Barriers_Generated; чужие ассеты не удаляются.
    public static class AutumnBarriersSetup
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string LocationRoot = "Assets/_Project/Art/Environment/Autumn/Location";
        const string ModelRoot = LocationRoot + "/Models/Barriers";
        const string TextureRoot = LocationRoot + "/Textures";
        const string MaterialRoot = LocationRoot + "/Materials";
        const string PrefabRoot = "Assets/_Project/Prefabs/Environment/Autumn/Barriers";
        const string LitShaderName = "HDRP/Lit";

        const string LevelRootName = "[Level]";
        const string PropsPath = "Greybox/Props";
        const string PlaceholderName = "Barriers";
        const string ContainerName = "Barriers_Generated";

        // Тропа из AutumnSceneBuilder: 3.6 м шириной, 12..162 по Z.
        // Барьеры стоят в 2.0 м от центра, то есть сразу за кромкой и в траве.
        const float TrailCenterX = 70f;
        const float BarrierOffsetX = 2.0f;
        const float TrailStartZ = 12f;
        const float TrailEndZ = 162f;

        const float TargetHeight = 0.9f;
        const float MinGap = 0.08f;
        const float MaxGap = 0.20f;
        const int Seed = 47;

        static readonly string[] Variants = { "A", "B", "C" };

        // Пороги LODGroup: LOD0 0.018, LOD1 0.007, LOD2 0.0025, LOD3 0.0008.
        static readonly float[] LodHeights = { 0.018f, 0.007f, 0.0025f, 0.0008f };

        [MenuItem("ShadowTan/Setup/Art/Build Autumn Barriers")]
        public static void Build()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError($"ShadowTan: не найдена сцена {ScenePath}. Барьеры не собраны.");
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // Не теряем несохранённые правки в текущей сцене молча.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("ShadowTan: открытие Autumn_Gameplay отменено — барьеры не собраны.");
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

            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);
            AssetDatabase.Refresh();

            var shader = Shader.Find(LitShaderName);
            if (shader == null)
            {
                Debug.LogError("ShadowTan: не найден шейдер HDRP/Lit. Проверь render pipeline.");
                return;
            }

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

            var report = new BarrierReport();
            bool saved = false;

            try
            {
                var variants = new List<BarrierVariant>();
                foreach (string id in Variants)
                {
                    var variant = BuildVariant(shader, id, report);
                    if (variant == null) continue;

                    variants.Add(variant);
                    report.BuiltCount = variants.Count;
                }

                if (variants.Count == 0)
                    Debug.LogWarning("ShadowTan: ни один вариант барьера не собран (нет FBX/текстур) — размещение пропущено.");

                RemovePlaceholder(props, report);
                var container = ResetContainer(props, ContainerName, report);

                if (variants.Count > 0)
                    PlaceSegments(container, variants, scene, report);
            }
            catch (Exception e)
            {
                Debug.LogError($"ShadowTan: сборка барьеров прервана исключением, сцена будет сохранена как есть.\n{e}");
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
                    Debug.LogError($"ShadowTan: не удалось сохранить ассеты барьеров.\n{e}");
                }

                report.Log(saved);
            }
        }

        // ---------------------------------------------------------------- prefabs

        static BarrierVariant BuildVariant(Shader shader, string id, BarrierReport report)
        {
            string modelPath = $"{ModelRoot}/SM_Barrier_{id}.fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                report.AddMissing(modelPath);
                Debug.LogWarning($"ShadowTan: не найден {modelPath} — вариант {id} пропущен.");
                return null;
            }

            var meshes = LoadLodMeshes(model, id, report);
            if (meshes.Count == 0)
            {
                report.AddMissing($"{modelPath} (нет мешей {id}_LOD0..{id}_LOD{LodHeights.Length - 1})");
                Debug.LogWarning($"ShadowTan: в {modelPath} нет пригодных LOD-мешей — вариант {id} пропущен.");
                return null;
            }

            var rawSize = meshes[0].bounds.size;
            if (!IsUsableSize(rawSize.y))
            {
                report.Note($"{id}: у LOD0 некорректная высота {rawSize.y} — вариант пропущен.");
                Debug.LogWarning($"ShadowTan: у {id}_LOD0 нулевая/некорректная высота ({rawSize.y}) — вариант {id} пропущен.");
                return null;
            }

            // Длинная горизонтальная ось: X или Z. Сегменты тропы вытянуты вдоль Z,
            // поэтому X-модели (C) разворачиваем на -90° по Y: локальный +X → мировой +Z.
            bool rotateToZ = rawSize.x > rawSize.z;
            float longAxis = rotateToZ ? rawSize.x : rawSize.z;
            if (!IsUsableSize(longAxis))
            {
                report.Note($"{id}: у LOD0 нулевая длина по длинной оси — вариант пропущен.");
                Debug.LogWarning($"ShadowTan: у {id}_LOD0 нулевая длина по длинной оси — вариант {id} пропущен.");
                return null;
            }

            var material = CreateMaterial(shader, id, report);
            if (material == null) return null;

            float scale = TargetHeight / rawSize.y;
            var rotation = rotateToZ ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;

            var root = new GameObject($"Barrier_{id}");
            try
            {
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = rotation;
                root.transform.localScale = Vector3.one * scale;

                var group = root.AddComponent<LODGroup>();
                group.fadeMode = LODFadeMode.None;
                group.animateCrossFading = false;

                var lods = new LOD[meshes.Count];
                for (int i = 0; i < meshes.Count; i++)
                {
                    var mesh = meshes[i];

                    var child = new GameObject($"LOD{i}");
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = Vector3.zero;
                    child.transform.localRotation = Quaternion.identity;
                    child.transform.localScale = Vector3.one;

                    var filter = child.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;

                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = SolidMaterials(material, mesh.subMeshCount);
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;

                    float transition = LodHeights[Mathf.Min(i, LodHeights.Length - 1)];
                    lods[i] = new LOD(transition, new Renderer[] { renderer });
                }

                group.SetLODs(lods);
                group.RecalculateBounds();

                string prefabPath = $"{PrefabRoot}/Barrier_{id}.prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"ShadowTan: не удалось сохранить префаб {prefabPath}.");
                    return null;
                }

                report.Note(
                    $"{id}: bounds LOD0 {rawSize.x:0.000}×{rawSize.y:0.000}×{rawSize.z:0.000} м, " +
                    $"scale {scale:0.000}, длинная ось {(rotateToZ ? "X → Z (поворот -90° Y)" : "Z")}, " +
                    $"длина сегмента {longAxis * scale:0.000} м, LOD {meshes.Count}.");

                return new BarrierVariant
                {
                    Id = id,
                    Prefab = prefab,
                    Rotation = rotation,
                    SegmentLength = longAxis * scale
                };
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static List<Mesh> LoadLodMeshes(GameObject model, string id, BarrierReport report)
        {
            var meshes = new List<Mesh>(LodHeights.Length);

            for (int i = 0; i < LodHeights.Length; i++)
            {
                string nodeName = $"{id}_LOD{i}";
                var node = FindNode(model.transform, nodeName);
                if (node == null)
                {
                    report.Note($"{id}: узел {nodeName} не найден.");
                    continue;
                }

                var filter = node.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    report.Note($"{id}: у {nodeName} нет MeshFilter/меша.");
                    continue;
                }

                meshes.Add(filter.sharedMesh);
            }

            return meshes;
        }

        // ---------------------------------------------------------------- materials

        static Material CreateMaterial(Shader shader, string id, BarrierReport report)
        {
            string texturePath = FindTexture($"T_Barrier_{id}_BaseColor");
            if (texturePath == null)
            {
                report.AddMissing($"{TextureRoot}/T_Barrier_{id}_BaseColor.png (.png/.jpg)");
                Debug.LogWarning($"ShadowTan: нет текстуры T_Barrier_{id}_BaseColor (.png/.jpg) — вариант {id} пропущен.");
                return null;
            }

            var texture = LoadBaseColorTexture(texturePath);
            if (texture == null)
            {
                report.AddMissing(texturePath);
                Debug.LogWarning($"ShadowTan: не удалось загрузить {texturePath} — вариант {id} пропущен.");
                return null;
            }

            string path = $"{MaterialRoot}/M_Autumn_Barrier_{id}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;

            if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTexture("_BaseColorMap", texture);
                material.SetTextureScale("_BaseColorMap", Vector2.one);
                material.SetTextureOffset("_BaseColorMap", Vector2.zero);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            SetFloat(material, "_Smoothness", 0.2f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_UVBase", 0f);
            SetFloat(material, "_SmoothnessRemapMin", 0f);
            SetFloat(material, "_SmoothnessRemapMax", 1f);

            // Tripo отдаёт только basecolor. Normal/mask не назначаем и выключаем
            // ключи: иначе на переиспользованном материале останутся карты прошлого прогона.
            if (material.HasProperty("_NormalMap")) material.SetTexture("_NormalMap", null);
            if (material.HasProperty("_MaskMap")) material.SetTexture("_MaskMap", null);
            material.DisableKeyword("_NORMALMAP");
            material.DisableKeyword("_NORMALMAP_TANGENT_SPACE");
            material.DisableKeyword("_MASKMAP");

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);

            report.Note($"{id}: материал {path}, текстура {texturePath}.");
            return material;
        }

        static string FindTexture(string baseName)
        {
            foreach (string extension in new[] { ".png", ".jpg" })
            {
                string path = $"{TextureRoot}/{baseName}{extension}";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) return path;

                // Файл уже на диске, но ещё не импортирован.
                if (!File.Exists(path)) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null) return path;
            }

            return null;
        }

        static Texture2D LoadBaseColorTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) return null;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return texture;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }
            if (!importer.sRGBTexture)
            {
                importer.sRGBTexture = true;
                changed = true;
            }
            if (importer.maxTextureSize != 4096)
            {
                importer.maxTextureSize = 4096;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return texture;
        }

        // ---------------------------------------------------------------- placement

        static void PlaceSegments(Transform container, List<BarrierVariant> variants, Scene scene, BarrierReport report)
        {
            var rnd = new System.Random(Seed);

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                bool left = sideIndex == 0;
                float side = left ? -1f : 1f;
                string sideTag = left ? "L" : "R";
                float x = TrailCenterX + side * BarrierOffsetX;
                float z = TrailStartZ;
                int index = 0;
                int previous = -1;

                while (true)
                {
                    int pick = PickVariant(rnd, variants.Count, previous);
                    var variant = variants[pick];
                    float length = variant.SegmentLength;

                    // Не выходим за z=162: следующий сегмент должен влезть целиком.
                    if (z + length > TrailEndZ + 1e-3f) break;

                    float yaw = rnd.Next(2) == 0 ? 0f : 180f;
                    var go = PrefabUtility.InstantiatePrefab(variant.Prefab, scene) as GameObject;
                    if (go == null)
                    {
                        Debug.LogWarning(
                            $"ShadowTan: не удалось инстанцировать {variant.Prefab.name} — " +
                            $"сторона {sideTag} оборвана на z={z:0.00}.");
                        break;
                    }

                    go.name = $"Barrier_{sideTag}_{variant.Id}_{index:000}";
                    go.transform.SetParent(container, true);
                    // Масштаб префаба не трогаем: он уже задан при сборке.
                    go.transform.position = new Vector3(x, 0f, z + length * 0.5f);
                    go.transform.rotation = variant.Rotation * Quaternion.Euler(0f, yaw, 0f);

                    report.Count(left, variant.Id);

                    z += length + Range(rnd, MinGap, MaxGap);
                    previous = pick;
                    index++;
                }
            }
        }

        static int PickVariant(System.Random rnd, int count, int previous)
        {
            if (count <= 1) return 0;

            // Первый выбор стороны — свободный; дальше исключаем вариант предыдущего
            // сегмента, чтобы соседние барьеры не повторялись. Стороны тянут из
            // одного RNG последовательно, поэтому не зеркалят друг друга.
            if (previous < 0) return rnd.Next(count);

            int index = rnd.Next(count - 1);
            if (index >= previous) index++;
            return index;
        }

        // ---------------------------------------------------------------- scene helpers

        static void RemovePlaceholder(Transform props, BarrierReport report)
        {
            var placeholder = props.Find(PlaceholderName);
            if (placeholder == null)
            {
                report.Note($"плейсхолдер {PropsPath}/{PlaceholderName} не найден — пропущено.");
                return;
            }

            Object.DestroyImmediate(placeholder.gameObject);
            report.Note($"плейсхолдер {PropsPath}/{PlaceholderName} удалён.");
        }

        static Transform ResetContainer(Transform props, string name, BarrierReport report)
        {
            var existing = props.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
                report.Note($"контейнер {name} пересобран (прошлый прогон удалён).");
            }

            var go = new GameObject(name);
            go.transform.SetParent(props, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        static Transform FindRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root.transform;
            }
            return null;
        }

        static Transform FindNode(Transform root, string name)
        {
            foreach (var node in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase)) return node;
            }
            return null;
        }

        // ---------------------------------------------------------------- misc

        static Material[] SolidMaterials(Material material, int subMeshCount)
        {
            int slots = Mathf.Max(1, subMeshCount);
            var result = new Material[slots];
            for (int i = 0; i < slots; i++) result[i] = material;
            return result;
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static bool IsUsableSize(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 1e-4f;
        }

        static float Range(System.Random rnd, float min, float max)
        {
            return min + (float)rnd.NextDouble() * (max - min);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        sealed class BarrierVariant
        {
            public string Id;
            public GameObject Prefab;
            public Quaternion Rotation;
            public float SegmentLength;
        }

        sealed class BarrierReport
        {
            public int BuiltCount;

            readonly Dictionary<string, int> leftByVariant = new Dictionary<string, int>();
            readonly Dictionary<string, int> rightByVariant = new Dictionary<string, int>();
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

            public void Count(bool left, string variant)
            {
                var counts = left ? leftByVariant : rightByVariant;
                counts.TryGetValue(variant, out int current);
                counts[variant] = current + 1;
            }

            public void Log(bool saved)
            {
                var sb = new StringBuilder();
                sb.AppendLine(saved
                    ? "ShadowTan: барьеры Autumn собраны и сцена сохранена."
                    : "ShadowTan: барьеры Autumn собраны, но сцена НЕ сохранена (см. ошибки выше).");
                sb.AppendLine($"  Сцена: {ScenePath}");
                sb.AppendLine($"  Варианты: {BuiltCount}/{Variants.Length}");
                sb.AppendLine(
                    $"  Левая сторона (x={TrailCenterX - BarrierOffsetX:0.0}): " +
                    $"{Total(leftByVariant)} сегментов ({FormatCounts(leftByVariant)})");
                sb.AppendLine(
                    $"  Правая сторона (x={TrailCenterX + BarrierOffsetX:0.0}): " +
                    $"{Total(rightByVariant)} сегментов ({FormatCounts(rightByVariant)})");
                sb.AppendLine($"  Префабы: {PrefabRoot}/Barrier_A|B|C.prefab");
                sb.AppendLine($"  Материалы: {MaterialRoot}/M_Autumn_Barrier_A|B|C.mat");

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

            static int Total(Dictionary<string, int> counts)
            {
                int total = 0;
                foreach (int value in counts.Values) total += value;
                return total;
            }

            static string FormatCounts(Dictionary<string, int> counts)
            {
                var sb = new StringBuilder();
                foreach (string variant in Variants)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    counts.TryGetValue(variant, out int count);
                    sb.Append(variant).Append(' ').Append(count);
                }
                return sb.ToString();
            }
        }
    }
}
