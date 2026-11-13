using UnityEngine;

namespace TanShadow.Combat
{
    // Нефритовое свечение клинка, пока заряжен хуацзинь. Вешается на игрока рядом с Huajin.
    public class HuajinBladeGlow : MonoBehaviour
    {
        [Tooltip("Меши клинка. Если пусто — все рендереры под объектом DaggerSocket")]
        public Renderer[] blade;
        [Tooltip("Необязательный точечный свет у клинка; держим выключенным")]
        public Light glowLight;

        [ColorUsage(false, true)] public Color color = new Color(0.35f, 1f, 0.65f);
        [Tooltip("Яркость свечения (HDRP Lit, _EmissiveColor)")]
        [Min(0)] public float intensity = 200f;
        [Min(0)] public float lightIntensity = 2000f;
        [Tooltip("Во сколько раз ярче вспышка в момент дефлекта и удара")]
        [Min(1)] public float flareMultiplier = 3f;
        [Min(0.01f)] public float flareTime = 0.2f;
        [Tooltip("За сколько секунд до конца окна свечение гаснет")]
        [Min(0.01f)] public float fadeOutTime = 0.4f;
        [Min(0)] public float pulseSpeed = 14f;

        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");

        Huajin huajin;
        MaterialPropertyBlock block;
        float flareLeft;
        bool lit;

        void Awake()
        {
            huajin = GetComponentInParent<Huajin>();
            block = new MaterialPropertyBlock();
            if (blade == null || blade.Length == 0) blade = FindBlade();
            if (glowLight != null) glowLight.enabled = false;

            if (huajin == null) return;
            huajin.Charged += Flare;
            huajin.Released += _ => Flare();
        }

        Renderer[] FindBlade()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "DaggerSocket") return t.GetComponentsInChildren<Renderer>(true);
            return new Renderer[0];
        }

        void Flare() => flareLeft = flareTime;

        void LateUpdate()
        {
            if (huajin == null) return;
            flareLeft -= Time.deltaTime;

            // Удар забирает заряд, но вспышка удара должна догореть.
            float flare = Mathf.Clamp01(flareLeft / flareTime);
            float level = 0f;
            if (huajin.IsCharged)
            {
                float fade = Mathf.Clamp01(huajin.TimeLeft / fadeOutTime);
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * pulseSpeed);
                level = fade * pulse;
            }
            level = Mathf.Max(level, flare * flareMultiplier);

            if (level <= 0f && !lit) return;
            Apply(level);
            lit = level > 0f;
        }

        void Apply(float level)
        {
            Color emission = color * (intensity * level);
            foreach (var r in blade)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(EmissiveColorId, emission);
                r.SetPropertyBlock(block);
            }

            if (glowLight == null) return;
            glowLight.enabled = level > 0f;
            glowLight.color = color;
            glowLight.intensity = lightIntensity * level;
        }
    }
}
