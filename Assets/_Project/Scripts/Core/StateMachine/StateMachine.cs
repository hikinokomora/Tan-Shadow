using System;

namespace ShadowTan.Core.StateMachine
{
    public sealed class StateMachine<TContext>
    {
        readonly bool allowReenter;

        public StateMachine(bool allowReenter = false)
        {
            this.allowReenter = allowReenter;
        }

        public IState<TContext> Current { get; private set; }
        public IState<TContext> Previous { get; private set; }

        public event Action<IState<TContext>, IState<TContext>> Changed;

        public void Change(IState<TContext> next, TContext context = default)
        {
            if (next == null) return;
            if (next == Current && !allowReenter) return;

            Current?.Exit(context);
            Previous = Current;
            Current = next;
            Current.Enter(context);
            Changed?.Invoke(Previous, Current);
        }

        public void Tick(TContext context, float deltaTime)
        {
            Current?.Tick(context, deltaTime);
        }
    }
}
