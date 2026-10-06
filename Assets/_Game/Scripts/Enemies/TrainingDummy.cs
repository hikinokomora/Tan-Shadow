using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Enemies
{
    // Манекен для отработки дефлекта: стоит, смотрит на игрока и бьёт одной атакой по таймеру.
    [RequireComponent(typeof(Combatant), typeof(AttackExecutor))]
    public class TrainingDummy : MonoBehaviour
    {
        public AttackData attack;
        [Tooltip("Пауза между атаками, с")]
        [Min(0)] public float interval = 1.5f;
        [Tooltip("Атакует, только если игрок ближе, м")]
        [Min(0)] public float attackRange = 3f;
        [Tooltip("Дополнительная пауза после того, как его атаку отразили, с")]
        [Min(0)] public float deflectedRecoil = 0.6f;
        [Min(0)] public float turnSpeed = 360f;

        Combatant combatant;
        AttackExecutor executor;
        Transform target;
        float timer;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            combatant.AttackLanded += OnAttackLanded;
            combatant.Hurt += OnHurt;
        }

        void Start()
        {
            var player = FindAnyObjectByType<Player.PlayerController>();
            if (player != null) target = player.transform;
        }

        void Update()
        {
            if (target == null) return;
            float dt = combatant.DeltaTime;

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
        }

        void OnHurt(HitInfo hit)
        {
            if (combatant.Health <= 0f) combatant.ResetHealth();
        }
    }
}
