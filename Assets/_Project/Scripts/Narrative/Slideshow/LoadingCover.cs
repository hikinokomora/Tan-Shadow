using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ShadowTan.Narrative
{
    // Заливка-загрузка, которая переживает смену сцены.
    //   Yang — белый экран, чёрная точка: все загрузки между сценами (до и после слайд-шоу, в космос и из него).
    //   Yin  — чёрный экран, белая точка: смерть героини (сейчас между сценами не используется).
    // Появляется мгновенно непрозрачным серым (сцена скрыта сразу) и за settleSeconds плавно доходит до белого
    // или чёрного. Держится не меньше minSeconds, а если сцена грузится дольше — пока не загрузится.
    // Временная замена TransitionService (§13), пока нет GameFlow.
    public sealed class LoadingCover : MonoBehaviour
    {
        public enum Kind { Yin, Yang }

        private const int SortingOrder = 32000;

        private static LoadingCover instance;

        public static bool IsCovering => instance != null && instance.covering;

        private LoadingCoverConfig config;
        private Image fill;
        private Image dot;
        private bool covering;
        private bool dotVisible;
        private float shownAt;
        private Color dotColor;
        private Kind kind;

        // Загрузить сцену под заливкой.
        public static void LoadScene(string sceneName, Kind kind, LoadingCoverConfig config)
        {
            var cover = Ensure(config);
            if (cover.covering) return;
            cover.Show(kind);
            cover.StartCoroutine(cover.Routine(sceneName));
        }

        // Сцена уже открыта (например, запущена прямо из редактора): просто выдержать заливку.
        public static void CoverCurrentScene(Kind kind, LoadingCoverConfig config)
        {
            var cover = Ensure(config);
            if (cover.covering) return;
            cover.Show(kind);
            cover.StartCoroutine(cover.Routine(null));
        }

        private static LoadingCover Ensure(LoadingCoverConfig config)
        {
            if (instance == null)
            {
                var go = new GameObject("LoadingCover");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<LoadingCover>();
                instance.Build();
            }
            instance.config = config;
            return instance;
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            fill = NewImage("Fill");
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            dot = NewImage("Dot");
            dot.sprite = DiscSprite();
            var drt = dot.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(1f, 0f);

            SetAlpha(0f);
        }

        private Image NewImage(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private void Show(LoadingCover.Kind kind)
        {
            covering = true;
            dotVisible = true;
            shownAt = Time.unscaledTime;
            this.kind = kind;
            fill.color = FillColor(0f);
            dotColor = kind == Kind.Yin ? Color.white : Color.black;
            SetAlpha(1f);
            UpdateDot();
        }

        private IEnumerator Routine(string sceneName)
        {
            float started = Time.unscaledTime;

            // Сначала дать заливке отрисоваться: старт асинхронной загрузки может подвесить кадр,
            // и без этого экран застывает на старой сцене до появления заливки.
            yield return new WaitForEndOfFrame();
            yield return null;

            if (!string.IsNullOrEmpty(sceneName))
            {
                // Низкий приоритет: загрузка берёт меньше времени кадра, и точка продолжает пульсировать.
                var priority = Application.backgroundLoadingPriority;
                Application.backgroundLoadingPriority = ThreadPriority.Low;

                var op = SceneManager.LoadSceneAsync(sceneName);
                op.allowSceneActivation = false;
                // Активацию держим, пока не прошло минимальное время, чтобы новая сцена не стартовала под заливкой раньше срока.
                while (op.progress < 0.9f || Time.unscaledTime - started < config.minSeconds) yield return null;
                op.allowSceneActivation = true;
                while (!op.isDone) yield return null;

                Application.backgroundLoadingPriority = priority;
                yield return null; // первый кадр новой сцены тоже под заливкой
            }
            else
            {
                while (Time.unscaledTime - started < config.minSeconds) yield return null;
            }

            dotVisible = false;
            UpdateDot();
            for (float t = 0f; t < config.fadeOutSeconds; t += Time.unscaledDeltaTime)
            {
                SetAlpha(1f - Mathf.SmoothStep(0f, 1f, t / config.fadeOutSeconds));
                yield return null;
            }
            SetAlpha(0f);
            covering = false;
        }

        private void Update()
        {
            if (!covering) return;
            var c = FillColor(Time.unscaledTime - shownAt);
            c.a = fill.color.a;
            fill.color = c;
            UpdateDot();
        }

        // Серый в момент появления → белый (Yang) или чёрный (Yin) за settleSeconds, со сглаживанием.
        private Color FillColor(float elapsed)
        {
            float start = kind == Kind.Yin ? config.yinStartGrey : config.yangStartGrey;
            float target = kind == Kind.Yin ? 0f : 1f;
            float k = config.settleSeconds > 0f ? Mathf.SmoothStep(0f, 1f, elapsed / config.settleSeconds) : 1f;
            float g = Mathf.Lerp(start, target, k);
            return new Color(g, g, g, 1f);
        }

        private void UpdateDot()
        {
            float elapsed = Time.unscaledTime - shownAt;
            if (!dotVisible || elapsed <= config.dotDelay)
            {
                dot.enabled = false;
                return;
            }

            dot.enabled = true;
            float appear = config.dotFadeIn > 0f ? Mathf.SmoothStep(0f, 1f, (elapsed - config.dotDelay) / config.dotFadeIn) : 1f;
            float beat = Heartbeat(elapsed - config.dotDelay);

            // Канвас без скейлера: единицы — пиксели экрана.
            float baseSize = Screen.height * config.dotSize;
            float size = baseSize * (1f + config.dotBeatGrow * beat);
            float margin = Screen.height * config.dotMargin;
            var rt = dot.rectTransform;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-margin - baseSize * 0.5f, margin + baseSize * 0.5f);

            var c = dotColor;
            c.a = fill.color.a * appear * Mathf.Lerp(config.dotRestAlpha, 1f, beat);
            dot.color = c;
        }

        private void SetAlpha(float a)
        {
            var c = fill.color;
            c.a = a;
            fill.color = c;
            fill.enabled = a > 0f;
        }

        // 0..1: два коротких удара подряд и пауза (как в YinYangCover).
        private float Heartbeat(float time)
        {
            float period = 60f / Mathf.Max(1f, config.heartRate);
            float t = Mathf.Repeat(time, period) / period;
            return Mathf.Max(Pulse(t), 0.75f * Pulse(t - config.secondBeatOffset));
        }

        private static float Pulse(float t)
        {
            if (t < 0f) return 0f;
            const float rise = 0.04f;
            return t < rise ? Mathf.SmoothStep(0f, 1f, t / rise) : Mathf.Exp(-(t - rise) * 14f);
        }

        private static Sprite DiscSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
