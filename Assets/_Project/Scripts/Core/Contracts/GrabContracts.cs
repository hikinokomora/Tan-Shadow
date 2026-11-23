using System;
using UnityEngine;

namespace ShadowTan.Core.Contracts
{
    public enum GrabMode
    {
        FlyWithTarget,
        HoldInPlace
    }

    public enum GrabReleaseReason
    {
        Released,
        Shaken,
        TargetUnavailable
    }

    // За что можно зацепиться. Зацеп не поднимает героиню (I-1).
    public interface IGrabTarget
    {
        GrabMode Mode { get; }
        Transform Anchor { get; }
        bool AllowsAttack { get; }
        bool CanGrab(IPlayerQuery player);
        void OnGrabbed();
        void OnReleased();

        // Колосс может стряхнуть героиню в любой момент (§9.6.5).
        event Action Shaken;
    }
}
