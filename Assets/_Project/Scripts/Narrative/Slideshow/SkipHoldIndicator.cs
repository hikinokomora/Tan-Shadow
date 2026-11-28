using UnityEngine;
using UnityEngine.UI;

namespace ShadowTan.Narrative
{
    // Надпись «зажмите, чтобы пропустить» и кружок, который заполняется по мере удержания.
    // Заполненная часть кольца светится так же, как активные клавиши подсказок.
    public sealed class SkipHoldIndicator : MonoBehaviour
    {
        [SerializeField] private Text hintRu;
        [SerializeField] private Text hintZh;
        [SerializeField] private Image ringBackground;
        [SerializeField] private Image ringGlow;
        [SerializeField] private Image ringFill;

        private Color idleColor;
        private Color litColor;

        public void Setup(SlideshowConfig config, Font ruFont, Font zhFont)
        {
            hintRu.text = config.skipHintRu;
            hintRu.color = config.skipTextColor;
            hintRu.font = ruFont;
            hintZh.text = config.skipHintZh;
            hintZh.color = config.skipSecondaryColor;
            hintZh.font = zhFont;

            ringBackground.sprite = RingSprite.Ring();
            ringFill.sprite = RingSprite.Ring();
            ringGlow.sprite = RingSprite.Glow();

            var faint = config.skipSecondaryColor;
            faint.a *= 0.3f;
            ringBackground.color = faint;
            idleColor = config.skipSecondaryColor;
            litColor = config.skipLitColor;
            SetProgress(0f);
        }

        public void SetProgress(float progress01)
        {
            float p = Mathf.Clamp01(progress01);
            ringFill.fillAmount = p;
            ringGlow.fillAmount = p;
            // Пока держат — дуга белая и светится, чем дольше, тем ярче ореол.
            ringFill.color = p > 0f ? litColor : idleColor;
            var glow = litColor;
            glow.a = p > 0f ? Mathf.Lerp(0.5f, 1f, p) : 0f;
            ringGlow.color = glow;
        }
    }

    // Кольца рисуются кодом, чтобы не нужны были отдельные png.
    internal static class RingSprite
    {
        private const int Size = 128;
        private static Sprite ring;
        private static Sprite glow;

        // Чёткое кольцо.
        public static Sprite Ring() => ring != null ? ring : ring = Build(outerFrac: 0.49f, thicknessFrac: 0.14f, softness: 1f);

        // Мягкий ореол вокруг кольца: тот же радиус, размытые края.
        // Ореол затухает раньше края текстуры, иначе по краям квадрата виден срез.
        public static Sprite Glow() => glow != null ? glow : glow = Build(outerFrac: 0.33f, thicknessFrac: 0.07f, softness: 7f);

        private static Sprite Build(float outerFrac, float thicknessFrac, float softness)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[Size * Size];
            float half = Size * 0.5f;
            float outer = Size * outerFrac;
            float inner = outer - Size * thicknessFrac;
            float mid = (outer + inner) * 0.5f;
            float halfWidth = (outer - inner) * 0.5f;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                float outside = Mathf.Max(0f, Mathf.Abs(d - mid) - halfWidth);
                float a = softness <= 1f
                    ? Mathf.Clamp01(1f - outside)
                    : Mathf.Exp(-(outside * outside) / (2f * softness * softness)) * 0.8f;
                pixels[y * Size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        }
    }
}
