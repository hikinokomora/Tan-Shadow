using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Combat
{
    // Тряска камеры и вибрация геймпада на ударах по игроку и его попаданиях.
    [RequireComponent(typeof(Combatant), typeof(CinemachineImpulseSource))]
    public class CombatFeedback : MonoBehaviour
    {
        Combatant combatant;
        CinemachineImpulseSource impulse;
        float rumbleUntil = -1f;

        CombatSettings Settings => combatant.settings;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            impulse = GetComponent<CinemachineImpulseSource>();
            combatant.Hurt += OnHurt;
            combatant.AttackLanded += OnAttackLanded;
        }

        void OnHurt(HitInfo hit)
        {
            switch (hit.result)
            {
                case HitResult.Deflected: Play(Settings.deflectShake, Settings.deflectRumble); break;
                case HitResult.Blocked: Play(Settings.blockShake, Settings.blockRumble); break;
                case HitResult.Hit: Play(Settings.hurtShake, Settings.hurtRumble); break;
            }
        }

        void OnAttackLanded(HitInfo hit)
        {
            if (hit.result == HitResult.Hit) impulse.GenerateImpulseWithForce(Settings.landShake);
        }

        void Play(float shake, Rumble rumble)
        {
            if (shake > 0f) impulse.GenerateImpulseWithForce(shake);

            var pad = Gamepad.current;
            if (pad == null || rumble.duration <= 0f) return;
            pad.SetMotorSpeeds(rumble.low, rumble.high);
            rumbleUntil = Time.unscaledTime + rumble.duration;
        }

        void Update()
        {
            if (rumbleUntil < 0f || Time.unscaledTime < rumbleUntil) return;
            StopRumble();
        }

        void OnDisable() => StopRumble();

        void StopRumble()
        {
            rumbleUntil = -1f;
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }
    }
}
