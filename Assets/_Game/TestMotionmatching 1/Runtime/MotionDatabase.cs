using System;
using UnityEngine;

namespace TanShadow.MotionMatchingLab
{
    public enum MotionKind { Ground, Crouch, Jump, Fall, Land, Vault, Hurdle, Mantle, Climb }
    public enum Gait { Idle, Walk, Run, Sprint, Crouch }

    [Serializable]
    public struct MotionPose
    {
        public Vector3 hips, leftFoot, rightFoot, leftVelocity, rightVelocity;

        public float Distance(MotionPose other)
        {
            return (hips - other.hips).sqrMagnitude * 4f
                 + ((leftFoot - other.leftFoot).sqrMagnitude + (rightFoot - other.rightFoot).sqrMagnitude) * 3f
                 + ((leftVelocity - other.leftVelocity).sqrMagnitude + (rightVelocity - other.rightVelocity).sqrMagnitude) * .055f;
        }
    }

    [Serializable]
    public struct MotionTrajectory
    {
        public const float Near = .20f;
        public const float Mid = .40f;
        public const float Far = .65f;
        public Vector2 near, mid, far, facingNear, facingFar;

        public float Distance(MotionTrajectory other)
        {
            return (near - other.near).sqrMagnitude * 3.4f
                 + (mid - other.mid).sqrMagnitude * 1.9f
                 + (far - other.far).sqrMagnitude * .95f
                 + (facingNear - other.facingNear).sqrMagnitude * .55f
                 + (facingFar - other.facingFar).sqrMagnitude * .4f;
        }
    }

    [Serializable]
    public struct MotionFrame
    {
        public int clip;
        public float time;
        public MotionPose pose;
        public MotionTrajectory trajectory;
        public Vector2 velocity;
    }

    [Serializable]
    public struct MotionRoot
    {
        public Vector3 position;
        public float yaw;
    }

    [Serializable]
    public sealed class MotionClip
    {
        public string label;
        public AnimationClip animation;
        public MotionKind kind;
        public Gait gait;
        public bool loop, catchFromAir, heavy;
        public float sampleStep, eventTime, exitTime, peakHeight;
        public MotionRoot[] root;
        public MotionPose[] actionPoses;

        public float Length => animation.length;

        public MotionRoot SampleRoot(float time)
        {
            if (!loop)
                return SampleWithin(time);

            int cycles = Mathf.FloorToInt(time / Length);
            MotionRoot pose = SampleWithin(time - cycles * Length);
            // Locomotion loops are accepted only when their cycle has negligible turning.
            pose.position += (root[root.Length - 1].position - root[0].position) * cycles;
            return pose;
        }

        MotionRoot SampleWithin(float time)
        {
            float frame = Mathf.Clamp(time / sampleStep, 0f, root.Length - 1);
            int i = Mathf.Min((int)frame, root.Length - 2);
            return new MotionRoot
            {
                position = Vector3.LerpUnclamped(root[i].position, root[i + 1].position, frame - i),
                yaw = Mathf.LerpAngle(root[i].yaw, root[i + 1].yaw, frame - i)
            };
        }

        public MotionPose ActionPose(float time)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(time / sampleStep), 0, actionPoses.Length - 1);
            return actionPoses[index];
        }

        public Vector3 Velocity(float time)
        {
            float before = loop ? time - sampleStep : Mathf.Max(0f, time - sampleStep);
            float after = loop ? time + sampleStep : Mathf.Min(Length, time + sampleStep);
            return (SampleRoot(after).position - SampleRoot(before).position) / Mathf.Max(.001f, after - before);
        }
    }

    [PreferBinarySerialization]
    public sealed class MotionDatabase : ScriptableObject
    {
        [SerializeField] MotionClip[] clips;
        [SerializeField] MotionFrame[] frames;
        [SerializeField] int idleClip;

        public MotionClip[] Clips => clips;
        public MotionFrame[] Frames => frames;
        public int IdleClip => idleClip;

        public void SetData(MotionClip[] sourceClips, MotionFrame[] sourceFrames, int idle)
        {
            clips = sourceClips;
            frames = sourceFrames;
            idleClip = idle;
        }

        public int Match(MotionKind kind, Gait gait, MotionPose pose, MotionTrajectory trajectory,
            Vector2 velocity, int playingClip, float playingTime)
        {
            int best = -1;
            float cost = float.PositiveInfinity;
            bool settled = (gait == Gait.Idle || gait == Gait.Crouch)
                           && velocity.sqrMagnitude < .04f && trajectory.far.sqrMagnitude < .025f
                           && trajectory.facingFar.y > .985f;
            for (int i = 0; i < frames.Length; i++)
            {
                ref readonly MotionFrame frame = ref frames[i];
                MotionClip clip = clips[frame.clip];
                if (clip.kind != kind)
                    continue;
                if (settled && (!clip.label.Contains("_Idle_", StringComparison.Ordinal)
                    || clip.label.Contains("_turn_", StringComparison.OrdinalIgnoreCase)))
                    continue;

                float continuity = frame.clip == playingClip && Mathf.Abs(frame.time - playingTime) < .14f ? -.11f : 0f;
                float gaitCost = clip.gait == gait ? 0f : (gait == Gait.Idle && velocity.sqrMagnitude < .12f
                    ? .53f : (gait == Gait.Sprint ? .23f : .14f));
                float candidate = trajectory.Distance(frame.trajectory)
                                + pose.Distance(frame.pose) * .74f
                                + (velocity - frame.velocity).sqrMagnitude * .15f
                                + gaitCost + continuity;
                if (candidate < cost)
                {
                    cost = candidate;
                    best = i;
                }
            }
            return best;
        }
    }
}
