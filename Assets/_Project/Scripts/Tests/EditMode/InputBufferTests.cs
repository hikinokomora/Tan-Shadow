using NUnit.Framework;
using ShadowTan.Player;

namespace ShadowTan.Tests
{
    public class InputBufferTests
    {
        [Test]
        public void Press_IsConsumedWithinWindow()
        {
            var buffer = new InputBuffer();
            buffer.Press(BufferedAction.Attack, 1f);

            Assert.IsTrue(buffer.Has(BufferedAction.Attack, 1.1f, 0.15f));
            Assert.IsTrue(buffer.Consume(BufferedAction.Attack, 1.1f, 0.15f));
            Assert.IsFalse(buffer.Has(BufferedAction.Attack, 1.1f, 0.15f));
        }

        [Test]
        public void Press_ExpiresAfterWindow()
        {
            var buffer = new InputBuffer();
            buffer.Press(BufferedAction.Roll, 2f);

            Assert.IsFalse(buffer.Has(BufferedAction.Roll, 2.2f, 0.15f));
            Assert.IsFalse(buffer.Consume(BufferedAction.Roll, 2.2f, 0.15f));
        }

        [Test]
        public void Press_ReplacesPreviousAction()
        {
            var buffer = new InputBuffer();
            buffer.Press(BufferedAction.Roll, 1f);
            buffer.Press(BufferedAction.Attack, 1.05f);

            Assert.IsFalse(buffer.Has(BufferedAction.Roll, 1.06f, 0.15f));
            Assert.IsTrue(buffer.Has(BufferedAction.Attack, 1.06f, 0.15f));
        }
    }
}
