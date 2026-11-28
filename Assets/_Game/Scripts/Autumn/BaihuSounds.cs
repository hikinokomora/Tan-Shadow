using TanShadow.Seasons;
using ShadowTan.Gameplay.Autumn;
using UnityEngine;
using UnityEngine.Audio;

namespace TanShadow.Autumn
{
    [DefaultExecutionOrder(170)]
    public sealed class BaihuSounds : MonoBehaviour
    {
        public BaihuEncounter encounter;
        public YinYangCover cover;
        public BaihuSoundBank bank;
        [SerializeField] AudioMixerGroup ambienceMix;
        [SerializeField] AudioMixerGroup effectsMix;
        [SerializeField] AutumnWindAudio sceneAudio;

        readonly AudioSource[] voices = new AudioSource[6];
        AudioSource ambience, noise;
        float nextCuckoo;
        bool defeated;

        void Awake()
        {
            noise = MakeSource("Aggro growl", effectsMix, true);
            if (sceneAudio) return;
            ambience = MakeSource("Forest ambience", ambienceMix, true);
            for (int i = 0; i < voices.Length; i++)
                voices[i] = MakeSource($"Baihu voice {i + 1}", effectsMix, false);
        }

        void OnEnable()
        {
            if (encounter)
            {
                encounter.Rustled += OnRustle;
                encounter.Scared += OnScared;
                encounter.Pounced += OnPounce;
                encounter.TigerGotUp += OnGetUp;
                encounter.TigerLeptAway += OnLeapAway;
                encounter.MonumentHit += OnMonumentHit;
                encounter.HeroKilled += OnHeroKilled;
                encounter.Defeated += OnDefeated;
                encounter.EncounterReset += OnReset;
            }
            if (cover)
            {
                cover.CoverShown += OnCovered;
                cover.CoverHidden += OnRevealed;
            }
            Resume();
        }

        void OnDisable()
        {
            if (encounter)
            {
                encounter.Rustled -= OnRustle;
                encounter.Scared -= OnScared;
                encounter.Pounced -= OnPounce;
                encounter.TigerGotUp -= OnGetUp;
                encounter.TigerLeptAway -= OnLeapAway;
                encounter.MonumentHit -= OnMonumentHit;
                encounter.HeroKilled -= OnHeroKilled;
                encounter.Defeated -= OnDefeated;
                encounter.EncounterReset -= OnReset;
            }
            if (cover)
            {
                cover.CoverShown -= OnCovered;
                cover.CoverHidden -= OnRevealed;
            }
            StopAudio();
        }

        bool Covered => cover && cover.IsCovering;

        void Update()
        {
            if (!bank || !encounter || defeated || Covered) return;
            if (noise.isPlaying && encounter.tuning)
            {
                var slot = bank.aggroNoiseLoop;
                float target = encounter.tuning.noiseByAggro.Evaluate(encounter.Aggro) * slot.volume;
                noise.volume = Mathf.MoveTowards(noise.volume, target, Time.unscaledDeltaTime * 1.85f);
            }
            if (sceneAudio || Time.time < nextCuckoo || encounter.Zone != BaihuZone.Trail || !encounter.hero) return;
            var p = encounter.hero.Body.position;
            float side = Random.value < .5f ? -1f : 1f;
            if (Play(bank.forestOneShot, p + new Vector3(side * 23f, 8f, 15f), 175, 1f, 7f, 64f))
                ScheduleCuckoo();
            else
                nextCuckoo = Time.time + 2f;
        }

        void OnRustle(Vector3 point)
            => Play(encounter.RustleIsRelocation ? bank?.fleetingRustle : bank?.rustle, point, 100);

        void OnScared(Vector3 point) => Play(bank?.fleetingRustle, point, 125, .8f);
        void OnPounce(Vector3 point) => Play(bank?.pounce, point, 80);
        void OnGetUp(Vector3 point) => Play(bank?.tigerGetUp, point, 110);
        void OnLeapAway(Vector3 point) => Play(bank?.tigerLeapAway, point, 100);

        void OnMonumentHit(int hits, int required, Vector3 point)
        {
            Play(bank?.monumentImpact, point, 70);
            float boost = Mathf.Lerp(.55f, 1f, (hits - 1f) / Mathf.Max(required - 1f, 1f));
            Play(bank?.monumentCrack, point, 95, boost);
            Play(bank?.monumentDebris, point, 120, boost);
        }

        void OnHeroKilled()
        {
            StopAudio();
            // The existing Yin cover takes over immediately. Its timing stays owned by the encounter.
            if (!cover && encounter.tigerBody) Play(bank?.tigerRoar, encounter.tigerBody.position, 65);
        }

        void OnCovered(YinYangCover.Kind _)
        {
            if (sceneAudio) sceneAudio.Suppress(true);
            StopAudio();
        }

        void OnRevealed(YinYangCover.Kind _)
        {
            if (sceneAudio) sceneAudio.Suppress(defeated);
            Resume();
        }

        void OnDefeated()
        {
            defeated = true;
            if (sceneAudio) sceneAudio.Suppress(true);
            StopAudio();
        }
        void OnReset() { defeated = false; if (!Covered) Resume(); }

        void Resume()
        {
            if (!bank || defeated || Covered) return;
            if (ambience) StartLoop(ambience, bank.forestAmbience, bank.forestAmbience.volume);
            StartLoop(noise, bank.aggroNoiseLoop, 0f);
            ScheduleCuckoo();
        }

        void ScheduleCuckoo() => nextCuckoo = Time.time + Random.Range(30f, 90f);

        void StopAudio()
        {
            if (ambience) ambience.Stop();
            if (noise) noise.Stop();
            foreach (var voice in voices)
                if (voice) voice.Stop();
        }

        bool Play(BaihuSoundBank.Slot slot, Vector3 position, int priority,
            float scale = 1f, float near = 4f, float far = 64f)
        {
            if (Covered || defeated || slot == null) return false;
            var clip = slot.Pick();
            if (!clip) return false;
            float pitch = Random.Range(1f - slot.pitchJitter, 1f + slot.pitchJitter);
            if (sceneAudio)
                return sceneAudio.PlaySpatial(clip, position, slot.volume * scale, pitch, near, far, priority, effectsMix);
            AudioSource available = null;
            foreach (var voice in voices)
            {
                if (!voice.isPlaying) { available = voice; break; }
                if (voice.priority > priority && (!available || voice.priority > available.priority)) available = voice;
            }
            if (!available) return false;
            available.Stop();
            available.transform.position = position;
            available.spatialBlend = slot.spatial;
            available.priority = priority;
            available.minDistance = near;
            available.maxDistance = far;
            available.pitch = pitch;
            available.volume = slot.volume * scale;
            available.clip = clip;
            available.Play();
            return true;
        }

        static void StartLoop(AudioSource source, BaihuSoundBank.Slot slot, float volume)
        {
            var clip = slot?.Pick();
            if (!clip || source.isPlaying) return;
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        AudioSource MakeSource(string label, AudioMixerGroup mixer, bool loop)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = loop ? 0f : 1f;
            source.dopplerLevel = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.outputAudioMixerGroup = mixer;
            return source;
        }
    }
}

