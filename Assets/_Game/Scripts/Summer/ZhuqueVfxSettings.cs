using UnityEngine;

namespace TanShadow.Summer
{
    // Числа летних эффектов (VFX-инструкция v3, раздел 4: SU-03…SU-10). Все — [Tbd]: стартовые предложения
    // инструкции, подбираются на сцене. Лавовые эмиттеры (SU-08, SU-09) строит SummerVfxBuilder по этим числам —
    // после правки пересобрать (Tools/Tan Shadow/Summer/Build Summer VFX).
    [CreateAssetMenu(menuName = "Tan Shadow/Summer/Zhuque VFX Settings", fileName = "ZhuqueVfxSettings")]
    public sealed class ZhuqueVfxSettings : ScriptableObject
    {
        [Header("SU-03 Скоростной ветер [Tbd]")]
        [Tooltip("Появление и исчезновение полос, с (инструкция: 0.15–0.3)")]
        [Min(0.01f)] public float windFadeIn = 0.2f, windFadeOut = 0.25f;
        [Tooltip("Наибольшая непрозрачность полос")]
        [Range(0, 1)] public float windAlpha = 0.5f;
        [Tooltip("Сколько полос одновременно")]
        [Min(1)] public int windStreaks = 18;
        [Tooltip("Скорость полос к краю экрана, доли полуэкрана в секунду")]
        public Vector2 windSpeed = new Vector2(1.6f, 2.8f);
        [Tooltip("Полосы живут в кольце от этой доли полуэкрана до края: центр кадра чистый")]
        [Range(0, 1)] public float windInner = 0.55f;
        [Tooltip("Длина и толщина полосы, пикселей при высоте экрана 1080")]
        public Vector2 windStreakSize = new Vector2(170f, 3f);

        [Header("SU-04 Отсечение перьев [Tbd]")]
        [Tooltip("Перьев на засчитанный удар (инструкция: 10–20)")]
        public Vector2Int featherCount = new Vector2Int(12, 18);
        [Tooltip("Скорость в стороны от удара, м/с")]
        public Vector2 featherSpeed = new Vector2(2.5f, 6f);
        [Tooltip("Какая доля скорости птицы передаётся перьям")]
        [Range(0, 1)] public float featherInherit = 0.35f;
        [Tooltip("Время жизни, с (инструкция: 0.4–0.9)")]
        public Vector2 featherLifetime = new Vector2(0.5f, 0.9f);
        [Tooltip("Размер пера, м")]
        public Vector2 featherSize = new Vector2(0.35f, 0.65f);

        [Header("SU-06 Огненная вспышка [Tbd]")]
        [Tooltip("Частиц на вспышку (инструкция: 12–24)")]
        public Vector2Int igniteCount = new Vector2Int(16, 24);
        [Tooltip("Радиус области корпуса и крыльев, откуда вылетает огонь, м")]
        [Min(0)] public float igniteRadius = 2.2f;
        [Tooltip("Радиальная скорость, м/с")]
        public Vector2 igniteSpeed = new Vector2(3f, 8f);
        public Vector2 igniteLifetime = new Vector2(0.3f, 0.6f);
        public Vector2 igniteSize = new Vector2(0.9f, 1.8f);

        [Header("SU-07 Пыль при падении птицы [Tbd]")]
        [Tooltip("Частиц (инструкция: 15–25)")]
        public Vector2Int fallDustCount = new Vector2Int(16, 24);
        [Tooltip("Скорость вдоль поверхности, м/с")]
        public Vector2 fallDustSpeed = new Vector2(2.5f, 6.5f);
        public Vector2 fallDustLifetime = new Vector2(0.8f, 1.4f);
        public Vector2 fallDustSize = new Vector2(1.6f, 3.2f);
        [Tooltip("Радиус пятна, откуда поднимается пыль, м")]
        [Min(0)] public float fallDustRadius = 2f;

        [Header("SU-08 Пузырение лавы [Tbd] — строит SummerVfxBuilder")]
        [Tooltip("Пузырей в секунду на поток")]
        [Min(0)] public float bubbleRate = 6f;
        [Tooltip("Одновременно пузырей на поток, не больше")]
        [Min(1)] public int bubbleMax = 12;
        public Vector2 bubbleLifetime = new Vector2(0.9f, 1.6f);
        public Vector2 bubbleSize = new Vector2(0.5f, 1.4f);

        [Header("SU-09 Искры лавы [Tbd] — строит SummerVfxBuilder")]
        [Tooltip("Искр в секунду на поток (инструкция: 2–5 на точку)")]
        [Min(0)] public float sparkRate = 8f;
        [Tooltip("Живых искр на поток, не больше (всего по сцене — около 60)")]
        [Min(1)] public int sparkMax = 12;
        public Vector2 sparkLifetime = new Vector2(0.8f, 1.6f);
        public Vector2 sparkSpeed = new Vector2(0.8f, 2.2f);
        public Vector2 sparkSize = new Vector2(0.06f, 0.12f);

        [Header("SU-10 Тень птицы на лаве [Tbd]")]
        [Tooltip("Размах силуэта тени, м (модель птицы — около 11 м)")]
        [Min(0.5f)] public float shadowSpan = 11f;
        [Tooltip("Насколько тень гасит свечение лавы (0 — никак, 1 — чёрная)")]
        [Range(0, 1)] public float shadowDarkness = 0.93f;
        [Tooltip("Скорость появления и исчезновения тени, 1/с")]
        [Min(0.1f)] public float shadowFade = 16f;
        [Tooltip("Насколько тень приподнята над лавой, м")]
        [Min(0)] public float shadowLift = 0.3f;
    }
}
