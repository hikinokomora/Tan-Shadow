namespace ShadowTan.Player
{
    // Состояние только тикает и сообщает «закончилось». Куда перейти — решает PlayerStateGraph (§9.5).
    public interface IPlayerState
    {
        PlayerStateId Id { get; }
        bool IsFinished { get; }
        void Enter(PlayerController player);
        void Tick(PlayerController player, float deltaTime);
        void Exit(PlayerController player);
    }
}
