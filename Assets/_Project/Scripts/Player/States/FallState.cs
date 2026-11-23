using ShadowTan.Core.Contracts;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    public sealed class FallState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Fall;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);
            player.Fall.BeginFall(player.Motor.LastGroundedHeight);
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
            player.Motor.MoveAt(Vector3.zero, deltaTime);

            if (!player.Motor.IsGrounded || IsFinished) return;
            IsFinished = true;

            var height = player.Fall.EndFall();
            if (height < player.Config.fallStateMinHeight) return;

            EventBus.Publish(new PlayerFell(height));

            if (height < player.Config.hardFallHeight) return;

            switch (player.Config.hardFallConsequence)
            {
                case HardFallConsequence.None:
                    break;
                case HardFallConsequence.Stun:
                    player.Stun(player.Config.hardFallStunDuration);
                    break;
                case HardFallConsequence.Death:
                    player.KillReceiver.Kill(DeathCause.Fall);
                    break;
            }
        }

        public override void Exit(PlayerController player) => player.Fall.EndFall();
    }
}
