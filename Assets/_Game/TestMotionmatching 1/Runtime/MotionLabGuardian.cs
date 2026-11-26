using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TanShadow.MotionMatchingLab
{
    [DisallowMultipleComponent]
    public sealed class MotionLabGuardian : MonoBehaviour
    {
        public enum Clip { Idle, Walk, Run, LeftSlash, RightSlash, JumpToBar, Hang, Stab, Dive, Rise }
        enum Mode { Locomotion, Attack, Align, Jump, Hang, Stab, Dive, Rise }

        [Serializable]
        public struct Travel
        {
            public Vector3[] samples;
            public float duration;

            public Vector3 At(float time)
            {
                if (samples == null || samples.Length == 0)
                    return Vector3.zero;
                float frame = Mathf.Clamp01(time / Mathf.Max(.01f, duration)) * (samples.Length - 1);
                int i = Mathf.FloorToInt(frame);
                return Vector3.Lerp(samples[i], samples[Mathf.Min(i + 1, samples.Length - 1)], frame - i);
            }
        }

        [Serializable]
        public struct DonorBone
        {
            public Transform source, target;
            public Quaternion sourceRestInverse, targetRest;
            public Vector3 sourcePosition, targetPosition;
            public bool transferPosition;
        }

        [SerializeField] MotionMatchingPlayer motor;
        [SerializeField] Animator visual;
        [SerializeField] Transform visualRoot;
        [SerializeField] Transform takeoff;
        [SerializeField] AnimationClip[] clips;
        [SerializeField] Travel jump, dive, rise;
        [SerializeField] DonorBone[] donorBones;

        readonly float[] weights = new float[10];
        readonly float[] startWeights = new float[10];
        readonly double[] times = new double[10];
        readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[10];
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        CharacterController capsule;
        Vector3 rootBind, actionOrigin, alignmentStart;
        Quaternion rootRotation, alignmentRotation;
        Clip active;
        Mode mode;
        float modeTime, blendTime, blendLength, pace, alignmentDuration, donorWeight;
        float attackBuffer;
        Clip bufferedAttack;
        bool walking, sprinting;

        public bool Ready => graph.IsValid();
        public Transform Takeoff => takeoff;
        public string Status => mode == Mode.Locomotion ? motor.CurrentState : mode.ToString();
        public string ClipName => !Ready ? string.Empty
            : mode == Mode.Locomotion && donorWeight > .5f ? motor.CurrentClip : clips[(int)active].name;

        public void Configure(MotionMatchingPlayer source, Animator target, Transform skeletonRoot,
            Transform spot, AnimationClip[] motions, Travel jumpPath, Travel divePath, Travel risePath,
            DonorBone[] legacyBindings)
        {
            motor = source;
            visual = target;
            visualRoot = skeletonRoot;
            takeoff = spot;
            clips = motions;
            jump = jumpPath;
            dive = divePath;
            rise = risePath;
            donorBones = legacyBindings;
        }

        void OnEnable()
        {
            if (!motor || !visual || !visualRoot || !takeoff || clips == null || clips.Length != playables.Length
                || Array.Exists(clips, clip => !clip))
                return;

            capsule = motor.GetComponent<CharacterController>();
            rootBind = visualRoot.localPosition;
            rootRotation = visualRoot.localRotation;
            visual.applyRootMotion = false;
            visual.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Guardian Motion Lab");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
                playables[i].SetApplyFootIK(false);
                playables[i].SetSpeed(0d);
                graph.Connect(playables[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Guardian", visual).SetSourcePlayable(mixer);
            graph.Play();
            ResetVisual();
        }

        void OnDisable()
        {
            if (graph.IsValid())
                graph.Destroy();
        }

        public void ResetVisual()
        {
            if (!Ready)
                return;
            mode = Mode.Locomotion;
            active = Clip.Idle;
            modeTime = attackBuffer = donorWeight = 0f;
            pace = 1f;
            Array.Clear(weights, 0, weights.Length);
            Array.Clear(times, 0, times.Length);
            weights[0] = 1f;
            blendLength = blendTime = 0f;
            Evaluate(0f);
        }

        public void Tick(MotionInput movement, bool left, bool right, bool ledge, bool diving, float deltaTime)
        {
            if (!Ready || deltaTime <= 0f)
                return;
            float dt = Mathf.Min(deltaTime, .05f);
            attackBuffer = Mathf.Max(0f, attackBuffer - dt);
            switch (mode)
            {
                case Mode.Locomotion:
                    Locomote(movement, left, right, ledge, diving, dt);
                    break;
                case Mode.Align:
                    Align(dt);
                    break;
                case Mode.Jump:
                    AdvanceTravel(jump, Mode.Hang, Clip.Hang, .075f, dt);
                    break;
                case Mode.Hang:
                    if (ledge)
                        Release();
                    else if (right)
                        Begin(Mode.Stab, Clip.Stab, .065f);
                    break;
                case Mode.Stab:
                    if (ledge)
                        Release();
                    else if (Ends(dt))
                        Begin(Mode.Hang, Clip.Hang, .075f);
                    break;
                case Mode.Attack:
                    if (left || right)
                    {
                        bufferedAttack = right ? Clip.RightSlash : Clip.LeftSlash;
                        attackBuffer = .16f;
                    }
                    if (Ends(dt))
                        FinishAttack();
                    break;
                case Mode.Dive:
                    AdvanceTravel(dive, Mode.Rise, Clip.Rise, .065f, dt);
                    break;
                case Mode.Rise:
                    AdvanceTravel(rise, Mode.Locomotion, Clip.Idle, .12f, dt);
                    break;
            }
            Evaluate(dt);
        }

        void Locomote(MotionInput movement, bool left, bool right, bool ledge, bool diving, float dt)
        {
            walking = movement.walk;
            sprinting = movement.sprint;
            if (motor.IsGrounded && (motor.CurrentState == "Ground" || motor.CurrentState == "Crouch"))
            {
                if (ledge && NearTakeoff())
                {
                    alignmentStart = transform.position;
                    alignmentRotation = transform.rotation;
                    alignmentDuration = Mathf.Lerp(.105f, .22f, Quaternion.Angle(alignmentRotation, takeoff.rotation) / 180f);
                    Begin(Mode.Align, Clip.JumpToBar, .10f);
                    return;
                }
                if (diving)
                {
                    actionOrigin = transform.position;
                    Begin(Mode.Dive, Clip.Dive, .085f);
                    return;
                }
                if (left || right || attackBuffer > 0f)
                {
                    Begin(Mode.Attack, right ? Clip.RightSlash : left ? Clip.LeftSlash : bufferedAttack, .075f);
                    attackBuffer = 0f;
                    return;
                }
            }
            motor.Simulate(movement, dt);
            float speed = motor.PlanarSpeed;
            Clip motion = speed < (active == Clip.Idle ? .31f : .21f) ? Clip.Idle
                : walking && !sprinting ? Clip.Walk : Clip.Run;
            float nominalSpeed = motion == Clip.Walk ? 1.88f : 4.70f;
            float targetPace = motion == Clip.Idle ? 1f : Mathf.Clamp(speed / nominalSpeed, .53f, 1.52f);
            pace = Mathf.Lerp(pace, targetPace, 1f - Mathf.Exp(-17f * dt));
            if (motion != active)
            {
                bool gaitChange = (active is Clip.Run or Clip.Walk) && (motion is Clip.Run or Clip.Walk);
                float strideOffset = gaitChange ? StridePhase(active, times[(int)active]) : 0f;
                Play(motion, gaitChange ? .13f : .105f, GaitTime(motion, strideOffset));
            }
        }

        float StridePhase(Clip clip, double time)
        {
            return Mathf.Repeat((float)time / clips[(int)clip].length * 4f, 1f);
        }

        float GaitTime(Clip clip, float phase) => clip is Clip.Run or Clip.Walk ? clips[(int)clip].length * phase / 4f : 0f;

        bool NearTakeoff()
        {
            Vector3 delta = takeoff.position - transform.position;
            return new Vector2(delta.x, delta.z).sqrMagnitude <= .82f * .82f && Mathf.Abs(delta.y) < .28f;
        }

        void Align(float dt)
        {
            modeTime += dt;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(modeTime / alignmentDuration));
            Vector3 goal = Vector3.Lerp(alignmentStart, takeoff.position, t);
            capsule.Move(goal - transform.position);
            transform.rotation = Quaternion.Slerp(alignmentRotation, takeoff.rotation, t);
            if (modeTime < alignmentDuration)
                return;
            if ((transform.position - takeoff.position).sqrMagnitude > .12f * .12f)
            {
                Release();
                return;
            }
            actionOrigin = transform.position;
            mode = Mode.Jump;
            modeTime = 0f;
        }

        void AdvanceTravel(Travel route, Mode next, Clip nextClip, float blend, float dt)
        {
            float startTime = modeTime;
            modeTime = Mathf.Min(modeTime + dt, clips[(int)active].length);
            Vector3 goal = actionOrigin + transform.rotation * route.At(modeTime);
            CollisionFlags collision = capsule.Move(goal - transform.position);
            if (mode == Mode.Jump && (collision & (CollisionFlags.Above | CollisionFlags.Sides)) != 0
                && Vector3.Distance(goal, transform.position) > .08f)
            {
                Release();
                return;
            }
            times[(int)active] = startTime;
            if (modeTime < clips[(int)active].length - .001f)
                return;
            if (next == Mode.Locomotion)
            {
                Release();
                return;
            }
            actionOrigin = transform.position;
            Begin(next, nextClip, blend);
        }

        bool Ends(float dt)
        {
            modeTime += dt;
            return modeTime >= clips[(int)active].length - .005f;
        }

        void FinishAttack()
        {
            if (attackBuffer > 0f && bufferedAttack != active)
            {
                Begin(Mode.Attack, bufferedAttack, .09f);
                attackBuffer = 0f;
            }
            else
                Release();
        }

        void Release()
        {
            motor.ResetTo(transform.position, transform.rotation);
            Begin(Mode.Locomotion, Clip.Idle, .105f);
        }

        void Begin(Mode next, Clip clip, float blend)
        {
            mode = next;
            modeTime = 0f;
            Play(clip, blend, 0f);
        }

        void Play(Clip clip, float blend, float phase)
        {
            Array.Copy(weights, startWeights, weights.Length);
            active = clip;
            int slot = (int)clip;
            if (weights[slot] < .08f)
                times[slot] = phase;
            blendLength = blend;
            blendTime = 0f;
        }

        void Evaluate(float dt)
        {
            blendTime += dt;
            float t = blendLength <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blendTime / blendLength));
            for (int i = 0; i < weights.Length; i++)
            {
                bool target = i == (int)active;
                weights[i] = Mathf.Lerp(startWeights[i], target ? 1f : 0f, t);
                mixer.SetInputWeight(i, weights[i]);
                if (weights[i] <= 0.0001f)
                    continue;

                Clip clip = (Clip)i;
                bool loop = clip is Clip.Idle or Clip.Walk or Clip.Run or Clip.Hang;
                float adjustment = clip is Clip.Walk or Clip.Run ? pace : 1f;
                if (!(target && mode == Mode.Align))
                    times[i] += dt * adjustment;
                times[i] = loop ? times[i] % clips[i].length : Math.Min(times[i], clips[i].length);
                playables[i].SetTime(times[i]);
            }
            graph.Evaluate(0f);
            visualRoot.SetLocalPositionAndRotation(rootBind, rootRotation);
            ApplyExistingTraversalPose(dt);
        }

        void ApplyExistingTraversalPose(float dt)
        {
            if (donorBones == null || donorBones.Length == 0)
                return;
            bool useDonor = mode == Mode.Locomotion && motor.CurrentState != "Ground";
            donorWeight = Mathf.MoveTowards(donorWeight, useDonor ? 1f : 0f, dt * 9f);
            if (donorWeight <= 0f)
                return;
            float blend = Mathf.SmoothStep(0f, 1f, donorWeight);
            Quaternion actor = transform.rotation;
            Quaternion inverse = Quaternion.Inverse(actor);
            foreach (DonorBone bone in donorBones)
            {
                if (!bone.source || !bone.target)
                    continue;
                if (bone.transferPosition)
                {
                    Vector3 delta = transform.InverseTransformPoint(bone.source.position) - bone.sourcePosition;
                    Vector3 position = transform.TransformPoint(bone.targetPosition + delta * .96f);
                    bone.target.position = Vector3.Lerp(bone.target.position, position, blend);
                }
                Quaternion pose = actor * (inverse * bone.source.rotation * bone.sourceRestInverse) * bone.targetRest;
                bone.target.rotation = Quaternion.Slerp(bone.target.rotation, pose, blend);
            }
        }
    }
}
