using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    public sealed class AttackState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Attack;

        float elapsed;
        float stopRate;
        Vector3 swingDirection;

        public float Elapsed => elapsed;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            player.Input.ConsumeBuffered(BufferedAction.Attack);
            var config = player.Config;

            swingDirection = player.Input.MoveWorldDirection;
            if (swingDirection.sqrMagnitude < 0.0001f) swingDirection = player.transform.forward;
            swingDirection.y = 0f;
            swingDirection.Normalize();
            player.Motor.SnapFace(swingDirection);

            elapsed = 0f;
            stopRate = player.Motor.PlanarVelocity.magnitude / Mathf.Max(config.attackStopTime, 0.0001f);

            player.ResetAnimatorHitWindow();
            player.Sword.BeginSwing();
            player.Sword.ScareSwing(swingDirection);
            player.Animator.TriggerAttack();
            EventBus.Publish(new PlayerAttackStarted(swingDirection, false));
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
            elapsed += deltaTime;
            var config = player.Config;

            player.Motor.MoveTowards(Vector3.zero, stopRate, stopRate, deltaTime);
            if (!player.Motor.IsGrounded) return;


            if (elapsed <= config.attackTurnWindow)
            {
                var turn = player.Input.MoveWorldDirection;
                if (turn.sqrMagnitude > 0.0001f)
                {
                    player.Motor.Face(turn, config.turnSpeed, deltaTime);
                    swingDirection = turn;
                }
            }

            if (player.HitWindowOpen(elapsed) && player.Sword.CheckSwing(false))
            {
                // Отскок прерывает взмах (§10.3).
                IsFinished = true;
                return;
            }

            if (elapsed >= config.attackDuration) IsFinished = true;
        }

        public bool CanCancelIntoRoll(PlayerController player)
        {
            return elapsed > player.Config.attackHitWindowEnd;
        }
    }
}
