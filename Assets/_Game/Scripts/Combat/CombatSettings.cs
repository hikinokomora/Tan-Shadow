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

        [Header("Смерть игрока")]
        [Tooltip("Сколько секунд героиня лежит после смерти до рестарта дуэли")]
        [Min(0)] public float deathRestartDelay = 2.5f;
        [Tooltip("Сколько секунд враг стоит на месте после рестарта, прежде чем пойти в бой")]
        [Min(0)] public float restartGrace = 1.5f;

        [Header("Цингун (轻功): прыжки")]
        [Tooltip("Высота прыжка с земли, м")]
        [Min(0)] public float jumpHeight = 1.4f;
        [Tooltip("Сколько раз можно оттолкнуться от воздуха")]
        [Min(0)] public int airJumps = 1;
        [Tooltip("Высота прыжка в воздухе, м")]
        [Min(0)] public float airJumpHeight = 1.2f;
        [Tooltip("Как быстро в воздухе меняется направление, м/с²")]
        [Min(0)] public float airAcceleration = 18f;
        [Tooltip("Сколько секунд после схода с края ещё можно прыгнуть с земли")]
        [Min(0)] public float coyoteTime = 0.12f;
        [Tooltip("Ниже этой высоты героиня считается упавшей: смерть и рестарт, м")]
        public float killHeight = -25f;

        [Header("Цингун (轻功): стены")]
        [Tooltip("Скорость бега вдоль стены, м/с")]
        [Min(0)] public float wallRunSpeed = 6.5f;
        [Tooltip("Сколько секунд держится бег вдоль стены")]
        [Min(0)] public float wallRunDuration = 1.1f;
        [Tooltip("Подброс вверх в начале бега вдоль стены, м/с")]
        [Min(0)] public float wallRunLift = 3f;
        [Tooltip("Доля гравитации во время бега вдоль стены")]
        [Range(0, 1)] public float wallRunGravityScale = 0.25f;
        [Tooltip("Скорость бега вверх по стене, м/с")]
        [Min(0)] public float wallClimbSpeed = 5f;
        [Tooltip("Сколько секунд держится бег вверх по стене (один раз за прыжок)")]
        [Min(0)] public float wallClimbDuration = 0.55f;
        [Tooltip("Если стик смотрит в стену под таким углом или прямее — бежим вверх, иначе вдоль, градусы")]
        [Range(0, 90)] public float wallClimbAngle = 35f;
        [Tooltip("Высота прыжка от стены, м")]
        [Min(0)] public float wallJumpHeight = 1.4f;
        [Tooltip("Скорость, с которой прыжок отбрасывает от стены, м/с")]
        [Min(0)] public float wallJumpPush = 6f;
        [Tooltip("Подскок, когда бег вверх выносит за край стены, м")]
        [Min(0)] public float ledgeHopHeight = 0.9f;
        [Tooltip("Скорость вперёд на край после подскока, м/с")]
        [Min(0)] public float ledgeHopForward = 3f;
        [Tooltip("Через сколько секунд можно снова зацепиться за ту же стену")]
        [Min(0)] public float wallReattachDelay = 0.35f;

        [Header("Стелс: зрение стражника")]
        [Tooltip("Дальность зрения, м")]
        [Min(0)] public float viewDistance = 12f;
        [Tooltip("Ширина поля зрения, градусы (вся, не половина)")]
        [Range(0, 360)] public float viewAngle = 110f;
        [Tooltip("Высота глаз стражника, м")]
        [Min(0)] public float eyeHeight = 1.7f;
        [Tooltip("Ближе этого героиню замечают сразу, м")]
        [Min(0)] public float instantAlertDistance = 2.5f;
        [Tooltip("Рост подозрения на краю зрения и вблизи, доля шкалы в секунду")]
        [Min(0)] public float suspicionRiseFar = 0.3f;
        [Min(0)] public float suspicionRiseNear = 1.5f;
        [Tooltip("Спад подозрения, когда героиню не видно, доля в секунду")]
        [Min(0)] public float suspicionDecay = 0.2f;
        [Tooltip("Сколько стражник смотрит в сторону подозрительного, прежде чем пойти проверить, с")]
        [Min(0)] public float searchDelay = 0.6f;
        [Tooltip("Через сколько секунд без героини в поле зрения стражник теряет её и идёт искать")]
        [Min(0)] public float loseTrackTime = 6f;
        [Tooltip("Поднявший тревогу зовёт стражников в этом радиусе, м")]
        [Min(0)] public float alertShareRadius = 10f;

        [Header("Стелс: укрытия и убийство из тени")]
        [Tooltip("Во тени героиню видно на такой доле дальности зрения")]
        [Range(0, 1)] public float shadowVisibility = 0.35f;
        [Tooltip("За занавесом героиню видно только вплотную, м")]
        [Min(0)] public float coverRevealDistance = 1.5f;
        [Tooltip("Убийство сзади: дальность, м")]
        [Min(0)] public float stealthKillRange = 1.8f;
        [Tooltip("Убийство сзади: насколько героиня должна быть за спиной (угол от взгляда стражника), градусы")]
        [Range(0, 180)] public float stealthKillBehindAngle = 110f;
        [Tooltip("Убийство сверху: дальность по горизонтали, м")]
        [Min(0)] public float stealthKillFromAboveRange = 2.2f;
        [Tooltip("Убийство сверху: насколько выше стражника должна быть героиня, м")]
        [Min(0)] public float stealthKillFromAboveHeight = 0.8f;

        [Header("Рывок")]
        [Min(0)] public float dashDistance = 3.5f;
        [Min(0.01f)] public float dashDuration = 0.25f;
        [Tooltip("Неуязвимость с начала рывка, с")]
        [Min(0)] public float dashInvulnerability = 0.2f;
        [Min(0)] public float dashCooldown = 0.3f;

        [Header("Ци игрока: шкала инь (−100) … ян (+100)")]
        [Tooltip("Сдвиг к ян за каждую начатую атаку")]
        [Min(0)] public float qiAttackShift = 12f;
        [Tooltip("Сдвиг к инь при блоке: доля qiDamage заблокированной атаки")]
        [Min(0)] public float qiBlockFactor = 0.8f;
        [Tooltip("Идеальный дефлект возвращает к центру на столько")]
        [Min(0)] public float qiDeflectRestore = 25f;
        [Tooltip("Через сколько секунд без изменений шкала начинает сама идти к центру")]
        [Min(0)] public float qiRecoverDelay = 1.5f;
        [Tooltip("Скорость возврата к центру, единиц в секунду")]
        [Min(0)] public float qiRecoverRate = 15f;
        [Tooltip("Оглушение при выходе за край шкалы, с")]
        [Min(0)] public float qiBreakStun = 1.5f;

        [Header("Ци врага (постура)")]
        [Tooltip("Враг получает такую долю qiDamage своей атаки, когда мы её отражаем")]
        [Min(0)] public float postureDeflectFactor = 1f;
        [Tooltip("Через сколько секунд без урона по ци она начинает восстанавливаться")]
        [Min(0)] public float postureRecoverDelay = 2f;
        [Min(0)] public float postureRecoverRate = 8f;
        [Tooltip("Сколько секунд враг сломлен и открыт для добивания")]
        [Min(0)] public float brokenDuration = 4f;

        [Header("Враг: блок против закликивания")]
        [Tooltip("После скольких наших попаданий подряд враг закрывается блоком")]
        [Min(1)] public int enemyGuardAfterHits = 3;
        [Tooltip("Попадания считаются подряд, если между ними не больше, с")]
        [Min(0)] public float enemyGuardHitGap = 1f;
        [Tooltip("Сколько враг держит блок, прежде чем контратаковать, с")]
        [Min(0)] public float enemyGuardDuration = 0.7f;

        [Header("Хуацзинь (化劲)")]
        [Tooltip("Сколько секунд после дефлекта следующий удар усилен")]
        [Min(0)] public float huajinWindow = 1.5f;
        [Tooltip("Урон по ци × (1 + force отражённой атаки × этот множитель)")]
        [Min(0)] public float huajinForceFactor = 0.5f;
        [Tooltip("Дополнительный хитстоп на усиленном ударе, с")]
        [Min(0)] public float huajinHitstopBonus = 0.05f;
        [Min(0)] public float huajinShake = 0.45f;

        [Header("Комбо")]
        [Tooltip("Сколько секунд после конца удара следующий удар ещё продолжает цепочку")]
        [Min(0)] public float comboChainGrace = 0.3f;

        [Header("Добивание")]
        [Tooltip("С какой дистанции можно добить сломленного врага, м")]
        [Min(0)] public float finisherRange = 2.8f;
        [Tooltip("Дистанция, на которую героиня подходит к врагу для добивания, м")]
        [Min(0)] public float finisherDistance = 1.1f;
        [Min(0.1f)] public float finisherDuration = 1.1f;
        [Tooltip("Доля длительности, в которую проходит удар добивания")]
        [Range(0, 1)] public float finisherImpactAt = 0.4f;
        [Min(0)] public float finisherHitstop = 0.18f;

        [Header("Фидбек: тряска камеры (сила импульса)")]
        [Min(0)] public float finisherShake = 1f;
        [Min(0)] public float deflectShake = 0.5f;
        [Min(0)] public float blockShake = 0.2f;
        [Min(0)] public float hurtShake = 0.6f;
        [Tooltip("Слом ци игрока у края шкалы инь/ян")]
        [Min(0)] public float qiBreakShake = 0.7f;
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
