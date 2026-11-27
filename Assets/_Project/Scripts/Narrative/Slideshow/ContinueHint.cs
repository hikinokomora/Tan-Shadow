using UnityEngine;
using UnityEngine.UI;

namespace ShadowTan.Narrative
{
    // Надпись на последнем слайде «нажмите пробел или ЛКМ, чтобы продолжить» с китайским переводом под ней.
    // Плавно проявляется, когда последний слайд дозвучал.
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ContinueHint : MonoBehaviour
    {
        [SerializeField] private Text hintRu;
        [SerializeField] private Text hintZh;

        private CanvasGroup group;
        private float fadeSeconds;
        private bool shown;

        public void Setup(SlideshowConfig config, Font ruFont, Font zhFont)
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 0f;
            fadeSeconds = config.navHintFadeSeconds;

            hintRu.text = config.continueHintRu;
            hintRu.color = config.skipTextColor;
            hintRu.font = ruFont;
            hintZh.text = config.continueHintZh;
            hintZh.color = config.skipSecondaryColor;
            hintZh.font = zhFont;
        }

        public void SetShown(bool value) => shown = value;

        private void Update()
        {
            float target = shown ? 1f : 0f;
            if (Mathf.Approximately(group.alpha, target)) return;
            group.alpha = fadeSeconds > 0f ? Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds) : target;
        }
    }
}
