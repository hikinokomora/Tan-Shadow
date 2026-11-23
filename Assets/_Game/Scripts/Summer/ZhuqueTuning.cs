using UnityEngine;

namespace TanShadow.Summer
{
    // Все числа испытания Чжуцюэ. «✅» — утверждено в GDD лета, остальное — плейсхолдеры (TBD),
    // их подбирают на грейбоксе.
    [CreateAssetMenu(menuName = "Tan Shadow/Summer/Zhuque Tuning", fileName = "ZhuqueTuning")]
    public sealed class ZhuqueTuning : ScriptableObject
    {
        [Header("Правило четырёх")]
        [Tooltip("✅ Сколько отсечений перьев нужно для победы")]
        [Min(1)] public int requiredCuts = 4;
        [Tooltip("✅ После какого удара птица открывает лавовые реки")]
        [Min(1)] public int lavaAfterCuts = 3;

        [Header("Обнаружение: обзор 360°, прячут только крупные валуны")]
        [Tooltip("TBD Сколько секунд героиня может быть на виду вблизи, пока птица не сорвётся. Строго меньше, чем у дракона (4 с). " +
                 "Расчёт по сцене (лучший маршрут от входа к каждому хвосту): с рывками хватает 2,1 с, бегом без рывков — 3,3 с. " +
                 "3,5 оставляет стартовый валун проходимым даже без рывков")]
        [Min(0.1f)] public float timeToDetect = 3.5f;
        [Tooltip("TBD Как быстро спадает шкала, когда героиню не видно, долей в секунду")]
        [Min(0)] public float detectionRecoverPerSecond = 0.8f;
        [Tooltip("TBD До этого расстояния птица замечает героиню с полной скоростью, м")]
        [Min(0)] public float detectFullRange = 15f;
        [Tooltip("TBD Дальше этого птица героиню не замечает, м")]
        [Min(1)] public float viewDistance = 45f;
        [Tooltip("TBD Во сколько раз медленнее растёт шкала на краю обзора (между detectFullRange и viewDistance — плавно)")]
        [Range(0, 1)] public float farDetectionFactor = 0.3f;
        [Tooltip("TBD Тревога перед взлётом: птица замирает и кричит, потом срывается, с")]
        [Min(0)] public float alertTime = 0.35f;

        [Tooltip("TBD На каком расстоянии от входа птица сидит в начале, м. Дальше viewDistance: сначала героиня видит птицу, потом птица — её")]
        [Min(0)] public float startPerchDistance = 60f;

        [Header("Перелёт на другой валун")]
        [Tooltip("TBD Не садится ближе этого к героине, м")]
        [Min(0)] public float minRelocateDistance = 30f;
        [Tooltip("TBD Предпочитает валун примерно на таком расстоянии от героини (не на другом конце кольца), м")]
        [Min(0)] public float preferredRelocateDistance = 45f;
        [Tooltip("TBD Скорость перелёта, м/с")]
        [Min(1)] public float relocateSpeed = 20f;
        [Tooltip("TBD Высота дуги перелёта, м")]
        [Min(0)] public float relocateArcHeight = 9f;

        [Header("Хвост")]
        [Tooltip("TBD Насколько близко к кончику хвоста надо подойти, чтобы схватиться, м (по горизонтали)")]
        [Min(0.2f)] public float grabRadius = 1.8f;
        [Tooltip("TBD Кончик хвоста не выше этого над ногами героини, м")]
        [Min(0)] public float grabMaxHeight = 2.4f;
        [Tooltip("TBD Насколько ниже кончика хвоста висят ноги героини, м")]
        [Min(0)] public float hangDrop = 1.9f;
        [Tooltip("TBD Сколько остаётся от хвоста после отсечения (доля)")]
        [Range(0.05f, 1f)] public float cutStub = 0.3f;

        [Header("Полёт по кольцу: одна и та же траектория, скорость и высота")]
        [Tooltip("TBD Высота полёта над дном долины, м")]
        [Min(2)] public float flightHeight = 10f;
        [Tooltip("TBD Скорость полёта, м/с. Река ≈ 7 м: на 15 м/с тень над ней ≈ 0,5 с (+ допуск lavaShadowMargin)")]
        [Min(1)] public float flightSpeed = 15f;
        [Tooltip("TBD Направление облёта: +1 против часовой (вид сверху), −1 по часовой")]
        public int flightDirection = 1;
        [Tooltip("TBD Взлёт с валуна на кольцо, с")]
        [Min(0.1f)] public float takeOffTime = 1.4f;
        [Tooltip("TBD Сколько можно висеть на хвосте, пока птица не стряхнёт героиню, с. " +
                 "holdLimit × flightSpeed должно быть больше самого длинного промежутка между реками (≈ 140 м), иначе 4-й удар может не успеть")]
        [Min(1)] public float holdLimit = 12f;
        [Tooltip("TBD Встряхивание перед тем, как сбросить героиню, с")]
        [Min(0)] public float shakeOffTime = 0.45f;

        [Header("Отсечение: героиню отбрасывает назад")]
        [Tooltip("TBD На сколько метров назад (против хода полёта) отбрасывает героиню, м")]
        [Min(0)] public float cutThrowDistance = 9f;
        [Tooltip("TBD Высота дуги отбрасывания над линией полёта, м")]
        [Min(0)] public float cutThrowHeight = 2f;
        [Tooltip("TBD Сколько длится отбрасывание до приземления, с")]
        [Min(0.1f)] public float cutThrowTime = 0.8f;

        [Header("Удары 1–3: падение и огненное восстановление")]
        [Tooltip("TBD Падение на землю после отсечения, с")]
        [Min(0.1f)] public float fallTime = 1.1f;
        [Tooltip("TBD Лежит после падения, с")]
        [Min(0)] public float lieTime = 0.6f;
        [Tooltip("TBD Восстановление: встаёт, расправляет крылья, вспыхивает, хвост отрастает, с")]
        [Min(0.1f)] public float regenerateTime = 2.4f;
        [Tooltip("TBD Сколько заливаются лавой русла на третьем восстановлении, с")]
        [Min(0.1f)] public float lavaOpenTime = 2.2f;

        [Header("Удар 4: над лавой")]
        [Tooltip("TBD Допуск: тень считается над лавой, если до реки не дальше этого, м")]
        [Min(0)] public float lavaShadowMargin = 1.5f;
        [Tooltip("TBD Отсвет лавы на птице, пока она над рекой (0..1). Подсказка момента: тень на светящейся лаве " +
                 "и в тени стены кальдеры почти не видна")]
        [Range(0, 1)] public float overLavaGlow = 0.15f;
        [Tooltip("TBD Падение в лаву, с")]
        [Min(0.1f)] public float finalFallTime = 1.2f;
        [Tooltip("TBD Белая янь появляется на столько раньше, чем птица коснётся магмы, с")]
        [Min(0)] public float cutLeadTime = 0.07f;

        [Header("Лава: смерть от касания и перегрева")]
        [Tooltip("TBD Ближе этого к реке героиню печёт, м")]
        [Min(0)] public float heatRadius = 1.35f;
        [Tooltip("TBD За сколько секунд у реки наступает смерть от перегрева, с")]
        [Min(0.5f)] public float heatTime = 8f;
        [Tooltip("TBD За сколько секунд вдали от реки жар спадает полностью, с")]
        [Min(0.1f)] public float heatCoolTime = 2f;
        [Tooltip("Ниже этой высоты героиня считается упавшей за пределы мира, м")]
        public float killHeight = -8f;
    }
}
