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

        void Awake()
        {
            seed = Random.value * 100f;
            if (candleLight != null) baseIntensity = candleLight.intensity;
            if (flame != null) baseFlameScale = flame.localScale;
        }

        void Update()
        {
            float t = Time.time * speed + seed;
            // два слоя шума: медленное «дыхание» и быстрая дрожь
            float n = Mathf.PerlinNoise(t, 0f) * 0.7f + Mathf.PerlinNoise(t * 3.7f, 1f) * 0.3f;
            float k = 1f + (n - 0.5f) * 2f * intensityJitter;

            if (candleLight != null) candleLight.intensity = baseIntensity * k;
            if (flame != null)
                flame.localScale = new Vector3(baseFlameScale.x * (0.9f + 0.1f * k), baseFlameScale.y * k, baseFlameScale.z * (0.9f + 0.1f * k));
        }
    }
}
