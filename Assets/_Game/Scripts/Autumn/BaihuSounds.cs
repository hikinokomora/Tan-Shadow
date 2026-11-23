using TanShadow.Seasons;
using static TanShadow.Seasons.PlaceholderSynth;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Слушает события испытания и играет звуки из BaihuSoundBank. Если в слоте нет клипа,
    // играет синтезированную заглушку, чтобы грейбокс уже звучал и было понятно, где какой звук.
    [DefaultExecutionOrder(170)]
    public sealed class BaihuSounds : MonoBehaviour
    {
        public BaihuEncounter encounter;
        public YinYangCover cover;
        public BaihuSoundBank bank;

        AudioSource oneShot2D, ambience, noise;
        AudioClip fbRustle, fbEyes, fbNoise, fbSwing, fbRun, fbGong, fbPounce, fbMiss,
                  fbImpact, fbCrack, fbFall, fbLand, fbYin, fbYang, fbForest,
                  fbTigerFall, fbTigerUp, fbTigerLeap;

        void Awake()
        {
            oneShot2D = Make2D("One shots");
            ambience = Make2D("Ambience");
            ambience.loop = true;
            noise = Make2D("Aggro noise");
            noise.loop = true;
            noise.volume = 0f;
            BuildPlaceholders();
        }

        void OnEnable()
        {
            if (encounter != null)
            {
                encounter.Rustled += OnRustle;
                encounter.EyesShown += OnEyes;
                encounter.SwingCounted += OnSwing;
                encounter.Scared += OnScared;
                encounter.GongStruck += OnGong;
                encounter.Pounced += OnPounce;
                encounter.PounceMissed += OnMiss;
                encounter.TigerFell += OnTigerFell;
                encounter.TigerGotUp += OnTigerUp;
                encounter.TigerLeptAway += OnTigerLeap;
                encounter.MonumentHit += OnMonumentHit;
                encounter.MonumentFalling += OnFall;
                encounter.MonumentLanded += OnLand;
                encounter.Defeated += OnDefeated;
            }
            if (cover != null) cover.CoverShown += OnCover;
        }

        void OnDisable()
        {
            if (encounter != null)
            {
                encounter.Rustled -= OnRustle;
                encounter.EyesShown -= OnEyes;
                encounter.SwingCounted -= OnSwing;
                encounter.Scared -= OnScared;
                encounter.GongStruck -= OnGong;
                encounter.Pounced -= OnPounce;
                encounter.PounceMissed -= OnMiss;
                encounter.TigerFell -= OnTigerFell;
                encounter.TigerGotUp -= OnTigerUp;
                encounter.TigerLeptAway -= OnTigerLeap;
                encounter.MonumentHit -= OnMonumentHit;
                encounter.MonumentFalling -= OnFall;
                encounter.MonumentLanded -= OnLand;
                encounter.Defeated -= OnDefeated;
            }
            if (cover != null) cover.CoverShown -= OnCover;
        }

        void Start()
        {
            StartLoop(ambience, bank != null ? bank.forestAmbience : null, fbForest);
            var noiseClip = bank != null && bank.aggroNoiseLoop.Pick() != null ? bank.aggroNoiseLoop.Pick() : fbNoise;
            noise.clip = noiseClip;
            noise.Play();
        }

        void Update()
        {
            if (encounter == null || encounter.tuning == null) return;
            float max = bank != null ? bank.aggroNoiseLoop.volume : 1f;
            float target = encounter.tuning.noiseByAggro.Evaluate(encounter.Aggro) * max;
            if (cover != null && cover.IsCovering) target = 0f;
            noise.volume = Mathf.MoveTowards(noise.volume, target, Time.unscaledDeltaTime * 3f);
        }

        // ---------------- Реакции на события ----------------

        void OnRustle(Vector3 p) => Play(bank?.rustle, fbRustle, p);
        void OnEyes(Vector3 p) => Play(bank?.eyesAppear, fbEyes, p);
        void OnSwing(int count, int required) => Play(bank?.swingCounted, fbSwing, transform.position);
        void OnScared(Vector3 p) => Play(bank?.scaredWithdraw, fbRun, p);
        void OnGong(Vector3 p) => Play(bank?.gong, fbGong, p);
        void OnPounce(Vector3 p) => Play(bank?.pounce, fbPounce, p);
        void OnMiss(Vector3 p) => Play(bank?.pounceMiss, fbMiss, p);
        void OnTigerFell(Vector3 p) => Play(bank?.tigerFall, fbTigerFall, p);
        void OnTigerUp(Vector3 p) => Play(bank?.tigerGetUp, fbTigerUp, p);
        void OnTigerLeap(Vector3 p) => Play(bank?.tigerLeapAway, fbTigerLeap, p);
        void OnFall(Vector3 p) => Play(bank?.monumentFall, fbFall, p);
        void OnLand(Vector3 p) => Play(bank?.monumentLand, fbLand, p);

        void OnMonumentHit(int hits, int required, Vector3 p)
        {
            Play(bank?.monumentImpact, fbImpact, p);
            // Треск громче с каждым ударом: слышно, что основание сдаёт.
            float boost = Mathf.Lerp(0.5f, 1f, required > 1 ? (hits - 1f) / (required - 1f) : 1f);
            Play(bank?.monumentCrack, fbCrack, p, boost);
        }

        void OnCover(YinYangCover.Kind kind)
        {
            if (kind == YinYangCover.Kind.Yin) Play(bank?.cutYin, fbYin, transform.position);
            else Play(bank?.cutYang, fbYang, transform.position);
        }

        void OnDefeated() => StartLoop(ambience, bank?.deadWorldAmbience, null);

        // ---------------- Проигрывание ----------------

        void Play(BaihuSoundBank.Slot slot, AudioClip fallback, Vector3 position, float volumeScale = 1f)
        {
            AudioClip clip = slot != null ? slot.Pick() : null;
            if (clip == null) clip = fallback;
            if (clip == null) return;
            float volume = (slot != null ? slot.volume : 1f) * volumeScale;
            float pitch = 1f + (slot != null ? Random.Range(-slot.pitchJitter, slot.pitchJitter) : 0f);
            float spatial = slot != null ? slot.spatial : 0f;

            if (spatial <= 0.01f)
            {
                oneShot2D.pitch = pitch;
                oneShot2D.PlayOneShot(clip, volume);
                return;
            }
            var go = new GameObject("Baihu sound · " + clip.name);
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = spatial;
            source.minDistance = 4f;
            source.maxDistance = 90f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        void StartLoop(AudioSource source, BaihuSoundBank.Slot slot, AudioClip fallback)
        {
            AudioClip clip = slot != null ? slot.Pick() : null;
            if (clip == null) clip = fallback;
            source.Stop();
            if (clip == null) return;
            source.clip = clip;
            source.volume = slot != null ? slot.volume : 0.3f;
            source.Play();
        }

        AudioSource Make2D(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        // ---------------- Синтезированные заглушки ----------------

        void BuildPlaceholders()
        {
            fbForest = Noise("PH forest wind", 6f, 0.02f, 0.25f, loop: true);
            fbRustle = Bursts("PH rustle", 1.4f, 9, 0.5f);
            fbEyes = Tone("PH eyes", 0.5f, 880f, 7f, 0.15f, 1.5f);
            fbNoise = Noise("PH aggro noise", 4f, 0.35f, 0.6f, loop: true);
            fbSwing = Tone("PH swing counted", 0.18f, 520f, 18f, 0.25f, 2f);
            fbRun = Bursts("PH withdraw", 1.0f, 7, 0.45f);
            fbGong = Gong("PH gong", 3.2f);
            fbPounce = Sweep("PH pounce", 0.6f, 300f, 1400f, 0.7f);
            fbMiss = Bursts("PH miss", 0.6f, 4, 0.5f);
            fbTigerFall = Thud("PH tiger fall", 0.7f, 48f, 0.8f);
            fbTigerUp = Tone("PH tiger get up", 0.9f, 72f, 2.5f, 0.5f, 1.5f);
            fbTigerLeap = Sweep("PH tiger leap away", 0.7f, 250f, 900f, 0.5f);
            fbImpact = Thud("PH impact", 0.8f, 55f, 1f);
            fbCrack = Bursts("PH crack", 0.5f, 14, 0.6f);
            fbFall = Sweep("PH fall", 1.3f, 120f, 40f, 0.6f);
            fbLand = Thud("PH land", 2.0f, 38f, 1f);
            fbYin = Thud("PH cut yin", 0.6f, 70f, 0.6f);
            fbYang = Tone("PH cut yang", 2.0f, 1320f, 1.6f, 0.4f, 1.5f);
        }
    }
}
