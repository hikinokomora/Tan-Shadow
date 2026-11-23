using System;
using ShadowTan.Core.Contracts;
using UnityEngine;

namespace ShadowTan.Tests.Manual
{
    // Тестовая цель зацепа для песочницы. Не двигается и не поднимает героиню:
    // зацеп по правилам I-1 только удерживает или переносит вместе с колоссом.
    public sealed class TestGrabPoint : MonoBehaviour, IGrabTarget
    {
        public GrabMode Mode => GrabMode.FlyWithTarget;
        public Transform Anchor => transform;
        public bool AllowsAttack => true;
        public event Action Shaken;

        public bool CanGrab(IPlayerQuery player) => true;

        public void OnGrabbed() => Debug.Log($"{name}: OnGrabbed", this);

        public void OnReleased() => Debug.Log($"{name}: OnReleased", this);

        [ContextMenu("Shake")]
        void Shake() => Shaken?.Invoke();
    }
}
