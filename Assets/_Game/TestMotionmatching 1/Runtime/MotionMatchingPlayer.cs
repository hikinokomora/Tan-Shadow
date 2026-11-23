using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TanShadow.MotionMatchingLab
{
    public struct MotionInput
    {
        public Vector3 move, facing;
        public bool walk, sprint, crouch, dodge, traverse;
    }

    [RequireComponent(typeof(CharacterController))]
    public sealed class MotionMatchingPlayer : MonoBehaviour
    {
        [SerializeField] MotionDatabase database;
        [SerializeField] AnimationClip aerialRoll;
        [SerializeField] Animator animator;
        [SerializeField] Transform root, hips, leftFoot, rightFoot;
        [SerializeField] LayerMask collisionMask = ~0;

        const float StandingHeight = 1.76f;
        const float CrouchHeight = 1.08f;
        const float TurnSpeed = 540f;
        const float TurnResponse = .105f;
        const float GroundAcceleration = 17f;
        const float MoveResponse = .145f;
        const float BrakeResponse = .18f;
        const float Gravity = 15f;
        const float DodgeDuration = .96f;
        const float DodgeDistance = 5.425f;
        const float DodgeLift = 1.02f;
        public const float DodgeTakeoff = .085f;
        public const float DodgeTouchdown = .84f;
        const float DodgeBrakeTime = .076f;

        enum Phase { Ground, Air, Landing, Parkour, Dodge }

        struct Playback
        {
            public AnimationClipPlayable playable;
            public int index;
            public float time, rate, strideRate;
            public AnimationClip overrideClip;
        }

        struct TraversalRoute
        {
            public MotionObstacle obstacle;
            public MotionClip clip;
            public Vector3 start, contact, end;
            public float entry, pivot, exit;

            public Vector3 Position(float time)
            {
                bool approaching = time < pivot;
                float fromTime = approaching ? entry : pivot;
                float toTime = approaching ? pivot : exit;
                Vector3 from = approaching ? start : contact;
                Vector3 to = approaching ? contact : end;
                Vector3 authoredStart = clip.SampleRoot(fromTime).position;
                Vector3 authoredEnd = clip.SampleRoot(toTime).position;
                Vector3 authored = clip.SampleRoot(time).position;
                float t = Mathf.InverseLerp(fromTime, toTime, time);
                float distance = authoredEnd.z - authoredStart.z;
                float progress = distance > .12f ? Mathf.Clamp01((authored.z - authoredStart.z) / distance) : t;
                Vector3 position = Vector3.Lerp(from, to, progress);
                position.y = Mathf.Lerp(from.y, to.y, t) + authored.y - Mathf.Lerp(authoredStart.y, authoredEnd.y, t);
                position.y = Mathf.Max(position.y, Mathf.Min(start.y, end.y) - .04f);
                return position;
            }
        }

        CharacterController capsule;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly Playback[] playback = new Playback[2];
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Collider[] overlaps = new Collider[24];
        int current;
        bool blending, grounded, crouched, fallingAnimation;
        float blendTime, blendDuration, searchTimer, coyoteTime, dodgeBuffer, parkourBuffer, verticalSpeed, phaseTimer, groundStep;
        float dodgeTime, dodgeFloor, dodgeSurface, dodgeRecovery;
        float dodgeLength, dodgeHeight, dodgeDuration;
        float dodgeLaunchMomentum, dodgeRampStart, dodgeStartYaw, dodgeTargetYaw, yawSpeed;
        bool dodgeBlocked, renderedSomersault;
        Vector3 bindPosition, rigBindPosition, planarSpeed, planarAcceleration, dodgeDirection;
        Quaternion bindRotation, rigBindRotation;
        Vector2 torsoLean, torsoLeanVelocity;
        Transform lowerSpine, landingFoot;
        Vector3 landingAnchor;
        Quaternion landingFootRotation;
        MotionPose pose;
        Phase phase;
        TraversalRoute route;
        float routeTime;
        Vector3 startPosition;
        Quaternion startRotation;

        public MotionDatabase Database => database;
        public string CurrentClip => !graph.IsValid() ? string.Empty : (playback[current].overrideClip
            ? playback[current].overrideClip.name : database.Clips[playback[current].index].label);
        public string CurrentState => phase == Phase.Parkour
            ? (route.clip.catchFromAir ? "Catch" : route.clip.kind.ToString())
            : (phase == Phase.Ground && crouched ? "Crouch" : phase.ToString());
        public bool IsGrounded => grounded;
        public float CameraFollowY => phase == Phase.Dodge ? dodgeFloor : transform.position.y;
        public float PlanarSpeed => planarSpeed.magnitude;
        public MotionKind? LastCompletedTraversal { get; private set; }
        public string LastTraversalClip { get; private set; }
        public int MatchTransitions { get; private set; }

        public void Configure(MotionDatabase source, Animator target, Transform skeletonRoot,
            Transform pelvis, Transform footL, Transform footR, AnimationClip rollAnimation)
        {
            database = source;
            animator = target;
            root = skeletonRoot;
            hips = pelvis;
            leftFoot = footL;
            rightFoot = footR;
            aerialRoll = rollAnimation;
        }

        public void SetAerialRoll(AnimationClip animation) => aerialRoll = animation;

        void Awake()
        {
            capsule = GetComponent<CharacterController>();
            if (!database || !animator || !root || !hips || !leftFoot || !rightFoot || database.Clips.Length == 0)
            {
                Debug.LogError("Motion Matching Lab: missing baked data or mannequin bindings.", this);
                enabled = false;
                return;
            }

            bindPosition = root.localPosition;
            bindRotation = root.localRotation;
            rigBindPosition = animator.transform.localPosition;
            rigBindRotation = animator.transform.localRotation;
            lowerSpine = hips.Find("spine_01");
            groundStep = capsule.stepOffset;
            startPosition = transform.position;
            startRotation = transform.rotation;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Motion Matching Lab");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            var output = AnimationPlayableOutput.Create(graph, "Mannequin", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
            ResetTo(startPosition, startRotation);
        }

        void OnDisable()
        {
            if (renderedSomersault && animator)
            {
                animator.transform.SetLocalPositionAndRotation(rigBindPosition, rigBindRotation);
                renderedSomersault = false;
            }
            if (phase == Phase.Parkour)
            {
                ReleaseObstacle();
                phase = Phase.Air;
                fallingAnimation = false;
            }
        }

        void OnDestroy()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        public void ResetToStart() => ResetTo(startPosition, startRotation);

        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            if (!graph.IsValid())
                return;

            ReleaseObstacle();
            capsule.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            SetHeight(StandingHeight);
            capsule.enabled = true;
            capsule.stepOffset = groundStep;
            grounded = (capsule.Move(Vector3.down * .06f) & CollisionFlags.Below) != 0;
            phase = grounded ? Phase.Ground : Phase.Air;
            crouched = false;
            fallingAnimation = false;
            coyoteTime = grounded ? .1f : 0f;
            searchTimer = phaseTimer = dodgeBuffer = parkourBuffer = verticalSpeed = dodgeRecovery = 0f;
            planarSpeed = planarAcceleration = Vector3.zero;
            torsoLean = torsoLeanVelocity = Vector2.zero;
            yawSpeed = 0f;
            landingFoot = null;
            LastCompletedTraversal = null;
            LastTraversalClip = string.Empty;
            CutTo(database.IdleClip, 0f);
            Animate(0f, true);
        }

        public void Simulate(MotionInput input, float deltaTime)
        {
            if (!enabled || !graph.IsValid() || deltaTime <= 0f)
                return;
            float dt = Mathf.Min(deltaTime, .05f);
            if (transform.position.y < -7f)
                ResetToStart();

            if (phase == Phase.Parkour)
            {
                AdvanceTraversal(dt);
                Animate(dt);
                return;
            }
            if (phase == Phase.Dodge)
            {
                AdvanceDodge(dt);
                Animate(dt);
                return;
            }

            Vector3 wish = Vector3.ClampMagnitude(new Vector3(input.move.x, 0f, input.move.z), 1f);
            Vector3 movementDirection = wish.sqrMagnitude > .001f ? wish.normalized : transform.forward;
            coyoteTime = grounded ? .10f : Mathf.Max(0f, coyoteTime - dt);
            dodgeRecovery = Mathf.Max(0f, dodgeRecovery - dt);
            dodgeBuffer = input.dodge ? .13f : Mathf.Max(0f, dodgeBuffer - dt);
            parkourBuffer = input.traverse ? .17f : Mathf.Max(0f, parkourBuffer - dt);

            if (parkourBuffer > 0f && TryTraverse(movementDirection, !grounded && coyoteTime <= 0f))
            {
                dodgeBuffer = parkourBuffer = 0f;
                Animate(dt);
                return;
            }

            if (grounded && dodgeBuffer > 0f && dodgeRecovery <= 0f && BeginDodge(movementDirection))
            {
                AdvanceDodge(dt);
                Animate(dt);
                return;
            }

            if (grounded)
            {
                crouched = input.crouch || (crouched && !IsFree(transform.position, StandingHeight, null));
                SetHeight(crouched ? CrouchHeight : StandingHeight);
            }

            Vector3 desiredFacing = input.facing.sqrMagnitude > .001f ? input.facing : (wish.sqrMagnitude > .001f ? wish : transform.forward);
            desiredFacing.y = 0f;
            if (desiredFacing.sqrMagnitude > .001f)
            {
                float desiredYaw = Mathf.Atan2(desiredFacing.x, desiredFacing.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, desiredYaw, ref yawSpeed, TurnResponse, TurnSpeed, dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }

            Gait gait = crouched ? Gait.Crouch : (wish.sqrMagnitude < .001f ? Gait.Idle :
                (input.sprint ? Gait.Sprint : (input.walk ? Gait.Walk : Gait.Run)));
            float speed = gait switch { Gait.Walk => 2f, Gait.Sprint => 7f, Gait.Crouch => 2.2f, Gait.Run => 5f, _ => 0f };
            Vector3 desiredVelocity = wish * speed;
            phaseTimer -= dt;
            searchTimer -= dt;
            if (phase == Phase.Landing && phaseTimer <= 0f)
                phase = Phase.Ground;
            if (phase == Phase.Ground && searchTimer <= 0f)
                Search(gait, desiredVelocity, desiredFacing);
            if (phase == Phase.Air && !fallingAnimation && phaseTimer <= 0f)
                PlayFall();

            Vector3 drive = desiredVelocity;
            if (phase == Phase.Ground && wish.sqrMagnitude > .001f)
            {
                MotionClip clip = database.Clips[playback[current].index];
                float time = playback[current].time;
                MotionRoot frameRoot = clip.SampleRoot(time);
                Vector3 authored = Quaternion.Euler(0f, -frameRoot.yaw, 0f) * clip.Velocity(time);
                float authoredSpeed = Vector3.Dot(transform.TransformDirection(authored), movementDirection);
                float tunedSpeed = Mathf.Clamp(authoredSpeed, speed * .72f, speed * 1.16f);
                drive = wish * Mathf.Lerp(speed, tunedSpeed, .65f);
            }

            bool wasGrounded = grounded;
            bool coasting = wish.sqrMagnitude < .001f;
            float acceleration = grounded ? GroundAcceleration : (coasting ? .7f : 6f);
            planarSpeed = Vector3.SmoothDamp(planarSpeed, drive, ref planarAcceleration, MoveTime(grounded, coasting), acceleration, dt);
            verticalSpeed = grounded && phase != Phase.Air ? -3.2f : verticalSpeed - Gravity * dt;
            capsule.stepOffset = grounded && phase != Phase.Air ? groundStep : 0f;
            Vector3 before = transform.position;
            var flags = capsule.Move((planarSpeed + Vector3.up * verticalSpeed) * dt);
            Vector3 actual = (transform.position - before) / dt;
            Vector3 actualPlanar = new Vector3(actual.x, 0f, actual.z);
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 blocked = (planarSpeed - actualPlanar).normalized;
                planarAcceleration -= blocked * Mathf.Max(0f, Vector3.Dot(planarAcceleration, blocked));
            }
            planarSpeed = actualPlanar;
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            grounded = (flags & CollisionFlags.Below) != 0 && verticalSpeed <= 0f;

            if (grounded && !wasGrounded && phase == Phase.Air)
                BeginLand();
            else if (!grounded && wasGrounded && phase != Phase.Air)
            {
                phase = Phase.Air;
                phaseTimer = .10f;
                fallingAnimation = false;
                verticalSpeed = Mathf.Max(verticalSpeed, -1f);
            }
            Animate(dt);
        }

        void SetHeight(float height)
        {
            if (!Mathf.Approximately(capsule.height, height))
            {
                capsule.height = height;
                capsule.center = Vector3.up * height * .5f;
            }
        }

        bool IsFree(Vector3 feet, float height, Collider ignored)
        {
            float radius = capsule.radius - .045f;
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (radius + .09f),
                feet + Vector3.up * (height - radius), radius, overlaps, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlaps[i] != ignored && overlaps[i] != capsule)
                    return false;
            return count < overlaps.Length;
        }

        void Search(Gait gait, Vector3 desiredVelocity, Vector3 desiredFacing)
        {
            searchTimer = .095f;
            if (blending)
                return;

            Vector3 wanted = transform.InverseTransformDirection(desiredVelocity);
            Vector3 speed = transform.InverseTransformDirection(planarSpeed);
            Vector3 acceleration = transform.InverseTransformDirection(planarAcceleration);
            Vector3 facing = transform.InverseTransformDirection(desiredFacing.normalized);
            MotionTrajectory trajectory = new MotionTrajectory
            {
                near = Predict(speed, wanted, acceleration, MotionTrajectory.Near),
                mid = Predict(speed, wanted, acceleration, MotionTrajectory.Mid),
                far = Predict(speed, wanted, acceleration, MotionTrajectory.Far),
                facingNear = Flat(Vector3.RotateTowards(Vector3.forward, facing, TurnSpeed * Mathf.Deg2Rad * MotionTrajectory.Near, 0f)),
                facingFar = Flat(facing)
            };
            int index = database.Match(crouched ? MotionKind.Crouch : MotionKind.Ground, gait, pose,
                trajectory, Flat(speed), playback[current].index, playback[current].time);
            if (index < 0)
                return;

            MotionFrame best = database.Frames[index];
            if (best.clip == playback[current].index && Mathf.Abs(best.time - playback[current].time) < .20f)
                return;
            MatchTransitions++;
            BlendTo(best.clip, best.time, .13f);
        }

        static float MoveTime(bool onGround, bool coasting) => onGround
            ? (coasting ? BrakeResponse : MoveResponse) : .26f;

        static Vector2 Predict(Vector3 velocity, Vector3 destination, Vector3 acceleration, float time)
        {
            Vector3 displacement = Vector3.zero;
            int steps = Mathf.CeilToInt(time * 20f);
            float dt = time / steps;
            float response = MoveTime(true, destination.sqrMagnitude < .001f);
            for (int i = 0; i < steps; i++)
            {
                Vector3 next = Vector3.SmoothDamp(velocity, destination, ref acceleration, response, GroundAcceleration, dt);
                displacement += (velocity + next) * (.5f * dt);
                velocity = next;
            }
            return Flat(displacement);
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        bool BeginDodge(Vector3 direction)
        {
            dodgeBuffer = 0f;
            if (!aerialRoll)
                return false;
            dodgeFloor = transform.position.y;
            dodgeSurface = TryReadDodgeSupport(transform.position, dodgeFloor, out float surface)
                ? surface : dodgeFloor - capsule.skinWidth;
            dodgeDirection = direction;
            dodgeLength = LimitDodgeTravel(transform.position, DodgeDistance);
            if (dodgeLength < .72f)
                return false;

            float range = dodgeLength / DodgeDistance;
            dodgeHeight = DodgeLift * Mathf.Sqrt(range);
            dodgeDuration = Mathf.Lerp(.80f, DodgeDuration, range);
            float carry = Mathf.InverseLerp(0f, 6f, Vector3.Dot(planarSpeed, direction));
            dodgeLaunchMomentum = carry * .67f;
            dodgeRampStart = Mathf.Lerp(.042f, 0f, carry);
            dodgeStartYaw = transform.eulerAngles.y;
            dodgeTargetYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            landingFoot = null;
            planarAcceleration = Vector3.zero;
            yawSpeed = 0f;
            parkourBuffer = coyoteTime = 0f;
            phase = Phase.Dodge;
            fallingAnimation = false;
            verticalSpeed = 0f;
            dodgeTime = 0f;
            dodgeBlocked = false;
            capsule.stepOffset = 0f;
            SetHeight(CrouchHeight);
            BlendTo(database.IdleClip, 0f, .085f, aerialRoll.length / dodgeDuration, aerialRoll);
            return true;
        }

        void AdvanceDodge(float dt)
        {
            float previousTime = dodgeTime;
            dodgeTime = Mathf.Min(dodgeTime + dt, dodgeDuration);
            float elapsed = dodgeTime / dodgeDuration, previousElapsed = previousTime / dodgeDuration;
            float progress = Mathf.InverseLerp(DodgeTakeoff, DodgeTouchdown, elapsed);
            float previous = Mathf.InverseLerp(DodgeTakeoff, DodgeTouchdown, previousElapsed);
            float advance = dodgeLength * (DodgeDisplacement(elapsed) - DodgeDisplacement(previousElapsed));
            Vector3 from = transform.position;
            float travel = dodgeBlocked || advance <= 0f ? 0f : LimitDodgeTravel(from, advance);
            dodgeBlocked |= travel < advance - .001f;

            // Takeoff and landing change speed on the ground. The flight itself has constant forward speed.
            float heading = Mathf.SmoothStep(0f, 1f, elapsed / (DodgeTakeoff + .055f));
            transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(dodgeStartYaw, dodgeTargetYaw, heading), 0f);
            float y = dodgeFloor + dodgeHeight * 4f * progress * (1f - progress);
            CollisionFlags flags = capsule.Move(dodgeDirection * travel + Vector3.up * (y - from.y));
            dodgeBlocked |= (flags & CollisionFlags.Sides) != 0;
            Vector3 delta = transform.position - from;
            planarSpeed = new Vector3(delta.x, 0f, delta.z) / dt;
            if (progress > 0f && progress < 1f)
                grounded = false;
            else if (progress >= 1f && previous < 1f)
                grounded = (capsule.Move(Vector3.down * .085f) & CollisionFlags.Below) != 0;
            if (dodgeTime < dodgeDuration)
                return;

            grounded = (capsule.Move(Vector3.down * .085f) & CollisionFlags.Below) != 0;
            crouched = !IsFree(transform.position, StandingHeight, null);
            SetHeight(crouched ? CrouchHeight : StandingHeight);
            phase = grounded ? Phase.Ground : Phase.Air;
            landingFoot = null;
            dodgeRecovery = .07f;
            searchTimer = phaseTimer = verticalSpeed = 0f;
            BlendTo(database.IdleClip, 0f, .10f);
        }

        float DodgeDisplacement(float time)
        {
            float ramp = DodgeTakeoff - dodgeRampStart;
            float launch = Mathf.Clamp(time - dodgeRampStart, 0f, ramp);
            float launchArea = launch * (dodgeLaunchMomentum + (1f - dodgeLaunchMomentum) * .5f * launch / ramp);
            float flightLength = DodgeTouchdown - DodgeTakeoff;
            float flight = Mathf.Clamp(time - DodgeTakeoff, 0f, flightLength);
            float brake = Mathf.Clamp(time - DodgeTouchdown, 0f, DodgeBrakeTime);
            float brakeArea = brake * (1f - .5f * brake / DodgeBrakeTime);
            float totalArea = ramp * (1f + dodgeLaunchMomentum) * .5f + flightLength + DodgeBrakeTime * .5f;
            return (launchArea + flight + brakeArea) / totalArea;
        }

        float LimitDodgeTravel(Vector3 from, float advance)
        {
            Vector3 floor = new Vector3(from.x, dodgeSurface, from.z);
            float radius = capsule.radius - .035f;
            int count = Physics.CapsuleCastNonAlloc(
                floor + Vector3.up * (radius + .012f), floor + Vector3.up * (CrouchHeight - radius),
                radius, dodgeDirection, hits, advance + .045f, collisionMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length)
                return 0f;

            float travel = advance;
            for (int i = 0; i < count; i++)
                if (hits[i].collider != capsule)
                    travel = Mathf.Min(travel, Mathf.Max(0f, hits[i].distance - .045f));

            // Scan to the first unsupported point, including when the far end of a gap is walkable.
            int samples = Mathf.Max(1, Mathf.CeilToInt(travel / .075f));
            float supportedTravel = 0f;
            for (int i = 1; i <= samples; i++)
            {
                float distance = travel * i / samples;
                Vector3 destination = floor + dodgeDirection * (distance + radius * .72f);
                if (!TryReadDodgeSupport(destination, dodgeSurface, out float surface) || surface > dodgeSurface + .025f)
                    break;
                supportedTravel = distance;
            }
            return supportedTravel;
        }

        bool TryReadDodgeSupport(Vector3 position, float referenceHeight, out float surface)
        {
            var probe = new Vector3(position.x, referenceHeight + .22f, position.z);
            int count = Physics.RaycastNonAlloc(probe, Vector3.down, hits, .35f,
                collisionMask, QueryTriggerInteraction.Ignore);
            surface = 0f;
            if (count == hits.Length)
                return false;

            float nearest = float.PositiveInfinity;
            bool supported = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == capsule || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                surface = hit.point.y;
                supported = hit.normal.y > .68f;
            }
            return supported;
        }

        void PlayFall()
        {
            fallingAnimation = true;
            for (int i = 0; i < database.Clips.Length; i++)
            {
                MotionClip clip = database.Clips[i];
                if (clip.kind != MotionKind.Fall || !clip.loop)
                    continue;
                BlendTo(i, 0f, .14f);
                break;
            }
        }

        void BeginLand()
        {
            int index = MatchLanding(planarSpeed, verticalSpeed < -8.2f, out float time);
            phase = Phase.Landing;
            phaseTimer = .24f;
            verticalSpeed = -2f;
            if (index >= 0)
            {
                phaseTimer = Mathf.Clamp(database.Clips[index].exitTime - time, .22f, .57f);
                BlendTo(index, time, .09f);
            }
        }

        int MatchLanding(Vector3 wanted, bool heavy, out float time)
        {
            float best = float.PositiveInfinity;
            int match = -1;
            time = 0f;
            Vector2 velocity = Flat(transform.InverseTransformDirection(wanted));
            for (int i = 0; i < database.Clips.Length; i++)
            {
                MotionClip clip = database.Clips[i];
                if (clip.kind != MotionKind.Land)
                    continue;
                float entry = Mathf.Max(0f, clip.eventTime - .04f);
                Vector2 authored = Flat(clip.Velocity(clip.eventTime + .16f));
                float cost = .65f * pose.Distance(clip.ActionPose(entry)) + (authored - velocity).sqrMagnitude * .32f
                             + (heavy != clip.heavy ? 4f : 0f);
                if (cost < best)
                {
                    best = cost;
                    match = i;
                    time = entry;
                }
            }
            return match;
        }

        bool TryTraverse(Vector3 direction, bool airborne)
        {
            float reach = airborne ? 1.58f : Mathf.Clamp(1.25f + PlanarSpeed * .22f, 1.25f, 2.65f);
            float probeHeight = airborne ? .22f : .57f;
            int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * probeHeight, .19f, direction,
                hits, reach, collisionMask, QueryTriggerInteraction.Ignore);
            MotionObstacle obstacle = null;
            float nearest = reach + 1f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == capsule || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                obstacle = hit.collider.GetComponent<MotionObstacle>();
            }
            if (!obstacle || obstacle.airCatchOnly && !airborne || Mathf.Abs(Vector3.Dot(direction, obstacle.transform.forward)) < .70f)
                return false;

            Vector3 from = transform.position;
            obstacle.GetRoute(from, direction, capsule.radius, out Vector3 forward, out Vector3 contact,
                out Vector3 end, out float distance);
            if (distance < -.05f || contact.y - from.y < .12f || contact.y - from.y > 2.85f)
                return false;
            if (!obstacle.LandsOnTop)
            {
                if (!Physics.Raycast(end + Vector3.up * 1.3f, Vector3.down, out RaycastHit surface, 4f,
                        collisionMask, QueryTriggerInteraction.Ignore) || surface.collider == obstacle.Volume)
                    return false;
                end.y = surface.point.y + .035f;
            }

            float best = float.PositiveInfinity, entry = 0f;
            int selected = -1;
            for (int i = 0; i < database.Clips.Length; i++)
            {
                MotionClip clip = database.Clips[i];
                if (clip.kind != obstacle.kind || clip.catchFromAir != airborne)
                    continue;
                Vector3 pivot = clip.SampleRoot(clip.eventTime).position;
                float first = Mathf.Max(0f, clip.eventTime - (airborne ? .55f : 1.05f));
                for (float t = first; t <= clip.eventTime - .09f; t += clip.sampleStep)
                {
                    Vector3 delta = pivot - clip.SampleRoot(t).position;
                    float forwardError = delta.z - Vector3.Dot(contact - from, forward);
                    float heightError = delta.y - (contact.y - from.y);
                    float speedError = clip.Velocity(t).z - Vector3.Dot(planarSpeed, forward);
                    float cost = forwardError * forwardError * 1.7f
                                 + heightError * heightError * (airborne ? 2.1f : .20f)
                                 + speedError * speedError * .15f
                                 + pose.Distance(clip.ActionPose(t)) * .47f;
                    if (cost < best)
                    {
                        best = cost;
                        selected = i;
                        entry = t;
                    }
                }
            }
            if (selected < 0)
                return false;

            MotionClip chosen = database.Clips[selected];
            var candidate = new TraversalRoute
            {
                obstacle = obstacle, clip = chosen, start = from, contact = contact, end = end,
                entry = entry, pivot = chosen.eventTime, exit = chosen.exitTime
            };
            for (int i = 1; i <= 12; i++)
                if (!IsFree(candidate.Position(Mathf.Lerp(entry, chosen.exitTime, i / 12f)), StandingHeight, obstacle.Volume))
                    return false;

            route = candidate;
            routeTime = entry;
            phase = Phase.Parkour;
            grounded = false;
            crouched = false;
            fallingAnimation = false;
            SetHeight(StandingHeight);
            capsule.stepOffset = 0f;
            transform.rotation = Quaternion.LookRotation(forward);
            Physics.IgnoreCollision(capsule, obstacle.Volume, true);
            BlendTo(selected, entry, .10f);
            return true;
        }

        void AdvanceTraversal(float dt)
        {
            routeTime = Mathf.Min(routeTime + dt, route.exit);
            Vector3 position = route.Position(routeTime);
            capsule.Move(position - transform.position);
            if ((position - transform.position).sqrMagnitude > .16f)
            {
                ReleaseObstacle();
                phase = Phase.Air;
                phaseTimer = 0f;
                verticalSpeed = 0f;
                return;
            }
            if (routeTime < route.exit)
                return;

            LastCompletedTraversal = route.clip.kind;
            LastTraversalClip = route.clip.label;
            ReleaseObstacle();
            grounded = (capsule.Move(Vector3.down * .085f) & CollisionFlags.Below) != 0;
            phase = grounded ? Phase.Ground : Phase.Air;
            verticalSpeed = 0f;
            phaseTimer = searchTimer = 0f;
            planarSpeed = planarAcceleration = Vector3.zero;
        }

        void ReleaseObstacle()
        {
            if (route.obstacle && capsule)
                Physics.IgnoreCollision(capsule, route.obstacle.Volume, false);
            route = default;
        }

        void CutTo(int index, float time, float rate = 1f, AnimationClip overrideClip = null)
        {
            for (int i = 0; i < playback.Length; i++)
                ClearSlot(i);
            current = 0;
            blending = false;
            StartSlot(current, index, time, rate, overrideClip);
            mixer.SetInputWeight(current, 1f);
        }

        void BlendTo(int index, float time, float duration, float rate = 1f, AnimationClip overrideClip = null)
        {
            if (!playback[current].playable.IsValid())
            {
                CutTo(index, time, rate, overrideClip);
                return;
            }

            int old = 1 - current;
            // If interrupted early, keep the currently dominant source pose.
            if (blending && mixer.GetInputWeight(old) > mixer.GetInputWeight(current))
                current = old;
            int next = 1 - current;
            ClearSlot(next);
            StartSlot(next, index, time, rate, overrideClip);
            mixer.SetInputWeight(current, 1f);
            mixer.SetInputWeight(next, 0f);
            current = next;
            blendDuration = duration;
            blendTime = 0f;
            blending = true;
        }

        void StartSlot(int slot, int index, float time, float rate, AnimationClip overrideClip)
        {
            var playable = AnimationClipPlayable.Create(graph, overrideClip ? overrideClip : database.Clips[index].animation);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(0);
            graph.Connect(playable, 0, mixer, slot);
            playback[slot] = new Playback
            {
                index = index, time = time, rate = rate, strideRate = 1f, playable = playable, overrideClip = overrideClip
            };
        }

        void ClearSlot(int slot)
        {
            if (!playback[slot].playable.IsValid())
                return;
            mixer.SetInputWeight(slot, 0f);
            graph.Disconnect(mixer, slot);
            graph.DestroyPlayable(playback[slot].playable);
            playback[slot] = default;
        }

        void Animate(float dt, bool resetPose = false)
        {
            if (blending)
            {
                blendTime += dt;
                float t = Mathf.SmoothStep(0f, 1f, blendTime / blendDuration);
                mixer.SetInputWeight(current, t);
                mixer.SetInputWeight(1 - current, 1f - t);
                if (t >= 1f)
                {
                    ClearSlot(1 - current);
                    blending = false;
                }
            }
            for (int i = 0; i < playback.Length; i++)
            {
                ref Playback slot = ref playback[i];
                if (!slot.playable.IsValid())
                    continue;
                MotionClip clip = database.Clips[slot.index];
                float stride = 1f;
                if (!slot.overrideClip && phase == Phase.Ground && PlanarSpeed > .60f
                    && (clip.kind == MotionKind.Ground || clip.kind == MotionKind.Crouch))
                {
                    Vector2 authored = Flat(clip.Velocity(slot.time));
                    if (authored.sqrMagnitude > 1.0f)
                        stride = Mathf.Clamp(PlanarSpeed / authored.magnitude, .82f, 1.18f);
                }
                slot.strideRate = Mathf.MoveTowards(slot.strideRate, stride, dt * 2.8f);
                float time = slot.time + dt * slot.rate * slot.strideRate;
                if (slot.overrideClip)
                    slot.time = Mathf.Min(time, slot.overrideClip.length);
                else
                    slot.time = clip.loop ? Mathf.Repeat(time, clip.Length) : Mathf.Min(time, clip.Length);
                slot.playable.SetTime(slot.time);
            }
            if (renderedSomersault)
            {
                animator.transform.SetLocalPositionAndRotation(rigBindPosition, rigBindRotation);
                renderedSomersault = false;
            }
            graph.Evaluate(0f);
            // The capsule owns displacement and heading, including traversal warping.
            root.SetLocalPositionAndRotation(bindPosition, bindRotation);
            if (phase == Phase.Dodge)
            {
                RotateSomersault();
                PlantLandingFoot();
            }
            ApplyTorsoInertia(dt);

            Vector3 left = transform.InverseTransformPoint(leftFoot.position);
            Vector3 right = transform.InverseTransformPoint(rightFoot.position);
            pose.leftVelocity = resetPose ? Vector3.zero : Vector3.ClampMagnitude((left - pose.leftFoot) / dt, 15f);
            pose.rightVelocity = resetPose ? Vector3.zero : Vector3.ClampMagnitude((right - pose.rightFoot) / dt, 15f);
            pose.leftFoot = left;
            pose.rightFoot = right;
            pose.hips = transform.InverseTransformPoint(hips.position);
        }

        void ApplyTorsoInertia(float dt)
        {
            if (!lowerSpine || dt <= 0f)
                return;

            bool locomotion = grounded && (phase == Phase.Ground || phase == Phase.Landing);
            Vector3 local = transform.InverseTransformDirection(planarAcceleration);
            Vector2 target = locomotion
                ? new Vector2(Mathf.Clamp(local.z * .43f, -5f, 8f), Mathf.Clamp(local.x * -.5f - yawSpeed * .008f, -7f, 7f))
                : Vector2.zero;
            torsoLean = Vector2.SmoothDamp(torsoLean, target, ref torsoLeanVelocity, .13f, 100f, dt);
            if (!locomotion)
                return;

            Quaternion lean = Quaternion.Euler(torsoLean.x, 0f, torsoLean.y);
            lowerSpine.rotation = transform.rotation * lean * Quaternion.Inverse(transform.rotation) * lowerSpine.rotation;
        }

        void PlantLandingFoot()
        {
            float landingTime = dodgeTime / dodgeDuration - DodgeTouchdown;
            if (landingTime < 0f || !grounded)
                return;

            if (!landingFoot)
            {
                landingFoot = leftFoot.position.y < rightFoot.position.y ? leftFoot : rightFoot;
                landingAnchor = landingFoot.position;
                landingFootRotation = landingFoot.rotation;
            }

            float release = Mathf.InverseLerp(.062f, .146f, landingTime);
            float weight = 1f - Mathf.SmoothStep(0f, 1f, release);
            if (weight <= 0f)
                return;
            Transform knee = landingFoot.parent;
            Transform thigh = knee.parent;
            Vector3 bend = Vector3.ProjectOnPlane(knee.position - thigh.position, landingFoot.position - thigh.position);
            MotionTwoBone.Reach(thigh, knee, landingFoot, Vector3.Lerp(landingFoot.position, landingAnchor, weight), bend);
            landingFoot.rotation = Quaternion.Slerp(landingFoot.rotation, landingFootRotation, weight);
        }

        void RotateSomersault()
        {
            float air = Mathf.InverseLerp(DodgeTakeoff, DodgeTouchdown, dodgeTime / dodgeDuration);
            float rotation = Mathf.InverseLerp(.09f, .90f, air);
            if (rotation <= 0f || rotation >= 1f)
                return;

            // Rotate the animated pose around its pelvis so its center follows the flight path.
            Quaternion turn = Quaternion.AngleAxis(360f * Mathf.SmoothStep(0f, 1f, rotation), Vector3.right);
            Vector3 pivot = transform.InverseTransformPoint(hips.position) + Vector3.up * .08f;
            animator.transform.SetLocalPositionAndRotation(pivot + turn * (rigBindPosition - pivot), turn * rigBindRotation);
            renderedSomersault = true;
        }
    }
}
