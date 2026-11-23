using TanShadow.Combat;
using TanShadow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Seasons
{
    // Мост для текущего PlayerController из main (кинжал, рывок, блок).
    // Когда появится новый контроллер героини, его мост заменит этот компонент.
    [RequireComponent(typeof(PlayerController))]
    public sealed class LegacyHeroBridge : HeroBridge
    {
        PlayerController controller;
        AttackExecutor attacks;
        CharacterController body;
        InputAction attackPress;   // своя копия кнопки удара: карта контроллера выключается вместе с ним

        public override bool IsDodging => controller != null && controller.State == PlayerState.Dash;
        public override bool AttackPressed => attackPress != null && attackPress.WasPressedThisFrame();

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            attacks = GetComponent<AttackExecutor>();
            body = GetComponent<CharacterController>();
            if (controller.controls != null) attackPress = controller.controls.FindAction("Player/Attack", true).Clone();
        }

        void OnEnable()
        {
            if (attacks != null) attacks.PhaseChanged += OnAttackPhase;
            attackPress?.Enable();
        }

        void OnDisable()
        {
            if (attacks != null) attacks.PhaseChanged -= OnAttackPhase;
            attackPress?.Disable();
        }

        void OnDestroy() => attackPress?.Dispose();

        void OnAttackPhase(AttackPhase phase)
        {
            // Взмах засчитывается в начале замаха: так отпугивание чувствуется сразу по нажатию.
            if (phase == AttackPhase.Windup) RaiseSwingStarted(transform.forward);
        }

        bool carried;

        public override void SetInputEnabled(bool enabled)
        {
            if (!enabled && attacks != null) attacks.Cancel();
            // Пока героиню несёт колосс, управление не возвращается: физика тела выключена.
            controller.enabled = enabled && !carried;
        }

        public override void Teleport(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = body != null && body.enabled;
            if (body != null) body.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (body != null) body.enabled = wasEnabled;
        }

        public override void SetCarried(bool carried)
        {
            if (carried && attacks != null) attacks.Cancel();
            this.carried = carried;
            controller.enabled = !carried;
            if (body != null) body.enabled = !carried;
        }
    }
}
