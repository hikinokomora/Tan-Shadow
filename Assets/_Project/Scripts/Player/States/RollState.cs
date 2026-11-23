using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    public sealed class RollState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Roll;

        float elapsed;
        Vector3 direction;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            player.Input.ConsumeBuffered(BufferedAction.Roll);
            direction = player.Input.MoveWorldDirection;
            if (direction.sqrMagnitude < 0.0001f) direction = player.transform.forward;
            direction.y = 0f;
            direction.Normalize();

            elapsed = 0f;
            player.MarkRollUsed();
            player.Animator.TriggerRoll();
            EventBus.Publish(new PlayerRolled(direction));
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
            var config = player.Config;
            var remaining = config.rollDuration - elapsed;
            if (remaining <= 0f)
            {
                IsFinished = true;
                return;
            }

            var step = Mathf.Min(deltaTime, remaining);
            elapsed += step;

            if (config.rollSteerable)
            {
                var input = player.Input.MoveWorldDirection;
                if (input.sqrMagnitude > 0.0001f)
                {
                    direction = Vector3.RotateTowards(direction, input, config.turnSpeed * Mathf.Deg2Rad * step, 0f);
                }
            }

            var normalized = elapsed / config.rollDuration;
            player.Motor.MoveAt(direction * config.RollSpeed(normalized), step);
            player.Motor.Face(direction, config.turnSpeed, step);

            if (step >= remaining) IsFinished = true;
        }
    }
}
