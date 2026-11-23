namespace ShadowTan.Player
{
    public abstract class PlayerStateBase : IPlayerState
    {
        public abstract PlayerStateId Id { get; }
        public bool IsFinished { get; protected set; }

        public virtual void Enter(PlayerController player)
        {
            IsFinished = false;
        }

        public abstract void Tick(PlayerController player, float deltaTime);

        public virtual void Exit(PlayerController player)
        {
        }
    }
}
