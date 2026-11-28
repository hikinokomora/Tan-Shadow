using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnVfxDirector : MonoBehaviour
    {
        [SerializeField] AutumnVegetationImpulse vegetation;
        [SerializeField] AutumnEyesGlow eyes;
        [SerializeField] AutumnMonumentFx monument;
        [SerializeField] AutumnLeafFx leaves;
        [SerializeField] AutumnSurfaceFx surface;
        [SerializeField] AutumnDebrisFx debris;
        [SerializeField] ParticleSystem[] systems;
        [SerializeField] Transform previewPoint;
        bool stopped;

        void Awake()
        {
            if (systems == null) return;
            foreach (var particles in systems)
            {
                if (!particles) continue;
                particles.Play(false);
                particles.Emit(particles.main.maxParticles);
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        void OnEnable()
        {
            EventBus.Subscribe<PlayerKilled>(OnKilled);
            EventBus.Subscribe<PlayerSpawned>(OnSpawned);
        }

        void OnDisable()
        {
            EventBus.Unsubscribe<PlayerKilled>(OnKilled);
            EventBus.Unsubscribe<PlayerSpawned>(OnSpawned);
            StopEffects();
        }

        void OnKilled(PlayerKilled _) => StopEffects();
        void OnSpawned(PlayerSpawned _) => ResetEffects();

        public void Rustle(Vector3 p) { if (!stopped && vegetation) vegetation.Rustle(p); }
        public void EyesShown() { if (!stopped && eyes) eyes.Show(); }
        public void Scared() { if (!stopped && eyes) eyes.Fade(); }

        public void Leap(Vector3 p, Vector3 direction)
        {
            if (stopped) return;
            if (vegetation) vegetation.Leap(p, direction);
            if (leaves) leaves.Leap(p, direction);
            if (monument) monument.Takeoff(p, direction);
        }

        public void Landing(Vector3 p) { if (!stopped && monument) monument.Landing(p); }
        public void Takeoff(Vector3 p, Vector3 direction) { if (!stopped && monument) monument.Takeoff(p, direction); }
        public void MonumentHit(Vector3 p) { if (!stopped && monument) monument.Hit(p); }
        public void MonumentLanded(Vector3 p) { if (!stopped && monument) monument.MonumentLanded(p); }

        public void StopEffects()
        {
            stopped = true;
            if (vegetation) vegetation.Stop();
            if (eyes) eyes.Stop();
            if (leaves) leaves.Running = false;
            if (surface) surface.Running = false;
            if (debris) debris.Running = false;
            if (systems == null) return;
            foreach (var particles in systems)
                if (particles) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void ResetEffects()
        {
            StopEffects();
            stopped = false;
            if (leaves) leaves.Running = true;
            if (surface) surface.Running = true;
            if (debris) debris.Running = true;
        }

        [ContextMenu("Preview AU-03 Rustle")]
        void PreviewRustle() { if (Application.isPlaying && previewPoint) Rustle(previewPoint.position); }

        [ContextMenu("Preview AU-04 / AU-08 / AU-17 Leap")]
        void PreviewLeap() { if (Application.isPlaying && previewPoint) Leap(previewPoint.position, previewPoint.forward); }

        [ContextMenu("Preview AU-07 / AU-09 / AU-11 Monument impact")]
        void PreviewImpact() { if (Application.isPlaying && previewPoint) MonumentHit(previewPoint.position); }

        [ContextMenu("Preview AU-13 Monument landing")]
        void PreviewFall() { if (Application.isPlaying && previewPoint) MonumentLanded(previewPoint.position); }
    }

    internal static class AutumnParticles
    {
        public static void Emit(ParticleSystem system, Vector3 point, Vector3 direction, int count,
            float speed, float size, float lifetime, float spread)
        {
            if (!system || !system.gameObject.activeInHierarchy) return;
            count = Mathf.Min(count, system.main.maxParticles - system.particleCount);
            if (count <= 0) return;
            if (!system.isPlaying) system.Play(false);
            for (int i = 0; i < count; i++)
            {
                Vector3 scatter = Random.insideUnitSphere * spread;
                var emission = new ParticleSystem.EmitParams
                {
                    position = point + new Vector3(scatter.x, 0f, scatter.z) * 0.28f,
                    velocity = (direction + scatter) * Random.Range(speed * 0.65f, speed * 1.15f),
                    startSize = Random.Range(size * 0.65f, size * 1.22f),
                    startLifetime = Random.Range(lifetime * 0.69f, lifetime * 1.11f),
                    rotation = Random.Range(0f, 360f)
                };
                system.Emit(emission, 1);
            }
        }
    }
}
