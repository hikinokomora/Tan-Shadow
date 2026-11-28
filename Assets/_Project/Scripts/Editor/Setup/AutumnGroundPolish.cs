using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowTan.EditorTools
{
    // Полировка земли Autumn_Gameplay: седьмой травяной слой TL_Autumn_Grass_C,
    // пересборка альфамапов на 7 слоёв и мягкие (alpha blend) наземные декали.
    //
    // Запускать после Build Autumn Location / Dress Autumn Location: скрипт
    // работает в уже собранной сцене, не создаёт объектов и не трогает бамбук,
    // барьеры, частицы, кусты травы и камни.
    //
    // Допущения:
    //  - AutumnTerrain: 400x400 м, alphamap 1024x1024, 6 слоёв в порядке
    //    forest, grassA, sandA, leaves, grassB, sandB. Седьмой слой добавляется
    //    в конец; при другом порядке/количестве слои пересобираются по путям.
    //  - Маски повторяют геометрию AutumnSceneBuilder: тропа x=70 ±1.8,
    //    z=12..162; поляна r=16 вокруг (70,0); арена r=26 вокруг (70,188).
    //    Края мягкие, с низкочастотным искажением кромки.
    //  - Лес травяной: TL_Autumn_Forest — это «земля/грязь» и получает только
    //    0..8%. Остаток делится между grassA/B/C низкочастотным Perlin-шумом:
    //    C 25-55% травы, B 20-40% остатка, A — остальное.
    //  - Песок делится между sandA/sandB тем же способом (25-75% на B).
    //  - Листва — пятна Perlin с усилением у кромок тропы, максимум 60%
    //    остатка после песка; на самой тропе и в центре арены её нет.
    //    Точные seed/частоты прежних пятен в проекте не сохранены (отдельного
    //    terrain-скрипта нет), поэтому рисунок листвы детерминированный, но
    //    не побитово прежний; формы песка повторяют геометрию сцены.
    //  - HDRP Lit: перевод декалей в Transparent выполняется только свойствами
    //    и ключами материала, без замены шейдера. Помимо _SurfaceType/_BlendMode
    //    и ключей задаются blend-факторы (_SrcBlend/_DstBlend/_DstBlend2/
    //    _AlphaSrcBlend/_AlphaDstBlend): transparent-пасс Lit.shader читает
    //    Blend из материала, и без них он остался бы One/Zero, то есть
    //    непрозрачным. _CullMode/_CullModeForward дублируются в
    //    _TransparentCullMode, чтобы transparent-проход HDRP тоже рисовал
    //    двусторонне.
    //  - Текстуры Soft2/WildGrass2 могут ещё не иметь .meta: скрипт импортирует
    //    их сам. Если Soft2 нет — берётся Soft, затем исходная RGBA-декаль.
    public static class AutumnGroundPolish
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string LocationRoot = "Assets/_Project/Art/Environment/Autumn/Location";
        const string TerrainRoot = LocationRoot + "/Terrain";
        const string TerrainAssetPath = TerrainRoot + "/AutumnTerrain.asset";
        const string TextureRoot = LocationRoot + "/Textures";
        const string MaterialRoot = LocationRoot + "/Materials";

        const string GrassCLayerPath = TerrainRoot + "/TL_Autumn_Grass_C.terrainlayer";
        const string GrassCLayerName = "TL_Autumn_Grass_C";

        // Ожидаемый порядок существующих слоёв. По нему же собирается fallback,
        // если в TerrainData порядок или количество не совпали.
        static readonly string[] BaseLayerNames =
        {
            "TL_Autumn_Forest",
            "TL_Autumn_Grass",
            "TL_Autumn_Sand",
            "TL_Autumn_Leaves",
            "TL_Autumn_Grass_B",
            "TL_Autumn_Sand_B"
        };

        // Итоговый порядок: forest(0), grassA(1), sandA(2), leaves(3),
        // grassB(4), sandB(5), grassC(6).
        const int ForestLayer = 0;
        const int GrassALayer = 1;
        const int SandALayer = 2;
        const int LeavesLayer = 3;
        const int GrassBLayer = 4;
        const int SandBLayer = 5;
        const int GrassCLayer = 6;
        const int LayerCount = 7;

        // TerrainData AutumnTerrain: alphamap 1024x1024. Массив весов
        // фиксированного размера — SetAlphamaps требует точного совпадения.
        const int AlphamapResolution = 1024;

        // Геометрия из AutumnSceneBuilder / AutumnLocationDressing.
        const float TrailCenterX = 70f;
        const float TrailHalfWidth = 1.8f;
        const float TrailStartZ = 12f;
        const float TrailEndZ = 162f;
        const float ClearingCenterX = 70f;
        const float ClearingCenterZ = 0f;
        const float ClearingRadius = 16f;
        const float ArenaCenterX = 70f;
        const float ArenaCenterZ = 188f;
        const float ArenaRadius = 26f;

        const float LeafMaxWeight = 0.6f;
        const float DirtMaxWeight = 0.08f;

        const string DecalDriedMaterialPath = MaterialRoot + "/M_Autumn_Decal_DriedBamboo.mat";
        const string DecalFlowersMaterialPath = MaterialRoot + "/M_Autumn_Decal_BambooFlowers.mat";

        [MenuItem("ShadowTan/Setup/Art/Polish Autumn Ground")]
        public static void Polish()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError($"ShadowTan: не найдена сцена {ScenePath}. Полировка земли не выполнена.");
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // Не теряем несохранённые правки в текущей сцене молча.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("ShadowTan: открытие Autumn_Gameplay отменено — полировка земли не выполнена.");
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
            AssetDatabase.Refresh();

            var report = new PolishReport();
            bool saved = false;

            try
            {
                PolishTerrain(scene, report);
                SoftenDecals(report);
            }
            catch (Exception e)
            {
                Debug.LogError($"ShadowTan: полировка земли прервана исключением, сцена будет сохранена как есть.\n{e}");
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
                    Debug.LogError($"ShadowTan: не удалось сохранить ассеты земли.\n{e}");
                }

                report.Log(saved);
            }
        }

        // ------------------------------------------------------------------ terrain

        static void PolishTerrain(Scene scene, PolishReport report)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainAssetPath);
            if (data == null)
            {
                report.AddMissing(TerrainAssetPath);
                Debug.LogError($"ShadowTan: не найден {TerrainAssetPath} — слой grass C и альфамапы пропущены.");
                return;
            }

            var terrain = FindTerrain(scene, data);
            if (terrain == null)
            {
                report.Note("в сцене не найден Terrain с AutumnTerrain — альфамапы пересобрать нельзя.");
                Debug.LogWarning("ShadowTan: в Autumn_Gameplay нет Terrain с AutumnTerrain. Ассет grass C будет обновлён, в terrainLayers он не попадёт, альфамапы — нет.");
            }

            var grassC = BuildGrassCLayer(report);
            if (grassC == null)
            {
                report.Note("TL_Autumn_Grass_C не обновлён: нет T_WildGrass2_BaseColor — альфамапы не пересобраны.");
                return;
            }

            var layers = BuildLayerOrder(data, grassC, report);
            if (layers == null)
            {
                report.Note("порядок слоёв не собран — альфамапы не пересобраны.");
                return;
            }

            report.TerrainLayerCount = data.terrainLayers != null ? data.terrainLayers.Length : 0;

            if (terrain == null)
            {
                // Без Terrain нельзя перевести мировые координаты в альфамапы.
                // Слой-ассет обновлён, но terrainLayers не трогаем, чтобы не
                // получить 7 слоёв с пустыми весами.
                report.Note("terrainLayers не изменён: в сцене нет Terrain, альфамапы не пересобраны.");
                return;
            }

            if (data.alphamapWidth != AlphamapResolution || data.alphamapHeight != AlphamapResolution)
            {
                report.Note(
                    $"terrainLayers не изменён: alphamap {data.alphamapWidth}x{data.alphamapHeight}, " +
                    $"ожидалось {AlphamapResolution}x{AlphamapResolution}.");
                return;
            }

            data.terrainLayers = layers;
            EditorUtility.SetDirty(data);
            report.TerrainLayerCount = data.terrainLayers != null ? data.terrainLayers.Length : 0;
            RebuildAlphamaps(data, terrain, report);
        }

        static TerrainLayer BuildGrassCLayer(PolishReport report)
        {
            var baseColor = LoadTexture(
                $"{TextureRoot}/T_WildGrass2_BaseColor.png",
                TextureImporterType.Default,
                true,
                2048,
                false,
                report,
                "T_WildGrass2_BaseColor");

            if (baseColor == null)
            {
                report.AddMissing($"{TextureRoot}/T_WildGrass2_BaseColor.png");
                Debug.LogWarning("ShadowTan: нет T_WildGrass2_BaseColor — TL_Autumn_Grass_C не создан.");
                return null;
            }

            var normal = LoadTexture(
                $"{TextureRoot}/T_WildGrass2_Normal.png",
                TextureImporterType.NormalMap,
                false,
                2048,
                false,
                report,
                "T_WildGrass2_Normal");

            var mask = LoadTexture(
                $"{TextureRoot}/T_WildGrass2_Mask.png",
                TextureImporterType.Default,
                false,
                2048,
                false,
                report,
                "T_WildGrass2_Mask");

            if (normal == null) report.AddMissing($"{TextureRoot}/T_WildGrass2_Normal.png");
            if (mask == null) report.AddMissing($"{TextureRoot}/T_WildGrass2_Mask.png");

            EnsureFolder(TerrainRoot);

            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassCLayerPath);
            if (layer == null && File.Exists(GrassCLayerPath))
            {
                // Файл есть, но ещё не импортирован/сломан. Пробуем импорт,
                // но чужой существующий файл не перезаписываем.
                AssetDatabase.ImportAsset(GrassCLayerPath, ImportAssetOptions.ForceSynchronousImport);
                layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(GrassCLayerPath);
            }

            if (layer == null)
            {
                if (File.Exists(GrassCLayerPath))
                {
                    report.AddMissing(GrassCLayerPath + " (не импортируется как TerrainLayer)");
                    Debug.LogError($"ShadowTan: {GrassCLayerPath} существует, но не читается как TerrainLayer — пропущено.");
                    return null;
                }

                layer = new TerrainLayer { name = GrassCLayerName };
                AssetDatabase.CreateAsset(layer, GrassCLayerPath);
                report.Note($"создан {GrassCLayerPath}");
            }
            else if (layer.name != GrassCLayerName)
            {
                layer.name = GrassCLayerName;
            }

            layer.diffuseTexture = baseColor;
            layer.normalMapTexture = normal;
            layer.maskMapTexture = mask;
            layer.tileSize = new Vector2(6.4f, 6.4f);
            layer.tileOffset = new Vector2(0.21f, 0.47f);
            layer.diffuseRemapMin = new Vector4(0.88f, 0.94f, 0.78f, 0f);
            layer.diffuseRemapMax = new Vector4(1f, 1f, 0.96f, 1f);
            layer.smoothness = 0.1f;
            layer.metallic = 0f;
            layer.normalScale = 1f;
            layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;

            EditorUtility.SetDirty(layer);

            report.Note(
                $"TL_Autumn_Grass_C: diffuse {baseColor.name}, " +
                $"normal {(normal != null ? normal.name : "нет")}, " +
                $"mask {(mask != null ? mask.name : "нет")}, tile 6.4/6.4, offset 0.21/0.47.");
            return layer;
        }

        static TerrainLayer[] BuildLayerOrder(TerrainData data, TerrainLayer grassC, PolishReport report)
        {
            var current = data.terrainLayers;
            bool orderOk = current != null && current.Length >= BaseLayerNames.Length;

            if (orderOk)
            {
                for (int i = 0; i < BaseLayerNames.Length; i++)
                {
                    if (current[i] == null || current[i].name != BaseLayerNames[i])
                    {
                        orderOk = false;
                        break;
                    }
                }
            }

            var result = new List<TerrainLayer>(LayerCount);

            if (orderOk)
            {
                for (int i = 0; i < BaseLayerNames.Length; i++) result.Add(current[i]);

                if (current.Length > BaseLayerNames.Length)
                {
                    report.Note(
                        $"в terrain было {current.Length} слоёв; лишние {current.Length - BaseLayerNames.Length} " +
                        "заменены седьмым grass C.");
                }
            }
            else
            {
                if (current != null && current.Length > 0)
                    report.Note($"порядок слоёв не совпал с ожидаемым ({DescribeLayers(current)}); пересобираю по путям.");

                for (int i = 0; i < BaseLayerNames.Length; i++)
                {
                    string path = $"{TerrainRoot}/{BaseLayerNames[i]}.terrainlayer";
                    var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                    if (layer == null)
                    {
                        report.AddMissing(path);
                        Debug.LogError($"ShadowTan: не найден слой {path} — альфамапы не пересобраны.");
                        return null;
                    }

                    result.Add(layer);
                }
            }

            result.Add(grassC);
            return result.ToArray();
        }

        static void RebuildAlphamaps(TerrainData data, Terrain terrain, PolishReport report)
        {
            int width = data.alphamapWidth;
            int height = data.alphamapHeight;

            if (width != AlphamapResolution || height != AlphamapResolution)
            {
                report.Note(
                    $"альфамапы не пересобраны: разрешение {width}x{height}, " +
                    $"ожидалось {AlphamapResolution}x{AlphamapResolution}.");
                Debug.LogWarning($"ShadowTan: alphamap {width}x{height} не совпадает с {AlphamapResolution} — пропущено.");
                return;
            }

            if (data.alphamapLayers != LayerCount)
            {
                report.Note($"альфамапы не пересобраны: слоёв {data.alphamapLayers}, ожидалось {LayerCount}.");
                Debug.LogWarning("ShadowTan: число слоёв terrain не 7 — SetAlphamaps пропущен.");
                return;
            }

            Vector3 size = data.size;
            if (size.x <= 1e-3f || size.z <= 1e-3f)
            {
                report.Note($"альфамапы не пересобраны: некорректный размер terrain {size}.");
                return;
            }

            Vector3 origin = terrain.transform.position;
            var weights = new float[AlphamapResolution, AlphamapResolution, LayerCount];
            var layerTotals = new double[LayerCount];
            double grassTotal = 0.0;
            double grassCTotal = 0.0;
            double grassBTotal = 0.0;
            float maxSumError = 0f;

            try
            {
                for (int y = 0; y < AlphamapResolution; y++)
                {
                    if ((y & 63) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "ShadowTan: Autumn ground",
                            $"Альфамапы {y}/{AlphamapResolution}",
                            y / (float)AlphamapResolution);
                    }

                    float worldZ = origin.z + (y + 0.5f) / AlphamapResolution * size.z;

                    for (int x = 0; x < AlphamapResolution; x++)
                    {
                        float worldX = origin.x + (x + 0.5f) / AlphamapResolution * size.x;
                        var pixel = Evaluate(worldX, worldZ);

                        float sum =
                            pixel.Forest + pixel.GrassA + pixel.SandA + pixel.Leaves +
                            pixel.GrassB + pixel.SandB + pixel.GrassC;

                        float error = Mathf.Abs(sum - 1f);
                        if (error > maxSumError) maxSumError = error;

                        weights[y, x, ForestLayer] = pixel.Forest;
                        weights[y, x, GrassALayer] = pixel.GrassA;
                        weights[y, x, SandALayer] = pixel.SandA;
                        weights[y, x, LeavesLayer] = pixel.Leaves;
                        weights[y, x, GrassBLayer] = pixel.GrassB;
                        weights[y, x, SandBLayer] = pixel.SandB;
                        weights[y, x, GrassCLayer] = pixel.GrassC;

                        layerTotals[ForestLayer] += pixel.Forest;
                        layerTotals[GrassALayer] += pixel.GrassA;
                        layerTotals[SandALayer] += pixel.SandA;
                        layerTotals[LeavesLayer] += pixel.Leaves;
                        layerTotals[GrassBLayer] += pixel.GrassB;
                        layerTotals[SandBLayer] += pixel.SandB;
                        layerTotals[GrassCLayer] += pixel.GrassC;

                        grassTotal += pixel.GrassA + pixel.GrassB + pixel.GrassC;
                        grassBTotal += pixel.GrassB;
                        grassCTotal += pixel.GrassC;
                    }
                }

                data.SetAlphamaps(0, 0, weights);
                EditorUtility.SetDirty(data);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            int pixelCount = AlphamapResolution * AlphamapResolution;
            for (int i = 0; i < LayerCount; i++)
                report.Coverage[i] = (float)(layerTotals[i] / pixelCount * 100.0);

            report.HasCoverage = true;
            report.GrassCShare = grassTotal > 0.0 ? (float)(grassCTotal / grassTotal * 100.0) : 0f;
            report.GrassBShareOfGrass = grassTotal > 0.0 ? (float)(grassBTotal / grassTotal * 100.0) : 0f;

            report.Note(
                $"альфамапы пересобраны на {LayerCount} слоёв, " +
                $"terrain {size.x:0}x{size.z:0} м, origin ({origin.x:0.##}; {origin.z:0.##}).");

            if (maxSumError > 0.001f)
                report.Note($"сумма весов до нормировки отклонялась от 1 на {maxSumError:0.0000}.");
        }

        // Вес пикселя. Первый индекс массива — Z (строки), второй — X.
        static PixelWeights Evaluate(float x, float z)
        {
            // Низкочастотное искажение кромок: песок и листва не выглядят
            // циркулем и линейкой.
            float edge = Noise(x * 0.08f + 5.2f, z * 0.08f + 9.7f);
            float edgeOffset = (edge - 0.5f) * 1.6f;

            float path = PathMask(x, z, edgeOffset * 0.35f);
            float clearing = CircleMask(x, z, ClearingCenterX, ClearingCenterZ, ClearingRadius, 2.0f, edgeOffset);
            float arena = CircleMask(x, z, ArenaCenterX, ArenaCenterZ, ArenaRadius, 1.6f, edgeOffset * 0.5f);
            float sand = Mathf.Clamp01(Mathf.Max(path, Mathf.Max(clearing, arena)));

            // Листва: пятна Perlin, заметнее у кромок тропы. Внутри тропы её
            // съедает песок, поэтому отдельно вычитать не нужно.
            float leafNoise = Noise(x * 0.05f + 31.7f, z * 0.05f + 11.3f);
            float leaf = Smooth01((leafNoise - 0.55f) / 0.14f);
            float trailBand = 1f - Smooth01((Mathf.Abs(x - TrailCenterX) - 2f) / 7f);
            leaf = Mathf.Max(leaf, Smooth01((leafNoise - 0.42f) / 0.14f) * trailBand);
            leaf = Mathf.Clamp01(leaf);

            // Голая земля: редкие пятна, не больше 8% остатка.
            float dirtNoise = Noise(x * 0.17f + 7.1f, z * 0.17f + 3.9f);
            float dirt = DirtMaxWeight * Smooth01((dirtNoise - 0.55f) / 0.20f);

            float remain = 1f;
            float sandW = Mathf.Min(sand, remain);
            remain -= sandW;
            float leafW = Mathf.Min(LeafMaxWeight * leaf, remain);
            remain -= leafW;
            float dirtW = Mathf.Min(dirt * remain, remain);
            remain -= dirtW;
            float grassW = Mathf.Max(0f, remain);

            // Песок: sandB получает 25-75% в зависимости от шума.
            float sandNoise = Noise(x * 0.06f + 53.2f, z * 0.06f + 29.4f);
            float sandBShare = Mathf.Lerp(0.25f, 0.75f, sandNoise);
            float sandB = sandW * sandBShare;
            float sandA = sandW - sandB;

            // Трава: C 25-55%, B 20-40% остатка, A — остальное.
            float cNoise = Fbm(x * 0.028f, z * 0.028f);
            float cShare = Mathf.Lerp(0.25f, 0.55f, cNoise);
            float grassC = grassW * cShare;
            float rest = grassW - grassC;

            float bNoise = Fbm(x * 0.045f + 13.7f, z * 0.045f + 8.1f);
            float bShare = Mathf.Lerp(0.20f, 0.40f, bNoise);
            float grassB = rest * bShare;
            float grassA = rest - grassB;

            float sum = dirtW + grassA + sandA + leafW + grassB + sandB + grassC;
            if (sum > 1e-5f)
            {
                float inv = 1f / sum;
                dirtW *= inv;
                grassA *= inv;
                sandA *= inv;
                leafW *= inv;
                grassB *= inv;
                sandB *= inv;
                grassC *= inv;
            }
            else
            {
                dirtW = 0f;
                grassA = 1f;
                sandA = 0f;
                leafW = 0f;
                grassB = 0f;
                sandB = 0f;
                grassC = 0f;
            }

            return new PixelWeights
            {
                Forest = dirtW,
                GrassA = grassA,
                SandA = sandA,
                Leaves = leafW,
                GrassB = grassB,
                SandB = sandB,
                GrassC = grassC
            };
        }

        static float PathMask(float x, float z, float edgeOffset)
        {
            float dx = Mathf.Abs(x - TrailCenterX) + edgeOffset;
            float across = 1f - Smooth01((dx - TrailHalfWidth) / 1.2f);
            if (across <= 0f) return 0f;

            float along =
                Smooth01((z - (TrailStartZ - 2f)) / 2f) *
                (1f - Smooth01((z - TrailEndZ) / 2f));

            return across * along;
        }

        static float CircleMask(float x, float z, float centerX, float centerZ, float radius, float feather, float edgeOffset)
        {
            float dx = x - centerX;
            float dz = z - centerZ;
            float distance = Mathf.Sqrt(dx * dx + dz * dz) + edgeOffset;
            return 1f - Smooth01((distance - radius) / feather);
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static float Noise(float x, float z)
        {
            return Mathf.Clamp01(Mathf.PerlinNoise(x, z));
        }

        static float Fbm(float x, float z)
        {
            float value =
                Mathf.PerlinNoise(x, z) * 0.65f +
                Mathf.PerlinNoise(x * 2.03f + 17.3f, z * 2.03f + 9.1f) * 0.35f;

            return Mathf.Clamp01(value);
        }

        static Terrain FindTerrain(Scene scene, TerrainData data)
        {
            Terrain fallback = null;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;

                foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
                {
                    if (terrain == null) continue;
                    if (terrain.terrainData == data) return terrain;
                    if (fallback == null) fallback = terrain;
                }
            }

            return fallback;
        }

        // ------------------------------------------------------------------ decals

        static void SoftenDecals(PolishReport report)
        {
            var dried = LoadDecalTexture("T_DriedBamboo_Decals", report);
            var flowers = LoadDecalTexture("T_BambooFlowers_Decals", report);

            report.DriedDecal = dried != null ? dried.name : "нет";
            report.FlowersDecal = flowers != null ? flowers.name : "нет";

            int updated = 0;
            if (ConfigureSoftDecal(DecalDriedMaterialPath, dried, report)) updated++;
            if (ConfigureSoftDecal(DecalFlowersMaterialPath, flowers, report)) updated++;

            report.Note($"декали: {updated}/2 материала переведены в Transparent (queue 3000).");
        }

        static Texture2D LoadDecalTexture(string baseName, PolishReport report)
        {
            // Предпочтение: Soft2 → Soft → исходная RGBA-декаль.
            string[] suffixes = { "_Soft2", "_Soft", string.Empty };

            foreach (string suffix in suffixes)
            {
                string path = $"{TextureRoot}/{baseName}{suffix}.png";
                var texture = LoadTexture(
                    path,
                    TextureImporterType.Default,
                    true,
                    2048,
                    true,
                    report,
                    baseName,
                    false);

                if (texture == null) continue;

                if (suffix != "_Soft2")
                    report.Note($"{baseName}: Soft2 отсутствует, взята {baseName}{suffix}.png.");

                return texture;
            }

            report.AddMissing($"{TextureRoot}/{baseName}_Soft2.png");
            report.AddMissing($"{TextureRoot}/{baseName}_Soft.png");
            report.Note($"{baseName}: нет ни Soft2, ни Soft — материал не изменён.");
            return null;
        }

        static bool ConfigureSoftDecal(string materialPath, Texture2D texture, PolishReport report)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                report.AddMissing(materialPath);
                Debug.LogWarning($"ShadowTan: не найден {materialPath} — декаль пропущена.");
                return false;
            }

            if (texture == null)
            {
                report.Note($"{material.name}: текстура не найдена — материал не изменён.");
                return false;
            }

            if (material.HasProperty("_BaseColorMap"))
            {
                material.SetTexture("_BaseColorMap", texture);
                material.SetTextureScale("_BaseColorMap", Vector2.one);
                material.SetTextureOffset("_BaseColorMap", Vector2.zero);
            }

            SetFloatIfExists(material, "_SurfaceType", 1f);       // Transparent
            SetFloatIfExists(material, "_BlendMode", 0f);         // Alpha
            SetFloatIfExists(material, "_AlphaCutoffEnable", 0f);
            SetFloatIfExists(material, "_AlphaCutoff", 0f);
            SetFloatIfExists(material, "_ZWrite", 0f);
            SetFloatIfExists(material, "_CullMode", 0f);
            SetFloatIfExists(material, "_CullModeForward", 0f);
            SetFloatIfExists(material, "_TransparentCullMode", 0f);

            // Blend-факторы Alpha-режима HDRP Lit: src * 1 + dst * (1 - src.a).
            // Без них transparent-пасс остаётся с One/Zero из opaque-прошлого.
            SetFloatIfExists(material, "_SrcBlend", 1f);          // One
            SetFloatIfExists(material, "_DstBlend", 10f);         // OneMinusSrcAlpha
            SetFloatIfExists(material, "_DstBlend2", 10f);        // OneMinusSrcAlpha
            SetFloatIfExists(material, "_AlphaSrcBlend", 1f);     // One
            SetFloatIfExists(material, "_AlphaDstBlend", 10f);    // OneMinusSrcAlpha
            SetFloatIfExists(material, "_TransparentZWrite", 0f);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ALPHA");
            material.DisableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;

            EditorUtility.SetDirty(material);

            report.Note($"{material.name}: _BaseColorMap = {texture.name}, alpha blend, renderQueue 3000.");
            return true;
        }

        static void SetFloatIfExists(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        // ------------------------------------------------------------------ textures

        static Texture2D LoadTexture(
            string path,
            TextureImporterType type,
            bool sRGB,
            int maxSize,
            bool alphaIsTransparency,
            PolishReport report,
            string label,
            bool reportMissing = true)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (texture == null)
            {
                // Файл уже на диске, но ещё не импортирован (например, его только
                // что сгенерировал другой проход).
                if (!File.Exists(path))
                {
                    if (reportMissing) report.AddMissing(path);
                    return null;
                }

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null)
                {
                    if (reportMissing) report.AddMissing(path);
                    return null;
                }
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;

                if (importer.textureType != type)
                {
                    importer.textureType = type;
                    changed = true;
                }
                if (importer.sRGBTexture != sRGB)
                {
                    importer.sRGBTexture = sRGB;
                    changed = true;
                }
                if (importer.maxTextureSize != maxSize)
                {
                    importer.maxTextureSize = maxSize;
                    changed = true;
                }
                if (alphaIsTransparency && !importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
            }

            report.Note($"{label}: {path}");
            return texture;
        }

        // ------------------------------------------------------------------ misc

        static string DescribeLayers(TerrainLayer[] layers)
        {
            var names = new List<string>(layers.Length);
            foreach (var layer in layers)
                names.Add(layer != null ? layer.name : "<null>");

            return string.Join(", ", names);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        struct PixelWeights
        {
            public float Forest;
            public float GrassA;
            public float SandA;
            public float Leaves;
            public float GrassB;
            public float SandB;
            public float GrassC;
        }

        sealed class PolishReport
        {
            public int TerrainLayerCount;
            public bool HasCoverage;
            public float GrassCShare = -1f;
            public float GrassBShareOfGrass = -1f;
            public readonly float[] Coverage = new float[AutumnGroundPolish.LayerCount];
            public string DriedDecal = "не изменялся";
            public string FlowersDecal = "не изменялся";

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

            public void Log(bool sceneSaved)
            {
                var sb = new StringBuilder();
                sb.AppendLine(sceneSaved
                    ? "ShadowTan: земля Autumn отполирована, сцена и ассеты сохранены."
                    : "ShadowTan: земля Autumn отполирована, но сцена НЕ сохранена (см. ошибки выше).");
                sb.AppendLine($"  Сцена: {ScenePath}");
                sb.AppendLine($"  Слоёв terrain: {TerrainLayerCount} (forest, grassA, sandA, leaves, grassB, sandB, grassC)");

                if (HasCoverage)
                {
                    sb.AppendLine(
                        "  Покрытие (средняя доля по всей площади terrain): " +
                        $"forest {Coverage[ForestLayer]:0.0}%, " +
                        $"grassA {Coverage[GrassALayer]:0.0}%, sandA {Coverage[SandALayer]:0.0}%, " +
                        $"leaves {Coverage[LeavesLayer]:0.0}%, grassB {Coverage[GrassBLayer]:0.0}%, " +
                        $"sandB {Coverage[SandBLayer]:0.0}%, grassC {Coverage[GrassCLayer]:0.0}%");
                    sb.AppendLine(
                        $"  Grass C: {GrassCShare:0.0}% травяной площади; " +
                        $"grass B: {GrassBShareOfGrass:0.0}% всей травы (B берёт 20-40% остатка после C).");
                }
                else
                {
                    sb.AppendLine("  Grass C: альфамапы не пересчитывались.");
                }

                sb.AppendLine($"  Декали: dried {DriedDecal}; flowers {FlowersDecal}; alpha blend, queue 3000.");

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
