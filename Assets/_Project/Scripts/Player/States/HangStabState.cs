using UnityEngine;

namespace ShadowTan.Player
{
    public sealed class HangStabState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.HangStab;

        float elapsed;
        float endTime;
        bool impacted;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            elapsed = 0f;
            endTime = player.Config.stabDuration;
            impacted = false;
            player.ResetAnimatorHitWindow();
            player.Sword.BeginSwing();
            player.Animator.TriggerStab();
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
            elapsed += deltaTime;

            // Если Animation Event StabImpact есть — он открывает момент удара; иначе середина клипа.
            var impactNow = player.ConsumeAnimatorStabImpact() || elapsed >= player.Config.stabDuration * 0.5f;
            if (!impacted && impactNow)
            {
                impacted = true;
                var deflected = player.Sword.Stab(true);
                if (deflected) endTime = elapsed + player.Config.deflectRecoilTime;
            }

            if (elapsed >= endTime) IsFinished = true;
        }
    }
}
