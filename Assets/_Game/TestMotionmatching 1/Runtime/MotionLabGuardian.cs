using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace TanShadow.MotionMatchingLab
{
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public sealed class MotionLabGuardian : MonoBehaviour
    {
        public enum Clip { Idle, Walk, Run, LeftSlash, RightSlash, JumpToBar, Hang, Stab, Dive, Rise }
        enum State { Ground, Attack, Align, Jump, Hang, Stab, Dive, Rise }

        [Serializable]
        public struct Travel
        {
            public Vector3[] samples;
            public float duration;

            public Vector3 At(float time)
            {
                if (samples == null || samples.Length == 0)
                    return Vector3.zero;
                float frame = Mathf.Clamp01(time / duration) * (samples.Length - 1);
                int i = Mathf.FloorToInt(frame);
                return Vector3.Lerp(samples[i], samples[Mathf.Min(i + 1, samples.Length - 1)], frame - i);
            }
        }

        const int ClipCount = (int)Clip.Rise + 1;
        [SerializeField] Animator visual;
        [SerializeField] Transform visualOffset, visualRoot, takeoff;
        [SerializeField] AnimationClip[] clips;
        [SerializeField] Travel jump, dive, rise;

        readonly float[] weights = new float[ClipCount];
        readonly float[] startWeights = new float[ClipCount];
        readonly double[] times = new double[ClipCount];
        readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[ClipCount];

        CharacterController capsule;
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        Vector3 visualBind, rootBind, spawn, journeyStart, alignFrom, velocity, acceleration;
        Quaternion alignRotation;
        State state;
        Clip active;
        bool advanceStride;
        float time, blendTime, blendLength, pace = 1f, alignDuration, verticalSpeed, yawSpeed;

        public bool Ready => graph.IsValid();
        public bool InputEnabled { get; set; } = true;
        public Transform Takeoff => takeoff;
        public string Status => state.ToString();
        public string ClipName => Ready ? clips[(int)active].name : string.Empty;

        public void Configure(Animator animator, Transform holder, Transform skeletonRoot, Transform launch,
            AnimationClip[] motions, Travel jumpPath, Travel divePath, Travel risePath)
        {
            visual = animator;
            visualOffset = holder;
            visualRoot = skeletonRoot;
            takeoff = launch;
            clips = motions;
            jump = jumpPath;
            dive = divePath;
            rise = risePath;
        }

        void OnEnable()
        {
            if (!visual || !visualOffset || !visualRoot || !takeoff || clips == null || clips.Length != ClipCount
                || Array.Exists(clips, clip => !clip))
                return;

            capsule = GetComponent<CharacterController>();
            spawn = transform.position;
            visualBind = visualOffset.localPosition;
            rootBind = transform.InverseTransformPoint(visualRoot.position);
            visual.applyRootMotion = false;
            visual.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            graph = PlayableGraph.Create("Guardian animations");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, ClipCount);
            for (int i = 0; i < ClipCount; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                playables[i].SetSpeed(0d);
                graph.Connect(playables[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Guardian", visual).SetSourcePlayable(mixer);
            graph.Play();
            ResetPose();
        }

        void OnDisable()
        {
            if (graph.IsValid())
                graph.Destroy();
            if (visualOffset)
                visualOffset.localPosition = visualBind;
        }

        void Update()
        {
            if (!InputEnabled || !Ready)
                return;
            Keyboard key = Keyboard.current;
            Mouse mouse = Mouse.current;
            Vector2 move = key == null ? Vector2.zero : new Vector2(
                (key.dKey.isPressed ? 1f : 0f) - (key.aKey.isPressed ? 1f : 0f),
                (key.wKey.isPressed ? 1f : 0f) - (key.sKey.isPressed ? 1f : 0f));
            Tick(move, key != null && (key.leftAltKey.isPressed || key.rightAltKey.isPressed),
                key != null && (key.leftShiftKey.isPressed || key.rightShiftKey.isPressed),
                mouse != null && mouse.leftButton.wasPressedThisFrame,
                mouse != null && mouse.rightButton.wasPressedThisFrame,
                key != null && key.eKey.wasPressedThisFrame,
                key != null && key.spaceKey.wasPressedThisFrame, Time.deltaTime);
        }

        public void Tick(Vector2 move, bool walk, bool sprint, bool left, bool right, bool grab, bool divePressed, float deltaTime)
        {
            if (!Ready || deltaTime <= 0)
                return;
            float dt = Mathf.Min(deltaTime, .05f);
            switch (state)
            {
                case State.Ground:
                    Locomote(move, walk, sprint, left, right, grab, divePressed, dt);
                    break;
                case State.Attack:
                    if (Ended(dt)) Begin(State.Ground, Clip.Idle, .105f);
                    break;
                case State.Align:
                    Align(dt);
                    break;
                case State.Jump:
                    AdvanceTravel(jump, State.Hang, Clip.Hang, .075f, dt);
                    break;
                case State.Hang:
                    if (right) Begin(State.Stab, Clip.Stab, .07f);
                    break;
                case State.Stab:
                    if (Ended(dt)) Begin(State.Hang, Clip.Hang, .08f);
                    break;
                case State.Dive:
                    AdvanceTravel(dive, State.Rise, Clip.Rise, .065f, dt);
                    break;
                case State.Rise:
                    AdvanceTravel(rise, State.Ground, Clip.Idle, .105f, dt);
                    break;
            }
            Evaluate(dt);
        }

        void ResetPose()
        {
            state = State.Ground;
            active = Clip.Idle;
            time = verticalSpeed = yawSpeed = 0f;
            advanceStride = false;
            velocity = acceleration = Vector3.zero;
            pace = 1f;
            Array.Clear(weights, 0, ClipCount);
            Array.Clear(times, 0, ClipCount);
            weights[0] = 1f;
            blendLength = blendTime = 0f;
            Evaluate(0f);
        }

        void Locomote(Vector2 input, bool walk, bool sprint, bool left, bool right, bool grab, bool divePressed, float dt)
        {
            if (transform.position.y < -5f)
            {
                capsule.enabled = false;
                transform.SetPositionAndRotation(spawn, takeoff.rotation);
                capsule.enabled = true;
                ResetPose();
            }
            if (capsule.isGrounded)
            {
                if (grab && NearTakeoff())
                {
                    StopMovement();
                    alignFrom = transform.position;
                    alignRotation = transform.rotation;
                    alignDuration = Mathf.Lerp(.09f, .20f, Quaternion.Angle(alignRotation, takeoff.rotation) / 180f);
                    Begin(State.Align, Clip.JumpToBar, .085f);
                    return;
                }
                if (divePressed)
                {
                    StopMovement();
                    journeyStart = transform.position;
                    Begin(State.Dive, Clip.Dive, .085f);
                    return;
                }
                if (left || right)
                {
                    StopMovement();
                    Begin(State.Attack, right ? Clip.RightSlash : Clip.LeftSlash, .075f);
                    return;
                }
            }

            Vector3 wish = Vector3.ClampMagnitude(new Vector3(input.x, 0f, input.y), 1f);
            bool moving = wish.sqrMagnitude > .001f;
            bool walking = walk && !sprint;
            float speed = sprint ? 6.45f : walking ? 1.88f : 4.70f;
            if (moving)
            {
                float desiredYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, desiredYaw, ref yawSpeed, .085f, 720f, dt);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                velocity = Vector3.SmoothDamp(velocity, wish * speed, ref acceleration, .115f, 36f, dt);
            }
            else
            {
                velocity = acceleration = Vector3.zero;
                yawSpeed = 0f;
            }
            verticalSpeed = capsule.isGrounded ? -2f : verticalSpeed - 18f * dt;
            Vector3 from = transform.position;
            CollisionFlags contact = capsule.Move((velocity + Vector3.up * verticalSpeed) * dt);
            Vector3 moved = (transform.position - from) / dt;
            moved.y = 0f;
            if ((contact & CollisionFlags.Sides) != 0)
                velocity = moved;

            advanceStride = moving && moved.magnitude >= (active == Clip.Idle ? .30f : .20f);
            Clip next = !advanceStride ? Clip.Idle : walking ? Clip.Walk : Clip.Run;
            float strideSpeed = next == Clip.Walk ? 1.88f : 4.70f;
            float targetRate = next == Clip.Idle ? 1f : Mathf.Clamp(moved.magnitude / strideSpeed, .5f, 1.45f);
            pace = Mathf.Lerp(pace, targetRate, 1f - Mathf.Exp(-18f * dt));
            if (active != next)
            {
                bool gaitSwitch = active != Clip.Idle && next != Clip.Idle;
                float stride = gaitSwitch ? Mathf.Repeat((float)times[(int)active] / clips[(int)active].length * 4f, 1f) : 0f;
                Play(next, gaitSwitch ? .13f : next == Clip.Idle ? .08f : .10f, clips[(int)next].length * stride / 4f);
            }
        }

        bool NearTakeoff()
        {
            Vector3 delta = takeoff.position - transform.position;
            return new Vector2(delta.x, delta.z).sqrMagnitude <= .82f * .82f && Mathf.Abs(delta.y) < .24f;
        }

        void StopMovement()
        {
            velocity = acceleration = Vector3.zero;
            verticalSpeed = yawSpeed = 0f;
            advanceStride = false;
        }

        void Align(float dt)
        {
            time += dt;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / alignDuration));
            Vector3 position = Vector3.Lerp(alignFrom, takeoff.position, blend);
            capsule.Move(position - transform.position);
            transform.rotation = Quaternion.Slerp(alignRotation, takeoff.rotation, blend);
            if (time < alignDuration)
                return;
            if (Vector3.Distance(transform.position, takeoff.position) > .095f)
            {
                Begin(State.Ground, Clip.Idle, .10f);
                return;
            }
            journeyStart = transform.position;
            time = 0f;
            state = State.Jump;
        }

        void AdvanceTravel(Travel travel, State nextState, Clip nextClip, float blend, float dt)
        {
            float before = time;
            float length = clips[(int)active].length;
            time = Mathf.Min(time + dt, length);
            Vector3 next = journeyStart + transform.rotation * travel.At(time);
            CollisionFlags hit = capsule.Move(next - transform.position);
            if (state == State.Jump && (hit & (CollisionFlags.Sides | CollisionFlags.Above)) != 0
                && Vector3.Distance(transform.position, next) > .075f)
            {
                Begin(State.Ground, Clip.Idle, .10f);
                return;
            }
            times[(int)active] = before;
            if (time >= length - .001f)
            {
                journeyStart = transform.position;
                Begin(nextState, nextClip, blend);
            }
        }

        bool Ended(float dt)
        {
            time += dt;
            return time >= clips[(int)active].length - .005f;
        }

        void Begin(State next, Clip clip, float blend)
        {
            state = next;
            time = 0f;
            Play(clip, blend, 0f);
        }

        void Play(Clip clip, float duration, float offset)
        {
            Array.Copy(weights, startWeights, ClipCount);
            active = clip;
            if (weights[(int)clip] < .08f)
                times[(int)clip] = offset;
            blendLength = duration;
            blendTime = 0f;
        }

        void Evaluate(float dt)
        {
            blendTime += dt;
            float blend = blendLength <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blendTime / blendLength));
            for (int i = 0; i < ClipCount; i++)
            {
                bool target = i == (int)active;
                weights[i] = Mathf.Lerp(startWeights[i], target ? 1f : 0f, blend);
                mixer.SetInputWeight(i, weights[i]);
                if (weights[i] <= .0001f)
                    continue;
                Clip clip = (Clip)i;
                bool gait = clip is Clip.Walk or Clip.Run;
                bool loop = gait || clip is Clip.Idle or Clip.Hang;
                if (!(target && state == State.Align) && (!gait || advanceStride))
                    times[i] += dt * (gait ? pace : 1f);
                times[i] = loop ? times[i] % clips[i].length : Math.Min(times[i], clips[i].length);
                playables[i].SetTime(times[i]);
            }
            visualOffset.localPosition = visualBind;
            graph.Evaluate(0f);
            // More than one top-level bone carries the translation in the FBX.
            // Cancel it on their shared model parent so no skinned branches can separate.
            visualOffset.localPosition = visualBind + rootBind - transform.InverseTransformPoint(visualRoot.position);
        }
    }
}
