using System;
using System.Collections.Generic;
using TanShadow.Combat;
using TanShadow.Environment;
using UnityEngine;

namespace TanShadow.Enemies
{
    public enum Awareness
    {
        Unaware,    // патруль или пост
        Suspicious, // что-то заметил: смотрит, идёт проверить
        Alert       // увидел героиню — бой (дальше ведёт GuardAI)
    }

    // Стелс стражника: зрение, подозрение, тревога и патруль.
    // Без этого компонента стражник сразу идёт в бой (дуэль). Числа — в CombatSettings («Стелс»).
    [RequireComponent(typeof(GuardAI))]
    public class GuardStealth : MonoBehaviour
    {
        public static readonly List<GuardStealth> All = new List<GuardStealth>();

        [Tooltip("Точки патруля по кругу; пусто — стоит на посту и осматривается")]
        public Transform[] patrol;
        [Min(0)] public float walkSpeed = 1.4f;
        [Tooltip("Сколько стоит в каждой точке патруля, с")]
        [Min(0)] public float patrolWait = 1.5f;
        [Tooltip("На посту поворачивает голову туда-сюда на столько, градусы")]
        [Range(0, 180)] public float lookAroundAngle = 50f;
        [Min(0)] public float turnSpeed = 240f;

        public Awareness Awareness { get; private set; }
        public float Suspicion { get; private set; } // 0–1, на 1 — тревога
        public bool IsAlert => Awareness == Awareness.Alert;
        public bool IsDead => combatant.IsDead;
        public bool CanSeePlayer { get; private set; }
        public Vector3 LastSeen => lastSeen;
        public event Action<Awareness> AwarenessChanged;

        GuardAI ai;
        Combatant combatant;
        CharacterController body;
        Transform player;
        Combatant playerCombatant;
        Vector3 postPosition;
        Quaternion postRotation;
        int patrolIndex;
        float waitLeft;
        float stateTime;
        Vector3 lastSeen;
        float lastSeenTime = float.NegativeInfinity;

        CombatSettings Settings => combatant.settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        void Awake()
        {
            ai = GetComponent<GuardAI>();
            combatant = GetComponent<Combatant>();
            body = GetComponent<CharacterController>();
            postPosition = transform.position;
            postRotation = transform.rotation;
            combatant.Hurt += OnHurt;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            var pc = FindAnyObjectByType<Player.PlayerController>();
            if (pc == null) return;
            player = pc.transform;
            playerCombatant = pc.GetComponent<Combatant>();
        }

        // Любой наш удар по нему (кроме смертельного из тени) — тревога.
        void OnHurt(HitInfo hit)
        {
            if (hit.result != HitResult.Evaded) Alert(true);
        }

        // Рестарт после смерти героини: всё забыто, снова на посту.
        public void ResetAwareness()
        {
            Suspicion = 0f;
            SetAwareness(Awareness.Unaware);
            patrolIndex = 0;
            waitLeft = 0f;
        }

        public void Alert(bool share)
        {
            if (player != null)
            {
                lastSeen = player.position;
                lastSeenTime = Time.time;
            }
            Suspicion = 1f;
            if (IsAlert) return;
            SetAwareness(Awareness.Alert);
            if (!share) return;
            foreach (var other in All)
            {
                if (other == this || other.IsDead || other.IsAlert) continue;
                if (Vector3.Distance(other.transform.position, transform.position) <= Settings.alertShareRadius) other.Alert(false);
            }
        }

        void SetAwareness(Awareness next)
        {
            if (Awareness == next) return;
            Awareness = next;
            stateTime = 0f;
            AwarenessChanged?.Invoke(next);
        }

        void Update()
        {
            if (player == null || combatant.IsDead || ai.State == GuardState.Broken) return;
            float dt = combatant.DeltaTime;
            stateTime += dt;

            if (playerCombatant.IsDead)
            {
                CanSeePlayer = false;
                return;
            }

            CanSeePlayer = SeesPlayer(out float distance);
            if (CanSeePlayer)
            {
                lastSeen = player.position;
                lastSeenTime = Time.time;
            }

            if (IsAlert)
            {
                // Потерял из виду надолго — снова подозрение и поиск там, где видел в последний раз.
                if (!CanSeePlayer && Time.time - lastSeenTime > Settings.loseTrackTime)
                {
                    Suspicion = 0.6f;
                    SetAwareness(Awareness.Suspicious);
                }
                return;
            }

            if (CanSeePlayer)
            {
                if (distance <= Settings.instantAlertDistance)
                {
                    Alert(true);
                    return;
                }
                float near = 1f - Mathf.InverseLerp(Settings.instantAlertDistance, Settings.viewDistance, distance);
                Suspicion += Mathf.Lerp(Settings.suspicionRiseFar, Settings.suspicionRiseNear, near) * dt;
                if (Suspicion >= 1f)
                {
                    Alert(true);
                    return;
                }
                SetAwareness(Awareness.Suspicious);
            }
            else
            {
                Suspicion = Mathf.Max(0f, Suspicion - Settings.suspicionDecay * dt);
                if (Suspicion <= 0f) SetAwareness(Awareness.Unaware);
            }

            // Пока не тревога, двигаемся сами; в бою и после тревоги стражником правит GuardAI.
            if (ai.State != GuardState.Idle) return;
            if (Awareness == Awareness.Suspicious) Investigate(dt);
            else Patrol(dt);
        }

        bool SeesPlayer(out float distance)
        {
            Vector3 eye = transform.position + Vector3.up * Settings.eyeHeight;
            Vector3 chest = player.position + Vector3.up * 1.2f;
            Vector3 flat = chest - eye;
            flat.y = 0f;
            distance = flat.magnitude;

            float range = Settings.viewDistance;
            if (StealthZone.TryGetZone(chest, out var zone))
            {
                if (zone == StealthZoneKind.Cover) range = Mathf.Min(range, Settings.coverRevealDistance);
                else range *= Settings.shadowVisibility;
            }
            if (distance > range) return false;
            // В тревоге стражник следит за героиней, даже если она за спиной.
            if (!IsAlert && Vector3.Angle(transform.forward, flat) > Settings.viewAngle * 0.5f) return false;

            return InLineOfSight(eye, chest) || InLineOfSight(eye, player.position + Vector3.up * 1.65f);
        }

        bool InLineOfSight(Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            if (!Physics.Raycast(eye, to.normalized, out var hit, to.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.transform.IsChildOf(player);
        }

        // --- Движение без тревоги ---

        void Patrol(float dt)
        {
            if (patrol == null || patrol.Length == 0)
            {
                if (WalkTo(postPosition, dt)) LookAround(postRotation.eulerAngles.y, dt);
                return;
            }

            var point = patrol[patrolIndex];
            if (point == null || WalkTo(point.position, dt))
            {
                waitLeft += dt;
                LookAround(transform.eulerAngles.y, 0f);
                if (waitLeft < patrolWait) return;
                waitLeft = 0f;
                patrolIndex = (patrolIndex + 1) % patrol.Length;
            }
        }

        void Investigate(float dt)
        {
            Vector3 to = lastSeen - transform.position;
            to.y = 0f;
            if (stateTime < Settings.searchDelay || to.magnitude < 1f)
            {
                Turn(to, dt);
                return;
            }
            WalkTo(lastSeen, dt);
        }

        // Идём к точке; true — уже на месте.
        bool WalkTo(Vector3 point, float dt)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.3f) return true;
            Turn(to, dt);
            body.Move(to.normalized * Mathf.Min(walkSpeed * dt, to.magnitude));
            return false;
        }

        void LookAround(float baseYaw, float dt)
        {
            if (dt <= 0f) return;
            float yaw = baseYaw + Mathf.Sin(Time.time * 0.5f) * lookAroundAngle;
            Turn(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, dt);
        }

        void Turn(Vector3 direction, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * dt);
        }
    }
}
