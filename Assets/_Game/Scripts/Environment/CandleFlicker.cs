using UnityEngine;

namespace TanShadow.Environment
{
    // Живое пламя свечи: шум Перлина по яркости света и размеру язычка.
    public class CandleFlicker : MonoBehaviour
    {
        public Light candleLight;
        public Transform flame;
        [Tooltip("Разброс яркости относительно базовой, 0–1")]
        [Range(0, 1)] public float intensityJitter = 0.25f;
        [Tooltip("Скорость мерцания")]
        [Min(0)] public float speed = 3f;

        float baseIntensity;
        Vector3 baseFlameScale;
        float seed;
        float disturbUntil = float.NegativeInfinity;
        float disturbAmount;
        float gutterStart = float.NegativeInfinity, gutterEnd = float.NegativeInfinity;

        // Порыв воздуха: кто-то прошёл рядом — пламя дрожит сильнее.
        public void Disturb(float amount, float duration)
        {
            disturbAmount = Mathf.Max(disturbAmount * (Time.time < disturbUntil ? 1f : 0f), amount);
            disturbUntil = Mathf.Max(disturbUntil, Time.time + duration);
        }

        // Пламя ложится и гаснет на время, потом разгорается снова.
        public void Gutter(float duration)
        {
            gutterStart = Time.time;
            gutterEnd = Time.time + duration;
        }

        void Awake()
        {
            seed = Random.value * 100f;
            if (candleLight != null) baseIntensity = candleLight.intensity;
            if (flame != null) baseFlameScale = flame.localScale;
        }

        void Update()
        {
            bool disturbed = Time.time < disturbUntil;
            float t = Time.time * speed * (disturbed ? 3f : 1f) + seed;
            // два слоя шума: медленное «дыхание» и быстрая дрожь
            float n = Mathf.PerlinNoise(t, 0f) * 0.7f + Mathf.PerlinNoise(t * 3.7f, 1f) * 0.3f;
            float jitter = disturbed ? Mathf.Max(intensityJitter, disturbAmount) : intensityJitter;
            float k = 1f + (n - 0.5f) * 2f * jitter;
            if (Time.time < gutterEnd)
            {
                // Быстро гаснет, последнюю треть разгорается обратно.
                float u = Mathf.InverseLerp(gutterStart, gutterEnd, Time.time);
                k *= u < 0.66f ? Mathf.Lerp(1f, 0.03f, u / 0.15f) : Mathf.InverseLerp(0.66f, 1f, u);
            }

            if (candleLight != null) candleLight.intensity = baseIntensity * k;
            if (flame != null)
                flame.localScale = new Vector3(baseFlameScale.x * (0.9f + 0.1f * k), baseFlameScale.y * k, baseFlameScale.z * (0.9f + 0.1f * k));
        }
    }
}
