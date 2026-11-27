using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using static ShadowTan.EditorTools.AutumnVegetationBake;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        sealed class Palette
        {
            internal Material Solids;
            internal Material Cards;
            internal Material Contact;
        }

        static Palette MakeAtlases(Prop[] assets, HashSet<string> retained)
        {
            const int tile = 512, width = 2048, pad = 8;
            const int cardTile = 128, cardWidth = 512, gutter = 5;
            var color = new Color32[width * width];
            var normals = new Color32[width * width];
            var cards = new Color32[cardWidth * cardWidth];

            for (int entry = 0; entry < assets.Length; entry++)
            {
                Prop asset = assets[entry];
                Color tint = asset.Material.GetColor("_BaseColor");
                int sx = entry % 4 * tile, sy = entry / 4 * tile;
                asset.Atlas = Rectangle(sx, sy, tile, pad, width);
                for (int y = 0; y < tile; y++)
                for (int x = 0; x < tile; x++)
                {
                    float u = Mathf.Clamp01((x - pad) / (float)(tile - 2 * pad - 1));
                    float v = Mathf.Clamp01((y - pad) / (float)(tile - 2 * pad - 1));
                    int source = TextureIndex(asset, new Vector2(u, v));
                    int target = sx + x + (sy + y) * width;
                    color[target] = TintColor(asset.Albedo[source], tint);
                    normals[target] = asset.Normal[source];
                }

                asset.CardAtlas = new Rect[2];
                for (int view = 0; view < 2; view++)
                {
                    int id = entry * 2 + view;
                    int cx = id % 4 * cardTile, cy = id / 4 * cardTile;
                    asset.CardAtlas[view] = Rectangle(cx, cy, cardTile, gutter, cardWidth);
                    Rasterize(asset, cards, cardWidth, cx + gutter, cy + gutter, cardTile - 2 * gutter, view, tint);
                }

                asset.Meshes[2] = SaveMesh($"{Folder}/SM_{asset.Name}_LOD2.asset",
                    CrossedCards(asset.Meshes[0].bounds, asset.CardAtlas), retained);
            }

            string solidPath = Folder + "/T_TripoStage_BaseColor.png";
            string normalPath = Folder + "/T_TripoStage_Normal.png";
            string cardPath = Folder + "/T_TripoStage_Impostors.png";
            WritePng(solidPath, width, width, color);
            WritePng(normalPath, width, width, normals);
            WritePng(cardPath, cardWidth, cardWidth, cards);
            var solid = MakeMaterial("M_TripoStage_Baked", ConfigureTexture(solidPath, false),
                ConfigureTexture(normalPath, true), Color.white, 0.18f, false);
            var cutout = MakeMaterial("M_TripoStage_Impostors", ConfigureTexture(cardPath, false, true),
                null, Color.white, 0.12f, true);
            foreach (Prop asset in assets) asset.CardMaterial = cutout;

            return new Palette { Solids = solid, Cards = cutout, Contact = MakeContactMaterial() };
        }

        static Rect Rectangle(int x, int y, int tile, int gutter, int width) =>
            new Rect((x + gutter + 0.5f) / width, (y + gutter + 0.5f) / width,
                (tile - 2 * gutter - 1f) / width, (tile - 2 * gutter - 1f) / width);

        static int TextureIndex(Prop asset, Vector2 uv) =>
            Mathf.Clamp(Mathf.RoundToInt(uv.x * (asset.Width - 1)), 0, asset.Width - 1) +
            Mathf.Clamp(Mathf.RoundToInt(uv.y * (asset.Height - 1)), 0, asset.Height - 1) * asset.Width;

        static Color32 TintColor(Color32 source, Color tint)
        {
            Color linear = ((Color)source).linear * tint;
            Color rgba = linear.gamma;
            rgba.a = 1f;
            return rgba;
        }

        static void Rasterize(Prop asset, Color32[] output, int stride, int startX, int startY, int tile,
            int view, Color tint)
        {
            var source = asset.Meshes[0];
            Vector3[] positions = source.vertices;
            Vector2[] uv = source.uv;
            int[] indices = source.triangles;
            Bounds bounds = source.bounds;
            var points = new Vector3[positions.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = positions[i];
                float u = view == 0 ? (p.x - bounds.min.x) / bounds.size.x : (p.z - bounds.min.z) / bounds.size.z;
                float v = (p.y - bounds.min.y) / bounds.size.y;
                points[i] = new Vector3(u * (tile - 1), v * (tile - 1), view == 0 ? p.z : -p.x);
            }

            var depth = new float[tile * tile];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.NegativeInfinity;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                Vector3 a = points[ia], b = points[ib], c = points[ic];
                float area = Edge(a, b, c);
                if (Mathf.Abs(area) < 0.00001f) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)), 0, tile - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)), 0, tile - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)), 0, tile - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)), 0, tile - 1);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector3(x, y, 0f);
                    float w0 = Edge(b, c, p) / area;
                    float w1 = Edge(c, a, p) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -0.0001f || w1 < -0.0001f || w2 < -0.0001f) continue;
                    float z = a.z * w0 + b.z * w1 + c.z * w2;
                    int index = x + y * tile;
                    if (z <= depth[index]) continue;
                    depth[index] = z;
                    Vector2 sample = uv[ia] * w0 + uv[ib] * w1 + uv[ic] * w2;
                    output[startX + x + (startY + y) * stride] = TintColor(asset.Albedo[TextureIndex(asset, sample)], tint);
                }
            }

            static float Edge(Vector3 a, Vector3 b, Vector3 p) =>
                (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }

        static Rect ContactTile(int index) =>
            new Rect((index * 64 + 2.5f) / 256f, 2.5f / 64f, 59f / 256f, 59f / 64f);

        static Material MakeContactMaterial()
        {
            const int size = 64, width = 256;
            var pixels = new Color32[width * size];
            Color earth = Linear("#3C2D21").gamma;
            var leaf = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            bool hasLeaves = false;
            string leavesPath = Location + "/Textures/T_DriedBamboo_Decals_Soft.png";
            try
            {
                if (File.Exists(leavesPath)) hasLeaves = ImageConversion.LoadImage(leaf, File.ReadAllBytes(leavesPath));
                for (int y = 2; y < size - 2; y++)
                for (int x = 2; x < size - 2; x++)
                {
                    var p = new Vector2((x - 1.5f) / 60f * 2f - 1f, (y - 1.5f) / 60f * 2f - 1f);
                    float radial = Mathf.Clamp01(1f - p.sqrMagnitude);
                    var shading = earth;
                    shading.a = radial * radial * 0.29f;
                    pixels[x + y * width] = shading;
                    if (hasLeaves)
                    {
                        Color detail = leaf.GetPixelBilinear((p.x + 1f) * 0.5f, (p.y + 1f) * 0.5f);
                        detail.a *= radial * radial * 0.49f;
                        pixels[x + size + y * width] = detail;
                    }

                    var mark = earth;
                    float sole = 1f - Mathf.Pow(p.x / Mathf.Lerp(0.43f, 0.65f, (p.y + 1f) * 0.5f), 2f) - p.y * p.y;
                    float tread = 0.79f + 0.21f * Mathf.Sin((p.y + p.x * 0.28f) * 39f);
                    mark.a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sole * 2.3f)) * tread * 0.16f;
                    pixels[x + 2 * size + y * width] = mark;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(leaf);
            }

            string path = Folder + "/T_TripoStage_Contact.png";
            WritePng(path, width, size, pixels);
            var map = ConfigureTexture(path, false, true, false);
            path = Folder + "/M_TripoStage_Contact.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("HDRP/Unlit")) { name = "M_TripoStage_Contact" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_UnlitColorMap", map);
            material.SetColor("_UnlitColor", Color.white);
            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_ZWrite", 0f);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
