using TanShadow.Combat;
using TanShadow.Player;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Мост для текущего PlayerController из main (кинжал, рывок, блок).
    // Когда появится новый контроллер героини, его мост заменит этот компонент.
    [RequireComponent(typeof(PlayerController))]
    public sealed class LegacyHeroBridge : HeroBridge
    {
        PlayerController controller;
        AttackExecutor attacks;
        CharacterController body;

        public override bool IsDodging => controller != null && controller.State == PlayerState.Dash;

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            attacks = GetComponent<AttackExecutor>();
            body = GetComponent<CharacterController>();
        }

        void OnEnable()
        {
            if (attacks != null) attacks.PhaseChanged += OnAttackPhase;
        }

        void OnDisable()
        {
            if (attacks != null) attacks.PhaseChanged -= OnAttackPhase;
        }

        void OnAttackPhase(AttackPhase phase)
        {
            // Взмах засчитывается в начале замаха: так отпугивание чувствуется сразу по нажатию.
            if (phase == AttackPhase.Windup) RaiseSwingStarted(transform.forward);
        }

        public override void SetInputEnabled(bool enabled)
        {
            if (!enabled && attacks != null) attacks.Cancel();
            controller.enabled = enabled;
        }

        public override void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = body != null && body.enabled;
            if (body != null) body.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (body != null) body.enabled = wasEnabled;
        }
    }
}
