using ShadowTan.Core.Contracts;
using UnityEngine;

namespace ShadowTan.Core.Events
{
    public readonly struct PlayerSpawned
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public PlayerSpawned(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public readonly struct PlayerDespawned
    {
    }

    public readonly struct PlayerStateChanged
    {
        public readonly string From;
        public readonly string To;

        public PlayerStateChanged(string from, string to)
        {
            From = from;
            To = to;
        }
    }

    public readonly struct PlayerRolled
    {
        public readonly Vector3 Direction;

        public PlayerRolled(Vector3 direction)
        {
            Direction = direction;
        }
    }

    public readonly struct PlayerAttackStarted
    {
        public readonly Vector3 Direction;
        public readonly bool WhileHanging;

        public PlayerAttackStarted(Vector3 direction, bool whileHanging)
        {
            Direction = direction;
            WhileHanging = whileHanging;
        }
    }

    public readonly struct PlayerGrabbed
    {
        public readonly IGrabTarget Target;
        public readonly GrabMode Mode;

        public PlayerGrabbed(IGrabTarget target, GrabMode mode)
        {
            Target = target;
            Mode = mode;
        }
    }

    public readonly struct PlayerReleased
    {
        public readonly IGrabTarget Target;
        public readonly GrabReleaseReason Reason;

        public PlayerReleased(IGrabTarget target, GrabReleaseReason reason)
        {
            Target = target;
            Reason = reason;
        }
    }

    public readonly struct PlayerFell
    {
        public readonly float Height;

        public PlayerFell(float height)
        {
            Height = height;
        }
    }

    public readonly struct PlayerKilled
    {
        public readonly DeathCause Cause;
        public readonly IKillSource Source;

        public PlayerKilled(DeathCause cause, IKillSource source)
        {
            Cause = cause;
            Source = source;
        }
    }
}
