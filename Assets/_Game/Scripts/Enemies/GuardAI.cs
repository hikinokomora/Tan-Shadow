using System;
using TanShadow.Combat;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TanShadow.Enemies
{
    public enum SpearMotion
    {
        Overhead,
        SweepRight,
        SweepLeft,
        Thrust
    }

    [Serializable]
    public struct GuardStep
    {
        public AttackData attack;
        public SpearMotion motion;
        [Tooltip("Пауза перед этим ударом, с — задаёт ритм серии")]
        [Min(0)] public float delayBefore;
        [Tooltip("Шаг вперёд во время активной фазы, м")]
        [Min(0)] public float lunge;
    }

    [Serializable]
    public class GuardPattern
    {
        public string name;
        [Min(0)] public float weight = 1f;
        [Tooltip("Выбирается, когда игрок не ближе, м")]
        [Min(0)] public float minRange;
        [Tooltip("Выбирается, когда игрок не дальше, м (дальше — стражник сначала подойдёт)")]
        [Min(0)] public float maxRange = 4f;
        public GuardStep[] steps;
    }

    public enum GuardState
    {
        Idle,
        Approach,
        Circle,
        Attack,
        Recoil,
        Broken,
        Dead
    }

    // Стражник с копьём: держит дистанцию, кружит, выбирает серию ударов, реагирует на дефлект и слом ци.
    [RequireComponent(typeof(CharacterController), typeof(Combatant), typeof(AttackExecutor))]
    public class GuardAI : MonoBehaviour
    {
        public GuardPattern[] patterns;

        [Header("Движение")]
        [Min(0)] public float aggroRange = 14f;
        [Tooltip("Дистанция, на которой стражник кружит вокруг игрока, м")]
        [Min(0)] public float preferredDistance = 3.2f;
        [Min(0)] public float moveSpeed = 3f;
        [Min(0)] public float circleSpeed = 1.5f;
        [Min(0)] public float turnSpeed = 420f;
        public float gravity = -20f;

        [Header("Ритм")]
        [Min(0)] public float cooldownMin = 0.7f;
        [Min(0)] public float cooldownMax = 1.8f;
        [Tooltip("Сколько стражник стоит открытым после отражённого последнего удара серии, с")]
        [Min(0)] public float deflectedRecoil = 0.8f;
        [Min(0)] public float deflectedKnockback = 3f;
        [Tooltip("Вздрагивание от попадания вне атаки, с")]
        [Min(0)] public float flinchTime = 0.35f;
        [Min(0)] public float reviveDelay = 4f;

        [Tooltip("Визуальная часть, которая шатается и падает")]
        public Transform visualRoot;

        public GuardState State { get; private set; }
        public SpearMotion CurrentMotion { get; private set; }
        public GuardPattern CurrentPattern { get; private set; }

        CharacterController body;
        Combatant combatant;
        AttackExecutor executor;
        Posture posture;
        Transform target;

        float stateTime;
        float cooldown;
        float circleDirection = 1f;
        float nextCircleSwitch;
        int step;
        float stepDelay;
        Vector3 knockback;
        float verticalSpeed;
        float recoilTime;
        float deadTime;
        Vector3 spawnPosition;
        Quaternion spawnRotation;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            posture = GetComponent<Posture>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            combatant.Hurt += OnHurt;
            combatant.AttackLanded += OnAttackLanded;
            combatant.Finished += OnFinished;
            if (posture != null)
            {
                posture.Broken += OnBroken;
                posture.Recovered += () => SetState(GuardState.Circle);
            }
        }

        void Start()
        {
            var player = FindAnyObjectByType<Player.PlayerController>();
            if (player != null) target = player.transform;
        }

        void Update()
        {
            float dt = combatant.DeltaTime;
            stateTime += dt;

            if (State == GuardState.Dead)
            {
                if (Time.time - deadTime >= reviveDelay) Revive();
                return;
            }

            switch (State)
            {
                case GuardState.Idle: UpdateIdle(); break;
                case GuardState.Approach: UpdateApproach(dt); break;
                case GuardState.Circle: UpdateCircle(dt); break;
                case GuardState.Attack: UpdateAttack(dt); break;
                case GuardState.Recoil:
                    if (stateTime >= recoilTime) SetState(GuardState.Circle);
                    break;
            }

            UpdateVisualPose();
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 12f * dt);
            verticalSpeed = body.isGrounded ? -2f : verticalSpeed + gravity * dt;
            body.Move((knockback + Vector3.up * verticalSpeed) * dt);
        }

        // --- Состояния ---

        void UpdateIdle()
        {
            if (target != null && Distance() <= aggroRange) SetState(GuardState.Approach);
        }

        void UpdateApproach(float dt)
        {
            Face(dt);
            if (Distance() <= preferredDistance + 0.2f)
            {
                cooldown = Random.Range(cooldownMin, cooldownMax);
                SetState(GuardState.Circle);
                return;
            }
            MoveFlat(ToTarget().normalized * moveSpeed * dt);
        }

        void UpdateCircle(float dt)
        {
            Face(dt);
            if (Time.time >= nextCircleSwitch)
            {
                circleDirection = Random.value < 0.5f ? -1f : 1f;
                nextCircleSwitch = Time.time + Random.Range(1.2f, 2.8f);
            }

            Vector3 to = ToTarget();
            Vector3 radial = to.normalized * Mathf.Clamp(Distance() - preferredDistance, -1f, 1f) * moveSpeed;
            Vector3 lateral = Vector3.Cross(Vector3.up, to.normalized) * circleDirection * circleSpeed;
            MoveFlat((radial + lateral) * dt);

            cooldown -= dt;
            if (cooldown <= 0f) StartPattern(ChoosePattern());
        }

        void UpdateAttack(float dt)
        {
            if (executor.IsBusy)
            {
                if (executor.Phase == AttackPhase.Windup)
                {
                    Face(dt);
                }
                else if (executor.Phase == AttackPhase.Active && executor.Current.active > 0f)
                {
                    float lunge = CurrentPattern.steps[step - 1].lunge;
                    MoveFlat(transform.forward * lunge / executor.Current.active * dt);
                }
                return;
            }

            if (step >= CurrentPattern.steps.Length)
            {
                cooldown = Random.Range(cooldownMin, cooldownMax);
                SetState(GuardState.Circle);
                return;
            }

            // Перед первым ударом серии подходим на его дальность (не дольше 2,5 с).
            var next = CurrentPattern.steps[step];
            float range = next.attack.range > 0f ? next.attack.range : 2f;
            Face(dt);
            if (step == 0 && Distance() > range * 0.85f)
            {
                if (stateTime > 2.5f)
                {
                    SetState(GuardState.Circle);
                    return;
                }
                MoveFlat(ToTarget().normalized * moveSpeed * dt);
                return;
            }

            stepDelay -= dt;
            if (stepDelay > 0f) return;

            CurrentMotion = next.motion;
            executor.Begin(next.attack);
            step++;
            stepDelay = step < CurrentPattern.steps.Length ? CurrentPattern.steps[step].delayBefore : 0f;
        }

        GuardPattern ChoosePattern()
        {
            float distance = Distance();
            float total = 0f;
            foreach (var p in patterns)
                if (distance >= p.minRange && distance <= p.maxRange + 1.5f) total += p.weight;
            if (total <= 0f) return patterns[Random.Range(0, patterns.Length)];

            float roll = Random.value * total;
            foreach (var p in patterns)
            {
                if (distance < p.minRange || distance > p.maxRange + 1.5f) continue;
                roll -= p.weight;
                if (roll <= 0f) return p;
            }
            return patterns[patterns.Length - 1];
        }

        void StartPattern(GuardPattern pattern)
        {
            CurrentPattern = pattern;
            step = 0;
            stepDelay = pattern.steps[0].delayBefore;
            SetState(GuardState.Attack);
        }

        void SetState(GuardState next)
        {
            State = next;
            stateTime = 0f;
        }

        // --- Реакции ---

        void OnAttackLanded(HitInfo hit)
        {
            if (hit.result != HitResult.Deflected) return;
            // Серию дефлект не прерывает: отдача только после последнего удара.
            bool lastStep = State == GuardState.Attack && step >= CurrentPattern.steps.Length;
            if (!lastStep) return;
            executor.Cancel();
            knockback = -transform.forward * deflectedKnockback;
            Recoil(deflectedRecoil);
        }

        void OnHurt(HitInfo hit)
        {
            if (hit.result != HitResult.Hit || State == GuardState.Broken || State == GuardState.Dead) return;
            // Во время удара — суперброня, иначе вздрагивает и теряет серию.
            if (executor.IsBusy) return;
            Vector3 away = transform.position - hit.attacker.transform.position;
            away.y = 0f;
            knockback = away.normalized * 1.5f;
            Recoil(flinchTime);
        }

        void Recoil(float duration)
        {
            recoilTime = duration;
            SetState(GuardState.Recoil);
        }

        void OnBroken()
        {
            executor.Cancel();
            SetState(GuardState.Broken);
        }

        void OnFinished(Combatant by)
        {
            executor.Cancel();
            deadTime = Time.time;
            SetState(GuardState.Dead);
            if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(-80f, 0f, 0f);
        }

        void Revive()
        {
            body.enabled = false;
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            body.enabled = true;
            combatant.Revive();
            if (posture != null) posture.ResetPosture();
            if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
            SetState(GuardState.Idle);
        }

        void UpdateVisualPose()
        {
            if (visualRoot == null) return;
            visualRoot.localRotation = State == GuardState.Broken
                ? Quaternion.Euler(-12f + Mathf.Sin(Time.time * 7f) * 4f, 0f, Mathf.Sin(Time.time * 5f) * 5f)
                : Quaternion.identity;
        }

        // --- Утилиты ---

        Vector3 ToTarget()
        {
            if (target == null) return transform.forward;
            Vector3 to = target.position - transform.position;
            to.y = 0f;
            return to;
        }

        float Distance() => ToTarget().magnitude;

        void Face(float dt)
        {
            Vector3 to = ToTarget();
            if (to.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * dt);
        }

        void MoveFlat(Vector3 delta)
        {
            delta.y = 0f;
            body.Move(delta);
        }
    }
}
