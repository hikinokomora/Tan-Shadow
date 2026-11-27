using TanShadow.Player;
using TanShadow.Seasons;
using UnityEngine;
using static TanShadow.Seasons.PlaceholderSynth;

namespace TanShadow.Summer
{
    // Слушает события испытания и играет звуки из ZhuqueSoundBank. Пустой слот — синтезированная заглушка,
    // чтобы грейбокс уже звучал и было понятно, где какой звук.
    [DefaultExecutionOrder(170)]
    public sealed class ZhuqueSounds : MonoBehaviour
    {
        public ZhuqueEncounter encounter;
        public YinYangCover cover;
        public ZhuqueSoundBank bank;
        [Tooltip("Шаги и кувырок героини; пусто — HeroVfx на героине испытания")]
        public HeroVfx heroVfx;

        AudioSource oneShot2D, ambience, lavaLoop, detection, wind;
        float gurgleTimer;
        Transform wingHand, wingRoot;     // кисть и грудь левого крыла: по ним ловим взмах
        float handHeight, handLow, handHigh;
        bool handRising, clawsPlayed;
        AudioClip fbAsh, fbLava, fbDetect, fbWind, fbAlert, fbWings, fbLand, fbFall, fbRegen, fbLavaOpen,
                  fbFinal, fbGrab, fbCut, fbRefused, fbShake, fbYin, fbYang;

        void Awake()
        {
            oneShot2D = Make2D("One shots", false);
            ambience = Make2D("Ambience", true);
            lavaLoop = Make2D("Lava rumble", true);
            detection = Make2D("Detection", true);
            wind = Make2D("Flight wind", true);
            BuildPlaceholders();
            if (heroVfx == null && encounter != null && encounter.hero != null) heroVfx = encounter.hero.GetComponent<HeroVfx>();
            if (encounter != null && encounter.bird != null)
                foreach (var t in encounter.bird.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Hand.L") wingHand = t;
                    else if (t.name == "Chest") wingRoot = t;
                }
        }

        void OnEnable()
        {
            if (encounter != null)
            {
                encounter.Alerted += OnAlert;
                encounter.TookOff += OnTakeOff;
                encounter.Landed += OnLanded;
                encounter.Grabbed += OnGrab;
                encounter.Cut += OnCut;
                encounter.CutRefused += OnRefused;
                encounter.ShakenOff += OnShake;
                encounter.BirdFell += OnFall;
                encounter.Regenerating += OnRegen;
                encounter.LavaOpened += OnLavaOpen;
                encounter.FellIntoLava += OnFinal;
                encounter.Defeated += OnDefeated;
                encounter.EncounterReset += OnReset;
            }
            if (cover != null) cover.CoverShown += OnCover;
            if (heroVfx != null) { heroVfx.SoftStep += OnStep; heroVfx.SoftRoll += OnRoll; }
        }

        void OnDisable()
        {
            if (encounter != null)
            {
                encounter.Alerted -= OnAlert;
                encounter.TookOff -= OnTakeOff;
                encounter.Landed -= OnLanded;
                encounter.Grabbed -= OnGrab;
                encounter.Cut -= OnCut;
                encounter.CutRefused -= OnRefused;
                encounter.ShakenOff -= OnShake;
                encounter.BirdFell -= OnFall;
                encounter.Regenerating -= OnRegen;
                encounter.LavaOpened -= OnLavaOpen;
                encounter.FellIntoLava -= OnFinal;
                encounter.Defeated -= OnDefeated;
                encounter.EncounterReset -= OnReset;
            }
            if (cover != null) cover.CoverShown -= OnCover;
            if (heroVfx != null) { heroVfx.SoftStep -= OnStep; heroVfx.SoftRoll -= OnRoll; }
        }

        void Start()
        {
            StartLoop(ambience, bank?.ashAmbience, fbAsh, 0.3f);
            StartLoop(detection, bank?.detectionLoop, fbDetect, 0f);
            StartLoop(wind, bank?.flightWind, fbWind, 0f);
        }

        void Update()
        {
            if (encounter == null) return;
            bool covered = cover != null && cover.IsCovering;
            float detectMax = Level(bank?.detectionLoop, detection.clip);
            // Шум слышен с первых долей шкалы: это главный сигнал «тебя видят» (GDD-00 §05).
            float watched = encounter.State == ZhuqueState.Alert ? 1f : encounter.Detection;
            float detectTarget = covered ? 0f : Mathf.Sqrt(watched) * detectMax;
            detection.volume = Mathf.MoveTowards(detection.volume, detectTarget, Time.unscaledDeltaTime * 3f);

            var s = encounter.State;
            bool flying = s == ZhuqueState.TakeOff || s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff;
            float windMax = Level(bank?.flightWind, wind.clip);
            wind.volume = Mathf.MoveTowards(wind.volume, flying && !covered ? windMax : 0f, Time.unscaledDeltaTime * 2f);
            if (!covered) { Gurgles(); Claws(s); }
        }

        // Взмах — по анимации: кисть крыла дошла до верхней точки и пошла вниз. Звук идёт с началом взмаха вниз,
        // поэтому совпадает с крыльями при любой скорости клипа; при планировании (крыло не поднимается) тишина.
        // LateUpdate — после Animator, кость уже в позе этого кадра.
        void LateUpdate()
        {
            if (encounter == null || wingHand == null || wingRoot == null) return;
            var s = encounter.State;
            bool airborne = s == ZhuqueState.TakeOff || s == ZhuqueState.Relocating || s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff;
            float h = Vector3.Dot(wingHand.position - wingRoot.position, encounter.bird.transform.up);
            if (!airborne) { handHeight = handLow = handHigh = h; handRising = false; return; }
            if (h > handHeight)
            {
                if (!handRising) handLow = handHeight;   // нижняя точка: крыло пошло вверх
                handRising = true;
                handHigh = h;
            }
            else if (h < handHeight)
            {
                float minRise = bank != null ? bank.flapMinRise : 0.6f;
                if (handRising && handHigh - handLow >= minRise && !(cover != null && cover.IsCovering))
                    Play(bank?.wingsTakeOff, fbWings, wingHand.position, encounter.bird.transform, AccentNow(bank?.wingsTakeOff));
                handRising = false;
            }
            handHeight = h;
        }

        // Когти по камню: звук начинается заранее, чтобы его главный момент пришёлся на касание (Landed).
        void Claws(ZhuqueState s)
        {
            if (s != ZhuqueState.Relocating) { clawsPlayed = false; return; }
            float accent = bank != null ? bank.perchLand.accent : 0f;
            if (clawsPlayed || encounter.RelocateTimeLeft > accent) return;
            clawsPlayed = true;
            Play(bank?.perchLand, fbLand, encounter.bird.transform.position, null, encounter.RelocateTimeLeft);
        }

        static float AccentNow(SoundSlot slot) => slot != null ? slot.accent : -1f;

        // Лава булькает в реке рядом с героиней: случайная точка в радиусе, луч вниз по коллайдеру реки.
        void Gurgles()
        {
            var lava = encounter.lava;
            if (lava == null || !lava.IsOpen || bank == null || bank.lavaGurgle.Pick() == null) return;
            gurgleTimer -= Time.deltaTime;
            if (gurgleTimer > 0f) return;
            gurgleTimer = Random.Range(bank.lavaGurgleInterval.x, bank.lavaGurgleInterval.y);
            Vector3 hero = encounter.hero.Body.position;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 o = Random.insideUnitCircle * bank.lavaGurgleRadius;
                var ray = new Ray(new Vector3(hero.x + o.x, hero.y + 40f, hero.z + o.y), Vector3.down);
                foreach (var river in lava.rivers)
                    if (river != null && river.Raycast(ray, out var hit, 120f))
                    {
                        Play(bank.lavaGurgle, null, hit.point, null, AccentNow(bank.lavaGurgle));
                        return;
                    }
            }
        }

        // ---------------- Реакции на события ----------------

        // Время ниже — «главный момент клипа (accent) прозвучит через столько секунд»: от событий испытания.
        void OnAlert(Vector3 p) => Play(bank?.alertCry, fbAlert, p);
        void OnTakeOff(Vector3 p) { }
        // Когти играет Claws заранее; без перелёта (начало сцены, сброс) птица просто стоит на камне.
        void OnLanded(Vector3 p) { }
        void OnGrab(Vector3 p) => Play(bank?.grab, fbGrab, p);
        void OnCut(int cuts, int required, Vector3 p)
        {
            Play(bank?.featherCut, fbCut, p);
            // Удары 1–3: птица теряет управление и падает; 4-й — падение в лаву (fallIntoLava).
            // Кульминация — в момент удара о землю (через fallTime).
            if (cuts < required) Play(bank?.loseControl, null, encounter.bird.BodyCenter, encounter.bird.transform, encounter.tuning.fallTime);
        }
        void OnRefused(Vector3 p) => Play(bank?.cutRefused, fbRefused, p);
        void OnShake(Vector3 p) => Play(bank?.shakeOff, fbShake, p);
        void OnFall(Vector3 p)
        {
            Play(bank?.birdFall, fbFall, p, null, AccentNow(bank?.birdFall));
            Play(bank?.birdTumble, null, p, null, TumbleAfterImpact);   // перекатилась после удара
        }

        const float TumbleAfterImpact = 0.3f;   // через сколько после удара о землю — кувырок, с

        void OnStep(Vector3 p) => Play(bank?.heroStep, null, p, null, AccentNow(bank?.heroStep));
        // Кувырок: главный момент — приземление в конце рывка.
        void OnRoll(Vector3 p) => Play(bank?.heroRoll, null, p, null, heroVfx != null && heroVfx.controller != null ? heroVfx.controller.settings.dashDuration : 0.25f);
        // Огонь сильнее всего на пике вспышки (ZhuqueEncounter.IgniteAt восстановления).
        void OnRegen(Vector3 p, bool opensLava) => Play(bank?.regenerate, fbRegen, p, null, ZhuqueEncounter.IgniteAt * encounter.tuning.regenerateTime);
        // Всплеск — когда птица касается магмы (белый экран — на cutLeadTime раньше).
        void OnFinal(Vector3 p) => Play(bank?.fallIntoLava, fbFinal, p, null, encounter.tuning.finalFallTime);

        void OnLavaOpen(Vector3 p)
        {
            Play(bank?.lavaOpen, fbLavaOpen, p);
            StartLoop(lavaLoop, bank?.lavaAmbience, fbLava, 0.5f);
        }

        void OnReset() => lavaLoop.Stop();

        void OnDefeated()
        {
            lavaLoop.Stop();
            StartLoop(ambience, bank?.deadWorldAmbience, null, 0.3f);
        }

        void OnCover(YinYangCover.Kind kind)
        {
            if (kind == YinYangCover.Kind.Yin) Play(bank?.cutYin, fbYin, transform.position);
            else Play(bank?.cutYang, fbYang, transform.position);
        }

        // ---------------- Проигрывание ----------------

        // follow — звук летит вместе с объектом (птица в полёте и при падении).
        // accentIn ≥ 0 — главный момент клипа (slot.accent) должен прозвучать через accentIn секунд: звук стартует
        // с задержкой или с середины клипа. Меньше 0 — просто сейчас, с начала.
        void Play(SoundSlot slot, AudioClip fallback, Vector3 position, Transform follow = null, float accentIn = -1f)
        {
            AudioClip clip = slot != null ? slot.Pick() : null;
            if (clip == null) clip = fallback;
            if (clip == null) return;
            float volume = Level(slot, clip, fallback);
            float pitch = 1f + (slot != null ? Random.Range(-slot.pitchJitter, slot.pitchJitter) : 0f);
            float spatial = slot != null ? slot.spatial : 0f;
            float delay = 0f, skip = 0f;
            if (accentIn >= 0f && slot != null && clip != fallback)
            {
                float shift = accentIn - slot.accent / pitch;
                delay = Mathf.Max(0f, shift);
                skip = Mathf.Min(Mathf.Max(0f, -shift) * pitch, clip.length - 0.05f);
            }

            if (spatial <= 0.01f && delay <= 0f && skip <= 0f)
            {
                oneShot2D.pitch = pitch;
                oneShot2D.PlayOneShot(clip, volume);
                return;
            }
            var go = new GameObject("Zhuque sound · " + clip.name);
            go.transform.position = position;
            if (follow != null) go.transform.SetParent(follow, true);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = spatial;
            source.minDistance = 6f;
            source.maxDistance = 140f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.time = skip;
            if (delay > 0f) source.PlayDelayed(delay); else source.Play();
            Destroy(go, delay + (clip.length - skip) / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        void StartLoop(AudioSource source, SoundSlot slot, AudioClip fallback, float volume)
        {
            AudioClip clip = slot != null ? slot.Pick() : null;
            if (clip == null) clip = fallback;
            source.Stop();
            if (clip == null) return;
            source.clip = clip;
            source.volume = volume > 0f ? Level(slot, clip, fallback, volume) : 0f;
            source.Play();
        }

        // Громкость звука: слот × общая громкость; заглушка (пустой слот) — ещё тише.
        float Level(SoundSlot slot, AudioClip clip, AudioClip fallback = null, float fallbackVolume = 1f)
        {
            float master = bank != null ? bank.master : 0.8f;
            bool placeholder = slot == null || slot.clips == null || slot.clips.Length == 0 || slot.clips[0] == null
                               || (fallback != null && clip == fallback);
            float v = slot != null ? slot.volume : fallbackVolume;
            return v * master * (placeholder ? (bank != null ? bank.placeholders : 0.25f) : 1f);
        }

        AudioSource Make2D(string label, bool loop)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.loop = loop;
            return s;
        }

        // ---------------- Синтезированные заглушки ----------------

        void BuildPlaceholders()
        {
            fbAsh = Noise("PH ash wind", 6f, 0.015f, 0.22f, loop: true);
            fbLava = Noise("PH lava rumble", 5f, 0.04f, 0.5f, loop: true);
            fbDetect = Noise("PH detection hiss", 3f, 0.6f, 0.35f, loop: true);
            fbWind = Noise("PH flight wind", 4f, 0.12f, 0.45f, loop: true);
            fbAlert = Sweep("PH alert cry", 0.45f, 900f, 1900f, 0.6f);
            fbWings = Bursts("PH wings", 0.9f, 6, 0.55f);
            fbLand = Bursts("PH perch land", 0.4f, 3, 0.4f);
            fbFall = Thud("PH bird fall", 0.8f, 52f, 0.9f);
            fbRegen = Sweep("PH regenerate flare", 1.8f, 140f, 700f, 0.6f);
            fbLavaOpen = Thud("PH lava open", 2.6f, 34f, 1f);
            fbFinal = Sweep("PH fall into lava", 1.2f, 600f, 80f, 0.7f);
            fbGrab = Thud("PH grab", 0.3f, 140f, 0.5f);
            fbCut = Tone("PH feather cut", 0.35f, 1500f, 12f, 0.35f, 2.3f);
            fbRefused = Tone("PH cut refused", 0.4f, 220f, 9f, 0.35f, 1.4f);
            fbShake = Bursts("PH shake off", 0.6f, 8, 0.5f);
            fbYin = Thud("PH cut yin", 0.6f, 70f, 0.6f);
            fbYang = Tone("PH cut yang", 2.0f, 1320f, 1.6f, 0.4f, 1.5f);
        }
    }
}
