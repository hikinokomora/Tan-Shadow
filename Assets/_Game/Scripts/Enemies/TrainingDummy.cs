using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Enemies
{
    // Манекен для отработки дефлекта: стоит, смотрит на игрока и бьёт одной атакой по таймеру.
    // Отлетает от дефлекта, шатается при сломе ци, падает от добивания и встаёт снова.
    [RequireComponent(typeof(Combatant), typeof(AttackExecutor))]
    public class TrainingDummy : MonoBehaviour
    {
        public AttackData attack;
        [Tooltip("Пауза между атаками, с")]
        [Min(0)] public float interval = 1.5f;
        [Tooltip("Атакует, только если игрок ближе, м")]
        [Min(0)] public float attackRange = 2.2f;
        [Tooltip("Дополнительная пауза после того, как его атаку отразили, с")]
        [Min(0)] public float deflectedRecoil = 0.6f;
        [Tooltip("Отлёт назад после дефлекта, м/с")]
        [Min(0)] public float deflectedKnockback = 2.5f;
        [Min(0)] public float turnSpeed = 360f;
        [Tooltip("Через сколько секунд после добивания манекен встаёт, с")]
        [Min(0)] public float reviveDelay = 3f;
        [Tooltip("Визуальная часть, которая шатается и падает")]
        public Transform visualRoot;

        Combatant combatant;
        AttackExecutor executor;
        Posture posture;
        Transform target;
        float timer;
        Vector3 knockback;
        float deadTime;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            posture = GetComponent<Posture>();
            combatant.AttackLanded += OnAttackLanded;
            combatant.Finished += OnFinished;
            if (posture != null) posture.Broken += executor.Cancel;
        }

        void Start()
        {
            var player = FindAnyObjectByType<Player.PlayerController>();
            if (player != null) target = player.transform;
        }

        void Update()
        {
            float dt = combatant.DeltaTime;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 12f * dt);
            transform.position += knockback * dt;

            if (combatant.IsDead)
            {
                if (Time.time - deadTime >= reviveDelay) Revive();
                return;
            }

            UpdateVisualPose();
            if (target == null || (posture != null && posture.IsBroken)) return;

            Vector3 to = target.position - transform.position;
            to.y = 0f;

            // В замахе доворачивается за игроком, после — удар уже не повернуть.
            bool canTurn = !executor.IsBusy || executor.Phase == AttackPhase.Windup;
            if (canTurn && to.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * dt);

            if (executor.IsBusy) return;
            timer += dt;
            if (timer >= interval && to.magnitude <= attackRange)
            {
                timer = 0f;
                executor.Begin(attack);
            }
        }

        void OnAttackLanded(HitInfo hit)
        {
            if (hit.result != HitResult.Deflected) return;
            executor.Cancel();
            timer = -deflectedRecoil;
            knockback = -transform.forward * deflectedKnockback;
        }

        void OnFinished(Combatant by)
        {
            executor.Cancel();
            deadTime = Time.time;
            if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(-80f, 0f, 0f);
        }

        void Revive()
        {
            combatant.Revive();
            if (posture != null) posture.ResetPosture();
            timer = -1f;
            if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
        }

        // Сломленный манекен шатается — видно, что он открыт.
        void UpdateVisualPose()
        {
            if (visualRoot == null) return;
            bool broken = posture != null && posture.IsBroken;
            visualRoot.localRotation = broken
                ? Quaternion.Euler(-12f + Mathf.Sin(Time.time * 7f) * 4f, 0f, Mathf.Sin(Time.time * 5f) * 5f)
                : Quaternion.identity;
        }
    }
}
