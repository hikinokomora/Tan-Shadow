using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ShadowTan.Narrative
{
    // Подсказка листания (клавиша и стрелка над ней). По ней можно кликнуть ЛКМ.
    // Тусклые картинки видны всегда, светящиеся плавно проявляются поверх, когда листать в эту сторону можно.
    public sealed class NavHint : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image[] litImages;

        public event Action Clicked;

        private float fadeSeconds;
        private float alpha;
        private bool lit;

        public void Setup(float fadeSecondsValue)
        {
            fadeSeconds = fadeSecondsValue;
            alpha = 0f;
            Apply();
        }

        public void SetLit(bool value) => lit = value;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
        }

        private void Update()
        {
            float target = lit ? 1f : 0f;
            if (Mathf.Approximately(alpha, target)) return;
            alpha = fadeSeconds > 0f ? Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeSeconds) : target;
            Apply();
        }

        private void Apply()
        {
            foreach (var image in litImages)
            {
                var c = image.color;
                c.a = alpha;
                image.color = c;
            }
        }
    }
}
