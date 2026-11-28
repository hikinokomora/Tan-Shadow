using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using ShadowTan.Player;
using UnityEngine;

namespace TanShadow.Seasons
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerController), typeof(PlayerMotor), typeof(CharacterController))]
    public sealed class BaihuHeroBridge : HeroBridge
    {
        PlayerController controller;
        PlayerMotor motor;
        CharacterController capsule;
        PlayerInputReader input;
        [SerializeField] AutumnPlayerCamera cameraRig;
        bool carried;
        bool controlEnabled = true;

        public override bool IsDodging => controller && controller.StateId == PlayerStateId.Roll;
        public override bool AttackPressed => false;

        void Awake()
        {
            controller = GetComponent<PlayerController>();
            motor = GetComponent<PlayerMotor>();
            capsule = GetComponent<CharacterController>();
            input = GetComponent<PlayerInputReader>();
        }

        void OnEnable() => EventBus.Subscribe<PlayerAttackStarted>(OnAttackStarted);
        void OnDisable() => EventBus.Unsubscribe<PlayerAttackStarted>(OnAttackStarted);

        void OnAttackStarted(PlayerAttackStarted attack)
        {
            if (controlEnabled && !carried && gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                RaiseSwingStarted(attack.Direction);
        }

        public override void SetInputEnabled(bool enabled)
        {
            controlEnabled = enabled;
            controller.SetControlEnabled(enabled && !carried);
            if (input) input.SetInputEnabled(enabled && !carried);
        }

        public override void Teleport(Vector3 position, Quaternion rotation)
        {
            motor.Warp(position);
            transform.rotation = rotation;
            if (cameraRig) cameraRig.ResetView(rotation);
        }

        public override void SetCarried(bool value)
        {
            if (carried == value) return;
            carried = value;
            capsule.enabled = !value;
            controller.SetControlEnabled(controlEnabled && !value);
            if (input) input.SetInputEnabled(controlEnabled && !value);
        }
    }
}
