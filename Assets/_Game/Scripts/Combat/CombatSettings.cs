using UnityEngine;

namespace TanShadow.Combat
{
    // Общие числа боя. Тюнятся в ассете, не в коде.
    [CreateAssetMenu(fileName = "CombatSettings", menuName = "Tan Shadow/Combat Settings")]
    public class CombatSettings : ScriptableObject
    {
        [Header("Движение")]
        [Min(0)] public float moveSpeed = 4.5f;
        [Tooltip("Скорость шага в блоке")]
        [Min(0)] public float blockMoveSpeed = 1.8f;
        [Tooltip("Поворот, градусов в секунду")]
        [Min(0)] public float turnSpeed = 720f;
        public float gravity = -20f;

        [Header("Ввод")]
        [Tooltip("Сколько секунд нажатие ждёт, пока действие станет доступно")]
        [Min(0)] public float inputBuffer = 0.15f;
        [Tooltip("Радиус мягкого доворота атаки на ближайшего врага, м")]
        [Min(0)] public float softLockRange = 3.5f;

        [Header("Блок и дефлект")]
        [Tooltip("Окно идеального дефлекта от нажатия блока, с")]
        [Min(0)] public float deflectWindow = 0.2f;
        [Tooltip("Хитстоп на дефлекте, с")]
        [Min(0)] public float deflectHitstop = 0.1f;
        [Tooltip("Короткая пауза после дефлекта, из которой можно сразу атаковать, с")]
        [Min(0)] public float deflectRecovery = 0.15f;
        [Tooltip("Нажатие блока быстрее этого интервала после прошлого считается спамом, с")]
        [Min(0)] public float deflectSpamInterval = 0.5f;
        [Tooltip("Множитель окна дефлекта за каждое спам-нажатие подряд. Удачный дефлект сбрасывает штраф")]
        [Range(0, 1)] public float deflectSpamFalloff = 0.5f;
        [Tooltip("Окно дефлекта не становится меньше этого, с")]
        [Min(0)] public float minDeflectWindow = 0.04f;

        [Header("Попадания")]
        [Tooltip("Оглушение игрока после пропущенного удара, с")]
        [Min(0)] public float staggerDuration = 0.45f;
        [Tooltip("Начальная скорость отбрасывания при попадании, м/с")]
        [Min(0)] public float hitKnockback = 4f;
        [Tooltip("Начальная скорость отбрасывания при блоке, м/с")]
        [Min(0)] public float blockKnockback = 2f;
        [Tooltip("Торможение отбрасывания, м/с²")]
        [Min(0)] public float knockbackDeceleration = 14f;

        [Header("Рывок")]
        [Min(0)] public float dashDistance = 3.5f;
        [Min(0.01f)] public float dashDuration = 0.25f;
        [Tooltip("Неуязвимость с начала рывка, с")]
        [Min(0)] public float dashInvulnerability = 0.2f;
        [Min(0)] public float dashCooldown = 0.3f;

        [Header("Фидбек: тряска камеры (сила импульса)")]
        [Min(0)] public float deflectShake = 0.5f;
        [Min(0)] public float blockShake = 0.2f;
        [Min(0)] public float hurtShake = 0.6f;
        [Tooltip("Когда наш удар попал по врагу")]
        [Min(0)] public float landShake = 0.15f;

        [Header("Фидбек: вибрация геймпада")]
        public Rumble deflectRumble = new Rumble { low = 0.25f, high = 0.9f, duration = 0.1f };
        public Rumble blockRumble = new Rumble { low = 0.3f, high = 0.3f, duration = 0.08f };
        public Rumble hurtRumble = new Rumble { low = 0.8f, high = 0.4f, duration = 0.2f };
    }

    [System.Serializable]
    public struct Rumble
    {
        [Tooltip("Низкочастотный мотор, 0–1")]
        [Range(0, 1)] public float low;
        [Tooltip("Высокочастотный мотор, 0–1")]
        [Range(0, 1)] public float high;
        [Tooltip("Длительность, с")]
        [Min(0)] public float duration;
    }
}
