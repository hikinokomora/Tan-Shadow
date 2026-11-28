using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using static ShadowTan.EditorTools.AutumnVegetationBake;

namespace ShadowTan.EditorTools
{
    // Палитра осени по GDD-AU §03 «Цветовая палитра»: доминанта — выцветший жёлто-оливковый бамбук #B0A15E,
    // 20–30 % остаточной зелени #33573E, тёмно-коричневая сухая трава #5C392A, песок #D8C18E, камни #78583D,
    // титаново-серый металл #727B80, фарфоровая шерсть #E0E1DD. Цвет задаётся альбедо материала: тинт = эталон /
    // средний цвет карты. Свет и PP_Autumn_Gameplay_Warm — нейтральные, без жёлтого фильтра; второй Volume не создаётся.
    static class AutumnFixPalette
    {
        const string Folder = Generated + "/FixPass";
        const string Wind = "Assets/_Project/Art/Environment/Autumn/Wind";
        const string Materials = Location + "/Materials";
        const string Post = Generated + "/Post/PP_Autumn_Gameplay_Warm.asset";

        internal const string Bamboo = "#33573E", BambooFaded = "#B0A15E", DryGrass = "#5C392A", Sand = "#D8C18E",
            Stone = "#78583D", Metal = "#727B80", Pelt = "#E0E1DD";
        const string Trail = "#B49A6E";
        // Альбедо с поправкой на голубой амбиент неба: в кадре песок ≈ #D8C18E, камни ≈ #78583D (замер пипеткой).
        internal const string SandAlbedo = "#D8B879", StoneAlbedo = "#785335", StoneBedAlbedo = "#7E5838";

        // Колонки атласа стеблей: 0–3 обычные (60 % стеблей чанка), 4–7 «светлые» (40 %). Чанк берёт одну колонку
        // из каждой половины, поэтому зелёная колонка 4 и оливково-зелёная 6 дают ≈20 % зелёных стеблей, пятнами по чанкам.
        // Зелёная колонка чуть теплее #33573E: в тени под голубым небом стебель уходил в бирюзу (H 147° вместо 138°).
        static readonly string[] CulmVariants = { BambooFaded, "#A39553", "#BBAD69", "#9A8F55", "#385A3B", "#B9AC6B", "#5E6B45", "#C4B878" };
        // Карточка листьев выбирает колонку случайно: одна зелёная из четырёх — 25 % листвы.
        static readonly string[] LeafVariants = { Bamboo, BambooFaded, "#A39556", "#8E8A50" };

        // Экспозиция подобрана по кадру: освещённый песок ≈ #D8C18E, средний тон рощи — оливковый.
        const float FixedExposure = 11.5f;
        // Небо (HDRISky из профиля HDRP по умолчанию, 11 EV) светлее: «спокойное небо» и больше амбиента в рощах.
        const float SkyExposure = 12.3f;

        internal static void Apply(Scene scene, List<string> report)
        {
            EnsureFolder(Folder);
            means.Clear();
            var notes = new List<string>();

            var culms = Load(Wind + "/BambooCulms_Wind.mat");
            var leaves = Load(Wind + "/BambooLeaves_Wind.mat");
            culms.SetTexture("_BaseColorMap", Recolor(Generated + "/Pass2/T_Autumn_CulmAtlas_Gold.png", "T_Autumn_CulmAtlas_Palette", CulmVariants));
            culms.SetColor("_BaseColor", Color.white);
            leaves.SetTexture("_BaseColorMap", Recolor(Generated + "/Pass2/T_Autumn_LeafAtlas_Gold.png", "T_Autumn_LeafAtlas_Palette", LeafVariants));
            leaves.SetColor("_BaseColor", Color.white);
            // Было (45, 37, 18): жёлтое самосвечение золотило рощу. Чуть-чуть просвета оставляем, нейтрально.
            leaves.SetColor("_EmissiveColor", new Color(6f, 6f, 6f, 1f));
            Dirty(culms, leaves);

            // Легаси Bamboo_Giant_* (поляна и рощи у входа).
            Tint(Wind + "/M_Bamboo_Culm_Dark_Wind.mat", "#9E9056", notes);
            Tint(Wind + "/M_Bamboo_Culm_Light_Wind.mat", "#BBAD6A", notes);
            Tint(Wind + "/M_Bamboo_Leaf_Green_Wind.mat", Bamboo, notes);
            Tint(Wind + "/M_Bamboo_Leaf_Yellow_Wind.mat", BambooFaded, notes);
            Tint(Wind + "/M_Bamboo_Leaf_Olive_Wind.mat", "#7F8350", notes);
            Tint(Materials + "/ForestBake_Culm.mat", "#9C8B55", notes);
            Tint(Materials + "/ForestBake_Leaves.mat", "#8E7D4A", notes, emission: 0f);
            Tint(Generated + "/Tripo/M_Autumn_BambooLog.mat", "#8C7E4C", notes);

            // Подстилка (GroundDecals, Litter_*, BambooLitter_*): сухие листья соломенные, цветы бурые — не серая щебёнка.
            Tint(Materials + "/M_Autumn_Decal_DriedBamboo.mat", "#A68B55", notes);
            Tint(Materials + "/M_Autumn_Decal_BambooFlowers.mat", "#7C5A3A", notes);

            // Сухая трава: тёмно-коричневая, без жёлтого самосвечения.
            Tint(Wind + "/DryGrass_Wind.mat", DryGrass, notes, emission: 1.2f);
            Tint(Wind + "/DenseGrass_Straw_Wind.mat", DryGrass, notes, emission: 1.2f);
            foreach (string guid in AssetDatabase.FindAssets("DenseGrass_Straw_Wind t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "DenseGrass_Straw_Wind") Tint(path, DryGrass, notes, emission: 1.2f);
            }
            Tint(Wind + "/M_Autumn_WheatGrass_Wind.mat", DryGrass, notes);
            Tint(Wind + "/M_Autumn_WheatGrass_Gold_Wind.mat", "#6A4630", notes);
            Tint(Wind + "/M_Autumn_WheatGrass_Pale_Wind.mat", "#7A5A3E", notes);
            Tint(Wind + "/M_Autumn_WheatGrass_Dry_Wind.mat", "#664330", notes);
            Tint(Wind + "/M_Autumn_WheatGrass_Brown_Wind.mat", "#4E3022", notes);
            Tint(Wind + "/M_Autumn_WheatGrass_Green_Wind.mat", "#545238", notes);

            // Земля: песок арены, тропа чуть темнее песка, трава у поляны темнее тропы.
            Layer("ArenaSand", SandAlbedo, notes);
            Layer("TroddenEarth", Trail, notes);
            Layer("WildGrassDetail", "#7A5C3E", notes);
            Layer("WildGrassMacro", "#6A4E36", notes);
            // Поверхность тропы светилась сама (474, 368, 187 нит) — отсюда жёлтая тропа.
            Tint(Materials + "/ForestBake_TrailSurface.mat", Trail, notes, emission: 0f);
            Tint(Materials + "/Entrance_Sand.mat", SandAlbedo, notes);

            // Камни у входа и на арене (насыпь — шаг 5 Stone Mound).
            Tint(Generated + "/Tripo/M_Autumn_Boulder.mat", StoneAlbedo, notes);
            Tint(Materials + "/EntranceFix_Gravel.mat", "#7E5E42", notes);
            Tint(Materials + "/EntranceFix_Curb.mat", StoneAlbedo, notes);
            Tint(Generated + "/ArenaPort/M_Arena_Pebble_warm.mat", StoneAlbedo, notes);

            // Металл монумента — холодный титан.
            Tint(Materials + "/M_Autumn_Metal.mat", Metal, notes);
            for (int i = 0; i < 4; i++) Tint($"{Generated}/ArenaPort/M_Autumn_ArtMonument_{i}.mat", Metal, notes);
            Tint(Generated + "/VFX/M_Autumn_MonumentChips.mat", "#5E666A", notes);

            // Морда Байху из травы (BaihuPeek); тело — M_Baihu_Fur (AutumnBaihuModel).
            Tint("Assets/_Game/Art/AutumnBlockout/Porcelain pelt.mat", Pelt, notes);

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.name == "Sun") light.color = new Color(1f, 0.975f, 0.95f);
                if (light.name == "FillLight") { light.intensity = 8500f; light.color = Color.white; }
                EditorUtility.SetDirty(light);
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Post);
            if (profile.TryGet(out Exposure exposure))
            {
                exposure.mode.Override(ExposureMode.Fixed);
                exposure.fixedExposure.Override(FixedExposure);
            }
            var defaults = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/HDRPDefaultResources/DefaultSettingsVolumeProfile.asset");
            if (defaults != null && defaults.TryGet(out HDRISky defaultSky))
            {
                if (!profile.TryGet(out HDRISky sky))
                {
                    sky = profile.Add<HDRISky>(true);
                    sky.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(sky, profile);
                }
                sky.hdriSky.Override(defaultSky.hdriSky.value);
                sky.exposure.Override(SkyExposure);
            }
            if (profile.TryGet(out ColorAdjustments color))
            {
                color.colorFilter.Override(Color.white);
                color.hueShift.Override(0f);
                color.saturation.Override(0f);
                color.contrast.Override(9f);
                color.postExposure.Override(0f);
            }
            if (profile.TryGet(out WhiteBalance balance))
            {
                balance.temperature.Override(0f);
                balance.tint.Override(0f);
            }
            if (profile.TryGet(out ShadowsMidtonesHighlights smh))
            {
                // w — не «вес», а сдвиг: HDRP прибавляет 4·w к каждому каналу. Было 0,96/1/1 — это ×5 яркости
                // всего кадра и тот самый пересвеченный золотой. Нейтраль — (1, 1, 1, 0).
                var neutral = new Vector4(1f, 1f, 1f, 0f);
                smh.shadows.Override(neutral);
                smh.midtones.Override(neutral);
                smh.highlights.Override(neutral);
            }
            if (profile.TryGet(out Bloom bloom)) bloom.tint.Override(Color.white);
            if (profile.TryGet(out Fog fog))
            {
                // Тёплая дымка вдали остаётся, но реже и тусклее: не заливает кадр золотом.
                var haze = Linear("#D6C6A6");
                fog.colorMode.Override(FogColorMode.ConstantColor);
                fog.color.Override(haze * 1.2f);
                fog.tint.Override(Color.white);
                fog.albedo.Override(haze);
                fog.meanFreePath.Override(190f);
            }
            foreach (var component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);

            int globals = 0;
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            {
                if (volume.isGlobal && volume.weight > 0f) globals++;
                if (volume.name == "TripoProp_Finish") volume.weight = 0f;
            }
            report.Add($"Palette (GDD-AU): culm atlas {string.Join(" ", CulmVariants)}; leaf atlas {string.Join(" ", LeafVariants)}; " +
                       $"PP filter → white, WB → 0, SMH → neutral (was w 0.96/1/1 = ×5), exposure EV {FixedExposure}, sky {SkyExposure} EV, fog #D6C6A6 ×1.2 mfp 190; " +
                       $"active global volumes: {globals}.\n  " + string.Join("\n  ", notes));
        }

        static Material Load(string path) =>
            AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new System.InvalidOperationException($"Missing {path}.");

        static void Dirty(params Material[] materials)
        {
            foreach (var m in materials) EditorUtility.SetDirty(m);
        }

        // Тинт материала так, чтобы средний цвет его карты стал эталоном. Если карта темнее эталона, тинт ярче белого
        // (до MaxTint): в среднем альбедо всё равно ≈ эталону. emission ≥ 0 — множитель самосвечения (нейтральный).
        const float MaxTint = 4f;
        internal static Color Tint(string path, string hex, List<string> notes, float emission = -1f)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { notes.Add($"{Path.GetFileName(path)}: missing, skipped."); return Color.white; }
            return Tint(material, hex, notes, emission);
        }

        internal static Color Tint(Material material, string hex, List<string> notes, float emission = -1f)
        {
            Color target = Linear(hex);
            var map = material.HasProperty("_BaseColorMap") ? material.GetTexture("_BaseColorMap") : null;
            Color mean = map != null ? MeanLinear(map) : Color.white;
            var tint = new Vector3(target.r / Mathf.Max(0.002f, mean.r), target.g / Mathf.Max(0.002f, mean.g), target.b / Mathf.Max(0.002f, mean.b));
            float over = Mathf.Max(tint.x, Mathf.Max(tint.y, tint.z));
            if (over > MaxTint) tint *= MaxTint / over;
            Color result = new Color(tint.x, tint.y, tint.z, 1f).gamma;
            material.SetColor("_BaseColor", result);
            if (emission >= 0f && material.HasProperty("_EmissiveColor"))
                material.SetColor("_EmissiveColor", new Color(emission, emission, emission, 1f));
            if (material.shader.name == "HDRP/Lit") HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            notes.Add($"{material.name}: {hex} → tint ({result.r:F2}, {result.g:F2}, {result.b:F2})" + (over > MaxTint ? $" (capped, albedo ×{MaxTint / over:F2} of target)" : ""));
            return result;
        }

        static readonly Dictionary<Texture, Color> means = new Dictionary<Texture, Color>();

        // Средний линейный цвет карты: блит в линейный float-RT 128×128.
        internal static Color MeanLinear(Texture texture)
        {
            if (means.TryGetValue(texture, out Color cached)) return cached;
            const int size = 128;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            // Взвешено по альфе: у тонкой травы на 128 px почти нет пикселей с полной непрозрачностью.
            Color sum = Color.clear;
            float weight = 0f;
            foreach (Color p in read.GetPixels())
            {
                sum += p * p.a;
                weight += p.a;
            }
            Object.DestroyImmediate(read);
            Color mean = weight > 0.001f ? sum / weight : Color.white;
            means[texture] = mean;
            return mean;
        }

        // Слой террейна: diffuseRemapMax — множитель альбедо карты; подгоняем средний цвет к эталону.
        static void Layer(string name, string hex, List<string> notes)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Generated + "/ScenePolish/" + name + ".terrainlayer");
            if (layer == null) { notes.Add($"{name}: missing layer."); return; }
            Color mean = MeanLinear(layer.diffuseTexture);
            Color target = Linear(hex);
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(target.r / mean.r, target.g / mean.g, target.b / mean.b, 1f);
            EditorUtility.SetDirty(layer);
            notes.Add($"Terrain {name}: {hex} → remap {(Vector3)(Vector4)layer.diffuseRemapMax:F2}");
        }

        // Перекрашивает колонки атласа в целевые цвета, сохраняя рисунок (яркость пикселя относительно среднего колонки).
        static Texture2D Recolor(string sourcePath, string name, string[] targets)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            source.LoadImage(File.ReadAllBytes(sourcePath));
            int width = source.width, height = source.height, columns = targets.Length, step = width / columns;
            var pixels = source.GetPixels();
            for (int c = 0; c < columns; c++)
            {
                double sum = 0; int count = 0;
                for (int y = 0; y < height; y++)
                for (int x = c * step; x < (c + 1) * step; x++)
                {
                    Color p = pixels[y * width + x];
                    if (p.a < 0.5f) continue;
                    sum += p.r * 0.2126 + p.g * 0.7152 + p.b * 0.0722;
                    count++;
                }
                float mean = count > 0 ? (float)(sum / count) : 1f;
                ColorUtility.TryParseHtmlString(targets[c], out Color target);
                for (int y = 0; y < height; y++)
                for (int x = c * step; x < (c + 1) * step; x++)
                {
                    int i = y * width + x;
                    Color p = pixels[i];
                    float ratio = Mathf.Clamp((p.r * 0.2126f + p.g * 0.7152f + p.b * 0.0722f) / Mathf.Max(0.001f, mean), 0f, 1.6f);
                    pixels[i] = new Color(Mathf.Clamp01(target.r * ratio), Mathf.Clamp01(target.g * ratio), Mathf.Clamp01(target.b * ratio), p.a);
                }
            }

            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            output.SetPixels(pixels);
            output.Apply();
            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, output.EncodeToPNG());
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(output);
            AssetDatabase.ImportAsset(path);

            var original = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = original.textureType;
            importer.sRGBTexture = original.sRGBTexture;
            importer.alphaIsTransparency = original.alphaIsTransparency;
            importer.alphaSource = original.alphaSource;
            importer.mipmapEnabled = original.mipmapEnabled;
            importer.mipMapsPreserveCoverage = original.mipMapsPreserveCoverage;
            importer.alphaTestReferenceValue = original.alphaTestReferenceValue;
            importer.wrapMode = original.wrapMode;
            importer.filterMode = original.filterMode;
            importer.anisoLevel = original.anisoLevel;
            importer.maxTextureSize = original.maxTextureSize;
            importer.textureCompression = original.textureCompression;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
