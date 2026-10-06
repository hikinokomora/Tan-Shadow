using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Audio
{
    // Звуки боя: звон дефлекта, блок, попадание, свист клинка. Один на сцену.
    public class CombatAudio : MonoBehaviour
    {
        public AudioClip[] deflect;
        public AudioClip[] block;
        public AudioClip[] hit;
        public AudioClip[] whoosh;

        [Range(0, 1)] public float deflectVolume = 1f;
        [Range(0, 1)] public float blockVolume = 0.8f;
        [Range(0, 1)] public float hitVolume = 0.9f;
        [Range(0, 1)] public float whooshVolume = 0.35f;
        [Tooltip("Случайный разброс высоты тона, чтобы повторы не звучали одинаково")]
        [Range(0, 0.3f)] public float pitchJitter = 0.06f;
        [Tooltip("0 — звук в голове, 1 — полностью в мире")]
        [Range(0, 1)] public float spatialBlend = 0.5f;
        [Min(1)] public int voices = 6;

        AudioSource[] sources;
        int next;

        void Awake()
        {
            sources = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = spatialBlend;
                source.minDistance = 3f;
                sources[i] = source;
            }
        }

        void OnEnable()
        {
            Combatant.AnyHit += OnHit;
            Combatant.AnyFinisher += OnFinisher;
            AttackExecutor.AnyPhaseChanged += OnPhase;
        }

        void OnDisable()
        {
            Combatant.AnyHit -= OnHit;
            Combatant.AnyFinisher -= OnFinisher;
            AttackExecutor.AnyPhaseChanged -= OnPhase;
        }

        // Пока нет отдельного звука: удар плюс звон.
        void OnFinisher(Combatant attacker, Combatant victim)
        {
            Vector3 at = victim.transform.position + Vector3.up * 1.3f;
            Play(hit, hitVolume, at);
            Play(deflect, deflectVolume * 0.7f, at);
        }

        void OnHit(HitInfo info)
        {
            Vector3 at = info.defender.transform.position + Vector3.up * 1.3f;
            switch (info.result)
            {
                case HitResult.Deflected: Play(deflect, deflectVolume, at); break;
                case HitResult.Blocked: Play(block, blockVolume, at); break;
                case HitResult.Hit: Play(hit, hitVolume, at); break;
            }
        }

        void OnPhase(AttackExecutor executor, AttackPhase phase)
        {
            if (phase == AttackPhase.Active)
                Play(whoosh, whooshVolume, executor.transform.position + Vector3.up * 1.2f);
        }

        void Play(AudioClip[] clips, float volume, Vector3 at)
        {
            if (clips == null || clips.Length == 0) return;
            var source = sources[next];
            next = (next + 1) % sources.Length;

            source.transform.position = at;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.clip = clips[Random.Range(0, clips.Length)];
            source.volume = volume;
            source.Play();
        }
    }
}
