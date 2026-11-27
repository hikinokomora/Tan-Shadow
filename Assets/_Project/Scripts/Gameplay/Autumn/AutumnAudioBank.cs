using System;
using ShadowTan.Combat;
using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    public sealed class AutumnAudioBank : ScriptableObject
    {
        [Serializable]
        public struct SurfaceClips
        {
            public SurfaceDefinition surface;
            public AudioClip footstep;
            public AudioClip roll;
        }

        [SerializeField] AudioClip bambooAmbience;
        [SerializeField] AudioClip forestOneShot;
        [SerializeField] SurfaceClips[] surfaceClips;

        public AudioClip BambooAmbience => bambooAmbience;
        public AudioClip ForestOneShot => forestOneShot;

        public AudioClip Find(SurfaceDefinition surface, bool roll)
        {
            if (!surface || surfaceClips == null) return null;
            foreach (var entry in surfaceClips)
                if (entry.surface == surface) return roll ? entry.roll : entry.footstep;
            return null;
        }
    }
}
