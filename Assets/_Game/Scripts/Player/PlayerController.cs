using TanShadow.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Player
{
    public enum PlayerState
    {
        Locomotion, // Idle и Move
        Attack,
        Block,
        Deflect,
        Stagger,
        Dash,
        Finisher
    }

    [RequireComponent(typeof(CharacterController), typeof(Combatant), typeof(AttackExecutor))]
    public class PlayerController : MonoBehaviour
    {
        public InputActionAsset controls;
        public CombatSettings settings;
        [Tooltip("Цепочка быстрых уколов кинжалом, по порядку")]
        public AttackData[] combo;
        [Tooltip("Если пусто — Camera.main")]
        public Transform cameraTransform;

        public PlayerState State { get; private set; }
        public int ComboStep { get; private set; } = -1;
        public Vector3 DashDirection => dashDirection;
        public float StaggerDuration => staggerDuration;

        CharacterController body;
        Combatant combatant;
        AttackExecutor executor;
        LockOnTargeting lockOn;
        YinYangQi qi;
        Huajin huajin;
        readonly InputBuffer buffer = new InputBuffer();
        InputActionMap map;
        InputAction move, attack, block, dash;

        float stateTime;
        float verticalSpeed;
        Vector3 dashDirection;
        float dashReadyTime;
        Vector3 knockback;
        float staggerDuration;
        Combatant finisherVictim;
        bool finisherImpactDone;
        Vector3 finisherStart, finisherEnd;
        float comboChainUntil = float.NegativeInfinity;
        Quaternion attackFacing = Quaternion.identity;

        void Awake()
        {
            body = GetComponent<CharacterController>();
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            lockOn = GetComponent<LockOnTargeting>();
            qi = GetComponent<YinYangQi>();
            if (qi != null) qi.Overflow += OnQiOverflow;
            huajin = GetComponent<Huajin>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

            map = controls.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            attack = map.FindAction("Attack", true);
            block = map.FindAction("Block", true);
            dash = map.FindAction("Dash", true);

            combatant.Hurt += OnHurt;
        }

        void OnEnable() => map.Enable();
        void OnDisable() => map.Disable();

        void Update()
        {
            float now = combatant.LocalTime;
            if (attack.WasPressedThisFrame()) buffer.Press(BufferedAction.Attack, now);
            if (dash.WasPressedThisFrame()) buffer.Press(BufferedAction.Dash, now);

            float dt = combatant.DeltaTime;
            stateTime += dt;

            switch (State)
            {
                case PlayerState.Locomotion: UpdateLocomotion(dt); break;
                case PlayerState.Attack: UpdateAttack(); break;
                case PlayerState.Block: UpdateBlock(dt); break;
                case PlayerState.Deflect: UpdateDeflect(); break;
                case PlayerState.Stagger: UpdateStagger(); break;
                case PlayerState.Dash: UpdateDash(dt); break;
                case PlayerState.Finisher: UpdateFinisher(); break;
            }

            ApplyKnockbackAndGravity(dt);
        }

        // --- Состояния ---

        void UpdateLocomotion(float dt)
        {
            if (block.IsPressed()) { EnterBlock(); return; }
            if (TryStartAttack()) return;
            if (TryStartDash()) return;
            MoveAndTurn(settings.moveSpeed, dt);
        }

        void UpdateAttack()
        {
            // Доворот к цели во время замаха — плавно, без рывка
            if (executor.Phase == AttackPhase.Windup)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, attackFacing, settings.turnSpeed * 2.5f * combatant.DeltaTime);

            // До активной фазы атаку можно отменить в блок.
            if (executor.Phase == AttackPhase.Windup && block.WasPressedThisFrame())
            {
                executor.Cancel();
                EnterBlock();
                return;
            }
            if (executor.Phase == AttackPhase.Recovery && (TryStartAttack() || TryStartDash())) return;
            if (!executor.IsBusy)
            {
                comboChainUntil = combatant.LocalTime + settings.comboChainGrace;
                SetState(PlayerState.Locomotion);
            }
        }

        void UpdateBlock(float dt)
        {
            if (!block.IsPressed())
            {
                combatant.StopBlock();
                SetState(PlayerState.Locomotion);
                return;
            }
            MoveAndTurn(settings.blockMoveSpeed, dt);
        }

        void UpdateDeflect()
        {
            if (TryStartAttack()) return;

            // Каждый следующий дефлект — новое нажатие со своим окном.
            if (!block.IsPressed())
            {
                combatant.StopBlock();
            }
            else if (block.WasPressedThisFrame())
            {
                combatant.StopBlock();
                combatant.StartBlock();
            }

            if (stateTime < settings.deflectRecovery) return;
            SetState(combatant.IsBlocking ? PlayerState.Block : PlayerState.Locomotion);
        }

        void UpdateStagger()
        {
            if (stateTime >= staggerDuration) SetState(PlayerState.Locomotion);
        }

        void UpdateFinisher()
        {
            float impactTime = settings.finisherDuration * settings.finisherImpactAt;

            // До удара подходим к врагу, дальше стоим.
            Vector3 wanted = Vector3.Lerp(finisherStart, finisherEnd, Mathf.Clamp01(stateTime / impactTime));
            Vector3 step = wanted - transform.position;
            step.y = 0f;
            body.Move(step);
            FaceTowards(finisherVictim.transform.position);

            if (!finisherImpactDone && stateTime >= impactTime)
            {
                finisherImpactDone = true;
                finisherVictim.ReceiveFinisher(combatant);
            }

            if (stateTime >= settings.finisherDuration)
            {
                combatant.IsInvulnerable = false;
                finisherVictim = null;
                SetState(PlayerState.Locomotion);
            }
        }

        void UpdateDash(float dt)
        {
            combatant.IsInvulnerable = stateTime < settings.dashInvulnerability;
            float speed = settings.dashDistance / settings.dashDuration;
            body.Move(dashDirection * speed * dt);

            if (stateTime >= settings.dashDuration)
            {
                combatant.IsInvulnerable = false;
                SetState(PlayerState.Locomotion);
            }
        }

        // --- Переходы ---

        void SetState(PlayerState next)
        {
            State = next;
            stateTime = 0f;
        }

        void EnterBlock()
        {
            combatant.StartBlock();
            SetState(PlayerState.Block);
        }

        bool TryStartAttack()
        {
            if (!buffer.Consume(BufferedAction.Attack, combatant.LocalTime, settings.inputBuffer)) return false;

            var victim = FindFinisherTarget();
            if (victim != null)
            {
                StartFinisher(victim);
                return true;
            }

            // Цепочка продолжается из восстановления прошлого удара или сразу после него.
            bool chaining = State == PlayerState.Attack || combatant.LocalTime <= comboChainUntil;
            ComboStep = chaining && ComboStep >= 0 ? (ComboStep + 1) % combo.Length : 0;

            combatant.StopBlock();
            FaceAttackTarget();
            executor.Begin(combo[ComboStep], huajin != null ? huajin.Consume() : 1f);
            SetState(PlayerState.Attack);
            return true;
        }

        bool TryStartDash()
        {
            if (combatant.LocalTime < dashReadyTime) return false;
            if (!buffer.Consume(BufferedAction.Dash, combatant.LocalTime, settings.inputBuffer)) return false;

            executor.Cancel();
            combatant.StopBlock();
            Vector3 input = CameraRelativeInput();
            dashDirection = input.sqrMagnitude > 0.01f ? input.normalized : -transform.forward;
            dashReadyTime = combatant.LocalTime + settings.dashDuration + settings.dashCooldown;
            SetState(PlayerState.Dash);
            return true;
        }

        void EnterStagger(float duration)
        {
            staggerDuration = duration;
            SetState(PlayerState.Stagger);
        }

        // Сломленный враг рядом: сначала захваченный, иначе ближайший перед нами.
        Combatant FindFinisherTarget()
        {
            if (lockOn != null && lockOn.IsLocked && CanFinish(lockOn.Target, 180f)) return lockOn.Target;

            Combatant best = null;
            float bestDistance = float.MaxValue;
            foreach (var other in Combatant.All)
            {
                if (other == combatant || !CanFinish(other, 90f)) continue;
                float distance = Vector3.Distance(other.transform.position, transform.position);
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }

        bool CanFinish(Combatant other, float maxAngle)
        {
            if (other.IsDead) return false;
            var posture = other.GetComponent<Posture>();
            if (posture == null || !posture.IsBroken) return false;

            Vector3 to = other.transform.position - transform.position;
            to.y = 0f;
            return to.magnitude <= settings.finisherRange && Vector3.Angle(transform.forward, to) <= maxAngle;
        }

        void StartFinisher(Combatant victim)
        {
            executor.Cancel();
            combatant.StopBlock();
            combatant.IsInvulnerable = true;
            finisherVictim = victim;
            finisherImpactDone = false;

            Vector3 to = victim.transform.position - transform.position;
            to.y = 0f;
            finisherStart = transform.position;
            finisherEnd = victim.transform.position - to.normalized * settings.finisherDistance;
            finisherEnd.y = transform.position.y;
            FaceTowards(victim.transform.position);
            SetState(PlayerState.Finisher);
        }

        void FaceTowards(Vector3 point)
        {
            Vector3 to = point - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(to);
        }

        void OnQiOverflow(int side)
        {
            if (State == PlayerState.Finisher) return;
            executor.Cancel();
            combatant.StopBlock();
            buffer.Clear();
            EnterStagger(settings.qiBreakStun);
        }

        void OnHurt(HitInfo hit)
        {
            Vector3 away = transform.position - hit.attacker.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward;

            switch (hit.result)
            {
                case HitResult.Deflected:
                    executor.Cancel();
                    SetState(PlayerState.Deflect);
                    break;
                case HitResult.Blocked:
                    knockback = away * settings.blockKnockback;
                    break;
                case HitResult.Hit:
                    executor.Cancel();
                    combatant.StopBlock();
                    buffer.Clear();
                    knockback = away * settings.hitKnockback;
                    EnterStagger(settings.staggerDuration);
                    if (combatant.Health <= 0f) combatant.ResetHealth(); // смерти пока нет
                    break;
            }
        }

        // --- Движение ---

        Vector3 CameraRelativeInput()
        {
            Vector2 raw = move.ReadValue<Vector2>();
            if (cameraTransform == null) return new Vector3(raw.x, 0f, raw.y);

            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            return Vector3.ClampMagnitude(forward * raw.y + right * raw.x, 1f);
        }

        void MoveAndTurn(float speed, float dt)
        {
            Vector3 input = CameraRelativeInput();
            body.Move(input * speed * dt);

            // В захвате стрейфим лицом к цели, иначе поворачиваемся по движению.
            Vector3 face = input;
            if (TryGetLockDirection(out var toTarget)) face = toTarget;
            if (face.sqrMagnitude > 0.01f)
            {
                var look = Quaternion.LookRotation(face);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, settings.turnSpeed * dt);
            }
        }

        bool TryGetLockDirection(out Vector3 direction)
        {
            direction = Vector3.zero;
            if (lockOn == null || !lockOn.IsLocked) return false;
            direction = lockOn.Target.transform.position - transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f;
        }

        // Мягкий доворот: бьём в ближайшего врага по направлению ввода, иначе — по вводу.
        void FaceAttackTarget()
        {
            if (TryGetLockDirection(out var toTarget))
            {
                attackFacing = Quaternion.LookRotation(toTarget);
                return;
            }

            Vector3 input = CameraRelativeInput();
            Vector3 wanted = input.sqrMagnitude > 0.01f ? input.normalized : transform.forward;

            Combatant best = null;
            float bestDistance = settings.softLockRange;
            foreach (var other in Combatant.All)
            {
                if (other == combatant) continue;
                Vector3 to = other.transform.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > bestDistance || Vector3.Angle(wanted, to) > 100f) continue;
                best = other;
                bestDistance = distance;
            }

            Vector3 face = wanted;
            if (best != null)
            {
                face = best.transform.position - transform.position;
                face.y = 0f;
            }
            attackFacing = face.sqrMagnitude > 0.001f ? Quaternion.LookRotation(face) : transform.rotation;
        }

        void ApplyKnockbackAndGravity(float dt)
        {
            verticalSpeed = body.isGrounded ? -2f : verticalSpeed + settings.gravity * dt;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, settings.knockbackDeceleration * dt);
            body.Move((knockback + Vector3.up * verticalSpeed) * dt);
        }
    }
}
