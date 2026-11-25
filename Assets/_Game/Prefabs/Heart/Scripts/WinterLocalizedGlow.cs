using UnityEngine;

namespace TanShadow.Winter
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Tan Shadow/Winter/Localized Glow")]
    public sealed class WinterLocalizedGlow : MonoBehaviour
    {
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int TranslucencyId = Shader.PropertyToID("_Translucency");

        [SerializeField] private Renderer glowRenderer;
        [SerializeField, Range(0f, 8f)] private float glowIntensity = 1.8f;
        [SerializeField, Range(0f, 1f)] private float translucency = 0.3f;
        [SerializeField, Range(0f, 1f)] private float pulseAmount;
        [SerializeField, Min(0.01f)] private float pulsesPerSecond = 1.1f;

        private MaterialPropertyBlock properties;
        private bool needsUpdate;

        public float GlowIntensity
        {
            get => glowIntensity;
            set
            {
                glowIntensity = Mathf.Clamp(value, 0f, 8f);
                Apply();
            }
        }

        public float Translucency
        {
            get => translucency;
            set
            {
                translucency = Mathf.Clamp01(value);
                Apply();
            }
        }

        public float PulseAmount
        {
            get => pulseAmount;
            set
            {
                pulseAmount = Mathf.Clamp01(value);
                Apply();
            }
        }

        private void OnEnable() => Apply();
        private void OnValidate() => needsUpdate = true;

        private void Update()
        {
            if (needsUpdate || (pulseAmount > 0f && Application.IsPlaying(gameObject)))
                Apply();
        }

        private void Apply()
        {
            needsUpdate = false;
            if (glowRenderer == null)
                return;

            float intensity = glowIntensity;
            if (pulseAmount > 0f && Application.IsPlaying(gameObject))
            {
                float phase = Mathf.Repeat(Time.time * pulsesPerSecond, 1f);
                float firstBeat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(phase - 0.16f) / 0.14f);
                float secondBeat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(phase - 0.38f) / 0.12f);
                intensity *= 1f - pulseAmount * (1f - Mathf.Max(firstBeat, 0.55f * secondBeat));
            }

            properties ??= new MaterialPropertyBlock();
            glowRenderer.GetPropertyBlock(properties);
            properties.SetFloat(GlowId, intensity);
            properties.SetFloat(TranslucencyId, translucency);
            glowRenderer.SetPropertyBlock(properties);
        }
    }
}
