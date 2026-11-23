using UnityEngine;

namespace ShadowTan.Player
{
    // Animation Events приходят на Visual, а PlayerAnimatorBridge находится на корне героини.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerAnimationEvents : MonoBehaviour
    {
        [SerializeField] PlayerAnimatorBridge bridge;

        public void HitWindowOpen() => bridge?.HitWindowOpen();
        public void HitWindowClose() => bridge?.HitWindowClose();
        public void RollEnd() => bridge?.RollEnd();
        public void Footstep() => bridge?.Footstep();
        public void StabImpact() => bridge?.StabImpact();
    }
}
