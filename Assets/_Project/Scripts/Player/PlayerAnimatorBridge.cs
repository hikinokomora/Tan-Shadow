using System;
using UnityEngine;

namespace ShadowTan.Player
{
    // Связь состояний и Animator. События анимации: HitWindowOpen, HitWindowClose,
    // RollEnd, Footstep, StabImpact (§9.7).
    public class PlayerAnimatorBridge : MonoBehaviour
    {
        [SerializeField] Animator animator;

        [Header("Параметры Animator")]
        [SerializeField] string speedParameter = "Speed";
        [SerializeField] string groundedParameter = "Grounded";
        [SerializeField] string stateParameter = "State";

        [Header("Триггеры")]
        [SerializeField] string rollTrigger = "Roll";
        [SerializeField] string attackTrigger = "Attack";
        [SerializeField] string grabTrigger = "Grab";
        [SerializeField] string stabTrigger = "Stab";

        int speedHash;
        int groundedHash;
        int stateHash;
        int rollHash;
        int attackHash;
        int grabHash;
        int stabHash;

        public event Action HitWindowOpened;
        public event Action HitWindowClosed;
        public event Action RollEnded;
        public event Action FootstepDetected;
        public event Action StabImpacted;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();

            speedHash = Animator.StringToHash(speedParameter);
            groundedHash = Animator.StringToHash(groundedParameter);
            stateHash = Animator.StringToHash(stateParameter);
            rollHash = Animator.StringToHash(rollTrigger);
            attackHash = Animator.StringToHash(attackTrigger);
            grabHash = Animator.StringToHash(grabTrigger);
            stabHash = Animator.StringToHash(stabTrigger);

            WarnIfMissing();
        }

        public void Tick(float planarSpeed, bool grounded)
        {
            if (animator == null) return;
            animator.SetFloat(speedHash, planarSpeed);
            animator.SetBool(groundedHash, grounded);
        }

        public void SetState(PlayerStateId state)
        {
            if (animator == null) return;
            animator.SetInteger(stateHash, (int)state);
        }

        public void TriggerRoll() => Trigger(rollHash);
        public void TriggerAttack() => Trigger(attackHash);
        public void TriggerGrab() => Trigger(grabHash);
        public void TriggerStab() => Trigger(stabHash);

        void Trigger(int hash)
        {
            if (animator != null) animator.SetTrigger(hash);
        }

        // Animation Events из клипов героини.
        public void HitWindowOpen() => HitWindowOpened?.Invoke();
        public void HitWindowClose() => HitWindowClosed?.Invoke();
        public void RollEnd() => RollEnded?.Invoke();
        public void Footstep() => FootstepDetected?.Invoke();
        public void StabImpact() => StabImpacted?.Invoke();

        void WarnIfMissing()
        {
            if (animator == null)
            {
                Debug.LogWarning("PlayerAnimatorBridge: Animator не найден", this);
                return;
            }

            var parameters = animator.parameters;
            WarnParameter(parameters, speedParameter, AnimatorControllerParameterType.Float);
            WarnParameter(parameters, groundedParameter, AnimatorControllerParameterType.Bool);
            WarnParameter(parameters, stateParameter, AnimatorControllerParameterType.Int);
            WarnParameter(parameters, rollTrigger, AnimatorControllerParameterType.Trigger);
            WarnParameter(parameters, attackTrigger, AnimatorControllerParameterType.Trigger);
        }

        void WarnParameter(AnimatorControllerParameter[] parameters, string parameterName, AnimatorControllerParameterType type)
        {
            foreach (var parameter in parameters)
            {
                if (parameter.name == parameterName && parameter.type == type) return;
            }

            Debug.LogWarning($"PlayerAnimatorBridge: в Animator нет параметра {parameterName} ({type})", this);
        }
    }
}
