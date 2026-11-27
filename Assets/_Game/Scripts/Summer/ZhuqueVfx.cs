using TanShadow.Player;
using UnityEngine;
using UnityEngine.UI;

namespace TanShadow.Summer
{
    // Летние эффекты (VFX-инструкция v3, раздел 4). Слушает события испытания, сам ничего не решает:
    // засчитанность ударов, лава и тень — у ZhuqueEncounter. Лавовые пузыри и искры (SU-08, SU-09) — частицы
    // внутри lavaRoot: включаются вместе с лавой после третьего восстановления.
    //   SU-03 полосы ветра — экранные полосы, пока героиня на хвосте;
    //   SU-04 перья — с кончика хвоста на засчитанный удар (Cut);
    //   SU-05 вспышка птицы — ZhuqueEncounter (свечение материалов), SU-06 огонь — на её пике (Ignited);
    //   SU-07 пепел — в точке, где упавшая птица коснулась земли (BirdFell);
    //   SU-10 тень — силуэт, гасящий свечение лавы там, куда по лучу солнца падает тень птицы (та же точка,
    //   по которой засчитывается удар 4). На суше видна обычная тень модели, на лаве — только эта.
    // G-02: удар 4 над сушей не прошёл (CutRefused) — вспышка отскока HeroVfx.
    [DefaultExecutionOrder(520)]
    public sealed class ZhuqueVfx : MonoBehaviour
    {
        public ZhuqueVfxSettings settings;
        public ZhuqueEncounter encounter;
        public HeroVfx heroVfx;

        [Header("Частицы (world space)")]
        public ParticleSystem feathers;
        public ParticleSystem ignite;
        public ParticleSystem fallDust;

        [Header("SU-03 Ветер")]
        public Canvas windCanvas;
        [Tooltip("Текстура одной полосы")]
        public Texture windTexture;

        [Header("SU-10 Тень")]
        [Tooltip("Повёрнут по нормали поверхности")]
        public Transform shadowRoot;
        [Tooltip("Силуэт внутри shadowRoot: поворот — по курсу птицы")]
        public Renderer shadowSilhouette;

        RectTransform[] streaks;
        RawImage[] streakImages;
        float[] streakRadius, streakSpeed;
        Vector2[] streakDir;
        float wind, shadow;
        Vector3 birdVelocity, lastBirdPosition;
        bool haveBird;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            BuildStreaks();
            if (shadowRoot != null) shadowRoot.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            if (encounter == null) return;
            encounter.Cut += OnCut;
            encounter.CutRefused += OnRefused;
            encounter.BirdFell += OnFell;
            encounter.Ignited += OnIgnited;
            encounter.HeroKilled += OnStopAll;
            encounter.Defeated += OnStopAll;
            encounter.EncounterReset += OnStopAll;
        }

        void OnDisable()
        {
            if (encounter == null) return;
            encounter.Cut -= OnCut;
            encounter.CutRefused -= OnRefused;
            encounter.BirdFell -= OnFell;
            encounter.Ignited -= OnIgnited;
            encounter.HeroKilled -= OnStopAll;
            encounter.Defeated -= OnStopAll;
            encounter.EncounterReset -= OnStopAll;
        }

        // Смерть, победа, перезапуск: оверлей и тень гаснут в тот же кадр, до чёрного или белого экрана.
        void OnStopAll()
        {
            wind = 0f;
            shadow = 0f;
            if (windCanvas != null) windCanvas.enabled = false;
            if (shadowRoot != null) shadowRoot.gameObject.SetActive(false);
            if (feathers != null) feathers.Clear();
            if (ignite != null) ignite.Clear();
            if (fallDust != null) fallDust.Clear();
            if (heroVfx != null) heroVfx.ClearAll();
        }

        void OnCut(int cuts, int required, Vector3 at)
        {
            if (feathers == null || settings == null) return;
            var bird = encounter.bird;
            Vector3 tip = bird.TailEnd;
            Vector3 along = Flat(bird.transform.forward);
            Vector3 side = Vector3.Cross(Vector3.up, along);
            int count = Random.Range(settings.featherCount.x, settings.featherCount.y + 1);
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                // В стороны от удара, немного вверх; часть скорости птицы — перья сначала летят с ней.
                Vector3 dir = (side * (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.6f, 1f)
                               + Vector3.up * Random.Range(-0.2f, 0.7f) - along * Random.Range(0f, 0.5f)).normalized;
                p.position = tip + Random.insideUnitSphere * 0.4f;
                p.velocity = dir * Random.Range(settings.featherSpeed.x, settings.featherSpeed.y) + birdVelocity * settings.featherInherit;
                p.startLifetime = Random.Range(settings.featherLifetime.x, settings.featherLifetime.y);
                p.startSize = Random.Range(settings.featherSize.x, settings.featherSize.y);
                p.rotation = Random.Range(0f, 360f);
                p.angularVelocity = Random.Range(-540f, 540f);
                feathers.Emit(p, 1);
            }
        }

        void OnRefused(Vector3 at)
        {
            if (heroVfx != null) heroVfx.Rebound(encounter.bird.TailEnd, -Flat(encounter.bird.transform.forward));
        }

        void OnIgnited(Vector3 center)
        {
            if (ignite == null || settings == null) return;
            var bird = encounter.bird.transform;
            int count = Random.Range(settings.igniteCount.x, settings.igniteCount.y + 1);
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                // Корпус и расправленные крылья: точки вытянуты вбок, разлёт от центра.
                Vector3 offset = bird.right * Random.Range(-1f, 1f) * settings.igniteRadius * 1.6f
                                 + bird.forward * Random.Range(-0.5f, 0.5f) * settings.igniteRadius
                                 + bird.up * Random.Range(-0.3f, 0.6f) * settings.igniteRadius;
                Vector3 dir = (offset + Random.insideUnitSphere * 0.5f + Vector3.up * 0.3f).normalized;
                p.position = center + offset;
                p.velocity = dir * Random.Range(settings.igniteSpeed.x, settings.igniteSpeed.y);
                p.startLifetime = Random.Range(settings.igniteLifetime.x, settings.igniteLifetime.y);
                p.startSize = Random.Range(settings.igniteSize.x, settings.igniteSize.y);
                p.rotation = Random.Range(0f, 360f);
                ignite.Emit(p, 1);
            }
        }

        void OnFell(Vector3 at)
        {
            if (fallDust == null || settings == null) return;
            Vector3 normal = Vector3.up;
            if (Physics.Raycast(at + Vector3.up * 2f, Vector3.down, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(encounter.bird.transform))
            {
                at = hit.point;
                normal = hit.normal;
            }
            Vector3 a = Vector3.Cross(normal, Vector3.forward).sqrMagnitude > 0.01f ? Vector3.Cross(normal, Vector3.forward).normalized : Vector3.right;
            Vector3 b = Vector3.Cross(normal, a);
            int count = Random.Range(settings.fallDustCount.x, settings.fallDustCount.y + 1);
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                Vector3 radial = a * Mathf.Cos(angle) + b * Mathf.Sin(angle);
                p.position = at + radial * Random.Range(0.2f, 1f) * settings.fallDustRadius + normal * 0.3f;
                p.velocity = radial * Random.Range(settings.fallDustSpeed.x, settings.fallDustSpeed.y) + normal * Random.Range(0.3f, 1.2f);
                p.startLifetime = Random.Range(settings.fallDustLifetime.x, settings.fallDustLifetime.y);
                p.startSize = Random.Range(settings.fallDustSize.x, settings.fallDustSize.y);
                p.rotation = Random.Range(0f, 360f);
                fallDust.Emit(p, 1);
            }
        }

        void LateUpdate()
        {
            if (encounter == null || settings == null || encounter.bird == null) return;
            float dt = Time.deltaTime;
            Vector3 birdPosition = encounter.bird.transform.position;
            if (haveBird && dt > 0f) birdVelocity = (birdPosition - lastBirdPosition) / dt;
            lastBirdPosition = birdPosition;
            haveBird = encounter.bird.gameObject.activeInHierarchy;

            var s = encounter.State;
            bool carried = s == ZhuqueState.TakeOff || s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff;
            UpdateWind(carried, s == ZhuqueState.TakeOff ? 0.6f : 1f, dt);
            UpdateShadow(s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff, dt);
        }

        // ---------------- SU-03 ----------------

        void BuildStreaks()
        {
            if (windCanvas == null || settings == null) return;
            int n = settings.windStreaks;
            streaks = new RectTransform[n];
            streakImages = new RawImage[n];
            streakRadius = new float[n];
            streakSpeed = new float[n];
            streakDir = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Wind streak " + i, typeof(RectTransform));
                go.transform.SetParent(windCanvas.transform, false);
                var img = go.AddComponent<RawImage>();
                img.texture = windTexture;
                img.raycastTarget = false;
                streaks[i] = (RectTransform)go.transform;
                streaks[i].anchorMin = streaks[i].anchorMax = new Vector2(0.5f, 0.5f);
                streakImages[i] = img;
                Respawn(i, Random.value);
            }
            windCanvas.enabled = false;
        }

        void Respawn(int i, float along)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            streakDir[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            streakRadius[i] = Mathf.Lerp(settings.windInner, 1.25f, along);
            streakSpeed[i] = Random.Range(settings.windSpeed.x, settings.windSpeed.y);
        }

        void UpdateWind(bool on, float speedScale, float dt)
        {
            if (windCanvas == null || streaks == null) return;
            wind = Mathf.MoveTowards(wind, on ? 1f : 0f, dt / (on ? settings.windFadeIn : settings.windFadeOut));
            windCanvas.enabled = wind > 0.001f;
            if (!windCanvas.enabled) return;
            var rect = ((RectTransform)windCanvas.transform).rect;
            Vector2 half = rect.size * 0.5f;
            float k = rect.height / 1080f;
            for (int i = 0; i < streaks.Length; i++)
            {
                streakRadius[i] += streakSpeed[i] * speedScale * dt;
                if (streakRadius[i] > 1.25f) Respawn(i, 0f);
                float r = streakRadius[i];
                // Ярче всего у края кольца, к центру и за краем экрана гаснет.
                float edge = Mathf.Clamp01((r - settings.windInner) / 0.15f) * Mathf.Clamp01((1.25f - r) / 0.25f);
                var c = Color.white;
                c.a = settings.windAlpha * wind * edge;
                streakImages[i].color = c;
                streaks[i].anchoredPosition = new Vector2(streakDir[i].x * half.x, streakDir[i].y * half.y) * r;
                streaks[i].sizeDelta = new Vector2(settings.windStreakSize.x * k * (0.6f + 0.4f * speedScale), settings.windStreakSize.y * k);
                streaks[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(streakDir[i].y * half.y, streakDir[i].x * half.x) * Mathf.Rad2Deg);
            }
        }

        // ---------------- SU-10 ----------------

        void UpdateShadow(bool flying, float dt)
        {
            if (shadowRoot == null || shadowSilhouette == null) return;
            var lava = encounter.lava;
            bool onLava = flying && lava != null && lava.IsOpen && lava.IsOverLava(encounter.ShadowPoint);
            shadow = Mathf.MoveTowards(shadow, onLava ? 1f : 0f, dt * settings.shadowFade);
            bool show = shadow > 0.001f;
            if (shadowRoot.gameObject.activeSelf != show) shadowRoot.gameObject.SetActive(show);
            if (!show) return;
            Vector3 n = encounter.ShadowNormal;
            Vector3 heading = Vector3.ProjectOnPlane(encounter.bird.transform.forward, n);
            if (heading.sqrMagnitude < 0.001f) heading = Vector3.ProjectOnPlane(Vector3.forward, n);
            shadowRoot.SetPositionAndRotation(encounter.ShadowPoint + n * settings.shadowLift, Quaternion.LookRotation(heading, n));
            shadowSilhouette.transform.localScale = Vector3.one * settings.shadowSpan;
            float d = settings.shadowDarkness;
            block.SetColor(ColorId, new Color(0.02f, 0.01f, 0.01f, d * shadow));   // лава в HDR очень яркая: гасим почти до чёрного
            shadowSilhouette.SetPropertyBlock(block);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }
    }
}
