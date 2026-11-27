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
    // Палитра осени по референсу (entrance_polish_grove_*, GDD-AU §03): ~70% жёлто-золотого, ~20% коричневого,
    // ~10% тёмно-оливкового. Без неона, без оранжевого заката, без голубого тумана и плоского жёлтого фильтра.
    // Правит существующие материалы, свет и PP_Autumn_Gameplay_Warm; второй Volume не создаётся.
    static class AutumnFixPalette
    {
        const string Folder = Generated + "/FixPass";
        const string Wind = "Assets/_Project/Art/Environment/Autumn/Wind";
        const string Post = Generated + "/Post/PP_Autumn_Gameplay_Warm.asset";

        // Варианты атласа стеблей: 0–3 обычные (60% стеблей), 4–7 «светлые» (40%).
        static readonly string[] CulmVariants = { "#7A5E2A", "#C9A24A", "#D8B84A", "#B8963F", "#5E5A28", "#D8B84A", "#C9A24A", "#A88A45" };
        static readonly string[] LeafVariants = { "#5E5A28", "#B39A45", "#C9A24A", "#8E7C3E" };

        internal static void Apply(Scene scene, List<string> report)
        {
            EnsureFolder(Folder);

            var culms = AssetDatabase.LoadAssetAtPath<Material>(Wind + "/BambooCulms_Wind.mat");
            var leaves = AssetDatabase.LoadAssetAtPath<Material>(Wind + "/BambooLeaves_Wind.mat");
            var grass = AssetDatabase.LoadAssetAtPath<Material>(Wind + "/DryGrass_Wind.mat");

            culms.SetTexture("_BaseColorMap", Recolor(Generated + "/Pass2/T_Autumn_CulmAtlas_Gold.png", "T_Autumn_CulmAtlas_Palette", CulmVariants));
            culms.SetColor("_BaseColor", new Color(0.82f, 0.72f, 0.54f, 1f));
            leaves.SetTexture("_BaseColorMap", Recolor(Generated + "/Pass2/T_Autumn_LeafAtlas_Gold.png", "T_Autumn_LeafAtlas_Palette", LeafVariants));
            leaves.SetColor("_BaseColor", new Color(0.92f, 0.86f, 0.66f, 1f));
            // Было (110, 92, 48): листья светились сами и давали неоновый жёлтый. Немного оставляем — имитация просвета.
            leaves.SetColor("_EmissiveColor", new Color(45f, 37f, 18f, 1f));
            grass.SetColor("_BaseColor", new Color(0.56f, 0.45f, 0.22f, 1f));
            grass.SetColor("_EmissiveColor", new Color(10f, 7.5f, 3.2f, 1f));
            foreach (var m in new[] { culms, leaves, grass }) EditorUtility.SetDirty(m);

            foreach (string guid in AssetDatabase.FindAssets("DenseGrass_Straw_Wind t:Material"))
            {
                var straw = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (straw == null || straw.name != "DenseGrass_Straw_Wind" || !straw.HasProperty("_EmissiveColor")) continue;
                straw.SetColor("_EmissiveColor", new Color(7f, 5f, 2.5f, 1f));
                EditorUtility.SetDirty(straw);
            }

            SetLayer("TroddenEarth", new Vector4(0.66f, 0.54f, 0.38f, 1f));
            SetLayer("ArenaSand", new Vector4(0.82f, 0.70f, 0.52f, 1f));

            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.name == "Sun") light.color = new Color(1f, 0.95f, 0.86f);
                if (light.name == "FillLight") { light.intensity = 8500f; light.color = new Color(1f, 0.97f, 0.92f); }
                EditorUtility.SetDirty(light);
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Post);
            if (profile.TryGet(out ColorAdjustments color))
            {
                // Лёгкий тёплый сдвиг вместо плоского жёлтого фильтра (было 1, 0.92, 0.80).
                color.colorFilter.Override(new Color(1f, 0.975f, 0.91f));
                color.hueShift.Override(0f);
                color.saturation.Override(0f);
                color.contrast.Override(9f);
            }
            if (profile.TryGet(out WhiteBalance balance))
            {
                balance.temperature.Override(6f);
                balance.tint.Override(0f);
            }
            if (profile.TryGet(out ShadowsMidtonesHighlights smh))
            {
                smh.shadows.Override(new Vector4(0.94f, 0.95f, 0.86f, 0.96f));
                smh.midtones.Override(new Vector4(1f, 0.99f, 0.95f, 1f));
                smh.highlights.Override(new Vector4(1f, 0.98f, 0.94f, 1f));
            }
            if (profile.TryGet(out Fog fog))
            {
                var haze = Linear("#D6C6A6");
                fog.colorMode.Override(FogColorMode.ConstantColor);
                fog.color.Override(haze * 2.6f);
                fog.tint.Override(Color.white);
                fog.albedo.Override(haze);
                fog.meanFreePath.Override(125f);
            }
            foreach (var component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);

            int globals = 0;
            foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            {
                if (volume.isGlobal && volume.weight > 0f) globals++;
                if (volume.name == "TripoProp_Finish") volume.weight = 0f;
            }
            report.Add($"Palette: culm/leaf atlases recoloured ({string.Join(" ", CulmVariants)}), leaf emission 110→45, " +
                       $"fill light 17.5→8.5 klx, PP filter (1,.92,.80)→(1,.975,.91), WB 14→6, fog #D6C6A6; active global volumes: {globals}.");
        }

        static void SetLayer(string name, Vector4 tint)
        {
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(Generated + "/ScenePolish/" + name + ".terrainlayer");
            if (layer == null) return;
            layer.diffuseRemapMax = tint;
            EditorUtility.SetDirty(layer);
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
