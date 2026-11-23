using ShadowTan.Core.Contracts;

namespace ShadowTan.Player
{
    // Служебное состояние: ввод игнорируется, смерть не показывается (I-5).
    public sealed class DeadState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Dead;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            if (player.Grab.IsAttached) player.Grab.Detach(GrabReleaseReason.Released);

            player.Motor.Suspend(false);
            player.Motor.StopImmediate();
            player.Input.SetInputEnabled(false);
            player.Input.ClearBuffer();
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
        }
    }
}
