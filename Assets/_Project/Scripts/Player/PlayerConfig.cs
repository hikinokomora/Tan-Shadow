using ShadowTan.Core.Data;
using UnityEngine;

namespace ShadowTan.Player
{
    // Все настраиваемые числа героини (§9.4). Значения по умолчанию — плейсхолдеры из ARCHITECTURE.
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "ShadowTan/Player Config")]
    public class PlayerConfig : ScriptableObject
    {
        [Header("Бег")]
        [Tbd("GDD-00 §13")]
        public float runSpeed = 6f;

        [Tbd("ARCHITECTURE §9.4")]
        public float acceleration = 30f;

        [Tbd("ARCHITECTURE §9.4")]
        public float deceleration = 40f;

        [Tbd("ARCHITECTURE §9.4")]
        public float turnSpeed = 720f;

        [Tbd("пологие склоны зимы")]
        public float maxWalkableSlope = 40f;

        [Tooltip("Выше — непроходимо (I-1)")]
        [Tbd("ARCHITECTURE §9.4")]
        public float stepOffset = 0.3f;

        [Tbd("ARCHITECTURE §9.4")]
        public float gravity = 25f;

        [Tooltip("Скорость сползания со слишком крутого склона, м/с")]
        [Tbd("осыпь лета")]
        public float steepSlideSpeed = 4f;

        [Tooltip("Сколько секунд без земли допустимо до перехода в падение")]
        [Tbd("ARCHITECTURE §9.4")]
        public float coyoteTime = 0.1f;

        [Header("Кувырок")]
        [Tooltip("Отсчитывается с момента начала кувырка")]
        [Tbd("GDD-00 §03: КД есть, число не задано")]
        public float rollCooldown = 0.6f;

        [Tbd("ARCHITECTURE §9.4")]
        public float rollDistance = 4f;

        [Tbd("ARCHITECTURE §9.4")]
        public float rollDuration = 0.45f;

        [Tooltip("Профиль скорости 0..1 по времени кувырка; интеграл задаёт пройденный путь")]
        public AnimationCurve rollSpeedCurve = AnimationCurve.Constant(0f, 1f, 1f);

        [Tbd("Q-7")]
        public bool rollGrantsIFrames;

        [Tbd("ARCHITECTURE §9.4")]
        public bool rollSteerable;

        [Header("Меч")]
        [Tbd("ARCHITECTURE §9.4")]
        public float attackDuration = 0.5f;

        [Tooltip("Начало окна попадания, секунды от начала удара")]
        [Tbd("ARCHITECTURE §9.4")]
        public float attackHitWindowStart = 0.15f;

        [Tooltip("Конец окна попадания, секунды от начала удара")]
        [Tbd("ARCHITECTURE §9.4")]
        public float attackHitWindowEnd = 0.3f;

        [Tbd("ARCHITECTURE §9.4")]
        public float attackRange = 1.6f;

        [Tbd("ARCHITECTURE §9.4")]
        public float attackArc = 100f;

        [Tooltip("Время гашения бега при начале удара (I-3)")]
        [Tbd("ARCHITECTURE §9.4")]
        public float attackStopTime = 0.05f;

        [Tooltip("Сколько можно доворачиваться в начале удара")]
        [Tbd("ARCHITECTURE §9.4")]
        public float attackTurnWindow = 0.1f;

        [Tbd("ARCHITECTURE §9.4")]
        public float scareConeAngle = 60f;

        [Tbd("ARCHITECTURE §10.5")]
        public float scareRange = 30f;

        [Tooltip("Разрешать кувырок вместо восстановления после окна удара")]
        [Tbd("ARCHITECTURE §9.5.1")]
        public bool rollCancelsAttackRecovery;

        [Header("Зацеп")]
        [Tbd("ARCHITECTURE §9.4")]
        public float grabSearchRadius = 1.5f;

        [Tbd("ARCHITECTURE §9.4")]
        public float grabSnapTime = 0.15f;

        [Header("Вонзание")]
        [Tbd("ARCHITECTURE §9.4")]
        public float stabDuration = 0.5f;

        [Tbd("ARCHITECTURE §9.4")]
        public float deflectRecoilTime = 0.4f;

        [Header("Падение")]
        [Tbd("GDD-00 §13")]
        public float fallStateMinHeight = 3f;

        [Tbd("GDD-00 §13")]
        public float hardFallHeight = 8f;

        [Tbd("GDD-00 §13")]
        public HardFallConsequence hardFallConsequence = HardFallConsequence.None;

        [Tbd("ARCHITECTURE §9.6.7")]
        public float hardFallStunDuration = 1.5f;

        [Header("Ввод")]
        [Tbd("ARCHITECTURE §9.4")]
        public float inputBufferTime = 0.15f;

        public float RollSpeed(float normalizedTime)
        {
            return rollSpeedCurve.Evaluate(Mathf.Clamp01(normalizedTime)) *
                   (rollDistance / Mathf.Max(rollDuration, 0.01f));
        }
    }
}
