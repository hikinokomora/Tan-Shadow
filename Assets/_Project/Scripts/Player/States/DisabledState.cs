using ShadowTan.Core.Contracts;

namespace ShadowTan.Player
{
    // Управление отключено: переходы, слайд-шоу, пауза, финал босса (§9.5.2).
    public sealed class DisabledState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Disabled;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            if (player.Grab.IsAttached) player.Grab.Detach(GrabReleaseReason.Released);

            player.Motor.Suspend(false);
            player.Motor.StopImmediate();
            player.Input.ClearBuffer();
        }

        public override void Tick(PlayerController player, float deltaTime)
        {
        }
    }
}
