using System.Collections.Generic;
using TanShadow.Characters;
using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Player
{
    // Переводит состояния PlayerController в Animator.
    // Передвижение подстраивается под реальное движение персонажа, а не наоборот (геймплей остаётся отзывчивым):
    //  • в свободном движении героиня всегда бежит «лицом вперёд» (без боковых шагов на поворотах),
    //    в захвате цели — стрейф и шаг назад по blend tree;
    //  • позиции клипов в blend tree равны их собственным скоростям, поэтому шаг совпадает с перемещением,
    //    а если персонаж быстрее клипа — цикл ускоряется (speed warping);
    //  • резкая смена направления — быстрый разворот клипом Turn90, корпус поворачивается по профилю клипа;
    //  • корпус наклоняется в поворот и при разгоне, голова смотрит вперёд или на врага;
    //  • удар по блоку отбрасывает руку с кинжалом.
    // Действия (удары, уворот, реакции) включаются в тот же кадр, плавность даёт PoseInertializer.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : MonoBehaviour
    {
        public Animator animator;
        public PlayerAnimationSet set;
        [Tooltip("Инерционные переходы. Если пусто — обычный кроссфейд аниматора")]
        public PoseInertializer inertializer;
        [Tooltip("За сколько секунд слой блока поднимает руки")]
        [Min(0.01f)] public float blockBlend = 0.1f;
        [Tooltip("Сколько длится визуальный уворот, с (дольше самого рывка — чтобы не дёргалось)")]
        [Min(0.05f)] public float dodgeVisualDuration = 0.5f;
        [Tooltip("Сколько длится анимация дефлекта, с")]
        [Min(0.05f)] public float deflectVisualDuration = 0.45f;

        static readonly int VelX = Animator.StringToHash("VelX");
        static readonly int VelZ = Animator.StringToHash("VelZ");
        static readonly int TurnRate = Animator.StringToHash("TurnRate");
        static readonly int LocoSpeed = Animator.StringToHash("LocoSpeed");
        static readonly int IdleVariant = Animator.StringToHash("IdleVariant");
        static readonly int DashX = Animator.StringToHash("DashX");
        static readonly int DashZ = Animator.StringToHash("DashZ");
        static readonly int ActionSpeed = Animator.StringToHash("ActionSpeed");

        // Имена состояний совпадают с именами клипов — так скорость считаем по длине клипа.
        public const string LocomotionState = "Locomotion";
        const string DeflectState = "Deflect_BlockReact";
        const string HitLightState = "Hit_Reaction";
        const string HitHeavyState = "Stagger_Impact";
        const string DodgeState = "Dodge";
        const string DodgeLengthKey = "Dodge_F_Pack";
        const string FinisherState = "Finisher_DoubleStab";
        const string TurnLeftState = "Turn90_L";
        const string TurnRightState = "Turn90_R";

        PlayerController player;
        Combatant combatant;
        AttackExecutor executor;
        LockOnTargeting lockOn;
        CombatSettings settings;
        readonly Dictionary<string, float> clipLength = new Dictionary<string, float>();
        int[] lastVariant;

        PlayerState lastState;
        string currentState = LocomotionState;
        Vector3 lastPosition;
        float lastYaw;
        float humanScale = 1f;

        Vector3 animVelocity, animVelocityRef;
        Vector3 smoothedLocalVelocity, smoothedVelocityRef, lastSmoothedLocalVelocity;
        float speed, prevSpeed;
        float yawRate;
        float locoSpeed = 1f;
        float blockWeight;

        int idleTarget;
        float idleValue, idleVelocity, idleTimer, nextIdleSwitch;

        float lean, leanVelocity, pitch, pitchVelocity;
        Transform spine, chest, neck, head, rightUpperArm;
        Vector3 neckFace, headFace;   // направление взгляда в осях костей (из бинд-позы)
        float lookWeight;

        bool turning;
        float turnTime, turnDuration, turnStartYaw, turnAngle;

        float kickTime = float.MaxValue, kickAmount;

        int offHandLayer = -1;
        float offHandWeight, offHandTarget;

        public string CurrentState => currentState;
        public bool IsTurning => turning;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            lockOn = GetComponent<LockOnTargeting>();
            settings = player.settings;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (inertializer == null) inertializer = GetComponentInChildren<PoseInertializer>();
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                clipLength[clip.name] = clip.length;

            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            offHandLayer = animator.GetLayerIndex("OffHand");
            // До первого кадра аниматора модель стоит в бинд-позе и смотрит вперёд вместе с персонажем.
            if (neck != null) neckFace = Quaternion.Inverse(neck.rotation) * transform.forward;
            if (head != null) headFace = Quaternion.Inverse(head.rotation) * transform.forward;

            lastVariant = new int[set != null && set.comboSteps != null ? set.comboSteps.Length : 0];
            for (int i = 0; i < lastVariant.Length; i++) lastVariant[i] = -1;

            executor.PhaseChanged += OnAttackPhase;
            combatant.Hurt += OnHurt;
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            nextIdleSwitch = set != null ? Random.Range(set.idleSwitchInterval.x, set.idleSwitchInterval.y) : 8f;
        }

        void Start()
        {
            if (animator.isHuman && animator.humanScale > 0.01f) humanScale = animator.humanScale;
        }

        void OnDestroy()
        {
            if (executor != null) executor.PhaseChanged -= OnAttackPhase;
            if (combatant != null) combatant.Hurt -= OnHurt;
        }

        void OnDisable()
        {
            if (turning) EndTurn(false);
        }

        void OnAttackPhase(AttackPhase phase)
        {
            // Замах приходит раньше, чем PlayerController переключит состояние на Attack, поэтому состояние не проверяем.
            if (phase != AttackPhase.Windup || executor.Current == null || set == null || set.comboSteps.Length == 0) return;
            if (turning) EndTurn(false);
            int step = Mathf.Clamp(player.ComboStep, 0, set.comboSteps.Length - 1);
            var clip = PickVariant(step);
            if (clip == null) return;
            offHandTarget = clip.offHandRest;

            // Момент удара в клипе совмещаем с серединой активной фазы атаки.
            float toImpact = Mathf.Max(0.05f, executor.Current.windup + executor.Current.active * 0.5f);
            float speedMul = Mathf.Clamp((clip.impact - clip.start) / toImpact, set.attackSpeedRange.x, set.attackSpeedRange.y);
            Play(clip.state, speedMul, clip.start, set.attackBlend);
        }

        PlayerAnimationSet.AttackClip PickVariant(int step)
        {
            var variants = set.comboSteps[step].variants;
            if (variants == null || variants.Length == 0) return null;
            int index = 0;
            if (variants.Length > 1)
            {
                // случайный, но не тот же, что в прошлый раз на этом шаге
                index = Random.Range(0, variants.Length - 1);
                if (index >= lastVariant[step] && lastVariant[step] >= 0) index++;
            }
            lastVariant[step] = index;
            return variants[index];
        }

        void OnHurt(HitInfo hit)
        {
            if (set == null) return;
            if (hit.result == HitResult.Deflected) StartKick(set.deflectKick);
            else if (hit.result == HitResult.Blocked) StartKick(set.blockKick);
        }

        void StartKick(float amount)
        {
            kickTime = 0f;
            kickAmount = amount;
        }

        void Update()
        {
            // хитстоп — замирают аниматоры участников, а не время
            animator.speed = combatant.InHitstop ? 0f : 1f;
            float dt = Time.deltaTime;
            if (dt <= 0f || set == null) return;

            if (player.State != lastState)
            {
                if (turning && player.State != PlayerState.Locomotion) EndTurn(false);
                OnStateChanged(player.State);
            }
            lastState = player.State;

            if (!combatant.InHitstop)
            {
                UpdateLocomotion(dt);
                UpdateTurn(dt);
            }

            bool guard = player.State == PlayerState.Deflect ||
                         (combatant.IsBlocking && (player.State == PlayerState.Block || player.State == PlayerState.Locomotion));
            blockWeight = Mathf.MoveTowards(blockWeight, guard && !turning ? 1f : 0f, dt / blockBlend);
            if (animator.layerCount > 1) animator.SetLayerWeight(1, blockWeight);

            // Свободная рука на ударе держится ближе к телу — бьёт только рука с кинжалом.
            if (player.State != PlayerState.Attack) offHandTarget = 0f;
            offHandWeight = Mathf.MoveTowards(offHandWeight, offHandTarget, dt / set.offHandBlend);
            if (offHandLayer >= 0) animator.SetLayerWeight(offHandLayer, offHandWeight);
        }

        bool IsLocked => lockOn != null && lockOn.IsLocked && lockOn.Target != null;

        void UpdateLocomotion(float dt)
        {
            // Реальная скорость персонажа в его локальных осях (м/с).
            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            velocity.y = 0f;
            Vector3 local = transform.InverseTransformDirection(velocity);
            prevSpeed = speed;
            speed = local.magnitude;

            float yaw = transform.eulerAngles.y;
            float rawYawRate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            yawRate = Mathf.Lerp(yawRate, rawYawRate, 1f - Mathf.Exp(-dt / 0.08f));

            // В захвате — честное направление (стрейф, шаг назад). Без захвата героиня смотрит туда, куда бежит,
            // поэтому берём только скорость: на поворотах ноги не уходят в боковой шаг.
            Vector3 wanted = IsLocked ? local : new Vector3(0f, 0f, speed);
            wanted /= humanScale;

            // Медленнее шага (неполный стик) — не смешиваем шаг со стойкой (ноги семенят и скользят), а замедляем сам шаг.
            float slowRate = 1f;
            float wantedSpeed = wanted.magnitude;
            float walkSpeed = DirectionalSpeed(wanted, set.walkForwardSpeed, set.walkBackwardSpeed, set.walkSideSpeed);
            if (wantedSpeed > set.standSpeed && wantedSpeed < walkSpeed)
            {
                slowRate = Mathf.Max(wantedSpeed / walkSpeed, set.minWalkRate);
                wanted *= walkSpeed / wantedSpeed;
            }
            // Старт и резкая остановка — ноги сразу переходят в нужный цикл, разницу поз гасит инерциализатор.
            // Иначе тело уже стоит (или уже бежит), а ноги ещё полсекунды семенят на месте.
            float wantedMag = wanted.magnitude, animMag = animVelocity.magnitude;
            bool canSnap = currentState == LocomotionState && !turning && inertializer != null && inertializer.isActiveAndEnabled;
            if (canSnap && wantedMag < set.standSpeed && animMag > 1f)
            {
                animVelocity = animVelocityRef = Vector3.zero;
                inertializer.Begin(set.stopBlend);
            }
            else if (canSnap && animMag < 0.3f && wantedMag > 1f)
            {
                animVelocity = wanted;
                animVelocityRef = Vector3.zero;
                inertializer.Begin(set.startBlend);
            }
            float damp = wanted.sqrMagnitude >= animVelocity.sqrMagnitude ? set.velocityDamp : set.velocityDampStop;
            animVelocity = Vector3.SmoothDamp(animVelocity, wanted, ref animVelocityRef, damp, Mathf.Infinity, dt);
            animator.SetFloat(VelX, animVelocity.x);
            animator.SetFloat(VelZ, animVelocity.z);

            bool standing = speed < set.standSpeed;
            float turn = 0f;
            float targetLocoSpeed = 1f;
            if (standing)
            {
                if (Mathf.Abs(yawRate) > set.turnDeadZone && !turning)
                {
                    turn = Mathf.Clamp(yawRate / set.turnClipRate, -1f, 1f);
                    targetLocoSpeed = Mathf.Clamp(Mathf.Abs(yawRate) / set.turnClipRate, 1f, 2f);
                }
            }
            else
            {
                // Быстрее самого быстрого клипа в этом направлении — ускоряем цикл, чтобы ноги не скользили.
                float runSpeed = DirectionalSpeed(wanted, set.runForwardSpeed, set.runBackwardSpeed, set.runSideSpeed) * humanScale;
                if (slowRate < 1f) targetLocoSpeed = slowRate;
                else if (speed > runSpeed) targetLocoSpeed = Mathf.Min(speed / runSpeed, set.maxPlaybackWarp);
            }
            animator.SetFloat(TurnRate, turn, 0.15f, dt);
            locoSpeed = Mathf.MoveTowards(locoSpeed, targetLocoSpeed, dt * 3f);
            animator.SetFloat(LocoSpeed, locoSpeed);

            UpdateIdleVariant(dt, standing && Mathf.Abs(turn) < 0.01f);
            UpdateLean(dt, local);

            if (!turning && player.State == PlayerState.Locomotion && !IsLocked) TryStartTurn();
        }

        // Скорость клипа в направлении v (смесь вперёд/назад/вбок).
        static float DirectionalSpeed(Vector3 v, float forward, float backward, float side)
        {
            Vector3 dir = v.normalized;
            float f = Mathf.Max(dir.z, 0f), b = Mathf.Max(-dir.z, 0f), s = Mathf.Abs(dir.x);
            return (f * forward + b * backward + s * side) / Mathf.Max(1e-3f, f + b + s);
        }

        // --- Разворот ---

        void TryStartTurn()
        {
            Vector3 input = player.MoveInput;
            input.y = 0f;
            if (input.sqrMagnitude < 0.09f) return; // стик едва тронут — обычный доворот

            float angle = Vector3.SignedAngle(transform.forward, input, Vector3.up);
            float abs = Mathf.Abs(angle);
            float threshold = prevSpeed <= set.turnStandMaxSpeed ? set.turnStartAngle : set.runPivotAngle;
            if (abs < threshold) return;

            turning = true;
            turnTime = 0f;
            turnStartYaw = transform.eulerAngles.y;
            turnAngle = angle;
            turnDuration = abs <= 90f
                ? set.turn90Duration * Mathf.Max(0.6f, abs / 90f)
                : Mathf.Lerp(set.turn90Duration, set.turn180Duration, (abs - 90f) / 90f);
            player.AnimationDrivesFacing = true;
            player.MoveSpeedScale = set.turnMoveScale;

            string state = angle < 0f ? TurnLeftState : TurnRightState;
            float len = clipLength.TryGetValue(state, out var l) ? l : turnDuration;
            Play(state, len / turnDuration, 0f, set.turnBlendIn);
        }

        void UpdateTurn(float dt)
        {
            if (!turning) return;
            if (IsLocked) { EndTurn(true); return; }

            turnTime += dt;
            float t = Mathf.Clamp01(turnTime / turnDuration);
            float yaw = turnStartYaw + turnAngle * set.turnProfile.Evaluate(t);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (t >= 1f) EndTurn(true);
        }

        void EndTurn(bool backToLocomotion)
        {
            turning = false;
            player.AnimationDrivesFacing = false;
            player.MoveSpeedScale = 1f;
            if (backToLocomotion && player.State == PlayerState.Locomotion)
                Play(LocomotionState, 1f, 0f, set.turnBlendOut);
        }

        void UpdateIdleVariant(float dt, bool idle)
        {
            if (set.idleVariants > 1 && idle && player.State == PlayerState.Locomotion)
            {
                idleTimer += dt;
                if (idleTimer >= nextIdleSwitch)
                {
                    idleTimer = 0f;
                    nextIdleSwitch = Random.Range(set.idleSwitchInterval.x, set.idleSwitchInterval.y);
                    int next = Random.Range(0, set.idleVariants - 1);
                    if (next >= idleTarget) next++;
                    idleTarget = next;
                }
            }
            idleValue = Mathf.SmoothDamp(idleValue, idleTarget, ref idleVelocity, set.idleBlendTime * 0.5f, Mathf.Infinity, dt);
            animator.SetFloat(IdleVariant, idleValue);
        }

        void UpdateLean(float dt, Vector3 local)
        {
            smoothedLocalVelocity = Vector3.SmoothDamp(smoothedLocalVelocity, local, ref smoothedVelocityRef, 0.12f, Mathf.Infinity, dt);
            Vector3 accel = (smoothedLocalVelocity - lastSmoothedLocalVelocity) / dt;
            lastSmoothedLocalVelocity = smoothedLocalVelocity;

            bool moving = (player.State == PlayerState.Locomotion || player.State == PlayerState.Block) && !turning;
            float leanTarget = 0f, pitchTarget = 0f;
            if (moving)
            {
                // в поворот — как при беге по дуге; вперёд при разгоне, назад при торможении
                leanTarget = Mathf.Clamp(-yawRate * speed * set.leanPerTurn, -set.maxLean, set.maxLean);
                pitchTarget = Mathf.Clamp(accel.z * set.leanPerAcceleration, -set.maxLean, set.maxLean);
            }
            lean = Mathf.SmoothDamp(lean, leanTarget, ref leanVelocity, set.leanSmoothing, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDamp(pitch, pitchTarget, ref pitchVelocity, set.leanSmoothing, Mathf.Infinity, dt);
        }

        // --- Поверх позы аниматора, до инерционного блендинга и вторичной анимации ---

        void LateUpdate()
        {
            if (set == null) return;
            float dt = Time.deltaTime;

            if (spine != null && chest != null && (Mathf.Abs(lean) > 0.01f || Mathf.Abs(pitch) > 0.01f))
            {
                ApplyLean(spine, 0.4f);
                ApplyLean(chest, 0.6f);
            }
            ApplyKick(dt);
            ApplyLook(dt);
        }

        void ApplyLean(Transform bone, float share)
        {
            var q = Quaternion.AngleAxis(lean * share, transform.forward) * Quaternion.AngleAxis(pitch * share, transform.right);
            bone.rotation = q * bone.rotation;
        }

        void ApplyKick(float dt)
        {
            if (kickTime > set.kickTime * 8f) return;
            // Отдача идёт по реальному времени, даже на хитстопе: замерший кадр показывает сам удар.
            kickTime += dt;
            float x = kickTime / set.kickTime;
            float k = kickAmount * x * Mathf.Exp(1f - x); // быстрый пик и плавное затухание
            if (chest != null) chest.rotation = Quaternion.AngleAxis(-set.kickChest * k, transform.right) * chest.rotation;
            if (rightUpperArm != null) rightUpperArm.rotation = Quaternion.AngleAxis(-set.kickArm * k, transform.right) * rightUpperArm.rotation;
        }

        void ApplyLook(float dt)
        {
            if (head == null || neck == null) return;
            float target;
            switch (player.State)
            {
                case PlayerState.Locomotion:
                case PlayerState.Block:
                    target = set.lookWeight; break;
                case PlayerState.Attack:
                case PlayerState.Deflect:
                case PlayerState.Finisher:
                    target = set.lookActionWeight; break;
                default:
                    target = 0.2f; break;
            }
            lookWeight = Mathf.MoveTowards(lookWeight, target, dt * 4f);
            if (lookWeight < 0.01f) return;

            Vector3 dir;
            if (IsLocked)
                dir = lockOn.Target.transform.position + Vector3.up * set.lookTargetHeight - head.position;
            else
            {
                // при развороте голова опережает корпус и смотрит туда, куда разворачиваемся
                float yaw = turning ? turnStartYaw + turnAngle : transform.eulerAngles.y;
                dir = Quaternion.Euler(set.lookDownAngle, yaw, 0f) * Vector3.forward;
            }

            // не выворачивать шею: ограничиваем относительно корпуса
            Vector3 local = transform.InverseTransformDirection(dir.normalized);
            float lookYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -set.maxLookYaw, set.maxLookYaw);
            float lookPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -set.maxLookPitch, set.maxLookPitch);
            dir = transform.rotation * (Quaternion.Euler(lookPitch, lookYaw, 0f) * Vector3.forward);

            RotateToward(neck, neckFace, dir, lookWeight * 0.4f);
            RotateToward(head, headFace, dir, lookWeight);
        }

        static void RotateToward(Transform bone, Vector3 faceLocal, Vector3 dir, float weight)
        {
            Vector3 current = bone.rotation * faceLocal;
            var delta = Quaternion.FromToRotation(current, dir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
        }

        // --- Действия ---

        void OnStateChanged(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Block:
                    if (currentState != LocomotionState && !turning) Play(LocomotionState, 1f, 0f, set.toLocomotionBlend);
                    break;
                case PlayerState.Deflect:
                    Play(DeflectState, Speed(DeflectState, deflectVisualDuration), 0f, set.reactionBlend);
                    break;
                case PlayerState.Stagger:
                    bool heavy = player.StaggerDuration > 1f;
                    string hit = heavy ? HitHeavyState : HitLightState;
                    Play(hit, Speed(hit, player.StaggerDuration), 0f, set.reactionBlend);
                    break;
                case PlayerState.Dash:
                    Vector3 d = transform.InverseTransformDirection(player.DashDirection);
                    animator.SetFloat(DashX, d.x);
                    animator.SetFloat(DashZ, d.z);
                    Play(DodgeState, Speed(DodgeLengthKey, dodgeVisualDuration), 0f, set.dodgeBlend);
                    break;
                case PlayerState.Finisher:
                    Play(FinisherState, Speed(FinisherState, settings.finisherDuration), 0f, set.finisherBlend);
                    break;
            }
        }

        float Speed(string clip, float duration)
        {
            float len = clipLength.TryGetValue(clip, out var l) ? l : duration;
            return duration > 0f ? len / duration : 1f;
        }

        void Play(string state, float speedMul, float startTime, float blend)
        {
            animator.SetFloat(ActionSpeed, speedMul);
            currentState = state;
            float len = clipLength.TryGetValue(state, out var l) && l > 0f ? l : 1f;
            float normalized = state == LocomotionState ? 0f : Mathf.Clamp01(startTime / len);

            if (inertializer != null && inertializer.isActiveAndEnabled)
            {
                inertializer.Begin(blend);
                animator.Play(state, 0, normalized);
            }
            else
            {
                animator.CrossFadeInFixedTime(state, blend, 0, startTime / Mathf.Max(0.01f, speedMul));
            }
        }
    }
}
