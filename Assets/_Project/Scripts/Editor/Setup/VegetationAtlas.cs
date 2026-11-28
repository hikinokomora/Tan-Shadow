using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    static class VegetationAtlas
    {
        internal const float Cutoff = 0.42f;
        const int Tile = 256;

        internal static Rect Card(int variant)
        {
            const float gutter = 2f / Tile;
            return new Rect((variant + gutter) / 4f, gutter, (1f - 2f * gutter) / 4f, 1f - 2f * gutter);
        }

        internal static Texture2D Culm(string path)
        {
            Color dark = Linear("#33281B");
            Color middle = Linear("#5F4A30");
            Color light = Linear("#8A6B45");
            var pixels = new Color32[8 * Tile * Tile];
            for (int variant = 0; variant < 8; variant++)
            {
                Color baseColor = variant < 4 ? Color.Lerp(dark, middle, 0.83f) : Color.Lerp(middle, light, 0.66f);
                baseColor = Color.Lerp(baseColor, baseColor * new Color(1.14f, 0.99f, 0.81f), (variant % 4) / 3f);
                for (int y = 0; y < Tile; y++)
                {
                    float v = (y + 0.5f) / Tile;
                    for (int x = 0; x < Tile; x++)
                    {
                        float u = (x + 0.5f) / Tile;
                        float fibers = Mathf.Sin(u * 14f * Mathf.PI) * 0.06f +
                                       Mathf.Sin(u * 78f * Mathf.PI) * 0.045f +
                                       (Mathf.PerlinNoise(u * 46f + variant, v * 1.7f) - 0.5f) * 0.20f;
                        float ring = Mathf.Exp(-Mathf.Pow((v - 0.17f - 0.008f * Mathf.Sin(u * 2f * Mathf.PI)) / 0.023f, 2f));
                        float lip = Mathf.Exp(-Mathf.Pow((v - 0.20f) / 0.012f, 2f));
                        Color color = baseColor * (1f + fibers - 0.55f * ring + 0.42f * lip);
                        color.a = 1f;
                        pixels[y * (8 * Tile) + variant * Tile + x] = color.gamma;
                    }
                }
            }

            return Save(path, pixels, 8 * Tile, false);
        }

        internal static Texture2D Leaves(string path)
        {
            Color dark = Linear("#7A4318");
            Color middle = Linear("#B9702A");
            Color pale = Linear("#E0A050");
            var canvas = new Cards();
            for (int variant = 0; variant < 4; variant++)
            {
                var random = new Random(6997 + variant * 73);
                var start = new Vector2(0.48f, 0.045f);
                var tip = new Vector2(Range(random, 0.40f, 0.57f), 0.96f);
                Vector2 curve = new Vector2(Range(random, 0.35f, 0.68f), 0.5f);
                canvas.Blade(variant, start, curve, tip, 0.012f, dark, middle);
                for (int leaf = 0; leaf < 17; leaf++)
                {
                    float t = 0.10f + leaf * 0.045f;
                    int sign = leaf % 2 == 0 ? -1 : 1;
                    Vector2 root = Curve(start, curve, tip, t);
                    float reach = Range(random, 0.18f, 0.40f) * Mathf.Lerp(1f, 0.42f, t);
                    var end = root + new Vector2(sign * reach, Range(random, 0.06f, 0.21f));
                    var bend = Vector2.Lerp(root, end, 0.50f) + new Vector2(sign * 0.035f, 0.042f);
                    Color color = Color.Lerp(dark, middle, Range(random, 0.32f, 0.98f));
                    Color sun = Color.Lerp(middle, pale, Range(random, 0.22f, 1f));
                    canvas.Blade(variant, root, bend, end, Range(random, 0.015f, 0.034f), color, sun);
                }

                canvas.Blade(variant, Curve(start, curve, tip, 0.83f), tip, tip + new Vector2(-0.025f, 0.02f),
                    0.030f, middle, pale);
            }

            return Save(path, canvas.Pixels, 4 * Tile, true);
        }

        internal static Texture2D Grass(string path)
        {
            Color dark = Linear("#7E6238");
            Color middle = Linear("#C7A468");
            Color pale = Linear("#E3C48D");
            var canvas = new Cards();
            for (int variant = 0; variant < 4; variant++)
            {
                var random = new Random(37721 + variant * 59);
                for (int blade = 0; blade < 24; blade++)
                {
                    float x = Range(random, 0.35f, 0.65f);
                    var root = new Vector2(x, Range(random, 0.025f, 0.09f));
                    var tip = new Vector2(Range(random, 0.045f, 0.955f), Range(random, 0.46f, 0.98f));
                    Vector2 bend = Vector2.Lerp(root, tip, 0.53f) + new Vector2((root.x - tip.x) * 0.43f, 0.10f);
                    Color baseColor = Color.Lerp(dark, middle, Range(random, 0.12f, 0.75f));
                    Color tipColor = Color.Lerp(middle, pale, Range(random, 0.40f, 1f));
                    bool seedHead = blade % 4 == 0;
                    canvas.Blade(variant, root, bend, tip, seedHead ? 0.008f : Range(random, 0.011f, 0.023f),
                        baseColor, tipColor);
                    if (!seedHead) continue;

                    for (int seed = 0; seed < 7; seed++)
                    {
                        Vector2 joint = Curve(root, bend, tip, 0.76f + seed * 0.031f);
                        float direction = seed % 2 == 0 ? 1f : -1f;
                        var husk = joint + new Vector2(direction * Range(random, 0.013f, 0.030f), 0.035f);
                        canvas.Blade(variant, joint, Vector2.Lerp(joint, husk, 0.5f), husk,
                            0.016f, middle, pale);
                    }
                }
            }

            return Save(path, canvas.Pixels, 4 * Tile, true);
        }

        static Vector2 Curve(Vector2 start, Vector2 bend, Vector2 end, float t) =>
            Vector2.LerpUnclamped(Vector2.LerpUnclamped(start, bend, t), Vector2.LerpUnclamped(bend, end, t), t);

        static Texture2D Save(string path, Color32[] pixels, int width, bool cutout)
        {
            var texture = new Texture2D(width, Tile, TextureFormat.RGBA32, false);
            byte[] bytes;
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                bytes = texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                File.WriteAllBytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool correct = importer.mipmapEnabled && importer.mipMapsPreserveCoverage == cutout &&
                           importer.wrapMode == (cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat) &&
                           Mathf.Approximately(importer.alphaTestReferenceValue, Cutoff) &&
                           importer.alphaIsTransparency == cutout && importer.anisoLevel == 4 &&
                           importer.textureCompression == TextureImporterCompression.CompressedHQ;
            if (!correct)
            {
                importer.mipmapEnabled = true;
                importer.mipMapsPreserveCoverage = cutout;
                importer.alphaTestReferenceValue = Cutoff;
                importer.alphaIsTransparency = cutout;
                importer.wrapMode = cutout ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        sealed class Cards
        {
            internal readonly Color32[] Pixels = new Color32[4 * Tile * Tile];

            internal void Blade(int variant, Vector2 start, Vector2 bend, Vector2 end, float width, Color bottom, Color top)
            {
                Vector2 a = start;
                const int steps = 28;
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    Vector2 b = Curve(start, bend, end, t);
                    float radius = width * (0.13f + 0.87f * Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.82f));
                    Stamp(variant, a, b, Mathf.Max(0.55f, radius * Tile), Color.Lerp(bottom, top, t));
                    a = b;
                }
            }

            void Stamp(int variant, Vector2 a, Vector2 b, float radius, Color linear)
            {
                a *= Tile;
                b *= Tile;
                int x0 = Mathf.Max(3, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - radius - 1f));
                int x1 = Mathf.Min(Tile - 4, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + radius + 1f));
                int y0 = Mathf.Max(3, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - radius - 1f));
                int y1 = Mathf.Min(Tile - 4, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + radius + 1f));
                Vector2 segment = b - a;
                float inverseLength = 1f / Mathf.Max(0.001f, segment.sqrMagnitude);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        var pixel = new Vector2(x + 0.5f, y + 0.5f);
                        float along = Mathf.Clamp01(Vector2.Dot(pixel - a, segment) * inverseLength);
                        float distance = Vector2.Distance(pixel, a + segment * along);
                        float alpha = Mathf.Clamp01(radius + 0.65f - distance);
                        int index = y * (4 * Tile) + variant * Tile + x;
                        if (alpha * 255f < Pixels[index].a || alpha == 0f) continue;

                        float vein = 1f + 0.15f * Mathf.Exp(-distance * distance * 0.65f);
                        Color color = (linear * vein).gamma;
                        color.a = alpha;
                        Pixels[index] = color;
                    }
                }
            }
        }
    }
}
