using System;
using System.Collections;
using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    [DefaultExecutionOrder(1000)]
    public sealed class AutumnEntranceMoments : MonoBehaviour
    {
        public enum Cue { Wind, LooseStones, Eyes }

        [Serializable]
        public struct StoneSlide
        {
            public Transform stone;
            public Vector3 start;
            public Vector3 middle;
            public Vector3 end;
            public float delay;
        }

        [SerializeField] Cue cue;
        [SerializeField] CharacterController heroine;
        [SerializeField] AudioSource sound;
        [SerializeField] ParticleSystem particles;
        [SerializeField] Camera playerCamera;
        [SerializeField] Transform eyes;
        [SerializeField] AutumnDebrisFx debris;
        [SerializeField] AutumnEyesGlow eyeGlow;
        [SerializeField] StoneSlide[] stones = Array.Empty<StoneSlide>();

        const float ShakeDuration = 0.24f;
        const float SlideDuration = 1.05f;
        const float EyesDuration = 0.95f;
        float shakeRemaining;
        Quaternion cameraBase;
        Quaternion appliedRotation;
        bool cameraOffsetApplied;
        bool played;

        void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled || played || other != heroine) return;
            if (Vector3.Dot(other.transform.position - transform.position, transform.forward) > 0f) return;

            played = true;
            if (sound != null && sound.clip != null) sound.Play();
            switch (cue)
            {
                case Cue.Wind:
                    EmitParticles(32);
                    shakeRemaining = ShakeDuration;
                    break;
                case Cue.LooseStones:
                    if (debris != null)
                    {
                        debris.Play(other.transform.position);
                        break;
                    }
                    EmitParticles(10);
                    foreach (var stone in stones)
                        if (stone.stone != null) StartCoroutine(Slide(stone));
                    break;
                case Cue.Eyes:
                    if (eyes != null) StartCoroutine(ShowEyes());
                    break;
            }
        }

        void EmitParticles(int count)
        {
            if (particles == null) return;
            particles.Play();
            particles.Emit(count);
        }

        IEnumerator Slide(StoneSlide slide)
        {
            if (slide.delay > 0f) yield return new WaitForSeconds(slide.delay);
            Transform stone = slide.stone;
            Quaternion rest = stone.localRotation;
            Vector3 axis = Vector3.Cross(Vector3.up, slide.end - slide.start).normalized;
            stone.localPosition = slide.start;
            stone.gameObject.SetActive(true);
            for (float elapsed = 0f; elapsed < SlideDuration; elapsed += Time.deltaTime)
            {
                float t = elapsed / SlideDuration;
                float progress = Mathf.SmoothStep(0f, 1f, t);
                stone.localPosition = progress < 0.55f
                    ? Vector3.Lerp(slide.start, slide.middle, progress / 0.55f)
                    : Vector3.Lerp(slide.middle, slide.end, (progress - 0.55f) / 0.45f);
                stone.localRotation = Quaternion.AngleAxis(460f * progress, axis) * rest;
                yield return null;
            }
            stone.localPosition = slide.end;
        }

        IEnumerator ShowEyes()
        {
            Vector3 fullSize = eyes.localScale;
            eyes.gameObject.SetActive(true);
            if (eyeGlow != null) eyeGlow.Show();
            for (float elapsed = 0f; elapsed < EyesDuration; elapsed += Time.deltaTime)
            {
                Vector3 size = fullSize;
                size.y *= Mathf.Min(Mathf.Clamp01(elapsed / 0.10f), Mathf.Clamp01((EyesDuration - elapsed) / 0.18f));
                eyes.localScale = size;
                yield return null;
            }
            eyes.gameObject.SetActive(false);
            eyes.localScale = fullSize;
        }

        void LateUpdate()
        {
            RestoreCamera();
            if (shakeRemaining <= 0f || playerCamera == null || !playerCamera.isActiveAndEnabled) return;

            Transform view = playerCamera.transform;
            cameraBase = view.localRotation;
            float strength = shakeRemaining / ShakeDuration;
            float phase = (ShakeDuration - shakeRemaining) * 49f;
            appliedRotation = cameraBase * Quaternion.Euler(
                Mathf.Sin(phase) * strength * 0.32f, 0f, Mathf.Sin(phase * 0.74f) * strength * 0.42f);
            view.localRotation = appliedRotation;
            cameraOffsetApplied = true;
            shakeRemaining -= Time.deltaTime;
        }

        void OnDisable()
        {
            RestoreCamera();
            StopAllCoroutines();
            if (eyes != null) eyes.gameObject.SetActive(false);
            foreach (var slide in stones)
                if (slide.stone != null) slide.stone.gameObject.SetActive(false);
        }

        void RestoreCamera()
        {
            if (!cameraOffsetApplied) return;
            cameraOffsetApplied = false;
            if (playerCamera != null && Quaternion.Angle(playerCamera.transform.localRotation, appliedRotation) < 0.01f)
                playerCamera.transform.localRotation = cameraBase;
        }
    }
}
