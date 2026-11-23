namespace ShadowTan.Player
{
    public sealed class HangState : PlayerStateBase
    {
        public override PlayerStateId Id => PlayerStateId.Hang;

        public override void Enter(PlayerController player)
        {
            base.Enter(player);

            // Из HangAttack/HangStab героиня возвращается в Hang уже висящей — заново не цепляемся.
            if (player.Grab.IsAttached || !player.Grab.TryAttach()) return;

            player.Animator.TriggerGrab();
        }

        public override void Tick(PlayerController player, float deltaTime) { }
    }
}
