using ShadowTan.Core.Contracts;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    // Владеет машиной состояний, конфигом и связывает компоненты героини. Тело двигает PlayerMotor (§9.2).
    [DefaultExecutionOrder(-50)]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] PlayerConfig config;
        [SerializeField] PlayerMotor motor;
        [SerializeField] PlayerInputReader input;
        [SerializeField] PlayerAnimatorBridge animator;
        [SerializeField] SwordController sword;
        [SerializeField] GrabController grab;
        [SerializeField] FallTracker fall;
        [SerializeField] PlayerKillReceiver killReceiver;
        [SerializeField] FootstepEmitter footstep;

        PlayerStateGraph graph;
        float rollCooldownUntil;
        float stunUntil;
        bool stunned;
        bool? animatorHitWindow;
        bool stabImpactPending;
        bool initialized;
        bool started;
        bool reportedSetupError;
        bool subscribedToAnimator;

        public PlayerConfig Config => config;
        public PlayerMotor Motor => motor;
        public PlayerInputReader Input => input;
        public PlayerAnimatorBridge Animator => animator;
        public SwordController Sword => sword;
        public GrabController Grab => grab;
        public FallTracker Fall => fall;
        public PlayerKillReceiver KillReceiver => killReceiver;

        public PlayerStateId StateId => graph != null ? graph.CurrentId : PlayerStateId.Disabled;
        public string StateName => StateId.ToString();
        public bool IsDead { get; private set; }
        public bool ControlEnabled { get; private set; } = true;
        public bool IsInvulnerable => config != null && config.rollGrantsIFrames && StateId == PlayerStateId.Roll;
        public bool CanRoll => !IsDead && ControlEnabled && Time.time >= rollCooldownUntil;

        void Awake() => EnsureInitialized();

        void Start()
        {
            EnsureInitialized();
            BeginPlay();
        }

        void OnDestroy()
        {
            if (subscribedToAnimator && animator != null)
            {
                animator.HitWindowOpened -= HandleHitWindowOpened;
                animator.HitWindowClosed -= HandleHitWindowClosed;
                animator.StabImpacted -= HandleStabImpact;
                animator.FootstepDetected -= HandleFootstep;
                subscribedToAnimator = false;
            }

            EventBus.Publish(new PlayerDespawned());
        }

        void Update()
        {
            // Страховка от порядка инициализации и отключённой перезагрузки домена:
            // без этого при пустом Awake героиня навсегда оставалась в Disabled.
            if (graph == null) EnsureInitialized();
            if (graph == null) return;

            BeginPlay();

            if (stunned && Time.time >= stunUntil && !IsDead)
            {
                stunned = false;
                SetControlEnabled(true);
            }

            if (!IsDead && grab != null && !grab.IsAttached) grab.Scan();

            graph.Tick(Time.deltaTime);
            animator.Tick(motor.PlanarVelocity.magnitude, motor.IsGrounded);
        }

        void EnsureInitialized()
        {
            if (graph != null) return;

            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (animator == null) animator = GetComponent<PlayerAnimatorBridge>();
            if (sword == null) sword = GetComponent<SwordController>();
            if (grab == null) grab = GetComponent<GrabController>();
            if (fall == null) fall = GetComponent<FallTracker>();
            if (killReceiver == null) killReceiver = GetComponent<PlayerKillReceiver>();
            if (footstep == null) footstep = GetComponent<FootstepEmitter>();

            if (config == null)
            {
                ReportSetupError("не назначен PlayerConfig");
                return;
            }

            if (motor == null || input == null || animator == null || sword == null || grab == null || fall == null)
            {
                ReportSetupError("на префабе не хватает компонентов героини");
                return;
            }

            motor.Configure(config);
            input.BufferWindow = config.inputBufferTime;
            grab.Configure(config.grabSearchRadius, config.grabSnapTime);

            graph = new PlayerStateGraph(this);

            if (animator != null)
            {
                animator.HitWindowOpened += HandleHitWindowOpened;
                animator.HitWindowClosed += HandleHitWindowClosed;
                animator.StabImpacted += HandleStabImpact;
                animator.FootstepDetected += HandleFootstep;
                subscribedToAnimator = true;
            }

            initialized = true;
        }

        void BeginPlay()
        {
            if (started || graph == null) return;

            started = true;
            graph.Start();
            EventBus.Publish(new PlayerSpawned(transform.position, transform.rotation));
        }

        void ReportSetupError(string message)
        {
            if (reportedSetupError) return;
            reportedSetupError = true;
            Debug.LogError($"PlayerController: {message}", this);
        }

        public void MarkRollUsed()
        {
            rollCooldownUntil = Time.time + config.rollCooldown;
        }

        public bool HitWindowOpen(float elapsed)
        {
            return elapsed >= config.attackHitWindowStart && elapsed <= config.attackHitWindowEnd &&
                   animatorHitWindow != false;
        }

        public void ResetAnimatorHitWindow()
        {
            animatorHitWindow = null;
            stabImpactPending = false;
        }

        public bool ConsumeAnimatorStabImpact()
        {
            if (!stabImpactPending) return false;
            stabImpactPending = false;
            return true;
        }

        public void SetControlEnabled(bool value)
        {
            if (IsDead) return;
            ControlEnabled = value;
            if (!value && input != null) input.ClearBuffer();
        }

        public void Stun(float seconds)
        {
            if (seconds <= 0f || IsDead) return;
            stunned = true;
            stunUntil = Time.time + seconds;
            SetControlEnabled(false);
        }

        public void EnterDead()
        {
            if (IsDead) return;
            IsDead = true;
            ControlEnabled = false;
            input.SetInputEnabled(false);
            motor.StopImmediate();
        }

        // Вызывается GameFlow после возрождения под заливкой (§18.4).
        public void Revive()
        {
            EnsureInitialized();
            if (graph == null) return;

            IsDead = false;
            ControlEnabled = true;
            stunned = false;
            if (killReceiver != null) killReceiver.ResetDead();
            if (grab != null && grab.IsAttached) grab.Detach(GrabReleaseReason.Released);
            ResetAnimatorHitWindow();
            input.SetInputEnabled(true);
            motor.Suspend(false);
            motor.StopImmediate();
            started = true;
            graph.Start();
        }

        void HandleHitWindowOpened() => animatorHitWindow = true;
        void HandleHitWindowClosed() => animatorHitWindow = false;
        void HandleStabImpact() => stabImpactPending = true;

        void HandleFootstep()
        {
            if (footstep != null) footstep.OnFootstep();
        }

        // Для отладочного оверлея (§25.1).
        public string DebugState => graph == null ? "—" : $"{graph.CurrentId}";
    }
}
