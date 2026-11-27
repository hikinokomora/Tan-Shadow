using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ShadowTan.Narrative
{
    // Показ слайд-шоу в отдельной сцене.
    // У слайда одна или несколько реплик; реплики листаются, картинка при этом остаётся.
    // Листать можно только после того, как дорожка реплики доиграла.
    // Смена слайда: погасить в чёрный → немного чёрного → проявить следующий.
    // Затемняет чёрный слой поверх слайда, поэтому градиентная рамка картинки видна и во время перехода.
    // До показа — белая заливка-загрузка, после — чёрная, под которой грузится следующая сцена.
    public sealed class SlideshowPlayer : MonoBehaviour
    {
        private const string MapName = "Slideshow";

        [SerializeField] private SlideshowData data;
        [SerializeField] private SlideshowConfig config;
        [SerializeField] private LoadingCoverConfig loadingConfig;
        [SerializeField] private InputActionAsset input;

        [Header("Ссылки на UI")]
        [SerializeField] private Image fadeCover;
        [SerializeField] private Image slideImage;
        [SerializeField] private AspectRatioFitter slideFitter;
        [SerializeField] private Image slideEdgeFade;
        [SerializeField] private Text captionRu;
        [SerializeField] private Text captionZh;
        [SerializeField] private SkipHoldIndicator skipIndicator;
        [SerializeField] private NavHint prevHint;
        [SerializeField] private NavHint nextHint;
        [SerializeField] private ContinueHint continueHint;
        [SerializeField] private AudioSource voiceSource;

        private InputAction nextAction;
        private InputAction prevAction;
        private InputAction skipAction;
        private InputAction continueAction;

        private int index;
        private int line;
        private bool started;
        private bool canNavigate;
        private bool leaving;
        private float skipProgress;
        private Coroutine slideRoutine;
        private float visibility;

        private void Awake()
        {
            var map = input.FindActionMap(MapName, throwIfNotFound: true);
            nextAction = map.FindAction("Next", true);
            prevAction = map.FindAction("Prev", true);
            skipAction = map.FindAction("Skip", true);
            continueAction = map.FindAction("Continue", true);

            var ruFont = config.captionRuFont != null ? config.captionRuFont : OsFont("Georgia", "Times New Roman", "Arial");
            var zhFont = config.captionZhFont != null ? config.captionZhFont : OsFont("Microsoft YaHei", "SimHei", "SimSun", "PingFang SC", "Noto Sans CJK SC");
            captionRu.font = ruFont;
            captionRu.color = config.captionRuColor;
            captionZh.font = zhFont;
            captionZh.color = config.captionZhColor;
            skipIndicator.Setup(config, ruFont, zhFont);
            prevHint.Setup(config.navHintFadeSeconds);
            nextHint.Setup(config.navHintFadeSeconds);
            continueHint.Setup(config, ruFont, zhFont);
            prevHint.Clicked += () => { if (CanGoPrev) GoPrev(); };
            nextHint.Clicked += () => { if (CanGoNext) GoNext(); };

            slideEdgeFade.sprite = EdgeFadeSprite.Get(config.edgeFade);
            SetVisibility(0f);
        }

        private void OnEnable() => input.FindActionMap(MapName, true).Enable();
        private void OnDisable() => input.FindActionMap(MapName, true).Disable();

        private IEnumerator Start()
        {
            // Пришли через загрузку — белая заливка уже висит. Сцену открыли напрямую — показать её сейчас.
            if (!LoadingCover.IsCovering) LoadingCover.CoverCurrentScene(LoadingCover.Kind.Yang, loadingConfig);
            while (LoadingCover.IsCovering) yield return null;

            started = true;
            if (data == null || data.slides.Count == 0)
            {
                Debug.LogWarning($"[Slideshow] Нет слайдов в {name}, сразу переход дальше.");
                LoadNextScene();
                yield break;
            }
            slideRoutine = StartCoroutine(ShowSlide(0, 0, fadeOutFirst: false));
        }

        private void Update()
        {
            if (!started) return;
            if (!leaving) UpdateSkip();

            // Клавиша светится, только когда листнуть в её сторону можно прямо сейчас.
            // На последнем слайде вправо листать нельзя — вместо этого подсказка «продолжить».
            prevHint.SetLit(CanGoPrev);
            nextHint.SetLit(CanGoNext);
            continueHint.SetShown(CanContinue);

            if (CanGoNext && nextAction.WasPressedThisFrame()) GoNext();
            else if (CanGoPrev && prevAction.WasPressedThisFrame()) GoPrev();
            // ЛКМ по кнопке A листает назад, а не продолжает.
            else if (CanContinue && continueAction.WasPressedThisFrame() && !PointerOverUi()) Leave();
        }

        private bool Ready => canNavigate && !leaving;
        private bool IsLastSlide => index >= data.slides.Count - 1;
        private bool IsLastLine => line >= LineCount(index) - 1;
        private bool CanGoPrev => Ready && (index > 0 || line > 0);
        private bool CanGoNext => Ready && !(IsLastSlide && IsLastLine);
        private bool CanContinue => Ready && IsLastSlide && IsLastLine;

        private int LineCount(int slideIndex) => Mathf.Max(1, data.slides[slideIndex].lines.Count);

        // Вперёд: следующая реплика того же слайда (картинка остаётся) или первая реплика следующего слайда.
        private void GoNext()
        {
            if (!IsLastLine) slideRoutine = StartCoroutine(ShowLine(line + 1));
            else slideRoutine = StartCoroutine(ShowSlide(index + 1, 0, fadeOutFirst: true));
        }

        // Назад: предыдущая реплика или последняя реплика предыдущего слайда.
        private void GoPrev()
        {
            if (line > 0) slideRoutine = StartCoroutine(ShowLine(line - 1));
            else slideRoutine = StartCoroutine(ShowSlide(index - 1, LineCount(index - 1) - 1, fadeOutFirst: true));
        }

        private static bool PointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void UpdateSkip()
        {
            if (skipAction.IsPressed())
                skipProgress += Time.unscaledDeltaTime / config.skipHoldSeconds;
            else if (config.skipReleaseDrainSeconds > 0f)
                skipProgress -= Time.unscaledDeltaTime / config.skipReleaseDrainSeconds;
            else
                skipProgress = 0f;

            skipProgress = Mathf.Clamp01(skipProgress);
            skipIndicator.SetProgress(skipProgress);

            if (skipProgress >= 1f) Leave();
        }

        private IEnumerator ShowSlide(int newIndex, int newLine, bool fadeOutFirst)
        {
            canNavigate = false;
            voiceSource.Stop();

            if (fadeOutFirst)
            {
                yield return Fade(1f, 0f, config.fadeOutSeconds);
                yield return Wait(config.blackHoldSeconds);
            }

            index = newIndex;
            var slide = data.slides[index];
            slideImage.sprite = slide.image;
            slideImage.enabled = slide.image != null;
            slideEdgeFade.enabled = slide.image != null;
            // Рамка картинки повторяет пропорции спрайта, чтобы градиент лёг ровно по её краям.
            if (slide.image != null) slideFitter.aspectRatio = slide.image.rect.width / slide.image.rect.height;

            SetLine(newLine);
            SetCaptionAlpha(1f);
            float lockSeconds = PlayLine();

            float shownAt = Time.unscaledTime;
            yield return Fade(0f, 1f, config.fadeInSeconds);
            yield return Wait(lockSeconds - (Time.unscaledTime - shownAt));

            canNavigate = true;
            slideRoutine = null;
        }

        // Смена реплики внутри слайда: картинка остаётся, меняется только текст.
        private IEnumerator ShowLine(int newLine)
        {
            canNavigate = false;
            voiceSource.Stop();

            yield return FadeCaptions(1f, 0f, config.lineFadeSeconds);
            SetLine(newLine);
            float lockSeconds = PlayLine();

            float shownAt = Time.unscaledTime;
            yield return FadeCaptions(0f, 1f, config.lineFadeSeconds);
            yield return Wait(lockSeconds - (Time.unscaledTime - shownAt));

            canNavigate = true;
            slideRoutine = null;
        }

        private void SetLine(int newLine)
        {
            line = newLine;
            var lines = data.slides[index].lines;
            var current = line < lines.Count ? lines[line] : null;
            captionRu.text = current?.captionRu;
            captionZh.text = current?.captionZh;
        }

        // Запускает дорожку реплики и возвращает, сколько секунд листать нельзя.
        private float PlayLine()
        {
            var lines = data.slides[index].lines;
            var clip = line < lines.Count ? lines[line].voiceOver : null;
            if (clip == null) return config.silentSlideSeconds;
            voiceSource.clip = clip;
            voiceSource.Play();
            return clip.length;
        }

        private IEnumerator FadeCaptions(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                SetCaptionAlpha(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            SetCaptionAlpha(to);
        }

        private void SetCaptionAlpha(float a)
        {
            var ru = captionRu.color; ru.a = a; captionRu.color = ru;
            var zh = captionZh.color; zh.a = a; captionZh.color = zh;
        }

        private void Leave()
        {
            leaving = true;
            canNavigate = false;
            if (slideRoutine != null) StopCoroutine(slideRoutine);
            StartCoroutine(LeaveRoutine());
        }

        private IEnumerator LeaveRoutine()
        {
            voiceSource.Stop();
            yield return Fade(visibility, 0f, config.fadeOutSeconds);
            LoadNextScene();
        }

        private void LoadNextScene()
        {
            // Заглушка: потом переход будет через GameFlow (§19.3).
            if (data != null && !string.IsNullOrEmpty(data.nextScene) && Application.CanStreamedLevelBeLoaded(data.nextScene))
                LoadingCover.LoadScene(data.nextScene, LoadingCover.Kind.Yin, loadingConfig);
            else
                Debug.LogWarning($"[Slideshow] Следующая сцена «{data?.nextScene}» не задана или не в Build Profiles.");
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                SetVisibility(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            SetVisibility(to);
        }

        // 0 — слайд целиком закрыт чёрным, 1 — виден полностью.
        private void SetVisibility(float value)
        {
            visibility = value;
            var c = fadeCover.color;
            c.a = 1f - value;
            fadeCover.color = c;
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        private static Font OsFont(params string[] names) => Font.CreateDynamicFontFromOSFont(names, 32);
    }
}
