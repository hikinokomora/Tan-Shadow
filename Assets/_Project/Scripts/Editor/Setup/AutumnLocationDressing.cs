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
    // Драссинг готового грейбокса Autumn_Gameplay загруженными ассетами:
    // HDRP/Lit-материалы на существующие плоскости, модель монумента, плотный
    // экран сухой травы, альфа-клип декали на земле. Бамбук не добавляется:
    // его сажает AutumnSceneBuilder.
    //
    // Запускать ПОСЛЕ ShadowTan/Setup/Art/Build Autumn Location. Скрипт не создаёт
    // сцену заново, работает в уже собранной и сохраняет её. Повторный запуск
    // пересобирает только собственные контейнеры (Foliage_DryGrass, GroundDecals,
    // SM_Monument), чтобы не плодить дубли.
    //
    // Допущения:
    //  - HDRP Lit использует один тайлинг (_BaseColorMap_ST) для base/normal/mask,
    //    поэтому SetTextureScale вызывается для всех трёх карт, но видимый результат
    //    задаёт базовая карта.
    //  - Примитив Plane в Unity уже лежит в XZ-плоскости нормалью вверх, поэтому
    //    поворот X=90 не ставится: он поставил бы декаль вертикально. Верх — identity
    //    плюс случайный yaw.
    //  - Высота лесной подстилки -0.2, площадок 0; трава и декали ставятся на
    //    фактическую поверхность + 0.02, а не на 0.02 абсолютных.
    //  - Тропа 3 м, бамбуковые стены ±2.25 (толщина 1.5), кольцо арены r=27
    //    с толщиной 2 м. Внешнее кольцо травы при необходимости поджимается
    //    внутрь кольца, чтобы трава была видна с арены.
    //  - Монумент ставится через пустой wrapper в (70,0,188): сама модель
    //    масштабируется до 5 м и опускается основанием на y=0, на wrapper
    //    висит BoxCollider 1.6 x 5 x 1.4.
    public static class AutumnLocationDressing
    {
        const string ScenePath = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        const string LocationRoot = "Assets/_Project/Art/Environment/Autumn/Location";
        const string MaterialRoot = LocationRoot + "/Materials";
        const string TextureRoot = LocationRoot + "/Textures";
        const string ModelRoot = LocationRoot + "/Models";

        // Геометрия из AutumnSceneBuilder: менять только вместе с ним.
        static readonly Vector3 ArenaCenter = new Vector3(70f, 0f, 188f);
        static readonly Vector3 ClearingCenter = new Vector3(70f, 0f, 0f);
        const float ClearingRadius = 16f;
        const float ArenaRadius = 26f;
        const float ForestFloorY = -0.2f;
        const float PadTopY = 0f;
        const float TrailCenterX = 70f;
        const float TrailHalfWidth = 1.8f;
        const float TrailStartZ = 12f;
        const float TrailEndZ = 162f;

        // Тёплый оттенок тропы: rocky soil сам по себе серо-коричневый,
        // тинт делает его песком с вкраплениями камня.
        static readonly Color PathTintSrgb = new Color(1.00f, 0.94f, 0.82f, 1f);
        static readonly Color WallTintSrgb = new Color(0.50f, 0.54f, 0.58f, 1f);
        static readonly Color ForestTintSrgb = new Color(0.30f, 0.22f, 0.14f, 1f);
        static readonly Color SlopeTintSrgb = new Color(0.58f, 0.55f, 0.50f, 1f);
        static readonly Color ArenaTintSrgb = new Color(0.96f, 0.90f, 0.78f, 1f);
        static readonly Color MetalTintSrgb = new Color(0.55f, 0.60f, 0.66f, 1f);

        enum TextureRole
        {
            BaseColor,
            Normal,
            Mask,
            Decal
        }

        [MenuItem("ShadowTan/Setup/Art/Dress Autumn Location")]
        public static void Dress()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null)
            {
                Debug.LogError($"ShadowTan: не найдена сцена {ScenePath}. Сначала собери её Build Autumn Location.");
                return;
            }

            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                // Не теряем несохранённые правки в текущей сцене молча.
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    Debug.LogWarning("ShadowTan: открытие Autumn_Gameplay отменено — драссинг не выполнен.");
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

            Transform level = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "[Level]")
                {
                    level = root.transform;
                    break;
                }
            }

            if (level == null)
            {
                Debug.LogError("ShadowTan: в сцене нет корня [Level]. Похоже, это не Autumn_Gameplay.");
                return;
            }

            var props = level.Find("Greybox/Props");
            var wallGrey = level.Find("Greybox/Walls/Wall_Grey");
            if (props == null)
            {
                Debug.LogError("ShadowTan: не найден [Level]/Greybox/Props. Пересобери грейбокс.");
                return;
            }

            if (wallGrey == null)
                Debug.LogWarning("ShadowTan: не найден Wall_Grey — материал стены не применён.");

            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("ShadowTan: не найден шейдер HDRP/Lit. Проверь render pipeline.");
                return;
            }

            var report = new DressingReport();
            bool saved = false;

            try
            {
                var materials = BuildMaterials(shader, report);
                ApplyGroundMaterials(level, wallGrey, materials, report);

                var trail = level.Find("Greybox/Ground/Trail");
                if (trail != null && trail.lossyScale.x > 3.5f)
                {
                    report.Note(
                        "в сцене ещё старая тропа " + trail.lossyScale.x.ToString("0.0") +
                        " м; координаты травы рассчитаны на новую 3-м тропу — пересобери сцену Build Autumn Location.");
                }

                report.Note("камни: SlopeStones получают M_Autumn_Stone; меши из cobble/pebble паков раскидываются как декор.");

                var oldRockPiles = props.Find("SlopeRockClusters");
                if (oldRockPiles != null)
                {
                    Object.DestroyImmediate(oldRockPiles.gameObject);
                    report.Note("старый контейнер SlopeRockClusters удалён: камни пересобираются в StoneDecor.");
                }

                BuildMonument(props, materials.Metal, scene, report);
                BuildFoliage(level, props, shader, scene, report);
                BuildStoneScatter(props, materials, scene, report);
                BuildGroundDecals(props, shader, report);
            }
            catch (Exception e)
            {
                Debug.LogError($"ShadowTan: драссинг прерван исключением, сцена будет сохранена как есть.\n{e}");
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
                    Debug.LogError($"ShadowTan: не удалось сохранить ассеты материалов.\n{e}");
                }

                report.Log(saved);
            }
        }

        // ---------------------------------------------------------------- materials

        static DressingMaterials BuildMaterials(Shader shader, DressingReport report)
        {
            var materials = new DressingMaterials();

            // Один материал не может иметь два тайлинга, поэтому тропа и поляна
            // получают два ассета из одних и тех же карт RockySoil.
            Color pathTint = Linear(PathTintSrgb);
            Color wallTint = Linear(WallTintSrgb);
            Color forestTint = Linear(ForestTintSrgb);
            Color slopeTint = Linear(SlopeTintSrgb);
            Color arenaTint = Linear(ArenaTintSrgb);
            Color metalTint = Linear(MetalTintSrgb);

            materials.Wall = ConfigureMaterial(shader, "M_Autumn_Wall", "T_CastleWall", new Vector2(28f, 12f), wallTint, report);
            materials.Grass = ConfigureMaterial(shader, "M_Autumn_Grass", "T_RockySoil", new Vector2(80f, 80f), forestTint, report);
            materials.Stone = ConfigureMaterial(shader, "M_Autumn_Stone", "T_Cobble", new Vector2(16f, 3f), slopeTint, report);
            materials.SlopeStone = ConfigureMaterial(shader, "M_Autumn_SlopeStone", "T_Cobble", new Vector2(80f, 8f), slopeTint, report);
            materials.CurbStone = ConfigureMaterial(shader, "M_Autumn_CurbStone", "T_Cobble", new Vector2(150f, 2f), slopeTint, report);
            materials.Sand = ConfigureMaterial(shader, "M_Autumn_Sand", "T_RockySoil", new Vector2(12f, 12f), arenaTint, report, 0f, 0.12f);
            materials.Metal = ConfigureMaterial(shader, "M_Autumn_Metal", "T_Monument", Vector2.one, metalTint, report, 1f, 1f, true);

            // Tripo-текстура почти чёрная; для «титанового» монумента берём
            // чистый холодный металл, а не baked basecolor. Маску оставляем:
            // R = metallic, G = AO, A = smoothness (1 - roughness).
            if (materials.Metal != null)
            {
                materials.Metal.SetTexture("_BaseColorMap", null);
                materials.Metal.SetColor("_BaseColor", Linear(new Color(0.62f, 0.66f, 0.70f, 1f)));
                SetFloatIfExists(materials.Metal, "_Metallic", 1f);
                SetFloatIfExists(materials.Metal, "_Smoothness", 1f);
                EditorUtility.SetDirty(materials.Metal);
            }

            materials.Path = ConfigureMaterial(shader, "M_Autumn_Path", "T_RockySoil", new Vector2(2f, 30f), pathTint, report);
            materials.PathClearing = ConfigureMaterial(shader, "M_Autumn_Path_Clearing", "T_RockySoil", new Vector2(7f, 7f), pathTint, report);
            materials.EntranceStone = ConfigureMaterial(shader, "M_Autumn_EntranceStone", "T_Cobble", new Vector2(4f, 4f), slopeTint, report);
            materials.Pebbles = ConfigureMaterial(shader, "M_Autumn_Pebbles", "T_Pebbles", Vector2.one, Color.white, report);
            materials.CobbleDecor = ConfigureMaterial(shader, "M_Autumn_CobbleDecor", "T_Cobble", Vector2.one, slopeTint, report);

            return materials;
        }

        static Material ConfigureMaterial(
            Shader shader,
            string materialName,
            string textureBaseName,
            Vector2 tiling,
            Color baseColor,
            DressingReport report,
            float metallic = 0f,
            float smoothness = 1f,
            bool useMask = true)
        {
            var material = LoadOrCreateMaterial(shader, materialName, report);
            if (material == null) return null;

            var baseMap = LoadMap($"{TextureRoot}/{textureBaseName}_BaseColor.png", TextureRole.BaseColor, report);
            var normal = LoadMap($"{TextureRoot}/{textureBaseName}_Normal.png", TextureRole.Normal, report);
            var mask = useMask ? LoadMap($"{TextureRoot}/{textureBaseName}_Mask.png", TextureRole.Mask, report) : null;

            if (baseMap == null)
            {
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
                SetFloatIfExists(material, "_Metallic", metallic);
                SetFloatIfExists(material, "_Smoothness", smoothness);
                EditorUtility.SetDirty(material);
                report.Note($"{materialName}: BaseColor отсутствует — материал оставлен с базовым цветом.");
                return material;
            }

            ApplyTextures(material, baseMap, normal, mask, tiling, baseColor, metallic, smoothness);
            report.MaterialsUpdated++;
            return material;
        }

        static Material LoadOrCreateMaterial(Shader shader, string name, DressingReport report)
        {
            var path = $"{MaterialRoot}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material == null)
            {
                // Билдер должен был создать материал; создаём только как аварийный путь.
                EnsureFolder(MaterialRoot);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
                report.Note($"материал {path} отсутствовал — создан с HDRP/Lit.");
            }

            if (material.shader != shader)
            {
                material.shader = shader;
                report.Note($"{name}: шейдер заменён на HDRP/Lit.");
            }

            return material;
        }

        static void ApplyTextures(
            Material material,
            Texture2D baseMap,
            Texture2D normal,
            Texture2D mask,
            Vector2 tiling,
            Color baseColor,
            float metallic = 0f,
            float smoothness = 1f)
        {
            material.SetTexture("_BaseColorMap", baseMap);
            if (normal != null)
            {
                material.SetTexture("_NormalMap", normal);
                // HDRP Lit использует карту только при включённых ключах; без них
                // нормаль игнорируется, даже если текстура назначена.
                SetFloatIfExists(material, "_NormalMapSpace", 0f);
                material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
                material.EnableKeyword("_NORMALMAP");
            }
            if (mask != null)
            {
                material.SetTexture("_MaskMap", mask);
                material.EnableKeyword("_MASKMAP");
            }
            else
            {
                // Металл монумента задаётся напрямую: в Tripo-маске нет metallic-канала.
                material.SetTexture("_MaskMap", null);
                material.DisableKeyword("_MASKMAP");
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
            SetFloatIfExists(material, "_Smoothness", smoothness);
            SetFloatIfExists(material, "_Metallic", metallic);
            SetFloatIfExists(material, "_SmoothnessRemapMin", 0f);
            SetFloatIfExists(material, "_SmoothnessRemapMax", 1f);
            SetFloatIfExists(material, "_UVBase", 0f);
            SetFloatIfExists(material, "_NormalScale", 1f);

            // HDRP Lit читает тайлинг всех карт из _BaseColorMap_ST; вызовы для normal/mask
            // оставлены по требованию задачи и безвредны.
            SetTiling(material, "_BaseColorMap", tiling);
            SetTiling(material, "_NormalMap", tiling);
            SetTiling(material, "_MaskMap", tiling);

            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        static Material BuildWheatGrassMaterial(Shader shader, DressingReport report)
        {
            var baseMap = LoadMap($"{TextureRoot}/T_WheatGrass_BaseColor.png", TextureRole.BaseColor, report);
            if (baseMap == null)
            {
                report.Note("T_WheatGrass отсутствует — у моделей травы остаются их собственные материалы.");
                return null;
            }

            var normal = LoadMap($"{TextureRoot}/T_WheatGrass_Normal.png", TextureRole.Normal, report);
            var mask = LoadMap($"{TextureRoot}/T_WheatGrass_Mask.png", TextureRole.Mask, report);

            var material = LoadOrCreateMaterial(shader, "M_Autumn_WheatGrass", report);
            ApplyTextures(material, baseMap, normal, mask, Vector2.one, Color.white);

            // Трава из карточек: альфа-клип и двусторонний рендер. HDRP включает
            // двойную сторону через _DoubleSidedEnable; _CullMode дублируем вручную,
            // чтобы не зависеть от прогона material setup.
            SetFloatIfExists(material, "_AlphaCutoffEnable", 1f);
            SetFloatIfExists(material, "_AlphaCutoff", 0.4f);
            SetFloatIfExists(material, "_SurfaceType", 0f);
            SetFloatIfExists(material, "_DoubleSidedEnable", 1f);
            SetFloatIfExists(material, "_DoubleSidedNormalMode", 1f); // Mirror
            SetFloatIfExists(material, "_CullMode", 0f);
            SetFloatIfExists(material, "_CullModeForward", 0f);
            if (material.HasProperty("_DoubleSidedConstants"))
                material.SetVector("_DoubleSidedConstants", new Vector4(1f, 1f, -1f, 0f));
            material.EnableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_DOUBLESIDED_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;

            report.MaterialsUpdated++;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Material[] BuildWheatGrassMaterials(Shader shader, DressingReport report)
        {
            var baseMaterial = BuildWheatGrassMaterial(shader, report);
            if (baseMaterial == null) return System.Array.Empty<Material>();

            // Один и тот же атлас травы, разные тинты: сухая, золотая, бурая,
            // зелёная и бледная. Так лес не выглядит одним зелёным ковром.
            string[] names =
            {
                "M_Autumn_WheatGrass_Dry",
                "M_Autumn_WheatGrass_Gold",
                "M_Autumn_WheatGrass_Brown",
                "M_Autumn_WheatGrass_Green",
                "M_Autumn_WheatGrass_Pale"
            };
            Color[] tints =
            {
                new Color(1.00f, 0.93f, 0.78f, 1f),
                new Color(0.95f, 0.78f, 0.50f, 1f),
                new Color(0.62f, 0.50f, 0.34f, 1f),
                new Color(0.62f, 0.68f, 0.45f, 1f),
                new Color(0.85f, 0.82f, 0.70f, 1f)
            };

            var result = new List<Material>(names.Length + 1) { baseMaterial };
            for (int i = 0; i < names.Length; i++)
            {
                var path = $"{MaterialRoot}/{names[i]}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(baseMaterial);
                    material.name = names[i];
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = baseMaterial.shader;
                }

                material.CopyPropertiesFromMaterial(baseMaterial);
                material.SetColor("_BaseColor", Linear(tints[i]));

                SetFloatIfExists(material, "_AlphaCutoffEnable", 1f);
                SetFloatIfExists(material, "_AlphaCutoff", 0.4f);
                SetFloatIfExists(material, "_SurfaceType", 0f);
                SetFloatIfExists(material, "_DoubleSidedEnable", 1f);
                SetFloatIfExists(material, "_CullMode", 0f);
                SetFloatIfExists(material, "_CullModeForward", 0f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_DOUBLESIDED_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                result.Add(material);
            }

            report.MaterialsUpdated += names.Length;

            // Сухая/золотая/бурая трава встречается чаще зелёной: иначе
            // осенний лес читается как один ярко-зелёный ковёр.
            var weighted = new List<Material>
            {
                result[0], result[1], result[1], result[2], result[2],
                result[3], result[3], result[5], result[4]
            };
            report.Note($"трава: {result.Count} материалов, {weighted.Count} слотов с тёплым перевесом.");
            return weighted.ToArray();
        }

        static Material ConfigureDecalMaterial(Shader shader, string name, Texture2D map, DressingReport report)
        {
            if (map == null) return null;

            var material = LoadOrCreateMaterial(shader, name, report);
            material.SetTexture("_BaseColorMap", map);

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            SetFloatIfExists(material, "_Smoothness", 0.1f);
            SetFloatIfExists(material, "_Metallic", 0f);
            SetFloatIfExists(material, "_UVBase", 0f);

            // Alpha-clip: фон декали полностью отсекается, квадратных
            // полупрозрачных подложек нет. Радиальный feather в текстуре
            // оставляет мягкий, но чёткий край на пороге 0.35.
            SetFloatIfExists(material, "_AlphaCutoffEnable", 1f);
            SetFloatIfExists(material, "_AlphaCutoff", 0.35f);
            SetFloatIfExists(material, "_SurfaceType", 0f);
            SetFloatIfExists(material, "_BlendMode", 0f);
            SetFloatIfExists(material, "_ZWrite", 1f);
            SetFloatIfExists(material, "_TransparentZWrite", 0f);
            SetFloatIfExists(material, "_EnableFogOnTransparent", 0f);
            SetFloatIfExists(material, "_CullMode", 2f);
            SetFloatIfExists(material, "_CullModeForward", 2f);

            SetTiling(material, "_BaseColorMap", Vector2.one);
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_BLENDMODE_ALPHA");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.enableInstancing = true;

            report.MaterialsUpdated++;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SetTiling(Material material, string property, Vector2 tiling)
        {
            if (material.GetTexture(property) == null) return;
            material.SetTextureScale(property, tiling);
            material.SetTextureOffset(property, Vector2.zero);
        }

        static void SetFloatIfExists(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        static Color Linear(Color color)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
        }

        // ---------------------------------------------------------------- ground materials

        static void ApplyGroundMaterials(Transform level, Transform wallGrey, DressingMaterials materials, DressingReport report)
        {
            int wallCount = 0;
            int grassCount = 0;
            int slopeCount = 0;
            int slopeStoneCount = 0;
            int sandCount = 0;
            int trailCount = 0;
            int clearingCount = 0;

            foreach (var renderer in level.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                var t = renderer.transform;

                if (materials.Wall != null && wallGrey != null && t.IsChildOf(wallGrey))
                {
                    SetMaterial(renderer, materials.Wall);
                    wallCount++;
                    continue;
                }

                string objectName = t.name;

                if (materials.Grass != null && objectName == "ForestFloor")
                {
                    SetMaterial(renderer, materials.Grass);
                    grassCount++;
                    continue;
                }

                if (materials.SlopeStone != null && objectName == "Slope")
                {
                    SetMaterial(renderer, materials.SlopeStone);
                    slopeCount++;
                    continue;
                }

                if (materials.CurbStone != null && objectName.StartsWith("StoneCurb", StringComparison.Ordinal))
                {
                    SetMaterial(renderer, materials.CurbStone);
                    continue;
                }

                if (materials.Stone != null && objectName.StartsWith("SlopeStone", StringComparison.Ordinal))
                {
                    SetMaterial(renderer, materials.Stone);
                    slopeStoneCount++;
                    continue;
                }

                if (materials.Path != null && objectName == "Trail")
                {
                    SetMaterial(renderer, materials.Path);
                    trailCount++;
                    continue;
                }

                if (materials.PathClearing != null && objectName == "Clearing")
                {
                    SetMaterial(renderer, materials.PathClearing);
                    clearingCount++;
                    continue;
                }

                if (materials.EntranceStone != null && objectName == "EntranceFloor")
                {
                    SetMaterial(renderer, materials.EntranceStone);
                    continue;
                }

                if (materials.Sand != null && objectName == "Arena")
                {
                    SetMaterial(renderer, materials.Sand);
                    sandCount++;
                    continue;
                }
            }

            // Коллизии бамбуковых стен остаются, но их коробки не рисуем:
            // визуальную стену даёт сам бамбук, а тропа не тонет в тени.
            foreach (var containerName in new[] { "Greybox/Walls/BambooWalls", "Greybox/Walls/BambooRing" })
            {
                var container = level.Find(containerName);
                if (container == null) continue;
                foreach (var r in container.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }

            report.SlopeStoneRenderers = slopeStoneCount;
            report.Note(
                $"материалы земли: Wall {wallCount}, ForestFloor {grassCount}, Slope {slopeCount}, " +
                $"SlopeStones {slopeStoneCount}, Trail {trailCount} (M_Autumn_Path), " +
                $"Clearing {clearingCount} (M_Autumn_Path_Clearing), Arena {sandCount} (M_Autumn_Sand).");
        }

        static void SetMaterial(Renderer renderer, Material material)
        {
            if (renderer == null || material == null) return;

            int slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
            var slots = new Material[slotCount];
            for (int i = 0; i < slotCount; i++) slots[i] = material;
            renderer.sharedMaterials = slots;
        }

        // ---------------------------------------------------------------- monument

        static void BuildMonument(Transform props, Material metal, Scene scene, DressingReport report)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/SM_Monument.fbx");
            var cube = props.Find("Monument_Base");

            if (model == null)
            {
                report.AddMissing($"{ModelRoot}/SM_Monument.fbx (оставлен куб Monument_Base)");
                report.MonumentSource = "Monument_Base (куб, FBX отсутствует)";
                report.MonumentHeight = 5f;
                report.MonumentRawHeight = 5f;
                report.MonumentScaleFactor = 1f;
                if (cube != null) AssignAll(cube.gameObject, metal);
                return;
            }

            var previous = props.Find("SM_Monument");

            var modelGo = PrefabUtility.InstantiatePrefab(model, scene) as GameObject;
            if (modelGo == null)
            {
                report.Note("PrefabUtility не смог инстанцировать SM_Monument — оставлен куб.");
                if (cube != null) AssignAll(cube.gameObject, metal);
                report.MonumentSource = "Monument_Base (куб, инстанцирование не удалось)";
                report.MonumentHeight = 5f;
                report.MonumentRawHeight = 5f;
                report.MonumentScaleFactor = 1f;
                return;
            }

            modelGo.name = "SM_Monument_Model";
            modelGo.transform.localRotation = Quaternion.identity;
            modelGo.transform.localPosition = Vector3.zero;

            if (!TryGetBounds(modelGo, out var rawBounds) || rawBounds.size.y <= 1e-4f)
            {
                Object.DestroyImmediate(modelGo);
                report.Note("у SM_Monument нет рендереров — оставлен куб.");
                if (cube != null) AssignAll(cube.gameObject, metal);
                report.MonumentSource = "Monument_Base (куб, у FBX нет рендереров)";
                report.MonumentHeight = 5f;
                report.MonumentRawHeight = 5f;
                report.MonumentScaleFactor = 1f;
                return;
            }

            // Tripo FBX приходит Z-up: в Unity длинная ось лежит вдоль Z,
            // то есть монумент лежит плашмя. Поднимаем его на -90° по X
            // и проверяем, что высота действительно стала по Y.
            if (rawBounds.size.z > rawBounds.size.y * 1.4f)
            {
                modelGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                if (!TryGetBounds(modelGo, out rawBounds) || rawBounds.size.y < rawBounds.size.z * 0.9f)
                {
                    modelGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    TryGetBounds(modelGo, out rawBounds);
                }
            }

            // FBX ~0.978 м по высоте после разворота: коэффициент ~5.11.
            // Считаем от bounds, чтобы не зависеть от точной высоты экспорта.
            float rawHeight = rawBounds.size.y;
            float factor = 5f / rawHeight;
            modelGo.transform.localScale *= factor;

            // Пустой wrapper держит ровно (70,0,188); модель внутри садится
            // основанием на y=0 и центрируется по X/Z.
            var wrapper = new GameObject("SM_Monument");
            wrapper.transform.SetParent(props, false);
            wrapper.transform.position = ArenaCenter;
            wrapper.transform.rotation = Quaternion.identity;

            modelGo.transform.SetParent(wrapper.transform, false);
            modelGo.transform.localPosition = Vector3.zero;

            if (TryGetBounds(modelGo, out var bounds))
            {
                modelGo.transform.position += new Vector3(
                    wrapper.transform.position.x - bounds.center.x,
                    wrapper.transform.position.y - bounds.min.y,
                    wrapper.transform.position.z - bounds.center.z);
            }

            AssignAll(modelGo, metal);

            int wallLayer = ResolveLayer("Wall");
            foreach (var t in wrapper.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = wallLayer;

            EnsureLodGroupIsUsable(modelGo, report);
            int fbxColliders = modelGo.GetComponentsInChildren<Collider>(true).Length;

            var box = wrapper.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 2.5f, 0f);
            box.size = new Vector3(1.6f, 5f, 1.4f);
            wrapper.layer = wallLayer;

            if (previous != null)
            {
                Object.DestroyImmediate(previous.gameObject);
                report.Note("предыдущий SM_Monument заменён новым.");
            }

            if (cube != null)
            {
                Object.DestroyImmediate(cube.gameObject);
                report.Note("куб Monument_Base заменён моделью SM_Monument.");
            }

            report.MonumentRawHeight = rawHeight;
            report.MonumentScaleFactor = factor;
            report.MonumentSource = $"SM_Monument (×{factor:0.00}, BoxCollider 1.6×5×1.4, FBX colliders {fbxColliders})";

            if (TryGetBounds(wrapper, out var finalBounds))
            {
                report.MonumentHeight = finalBounds.size.y;
                report.Note($"монумент: основание y={finalBounds.min.y:0.00}, высота {finalBounds.size.y:0.00} м.");
            }
            else
            {
                report.MonumentHeight = 5f;
            }
        }

        static void EnsureLodGroupIsUsable(GameObject go, DressingReport report)
        {
            var group = go.GetComponentInChildren<LODGroup>(true);
            if (group == null) return;

            var lods = group.GetLODs();
            bool lod0Valid = lods.Length > 0 && lods[0].renderers != null && lods[0].renderers.Length > 0;

            if (!lod0Valid)
            {
                Object.DestroyImmediate(group);
                report.Note("у монумента удалён нерабочий LODGroup (пустой LOD0).");
                return;
            }

            report.Note($"у монумента LODGroup: {lods.Length} уровн., в LOD0 {lods[0].renderers.Length} рендереров.");
        }

        // ---------------------------------------------------------------- foliage

        static void BuildFoliage(Transform level, Transform props, Shader shader, Scene scene, DressingReport report)
        {
            var models = new List<GameObject>();
            foreach (var letter in new[] { "A", "B", "C", "D" })
            {
                var path = $"{ModelRoot}/SM_WheatGrass_{letter}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) report.AddMissing(path);
                else models.Add(model);
            }

            if (models.Count == 0)
            {
                report.Note("сухая трава пропущена: нет ни одного SM_WheatGrass_A-D.fbx.");
                return;
            }

            var grassMaterials = BuildWheatGrassMaterials(shader, report);
            var rawHeights = MeasureGrassHeights(models, scene, report);
            var root = ResetContainer(props, "Foliage_DryGrass", report);
            var rnd = new System.Random(23);
            var modelArray = models.ToArray();

            int total = 0;
            int trailCount = 0;
            int arenaCount = 0;
            int clearingCount = 0;

            // 1) Один очень плотный ряд с каждой стороны тропы. Кусты наклонены
            //    к центру, поэтому смыкаются аркой и закрывают примерно половину
            //    ширины тропы. Шаг 0.8 м при ширине куста 1.4-1.7 м даёт сплошную стену.
            float[] lateralOffsets = { -3.4f, 3.4f };
            for (float z = 13.0f; z <= TrailEndZ + 0.01f; z += 0.8f)
            {
                foreach (float offset in lateralOffsets)
                {
                    float x = TrailCenterX + offset + Range(rnd, -0.15f, 0.15f);
                    float zz = z + Range(rnd, -0.25f, 0.25f);
                    Vector3 position = new Vector3(x, GroundTopY(x, zz), zz);
                    float tilt = 0f;
                    if (PlaceGrass(root, modelArray, rawHeights, scene, rnd, position, grassMaterials, ref total, tilt))
                        trailCount++;
                }
            }

            // 2) Четыре кольца внутри арены: 23.3..26.0. Внешнее кольцо поджимается
            //    к внутренней грани бамбуковой коллизии, иначе трава скрыта стеной.
            float ringInner = GetBambooRingInnerRadius(level, ArenaRadius);
            float outerArenaRadius = Mathf.Min(26.0f, ringInner - 0.15f);
            if (outerArenaRadius < 25.99f)
            {
                report.Note(
                    $"кольцо арены: внутренняя грань бамбука r≈{ringInner:0.00}; " +
                    $"внешнее кольцо травы {outerArenaRadius:0.00} вместо 26.0, иначе оно скрыто стеной.");
            }

            // Только кромка арены: 24.4..25.9, без двух внутренних колец,
            // которые залезали в песок и читались как «растянутая» трава.
            float[] arenaRadii = { 24.4f, 25.3f, outerArenaRadius };
            for (int ring = 0; ring < arenaRadii.Length; ring++)
            {
                for (int i = 0; i < 100; i++)
                {
                    float angle = i * 3.6f + ring * 1.5f + Range(rnd, -0.7f, 0.7f);
                    Vector3 position = Polar(ArenaCenter, arenaRadii[ring], angle);
                    position.y = GroundTopY(position.x, position.z);
                    if (PlaceGrass(root, modelArray, rawHeights, scene, rnd, position, grassMaterials, ref total))
                        arenaCount++;
                }
            }

            // 3) Три дуги вокруг поляны снаружи r=17.2..20.2, 30 кустов на дугу.
            //    Устье тропы (90°) и насыпь (>148°) не занимаем.
            const int clearingClumps = 30;
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = 17.2f + ring * 1.5f;
                for (int i = 0; i < clearingClumps; i++)
                {
                    float u = (i + 0.5f) / clearingClumps;
                    float angle = u < 0.6f
                        ? Mathf.Lerp(8f, 78f, u / 0.6f)
                        : Mathf.Lerp(102f, 148f, (u - 0.6f) / 0.4f);
                    angle += Range(rnd, -1f, 1f);
                    Vector3 position = Polar(ClearingCenter, radius + Range(rnd, -0.4f, 0.4f), angle);
                    position.y = GroundTopY(position.x, position.z);
                    if (PlaceGrass(root, modelArray, rawHeights, scene, rnd, position, grassMaterials, ref total))
                        clearingCount++;
                }
            }

            report.TrailGrass = trailCount;
            report.ArenaGrass = arenaCount;
            report.ClearingGrass = clearingCount;
            report.GrassClumps = total;
        }

        static float[] MeasureGrassHeights(List<GameObject> models, Scene scene, DressingReport report)
        {
            var heights = new float[models.Count];
            var sb = new StringBuilder();

            for (int i = 0; i < models.Count; i++)
            {
                var probe = PrefabUtility.InstantiatePrefab(models[i], scene) as GameObject;
                if (probe == null) continue;

                // Меряем в той же ориентации, в которой ставим кусты (identity + yaw).
                probe.transform.rotation = Quaternion.identity;
                if (TryGetBounds(probe, out var bounds)) heights[i] = bounds.size.y;
                Object.DestroyImmediate(probe);

                sb.Append($"{models[i].name}={heights[i]:0.00} ");
            }

            report.Note($"высоты FBX травы: {sb.ToString().Trim()}; целевая высота куста 1.5-1.7 м.");
            return heights;
        }

        static bool PlaceGrass(
            Transform root,
            GameObject[] models,
            float[] rawHeights,
            Scene scene,
            System.Random rnd,
            Vector3 position,
            Material[] materials,
            ref int total,
            float tiltDegrees = 0f)
        {
            int index = rnd.Next(models.Length);
            var go = PrefabUtility.InstantiatePrefab(models[index], scene) as GameObject;
            if (go == null) return false;

            go.name = $"DryGrass_{total:000}";
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f);
            if (Mathf.Abs(tiltDegrees) > 0.01f)
            {
                // Мировой наклон вокруг оси тропы (Z): верх куста уходит к центру.
                go.transform.rotation = Quaternion.AngleAxis(tiltDegrees, Vector3.forward) * go.transform.rotation;
            }

            // Каждый вариант FBX имеет свою высоту 1.12-1.33 м; масштабируем
            // до целевых 1.6-1.8 м, сохраняя исходный масштаб префаба.
            float targetHeight = Range(rnd, 1.6f, 1.8f);
            float rawHeight = rawHeights[index];
            if (rawHeight > 1e-3f) go.transform.localScale *= targetHeight / rawHeight;

            // Шире, но той же высоты: меньше кустов на ту же стену травы.
            // Это дешевле, чем ещё сотни инстансов, и лучше закрывает щели.
            float widthScale = Range(rnd, 1.1f, 1.3f);
            Vector3 scale = go.transform.localScale;
            go.transform.localScale = new Vector3(scale.x * widthScale, scale.y, scale.z * widthScale);

            // Если пивот модели не на земле, опускаем куст основанием на position.y.
            if (TryGetBounds(go, out var bounds))
            {
                Vector3 p = go.transform.position;
                go.transform.position = new Vector3(p.x, p.y + (position.y - bounds.min.y), p.z);
            }

            if (materials != null && materials.Length > 0)
                AssignAll(go, materials[rnd.Next(materials.Length)]);

            // Трава — тонкие карточки; тени от 1500 кустов дороже, чем дают.
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = ShadowCastingMode.Off;

            RemoveColliders(go);
            total++;
            return true;
        }

        // ---------------------------------------------------------------- stone decor

        static void BuildStoneScatter(Transform props, DressingMaterials materials, Scene scene, DressingReport report)
        {
            // Паки содержат 5/7 отдельных мешей, все с пивотом в origin. Берём меши
            // по одному и раскидываем как самостоятельный декор, а не ставим пачку.
            string[] packPaths =
            {
                $"{ModelRoot}/SM_RoundedCobblePack_Fixed.fbx",
                $"{ModelRoot}/SM_StonesPebblesPack_Fixed.fbx"
            };
            Material[] packMaterials = { materials.CobbleDecor, materials.Pebbles };
            float[] packMin = { 0.25f, 0.12f };
            float[] packMax = { 0.70f, 0.40f };
            int[] packCount = { 45, 60 };

            var root = ResetContainer(props, "StoneDecor", report);
            var rnd = new System.Random(71);
            int total = 0;

            for (int p = 0; p < packPaths.Length; p++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(packPaths[p]);
                if (asset == null)
                {
                    report.AddMissing(packPaths[p]);
                    continue;
                }

                var source = PrefabUtility.InstantiatePrefab(asset, scene) as GameObject;
                if (source == null) continue;

                var meshes = new List<Mesh>();
                foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
                }
                Object.DestroyImmediate(source);

                if (meshes.Count == 0)
                {
                    report.Note($"{packPaths[p]}: мешей не найдено.");
                    continue;
                }

                var material = packMaterials[p] != null ? packMaterials[p] : materials.Stone;
                for (int i = 0; i < packCount[p]; i++)
                {
                    var mesh = meshes[rnd.Next(meshes.Count)];
                    var go = new GameObject($"StoneDecor_{total:000}");
                    go.transform.SetParent(root, false);

                    var filter = go.AddComponent<MeshFilter>();
                    filter.sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                    go.isStatic = true;

                    Vector3 position = StonePosition(rnd, i);
                    go.transform.position = position;
                    go.transform.rotation = Quaternion.Euler(
                        Range(rnd, -6f, 6f),
                        Range(rnd, 0f, 360f),
                        Range(rnd, -6f, 6f));

                    // Масштаб по самому длинному измерению меша: камни от 12 см до 70 см.
                    Vector3 meshSize = mesh.bounds.size;
                    float maxDim = Mathf.Max(meshSize.x, Mathf.Max(meshSize.y, meshSize.z));
                    float target = Range(rnd, packMin[p], packMax[p]);
                    float scale = maxDim > 1e-4f ? target / maxDim : 1f;
                    go.transform.localScale = Vector3.one * scale;

                    // Сажаем основание на грунт и слегка утапливаем, чтобы не висело.
                    if (TryGetBounds(go, out var bounds))
                    {
                        go.transform.position += Vector3.up * (position.y - bounds.min.y - 0.02f);
                    }

                    total++;
                }
            }

            report.StoneDecor = total;
            report.Note($"каменный декор: {total} отдельных мешей из паков, без коллайдеров.");
        }

        static Vector3 StonePosition(System.Random rnd, int index)
        {
            int zone = index % 5;

            if (zone <= 2)
            {
                // Кромки тропы и полоса под бамбуком.
                float side = rnd.Next(2) == 0 ? -1f : 1f;
                float x = TrailCenterX + side * Range(rnd, 1.5f, 7.5f);
                float z = Range(rnd, 14f, 160f);
                return new Vector3(x, GroundTopY(x, z) + 0.01f, z);
            }

            if (zone == 3)
            {
                // Поляна, кроме настила насыпи.
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float angle = Range(rnd, 0f, 360f);
                    float radius = Range(rnd, 2f, 15f);
                    Vector3 p = Polar(ClearingCenter, radius, angle);
                    p.y = PadTopY + 0.01f;
                    if (!IsUnderRamp(p.x, p.z)) return p;
                }
                return new Vector3(ClearingCenter.x + 9f, PadTopY + 0.01f, ClearingCenter.z + 7f);
            }

            // Кромка арены или лесная подстилка.
            if (rnd.Next(2) == 0)
            {
                float angle = Range(rnd, 0f, 360f);
                float radius = Range(rnd, 20f, 25.5f);
                Vector3 p = Polar(ArenaCenter, radius, angle);
                p.y = PadTopY + 0.01f;
                return p;
            }

            float fx = TrailCenterX + (rnd.Next(2) == 0 ? -1f : 1f) * Range(rnd, 3.5f, 10f);
            float fz = Range(rnd, 14f, 230f);
            return new Vector3(fx, GroundTopY(fx, fz) + 0.01f, fz);
        }

        // ---------------------------------------------------------------- decals

        static void BuildGroundDecals(Transform props, Shader shader, DressingReport report)
        {
            var driedTexture = LoadSoftDecal(
                $"{TextureRoot}/T_DriedBamboo_Decals_Soft.png",
                $"{TextureRoot}/T_DriedBamboo_Decals.png",
                report);
            var flowersTexture = LoadSoftDecal(
                $"{TextureRoot}/T_BambooFlowers_Decals_Soft.png",
                $"{TextureRoot}/T_BambooFlowers_Decals.png",
                report);

            var driedMaterial = ConfigureDecalMaterial(shader, "M_Autumn_Decal_DriedBamboo", driedTexture, report);
            var flowersMaterial = ConfigureDecalMaterial(shader, "M_Autumn_Decal_BambooFlowers", flowersTexture, report);

            if (driedMaterial == null && flowersMaterial == null)
            {
                report.Note("декали пропущены: нет T_DriedBamboo_Decals/T_BambooFlowers_Decals.");
                return;
            }

            var root = ResetContainer(props, "GroundDecals", report);
            var rnd = new System.Random(59);
            int driedCount = 0;
            int flowersCount = 0;

            // По 40 штук: 20 на кромках тропы, 10 на поляне, 6 на кромке арены,
            // 4 на лесной подстилке. Крупные и перекрывающиеся, с мягкой альфой.
            const int decalsPerType = 40;
            if (driedMaterial != null)
            {
                for (int i = 0; i < decalsPerType; i++)
                {
                    CreateGroundDecal(root, rnd, driedMaterial, $"GroundDecal_DriedBamboo_{i:00}", i);
                    driedCount++;
                }
            }

            if (flowersMaterial != null)
            {
                for (int i = 0; i < decalsPerType; i++)
                {
                    CreateGroundDecal(root, rnd, flowersMaterial, $"GroundDecal_BambooFlowers_{i:00}", i);
                    flowersCount++;
                }
            }

            report.DriedDecals = driedCount;
            report.FlowerDecals = flowersCount;
        }

        static void CreateGroundDecal(Transform root, System.Random rnd, Material material, string name, int index)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = name;
            go.transform.SetParent(root, false);

            float size = Range(rnd, 3.5f, 6.5f); // фактическая длинная сторона квада в метрах
            float aspect = Range(rnd, 0.75f, 1.35f);
            Vector3 position = DecalPosition(rnd, index);
            position.y += Range(rnd, 0.004f, 0.014f);
            go.transform.position = position;
            // Plane уже горизонтален; yaw поверх, без X=90 (иначе декаль встанет вертикально).
            go.transform.rotation = Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f);
            go.transform.localScale = new Vector3(size * aspect / 10f, 1f, size / 10f);

            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }
        }

        static Vector3 DecalPosition(System.Random rnd, int index)
        {
            if (index < 20)
            {
                // Кромки тропы: часть декали на настиле, часть уже на подстилке.
                float side = (index % 2 == 0) ? -1f : 1f;
                float dx = index < 10 ? Range(rnd, 0.2f, 1.4f) : Range(rnd, 1.7f, 5.5f);
                float x = TrailCenterX + side * dx;
                float z = Range(rnd, 16f, 158f);
                return new Vector3(x, GroundTopY(x, z) + 0.02f, z);
            }

            if (index < 30)
            {
                // Поляна; часть круга перекрыта насыпью (x < 65, z 0..8),
                // туда наземные декали не кладём.
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    float angle = Range(rnd, 0f, 360f);
                    float radius = Range(rnd, 3f, 15f);
                    Vector3 candidate = Polar(ClearingCenter, radius, angle);
                    candidate.y = PadTopY + 0.02f;
                    if (!IsUnderRamp(candidate.x, candidate.z)) return candidate;
                }

                // Детерминированный запасной вариант на безопасной части поляны.
                return new Vector3(ClearingCenter.x + 8f, PadTopY + 0.02f, ClearingCenter.z + 9f);
            }

            if (index < 36)
            {
                // Кромка арены, но не центр и не монумент.
                float angle = Range(rnd, 0f, 360f);
                float radius = Range(rnd, 18f, 25.4f);
                Vector3 p = Polar(ArenaCenter, radius, angle);
                p.y = PadTopY + 0.02f;
                return p;
            }

            // Лесная подстилка у маршрута, снаружи стен; z<=160, чтобы не лезть на арену.
            float s = rnd.Next(2) == 0 ? -1f : 1f;
            float fx = TrailCenterX + s * Range(rnd, 3.6f, 11f);
            float fz = Range(rnd, 20f, 160f);
            return new Vector3(fx, GroundTopY(fx, fz) + 0.02f, fz);
        }

        // ---------------------------------------------------------------- shared helpers

        static bool IsUnderRamp(float x, float z)
        {
            // Настил насыпи: x 0..65, z 0..8 (с запасом). Верх высокий, наземный декор там не виден.
            return x > -2.5f && x < 66f && z > -0.5f && z < 8.5f;
        }

        static float GroundTopY(float x, float z)
        {
            if (z > TrailStartZ && z < TrailEndZ && Mathf.Abs(x - TrailCenterX) <= TrailHalfWidth)
                return PadTopY;

            var point = new Vector3(x, 0f, z);
            if (XZDistance(point, ClearingCenter) <= ClearingRadius) return PadTopY;
            if (XZDistance(point, ArenaCenter) <= ArenaRadius) return PadTopY;
            // Вся земля теперь один плоский Terrain на y=0.
            return PadTopY;
        }

        static float GetBambooRingInnerRadius(Transform level, float fallback)
        {
            var ring = level.Find("Greybox/Walls/BambooRing");
            if (ring == null) return fallback;

            float inner = float.PositiveInfinity;
            foreach (Transform segment in ring)
            {
                if (segment.GetComponent<Renderer>() == null) continue;

                float radius = XZDistance(segment.position, ArenaCenter);
                float halfThickness = Mathf.Abs(segment.lossyScale.x) * 0.5f;
                inner = Mathf.Min(inner, radius - halfThickness);
            }

            return float.IsInfinity(inner) ? fallback : inner;
        }

        static float XZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool TryGetBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds(go.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                Bounds rendererBounds = renderer.bounds;
                if (rendererBounds.size.sqrMagnitude <= 1e-8f) continue;

                if (!hasBounds)
                {
                    bounds = rendererBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(rendererBounds);
                }
            }

            return hasBounds;
        }

        static void AssignAll(GameObject go, Material material)
        {
            if (go == null || material == null) return;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;

                int slotCount = Mathf.Max(1, renderer.sharedMaterials.Length);
                var slots = new Material[slotCount];
                for (int i = 0; i < slotCount; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }

        static void RemoveColliders(GameObject go)
        {
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null) Object.DestroyImmediate(collider);
            }
        }

        static Transform ResetContainer(Transform parent, string name, DressingReport report)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
                report.Note($"контейнер {name} пересобран (прошлый прогон удалён).");
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        static Vector3 Polar(Vector3 center, float radius, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(
                center.x + Mathf.Cos(radians) * radius,
                ForestFloorY,
                center.z + Mathf.Sin(radians) * radius);
        }

        static float Range(System.Random rnd, float min, float max)
        {
            return min + (float)rnd.NextDouble() * (max - min);
        }

        // ---------------------------------------------------------------- texture import

        static Texture2D LoadSoftDecal(string softPath, string fallbackPath, DressingReport report)
        {
            // Мягкая версия готовится отдельным проходом (radial alpha feather).
            // Если её нет — работаем с исходной RGBA-декалью.
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(softPath) != null)
                return LoadMap(softPath, TextureRole.Decal, report);

            return LoadMap(fallbackPath, TextureRole.Decal, report);
        }

        static Texture2D LoadMap(string path, TextureRole role, DressingReport report)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                report.AddMissing(path);
                return null;
            }

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return texture;

            bool changed = false;
            int maxSize = (role == TextureRole.Mask || role == TextureRole.Decal) ? 2048 : 4096;

            if (importer.maxTextureSize != maxSize)
            {
                importer.maxTextureSize = maxSize;
                changed = true;
            }

            switch (role)
            {
                case TextureRole.BaseColor:
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
                    break;

                case TextureRole.Normal:
                    if (importer.textureType != TextureImporterType.NormalMap)
                    {
                        importer.textureType = TextureImporterType.NormalMap;
                        changed = true;
                    }
                    if (importer.sRGBTexture)
                    {
                        importer.sRGBTexture = false;
                        changed = true;
                    }
                    break;

                case TextureRole.Mask:
                    if (importer.textureType != TextureImporterType.Default)
                    {
                        importer.textureType = TextureImporterType.Default;
                        changed = true;
                    }
                    if (importer.sRGBTexture)
                    {
                        importer.sRGBTexture = false;
                        changed = true;
                    }
                    break;

                case TextureRole.Decal:
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
                    if (!importer.alphaIsTransparency)
                    {
                        importer.alphaIsTransparency = true;
                        changed = true;
                    }
                    break;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            return texture;
        }

        // ---------------------------------------------------------------- misc

        static int ResolveLayer(string name)
        {
            int index = LayerMask.NameToLayer(name);
            if (index < 0)
            {
                Debug.LogWarning($"ShadowTan: слой \"{name}\" не найден, монумент уйдёт в Default.");
                return 0;
            }
            return index;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        sealed class DressingMaterials
        {
            public Material Wall;
            public Material Grass;
            public Material Stone;
            public Material SlopeStone;
            public Material CurbStone;
            public Material Metal;
            public Material Sand;
            public Material Path;
            public Material PathClearing;
            public Material EntranceStone;
            public Material Pebbles;
            public Material CobbleDecor;
        }

        sealed class DressingReport
        {
            public int GrassClumps;
            public int TrailGrass;
            public int ArenaGrass;
            public int ClearingGrass;
            public int SlopeStoneRenderers;
            public int StoneDecor;
            public int DriedDecals;
            public int FlowerDecals;
            public int MaterialsUpdated;
            public float MonumentHeight;
            public float MonumentRawHeight;
            public float MonumentScaleFactor;
            public string MonumentSource = "не изменялся";

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
                    ? "ShadowTan: Autumn_Gameplay dressed and saved."
                    : "ShadowTan: Autumn_Gameplay dressed, но сцена НЕ сохранена (см. ошибки выше).");
                sb.AppendLine($"  Grass clumps: {GrassClumps} (trail {TrailGrass}, arena {ArenaGrass}, clearing {ClearingGrass})");
                sb.AppendLine($"  Stones: {SlopeStoneRenderers} existing SlopeStones retextured; {StoneDecor} decor stones from cobble/pebble packs");
                sb.AppendLine($"  Decals: {DriedDecals + FlowerDecals} (dried bamboo {DriedDecals}, bamboo flowers {FlowerDecals})");
                sb.AppendLine($"  Monument: {MonumentSource}, height {MonumentHeight:0.00} m, raw {MonumentRawHeight:0.000} m x{MonumentScaleFactor:0.00}");
                sb.AppendLine($"  Materials updated: {MaterialsUpdated}");

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
