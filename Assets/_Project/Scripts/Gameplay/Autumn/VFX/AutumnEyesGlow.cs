using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnEyesGlow : MonoBehaviour
    {
        [SerializeField] Renderer[] eyes;
        [SerializeField, ColorUsage(false, true)] Color emission = new(4f, 0.10f, 0.025f);

        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
        MaterialPropertyBlock block;
        float fade;
        bool fading;

        void Awake() => block = new MaterialPropertyBlock();

        public void Show()
        {
            fading = false;
            SetIntensity(1f);
        }

        public void Fade()
        {
            fade = 1f;
            fading = true;
        }

        public void Stop()
        {
            fading = false;
            SetIntensity(0f);
        }

        void Update()
        {
            if (!fading) return;
            fade = Mathf.Max(0f, fade - Time.deltaTime / .24f);
            SetIntensity(fade);
            fading = fade > 0f;
        }

        void SetIntensity(float value)
        {
            if (block == null || eyes == null) return;
            foreach (var eye in eyes)
            {
                if (!eye) continue;
                eye.GetPropertyBlock(block);
                block.SetColor(EmissiveColor, emission * value);
                eye.SetPropertyBlock(block);
            }
        }
    }
}
