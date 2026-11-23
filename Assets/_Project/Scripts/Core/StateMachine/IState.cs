namespace ShadowTan.Core.StateMachine
{
    // Общий механизм §8.7: состояние не знает о следующем, машина только переключает и тикает.
    public interface IState<in TContext>
    {
        void Enter(TContext context);
        void Tick(TContext context, float deltaTime);
        void Exit(TContext context);
    }
}
