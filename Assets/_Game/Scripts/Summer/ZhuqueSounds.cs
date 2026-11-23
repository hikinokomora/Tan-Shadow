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

        AudioSource oneShot2D, ambience, lavaLoop, detection, wind;
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
            float detectMax = bank != null ? bank.detectionLoop.volume : 0.6f;
            // Шум слышен с первых долей шкалы: это главный сигнал «тебя видят» (GDD-00 §05).
            float watched = encounter.State == ZhuqueState.Alert ? 1f : encounter.Detection;
            float detectTarget = covered ? 0f : Mathf.Sqrt(watched) * detectMax;
            detection.volume = Mathf.MoveTowards(detection.volume, detectTarget, Time.unscaledDeltaTime * 3f);

            var s = encounter.State;
            bool flying = s == ZhuqueState.TakeOff || s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff;
            float windMax = bank != null ? bank.flightWind.volume : 0.6f;
            wind.volume = Mathf.MoveTowards(wind.volume, flying && !covered ? windMax : 0f, Time.unscaledDeltaTime * 2f);
        }

        // ---------------- Реакции на события ----------------

        void OnAlert(Vector3 p) => Play(bank?.alertCry, fbAlert, p);
        void OnTakeOff(Vector3 p) => Play(bank?.wingsTakeOff, fbWings, p);
        void OnLanded(Vector3 p) => Play(bank?.perchLand, fbLand, p);
        void OnGrab(Vector3 p) => Play(bank?.grab, fbGrab, p);
        void OnCut(int cuts, int required, Vector3 p) => Play(bank?.featherCut, fbCut, p);
        void OnRefused(Vector3 p) => Play(bank?.cutRefused, fbRefused, p);
        void OnShake(Vector3 p) => Play(bank?.shakeOff, fbShake, p);
        void OnFall(Vector3 p) => Play(bank?.birdFall, fbFall, p);
        void OnRegen(Vector3 p, bool opensLava) => Play(bank?.regenerate, fbRegen, p);
        void OnFinal(Vector3 p) => Play(bank?.fallIntoLava, fbFinal, p);

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

        void Play(SoundSlot slot, AudioClip fallback, Vector3 position)
        {
            AudioClip clip = slot != null ? slot.Pick() : null;
            if (clip == null) clip = fallback;
            if (clip == null) return;
            float volume = slot != null ? slot.volume : 1f;
            float pitch = 1f + (slot != null ? Random.Range(-slot.pitchJitter, slot.pitchJitter) : 0f);
            float spatial = slot != null ? slot.spatial : 0f;

            if (spatial <= 0.01f)
            {
                oneShot2D.pitch = pitch;
                oneShot2D.PlayOneShot(clip, volume);
                return;
            }
            var go = new GameObject("Zhuque sound · " + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = spatial;
            source.minDistance = 6f;
            source.maxDistance = 140f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        void StartLoop(AudioSource source, SoundSlot slot, AudioClip fallback, float volume)
        {
            AudioClip clip = slot != null ? slot.Pick() : null;
            if (clip == null) clip = fallback;
            source.Stop();
            if (clip == null) return;
            source.clip = clip;
            source.volume = volume > 0f && slot != null ? slot.volume : volume;
            source.Play();
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
