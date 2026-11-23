using NUnit.Framework;
using ShadowTan.Core.StateMachine;

namespace ShadowTan.Tests
{
    public class StateMachineTests
    {
        class DummyState : IState<int>
        {
            public int Enters;
            public int Exits;
            public int LastContext;

            public void Enter(int context)
            {
                Enters++;
                LastContext = context;
            }

            public void Tick(int context, float deltaTime)
            {
            }

            public void Exit(int context)
            {
                Exits++;
            }
        }

        [Test]
        public void Change_EntersAndExitsInOrder()
        {
            var machine = new StateMachine<int>();
            var first = new DummyState();
            var second = new DummyState();

            machine.Change(first, 1);
            machine.Change(second, 2);

            Assert.AreEqual(1, first.Enters);
            Assert.AreEqual(1, first.Exits);
            Assert.AreEqual(1, second.Enters);
            Assert.AreEqual(2, second.LastContext);
            Assert.AreSame(second, machine.Current);
            Assert.AreSame(first, machine.Previous);
        }

        [Test]
        public void Change_SameState_DoesNotReenter()
        {
            var machine = new StateMachine<int>();
            var state = new DummyState();

            machine.Change(state, 1);
            machine.Change(state, 2);

            Assert.AreEqual(1, state.Enters);
            Assert.AreEqual(0, state.Exits);
        }

        [Test]
        public void Change_ReenterAllowed_EntersAgain()
        {
            var machine = new StateMachine<int>(true);
            var state = new DummyState();

            machine.Change(state, 1);
            machine.Change(state, 2);

            Assert.AreEqual(2, state.Enters);
        }
    }
}
