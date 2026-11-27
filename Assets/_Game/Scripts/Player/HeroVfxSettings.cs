using UnityEngine;

namespace TanShadow.Player
{
    // Числа общих эффектов героини (VFX-инструкция v3, раздел 2 и SU-01/SU-02). Все — [Tbd]: стартовые
    // предложения инструкции, подбираются на сцене. Цвет мягкого грунта — у сцены (HeroVfx.groundColor).
    [CreateAssetMenu(menuName = "Tan Shadow/VFX/Hero VFX Settings", fileName = "HeroVfxSettings")]
    public sealed class HeroVfxSettings : ScriptableObject
    {
        [Header("G-01 След от меча [Tbd]")]
        [Tooltip("Время жизни шлейфа, с (инструкция: 0.10–0.18)")]
        [Min(0.01f)] public float trailTime = 0.14f;
        [Tooltip("Ширина шлейфа у лезвия, м")]
        [Min(0.01f)] public float trailWidth = 0.16f;
        [Tooltip("Новая точка шлейфа не ближе этого, м")]
        [Min(0.001f)] public float trailMinVertexDistance = 0.04f;
        [Tooltip("Яркость шлейфа, нит")]
        [Min(0)] public float trailNits = 900f;

        [Header("G-02 Отскок меча [Tbd]")]
        [Tooltip("Время жизни вспышки, с (инструкция: 0.08–0.15)")]
        [Min(0.01f)] public float reboundTime = 0.12f;
        [Tooltip("Размер вспышки, м")]
        [Min(0.01f)] public float reboundSize = 0.7f;

        [Header("G-03 / SU-02 Пыль при кувырке [Tbd]")]
        [Tooltip("Частиц на кувырок (инструкция: 8–16)")]
        public Vector2Int rollCount = new Vector2Int(8, 16);
        [Tooltip("Скорость назад по кувырку, м/с")]
        public Vector2 rollSpeed = new Vector2(1.5f, 3.5f);
        [Tooltip("Скорость вверх, м/с")]
        public Vector2 rollLift = new Vector2(0.4f, 1.2f);
        [Tooltip("Время жизни, с (инструкция: 0.3–0.7)")]
        public Vector2 rollLifetime = new Vector2(0.3f, 0.7f);
        [Tooltip("Размер облачка, м")]
        public Vector2 rollSize = new Vector2(0.35f, 0.8f);
        [Tooltip("Земля дальше этого под ногами — кувырок в воздухе, пыли нет, м")]
        [Min(0.05f)] public float groundProbe = 0.5f;

        [Header("SU-01 Ноги в песке: вмятины [Tbd]")]
        [Tooltip("Сколько вмятин видно одновременно (кольцевой пул)")]
        [Min(2)] public int footprintCount = 16;
        [Tooltip("Размер вмятины: ширина и длина, м")]
        public Vector2 footprintSize = new Vector2(0.17f, 0.3f);
        [Tooltip("Насколько темнеет грунт в центре вмятины (0 — никак, 1 — чёрный)")]
        [Range(0, 1)] public float footprintDarkness = 0.25f;
        [Tooltip("Вмятина держится и потом гаснет, с")]
        public float footprintHold = 2.5f, footprintFade = 1.5f;
        [Tooltip("Стопа ниже этого над корнем героини — касание, выше второго числа — отрыв, м")]
        public float footContactHeight = 0.1f, footReleaseHeight = 0.17f;
    }
}
