using System;
using UnityEngine;

namespace TanShadow.Seasons
{
    // Один звук испытания: несколько вариантов клипа, громкость, разброс тона, 2D/3D.
    // Пустой слот — испытание играет синтезированную заглушку (PlaceholderSynth).
    [Serializable]
    public sealed class SoundSlot
    {
        [Tooltip("Клипы; играется случайный. Пусто — синтезированная заглушка")]
        public AudioClip[] clips;
        [Range(0, 1)] public float volume = 1f;
        [Tooltip("Случайный разброс высоты тона, ±")]
        [Range(0, 0.5f)] public float pitchJitter = 0.05f;
        [Tooltip("0 — звук без положения (2D), 1 — в мире (3D)")]
        [Range(0, 1)] public float spatial = 1f;
        [Tooltip("Где в клипе главный момент (удар, вспышка, всплеск), с от начала. По нему звук выравнивается со временем события")]
        [Min(0)] public float accent;

        public SoundSlot() { }
        public SoundSlot(float volume, float spatial) { this.volume = volume; this.spatial = spatial; }

        public AudioClip Pick()
        {
            if (clips == null || clips.Length == 0) return null;
            return clips[UnityEngine.Random.Range(0, clips.Length)];
        }
    }
}
