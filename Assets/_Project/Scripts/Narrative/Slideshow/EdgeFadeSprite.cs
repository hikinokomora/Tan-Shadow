using UnityEngine;

namespace ShadowTan.Narrative
{
    // Чёрная рамка с мягким переходом к прозрачному центру: края слайда растворяются в фоне.
    internal static class EdgeFadeSprite
    {
        private const int Size = 256;
        private static Sprite cached;
        private static float cachedWidth = -1f;

        // edgeWidth01 — доля стороны, на которой идёт переход в чёрный.
        public static Sprite Get(float edgeWidth01)
        {
            if (cached != null && Mathf.Approximately(cachedWidth, edgeWidth01)) return cached;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[Size * Size];
            float w = Mathf.Max(0.001f, edgeWidth01);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                // Затухание по осям перемножается, поэтому в углах нет диагональных швов.
                float fadeX = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) / w);
                float fadeY = Mathf.SmoothStep(0f, 1f, Mathf.Min(v, 1f - v) / w);
                float a = 1f - fadeX * fadeY;
                pixels[y * Size + x] = new Color32(0, 0, 0, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            cached = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
            cachedWidth = edgeWidth01;
            return cached;
        }
    }
}
