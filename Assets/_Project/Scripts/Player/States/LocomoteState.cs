using UnityEngine;

namespace ShadowTan.Player
{
    public sealed class LocomoteState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Locomote;

        public override void Tick(PlayerController player, float deltaTime)
        {
            var config = player.Config;
            var direction = player.Input.MoveWorldDirection;

            player.Motor.MoveTowards(direction * config.runSpeed, config.acceleration, config.deceleration, deltaTime);

            if (direction.sqrMagnitude > 0.0001f)
            {
                player.Motor.Face(direction, config.turnSpeed, deltaTime);
            }
        }
    }
}
