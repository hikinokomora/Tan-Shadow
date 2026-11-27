using ShadowTan.Core.Data;
using UnityEngine;

namespace ShadowTan.Narrative
{
    // Общие настройки показа: тайминги, цвета, шрифты, тексты подсказки пропуска.
    [CreateAssetMenu(menuName = "ShadowTan/Narrative/Slideshow Config", fileName = "SlideshowConfig")]
    public sealed class SlideshowConfig : ScriptableObject
    {
        [Header("Переход через чёрный")]
        [Tbd, Min(0f)] public float fadeOutSeconds = 0.8f;
        [Tbd, Min(0f)] public float blackHoldSeconds = 0.4f;
        [Tbd, Min(0f)] public float fadeInSeconds = 0.8f;

        [Header("Картинка")]
        [Tooltip("Доля стороны картинки, на которой она плавно уходит в чёрный.")]
        [Tbd, Range(0f, 0.5f)] public float edgeFade = 0.12f;

        [Header("Слайд без дорожки (заглушка)")]
        [Tbd, Min(0f)] public float silentSlideSeconds = 2f;

        [Header("Смена реплики внутри слайда (картинка остаётся)")]
        [Tbd, Min(0f)] public float lineFadeSeconds = 0.3f;

        [Header("Пропуск")]
        [Min(0.1f)] public float skipHoldSeconds = 3f;
        [Tbd, Min(0f)] public float skipReleaseDrainSeconds = 0.5f;

        [Header("Цвета")]
        public Color captionRuColor = new Color(1f, 0.89f, 0.45f);
        public Color captionZhColor = new Color(0.78f, 0.62f, 0.28f);
        public Color skipTextColor = Color.white;
        public Color skipSecondaryColor = new Color(0.6f, 0.6f, 0.6f);
        public Color skipLitColor = Color.white;

        [Header("Подсказки листания")]
        [Tbd, Min(0f)] public float navHintFadeSeconds = 0.25f;

        [Header("Шрифты (пусто — системный шрифт ОС, заглушка)")]
        public Font captionRuFont;
        public Font captionZhFont;

        [Header("Подсказка пропуска")]
        public string skipHintRu = "Зажмите Backspace, чтобы пропустить…";
        public string skipHintZh = "按住退格键跳过……";

        [Header("Подсказка на последнем слайде")]
        public string continueHintRu = "Нажмите пробел или ЛКМ, чтобы продолжить";
        public string continueHintZh = "按空格键或鼠标左键继续";
    }
}
