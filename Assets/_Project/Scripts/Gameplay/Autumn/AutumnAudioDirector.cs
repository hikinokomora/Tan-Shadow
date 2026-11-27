using ShadowTan.Combat;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using ShadowTan.Player;
using UnityEngine;
using UnityEngine.Audio;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnAudioDirector : MonoBehaviour
    {
        [SerializeField] AutumnAudioBank bank;
        [SerializeField] AutumnWindAudio windAudio;
        [SerializeField] AutumnSurfaceFx ground;
        [SerializeField] AudioMixerGroup surfaceMix;
        [SerializeField] Vector2 trailRange = new(10f, 161f);

        FootstepEmitter steps;
        float nextCuckoo, nextStep;
        bool stopped;

        void OnEnable()
        {
            steps = ground ? ground.Footsteps : null;
            if (steps) steps.FootstepDetected += OnStep;
            EventBus.Subscribe<PlayerRolled>(OnRoll);
            EventBus.Subscribe<PlayerKilled>(OnKilled);
            EventBus.Subscribe<PlayerSpawned>(OnSpawned);
            ScheduleCuckoo();
        }

        void OnDisable()
        {
            if (steps) steps.FootstepDetected -= OnStep;
            EventBus.Unsubscribe<PlayerRolled>(OnRoll);
            EventBus.Unsubscribe<PlayerKilled>(OnKilled);
            EventBus.Unsubscribe<PlayerSpawned>(OnSpawned);
        }

        void OnStep(SurfaceDefinition tagged)
        {
            if (stopped || !bank || !windAudio || !steps || Time.time < nextStep) return;
            var hit = steps.LastContact;
            var clip = bank.Find(ground.Resolve(tagged, hit), false);
            if (!clip) return;
            nextStep = Time.time + .17f;
            windAudio.PlaySpatial(clip, hit.point, .45f, Random.Range(.97f, 1.06f), 1.6f, 13f, 85, surfaceMix);
        }

        void OnRoll(PlayerRolled _)
        {
            if (stopped || !bank || !windAudio || !steps || !steps.TryGetContact(out var hit)) return;
            var tag = hit.collider.GetComponentInParent<SurfaceTag>();
            var clip = bank.Find(ground.Resolve(tag ? tag.Surface : null, hit), true);
            if (clip) windAudio.PlaySpatial(clip, hit.point, .52f, 1f, 2.2f, 15f, 75, surfaceMix);
        }

        void OnKilled(PlayerKilled _)
        {
            stopped = true;
            if (windAudio) windAudio.Suppress(true);
        }

        void OnSpawned(PlayerSpawned _)
        {
            stopped = false;
            if (windAudio) windAudio.Suppress(false);
            ScheduleCuckoo();
        }

        void Update()
        {
            if (stopped || !bank || !bank.ForestOneShot || !windAudio || !steps || Time.time < nextCuckoo) return;
            var position = steps.transform.position;
            if (position.z < trailRange.x || position.z > trailRange.y || Mathf.Abs(position.x - 70f) > 6f) return;
            float side = Random.value > .5f ? 1f : -1f;
            if (windAudio.PlaySpatial(bank.ForestOneShot, position + new Vector3(side * 18f, 8f, 14f),
                .32f, Random.Range(.95f, 1.04f), 6f, 58f, 165))
                ScheduleCuckoo();
            else
                nextCuckoo = Time.time + 2f;
        }

        void ScheduleCuckoo() => nextCuckoo = Time.time + Random.Range(30f, 90f);
    }
}
