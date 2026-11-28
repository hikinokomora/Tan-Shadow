using System;
using UnityEngine;
using UnityEngine.Audio;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AutumnWindController))]
    public sealed class AutumnWindAudio : MonoBehaviour
    {
        public enum FoliageSound { Bamboo, Grass, Leaves, DistantGust }

        [Serializable]
        struct Emitter
        {
            public Transform point;
            public FoliageSound foliage;
        }

        [SerializeField] AudioListener listener;
        [SerializeField] AudioMixerGroup output;
        [SerializeField] AudioSource ambient;
        [SerializeField] AudioClip[] bambooClips = Array.Empty<AudioClip>();
        [SerializeField] AudioClip[] grassClips = Array.Empty<AudioClip>();
        [SerializeField] AudioClip[] leafClips = Array.Empty<AudioClip>();
        [SerializeField] AudioClip[] gustClips = Array.Empty<AudioClip>();
        [SerializeField] Emitter[] emitters = Array.Empty<Emitter>();
        [SerializeField, Range(0f, 1f)] float ambientVolume = 0.35f;
        [SerializeField, Range(0f, 1f)] float oneShotVolume = 0.83f;
        [SerializeField, Min(0.1f)] float minDistance = 6.5f;
        [SerializeField, Min(0.1f)] float maxDistance = 23f;
        [SerializeField, Min(4f)] float gustInterval = 4f;
        [SerializeField, Min(4f)] float lullInterval = 6.25f;

        readonly AudioSource[] voices = new AudioSource[6];
        readonly System.Random random = new();
        AutumnWindController wind;
        float timeSinceShot;
        float intervalProgress;
        float intervalVariation;
        float nextProbeTime;
        int lastEmitter = -1;

        bool suppressed;

        void Awake()
        {
            wind = GetComponent<AutumnWindController>();
            for (int i = 0; i < voices.Length; i++) voices[i] = CreateVoice(i);
        }

        void OnEnable()
        {
            if (!suppressed) Resume();
        }

        void Resume()
        {
            lastEmitter = -1;
            nextProbeTime = 0f;
            ResetInterval();
            if (!ambient || !ambient.clip) return;

            ambient.volume = 0f;
            ambient.loop = true;
            ambient.outputAudioMixerGroup = output;
            ambient.Play();
        }

        void OnDisable() => StopAudio();

        void StopAudio()
        {
            if (ambient) ambient.Stop();
            foreach (var voice in voices)
                if (voice) voice.Stop();
        }

        void LateUpdate()
        {
            if (suppressed) return;
            float energy = Mathf.Clamp01(wind.CurrentStrength * 0.72f + wind.Gust * 0.28f);
            if (ambient && ambient.clip)
                ambient.volume = Mathf.MoveTowards(ambient.volume, ambientVolume * Mathf.Lerp(0.35f, 1f, energy),
                    Time.deltaTime * 0.35f);

            float interval = Mathf.Clamp(Mathf.Lerp(lullInterval, gustInterval, energy) + intervalVariation,
                Mathf.Min(gustInterval, lullInterval), Mathf.Max(gustInterval, lullInterval));
            timeSinceShot += Time.deltaTime;
            intervalProgress = Mathf.Clamp01(intervalProgress + Time.deltaTime / interval);
            if (timeSinceShot < 4f || intervalProgress < 1f || Time.time < nextProbeTime) return;

            nextProbeTime = Time.time + 0.35f;
            if (listener && listener.isActiveAndEnabled && PlayFoliage(listener.transform.position, energy))
                ResetInterval();
        }

        void ResetInterval()
        {
            timeSinceShot = 0f;
            intervalProgress = 0f;
            intervalVariation = RandomRange(-0.65f, 0.65f);
        }

        bool PlayFoliage(Vector3 listenerPosition, float energy)
        {
            float range = Mathf.Max(minDistance, maxDistance);
            float totalWeight = 0f;
            int selected = -1;

            for (int i = 0; i < emitters.Length; i++)
            {
                var emitter = emitters[i];
                if (!emitter.point || ClipsFor(emitter.foliage).Length == 0) continue;

                float distance = Vector3.Distance(emitter.point.position, listenerPosition);
                float weight = Mathf.Clamp01(1f - distance / range);
                weight *= weight;
                if (emitter.foliage == FoliageSound.DistantGust) weight *= wind.Gust;
                if (i == lastEmitter) weight *= 0.3f;
                if (weight <= 0f) continue;

                totalWeight += weight;
                if ((float)random.NextDouble() * totalWeight <= weight) selected = i;
            }

            if (selected < 0) return false;
            var chosen = emitters[selected];
            AudioClip[] clips = ClipsFor(chosen.foliage);
            AudioClip clip = clips[random.Next(clips.Length)];
            if (!clip) return false;

            if (!PlaySpatial(clip, chosen.point.position, oneShotVolume * Mathf.Lerp(.62f, 1f, energy),
                RandomRange(.93f, 1.07f), minDistance, range, 160)) return false;
            lastEmitter = selected;
            return true;
        }

        public bool PlaySpatial(AudioClip clip, Vector3 position, float volume, float pitch,
            float near, float far, int priority, AudioMixerGroup mixer = null)
        {
            if (suppressed || !isActiveAndEnabled || !clip) return false;
            AudioSource selected = null;
            foreach (var voice in voices)
            {
                if (!voice) continue;
                if (!voice.isPlaying) { selected = voice; break; }
                if (voice.priority > priority && (!selected || voice.priority > selected.priority)) selected = voice;
            }
            if (!selected) return false;
            selected.Stop();
            selected.transform.position = position;
            selected.pitch = pitch;
            selected.volume = volume;
            selected.minDistance = near;
            selected.maxDistance = Mathf.Max(near, far);
            selected.priority = priority;
            selected.outputAudioMixerGroup = mixer ? mixer : output;
            selected.clip = clip;
            selected.Play();
            return true;
        }

        public void Suppress(bool value)
        {
            if (suppressed == value) return;
            suppressed = value;
            if (value) StopAudio();
            else if (isActiveAndEnabled) Resume();
        }

        float RandomRange(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        AudioClip[] ClipsFor(FoliageSound foliage) => foliage switch
        {
            FoliageSound.Bamboo => bambooClips,
            FoliageSound.Grass => grassClips,
            FoliageSound.Leaves => leafClips,
            FoliageSound.DistantGust => gustClips,
            _ => Array.Empty<AudioClip>()
        };

        AudioSource CreateVoice(int index)
        {
            var voice = new GameObject($"Wind one-shot {index + 1}").AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false;
            voice.outputAudioMixerGroup = output;
            voice.spatialBlend = 1f;
            voice.spatialize = !string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName());
            voice.dopplerLevel = 0f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            voice.minDistance = minDistance;
            voice.maxDistance = Mathf.Max(minDistance, maxDistance);
            voice.priority = 160;
            return voice;
        }
    }
}
