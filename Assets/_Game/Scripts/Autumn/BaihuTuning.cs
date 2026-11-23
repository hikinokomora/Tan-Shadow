using UnityEngine;

namespace TanShadow.Autumn
{
    // Все числа испытания Байху. Утверждённые GDD значения помечены «✅», остальное — плейсхолдеры (TBD),
    // их подбирают на грейбоксе.
    [CreateAssetMenu(menuName = "Tan Shadow/Autumn/Baihu Tuning", fileName = "BaihuTuning")]
    public sealed class BaihuTuning : ScriptableObject
    {
        [Header("Правило четырёх")]
        [Tooltip("✅ Сколько ударов тигра о столб нужно для победы")]
        [Min(1)] public int requiredHits = 4;

        [Header("Цикл охоты")]
        [Tooltip("✅ Пауза до нового появления после отпугивания или промаха, с")]
        public Vector2 reappearDelay = new Vector2(3f, 5f);
        [Tooltip("TBD Первое появление после активации тропы, с")]
        [Min(0)] public float firstAppearDelay = 1.2f;
        [Tooltip("TBD Сколько шевелится трава до появления глаз, с")]
        [Min(0)] public float rustleDuration = 1.6f;
        [Tooltip("TBD За сколько секунд глаз агрессия тигра дорастает до прыжка (шкала 0→1), с")]
        [Min(0.1f)] public float aggroFillTime = 3.2f;
        [Tooltip("TBD Сколько длится удар гонга перед прыжком: от гонга до броска, с")]
        [Min(0)] public float gongToPounce = 0.55f;

        [Header("Отпугивание мечом")]
        [Tooltip("TBD Сколько направленных взмахов нужно, чтобы отпугнуть тигра на тропе")]
        [Min(1)] public int scareSwingsRequired = 3;
        [Tooltip("TBD Конус взмаха: взмах засчитывается, если направление на глаза в пределах этого угла, °")]
        [Range(10, 180)] public float scareConeAngle = 80f;
        [Tooltip("TBD Дальность, с которой взмах пугает тигра, м")]
        [Min(1)] public float scareRange = 26f;
        [Tooltip("TBD На арене взмах не прогоняет тигра, а только сбивает агрессию на эту долю")]
        [Range(0, 1)] public float arenaSwingAggroDrop = 0.35f;
        [Tooltip("TBD Как быстро гаснет агрессия после отпугивания, в долях в секунду")]
        [Min(0)] public float aggroDecayPerSecond = 2f;

        [Header("Преследование на тропе: убежать нельзя, спасает только отпугивание")]
        [Tooltip("TBD Если героиня ушла дальше этого за глаза (по ходу тропы), тигр переползает в траве вперёд, не теряя агрессии, м")]
        [Min(0)] public float trailLeaveBehind = 3f;
        [Tooltip("TBD Если героиня дальше этого от глаз, тигр тоже переползает к ней, м")]
        [Min(1)] public float trailKeepRange = 14f;
        [Tooltip("TBD Сколько длится переползание (короткое шевеление травы), с")]
        [Min(0)] public float trailRelocateTime = 0.4f;
        [Tooltip("TBD Тигр прыгает не дальше этого: если героиня далеко, он сначала оказывается в траве рядом с ней, м")]
        [Min(2)] public float pounceMaxDistance = 10f;

        [Header("Отпугнут (тропа): морда уходит обратно в бамбук")]
        [Tooltip("TBD За сколько морда скрывается в зарослях, с")]
        [Min(0.1f)] public float withdrawDuration = 0.9f;
        [Tooltip("TBD Насколько морда пятится вглубь бамбука, м")]
        [Min(0)] public float withdrawBack = 2.2f;
        [Tooltip("TBD Насколько морда опускается, уходя, м")]
        [Min(0)] public float withdrawSink = 0.5f;

        [Header("Точки появления")]
        [Tooltip("TBD Тропа: на сколько метров впереди героини появляются глаза (мин–макс)")]
        public Vector2 trailAhead = new Vector2(5f, 12f);
        [Tooltip("TBD Тропа: расстояние морды от оси тропы, м. Кромка травы ≈ 2,65 м: морда высовывается из стеблей у края тропы")]
        [Min(0)] public float trailEyesSideOffset = 3.3f;
        [Tooltip("TBD Арена: на сколько метров за кромкой арены прячется тигр (мин–макс). Разная глубина = разные радиусы")]
        public Vector2 arenaEyesDepth = new Vector2(0.5f, 4.5f);
        [Tooltip("TBD Арена: тигр появляется с той же стороны столба, что и героиня, с разбросом ± столько градусов")]
        [Range(0, 90)] public float arenaSameSideSpread = 30f;
        [Tooltip("TBD Арена: если героиня обошла столб дальше этого угла от глаз, тигр переползает на её сторону, °")]
        [Range(10, 180)] public float arenaSideTolerance = 70f;
        [Tooltip("TBD Арена: сколько длится переползание на сторону героини (шевеление травы), с")]
        [Min(0)] public float arenaRelocateTime = 0.6f;
        [Tooltip("TBD Высота глаз над землёй, м")]
        [Min(0)] public float eyesHeight = 1.15f;
        [Tooltip("TBD Расстояние между глазами, м")]
        [Min(0)] public float eyesSpacing = 0.22f;

        [Header("Прыжок на героиню: дуга, как прыжок обратно в лес")]
        [Tooltip("TBD Длительность прыжка с любого расстояния, с")]
        [Min(0.1f)] public float pounceDuration = 0.6f;
        [Tooltip("TBD Высота дуги прыжка, м")]
        [Min(0)] public float pounceLeapHeight = 2.5f;
        [Tooltip("TBD За столько до момента, когда тигр долетит до героини, он перестаёт доворачивать на неё. " +
                 "Рывок раньше бесполезен (тигр довернёт), позже — уже поздно. " +
                 "Окно уворота ≈ это − cutLeadTime − время, за которое рывок уносит на killCutDistance (≈ 0,11 с), с")]
        [Min(0)] public float pounceCommitTime = 0.45f;
        [Tooltip("TBD Как быстро тигр доворачивает на героиню, м/с. Должно быть быстрее рывка (14 м/с)")]
        [Min(1)] public float pounceTrackSpeed = 45f;
        [Tooltip("На тропе тигр доворачивает до самого конца: тропа узкая, уйти с траектории нельзя, спасает только отпугивание (GDD-AU)")]
        public bool trailPounceAlwaysHoming = true;
        [Tooltip("TBD Насколько тигр пролетает дальше точки, где была героиня. Увернулась перед столбом — тигр врежется в него, м")]
        [Min(0)] public float pounceOvershoot = 9f;
        [Tooltip("TBD Если тигр пролетит ближе этого к героине — смерть (чёрная инь до контакта), м")]
        [Min(0.1f)] public float killCutDistance = 1.5f;
        [Tooltip("TBD Экран инь/янь появляется на столько раньше контакта (смерти героини или падения столба на тигра), с. 0,07 ≈ 4 кадра")]
        [Min(0)] public float cutLeadTime = 0.07f;
        [Tooltip("TBD Допуск попадания в столб: неточное попадание тоже засчитывается, м")]
        [Min(0)] public float monumentHitTolerance = 0.9f;

        [Header("После удара о столб: падает → встаёт → бродит → прыгает в лес")]
        [Tooltip("TBD Насколько тигра отбрасывает от столба, м")]
        [Min(0)] public float knockback = 2.5f;
        [Tooltip("TBD Сколько длится падение на бок, с")]
        [Min(0.05f)] public float knockFallTime = 0.18f;
        [Tooltip("TBD Сколько тигр лежит, с")]
        [Min(0)] public float knockLieTime = 0.6f;
        [Tooltip("TBD Сколько тигр поднимается, с")]
        [Min(0.05f)] public float knockGetUpTime = 0.45f;
        [Tooltip("TBD Сколько тигр бродит, приходя в себя, с")]
        [Min(0)] public float wanderTime = 1.3f;
        [Tooltip("TBD Сколько метров проходит, пока бродит, м")]
        [Min(0)] public float wanderDistance = 2.5f;
        [Tooltip("TBD Длительность прыжка обратно в лес (после столба или промаха), с")]
        [Min(0.1f)] public float retreatDuration = 0.55f;
        [Tooltip("TBD Высота прыжка обратно в лес, м")]
        [Min(0)] public float retreatLeapHeight = 2.5f;

        [Header("Финал")]
        [Tooltip("TBD Сколько тигр лежит оглушённый до падения столба (после падения на бок), с")]
        [Min(0)] public float stunBeforeFall = 0.6f;
        [Tooltip("TBD Длительность падения столба, с")]
        [Min(0.1f)] public float monumentFallDuration = 1.3f;

        // Угроза растёт без шкалы на экране (GDD-00 §05): шум и дрожь камеры.
        [Header("Ощущение угрозы · звук")]
        [Tooltip("Громкость серого шума / жужжания от агрессии 0..1")]
        public AnimationCurve noiseByAggro = AnimationCurve.EaseInOut(0, 0, 1, 0.9f);

        [Header("Ощущение угрозы · камера")]
        [Tooltip("Мелкая дрожь камеры от агрессии, °")]
        public AnimationCurve tremorByAggro = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 0.12f), new Keyframe(1, 0.5f));
        [Tooltip("Медленное раскачивание от агрессии, °")]
        public AnimationCurve swayByAggro = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0.55f));
        [Tooltip("Толчок камеры при гонге, °")]
        [Min(0)] public float gongKick = 1.1f;
    }
}
