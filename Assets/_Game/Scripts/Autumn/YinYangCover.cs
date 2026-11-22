using System;
using System.Collections;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Заливка смерти и победы. Она же — экран загрузки: всё, что меняет мир, делается под ней.
    //   Инь — смерть героини: чёрный экран, белая точка.
    //   Янь — смерть колосса: белый экран, чёрная точка.
    // Экран появляется резко, без затухания. Точка в правом нижнем углу (GDD-00 §06) проявляется
    // с задержкой и пульсирует в ритме сердцебиения: удар — второй удар — пауза.
    public sealed class YinYangCover : MonoBehaviour
    {
        public enum Kind { Yin, Yang }

        [Tooltip("Минимум, сколько держится заливка, даже если работа под ней закончилась мгновенно, с")]
        public float yinMinHold = 1.6f;
        public float yangMinHold = 2.2f;
        [Tooltip("Время исчезновения заливки, с (плавное, со сглаживанием)")]
        public float fadeOut = 0.3f;

        [Header("Точка-сердцебиение в углу")]
        public bool showCornerDot = true;
        [Tooltip("Через сколько после появления экрана проявляется точка, с")]
        [Min(0)] public float dotDelay = 0.45f;
        [Tooltip("За сколько точка проявляется после задержки, с")]
        [Min(0)] public float dotFadeIn = 0.25f;
        [Tooltip("Пульс, ударов в минуту")]
        [Range(30, 180)] public float heartRate = 66f;
        [Tooltip("Второй удар («тук-ТУК») через столько после первого, доля периода")]
        [Range(0.1f, 0.6f)] public float secondBeatOffset = 0.3f;
        [Tooltip("Яркость точки между ударами (0 — гаснет совсем)")]
        [Range(0, 1)] public float dotRestAlpha = 0.3f;
        [Tooltip("Насколько точка увеличивается на ударе (0.3 = на 30%)")]
        [Range(0, 1)] public float dotBeatGrow = 0.35f;
        [Tooltip("Размер точки в долях высоты экрана")]
        public float dotSize = 0.032f;
        [Tooltip("Отступ точки от правого нижнего угла в долях высоты экрана")]
        public float dotMargin = 0.05f;

        public bool IsCovering { get; private set; }

        public event Action<Kind> CoverShown;
        public event Action<Kind> CoverHidden;

        Kind kind;
        float alpha;
        bool dotVisible;
        float shownAt;
        static Texture2D pixel, disc;

        public void Run(Kind coverKind, Func<IEnumerator> workUnderCover, Action onRevealed = null)
        {
            if (IsCovering) return;
            StartCoroutine(Routine(coverKind, workUnderCover, onRevealed));
        }

        IEnumerator Routine(Kind coverKind, Func<IEnumerator> work, Action onRevealed)
        {
            IsCovering = true;
            kind = coverKind;
            alpha = 1f;
            dotVisible = true;
            shownAt = Time.unscaledTime;
            CoverShown?.Invoke(kind);

            float started = Time.unscaledTime;
            if (work != null) yield return StartCoroutine(work());
            float hold = kind == Kind.Yin ? yinMinHold : yangMinHold;
            while (Time.unscaledTime - started < hold) yield return null;

            dotVisible = false;
            for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
            {
                alpha = 1f - Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, fadeOut));
                yield return null;
            }
            alpha = 0f;
            IsCovering = false;
            CoverHidden?.Invoke(kind);
            onRevealed?.Invoke();
        }

        void OnGUI()
        {
            if (alpha <= 0f) return;
            EnsureTextures();
            GUI.depth = -1000;

            Color fill = kind == Kind.Yin ? Color.black : Color.white;
            Color other = kind == Kind.Yin ? Color.white : Color.black;

            GUI.color = new Color(fill.r, fill.g, fill.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), pixel);

            float elapsed = Time.unscaledTime - shownAt;
            if (showCornerDot && dotVisible && elapsed > dotDelay)
            {
                float appear = dotFadeIn > 0f ? Mathf.SmoothStep(0f, 1f, (elapsed - dotDelay) / dotFadeIn) : 1f;
                float beat = Heartbeat(elapsed - dotDelay);
                float size = Screen.height * dotSize * (1f + dotBeatGrow * beat);
                float margin = Screen.height * dotMargin;
                float cx = Screen.width - margin - Screen.height * dotSize * 0.5f;
                float cy = Screen.height - margin - Screen.height * dotSize * 0.5f;
                GUI.color = new Color(other.r, other.g, other.b, alpha * appear * Mathf.Lerp(dotRestAlpha, 1f, beat));
                GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), disc);
            }
            GUI.color = Color.white;
        }

        // 0..1: два коротких удара подряд и пауза. Каждый удар — резкий подъём и быстрый спад.
        float Heartbeat(float time)
        {
            float period = 60f / Mathf.Max(1f, heartRate);
            float t = Mathf.Repeat(time, period) / period;
            return Mathf.Max(Pulse(t), 0.75f * Pulse(t - secondBeatOffset));
        }

        static float Pulse(float t)
        {
            if (t < 0f) return 0f;
            const float rise = 0.04f;
            return t < rise ? Mathf.SmoothStep(0f, 1f, t / rise) : Mathf.Exp(-(t - rise) * 14f);
        }

        static void EnsureTextures()
        {
            if (pixel == null)
            {
                pixel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                pixel.SetPixel(0, 0, Color.white);
                pixel.Apply();
            }
            if (disc == null)
            {
                const int size = 128;
                disc = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                float r = size * 0.5f;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    disc.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
                }
                disc.Apply();
            }
        }
    }
}
