using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    public sealed class HangAttackState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.HangAttack;

        float elapsed;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            player.Input.ConsumeBuffered(BufferedAction.Attack);
            elapsed = 0f;
            player.ResetAnimatorHitWindow();
            player.Sword.BeginSwing();
            player.Animator.TriggerAttack();
            EventBus.Publish(new PlayerAttackStarted(player.transform.forward, true));
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
            elapsed += deltaTime;

            if (player.HitWindowOpen(elapsed) && player.Sword.CheckSwing(true))
            {
                IsFinished = true;
                return;
            }

            if (elapsed >= player.Config.attackDuration) IsFinished = true;
        }
    }
}
