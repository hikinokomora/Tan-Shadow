using NUnit.Framework;
using ShadowTan.Player;
using UnityEngine;

namespace ShadowTan.Tests
{
    public class SwordArcTests
    {
        static readonly Vector3 Origin = Vector3.zero;
        static readonly Vector3 Forward = Vector3.forward;

        [Test]
        public void Contains_PointInFrontWithinRange()
        {
            Assert.IsTrue(SwordArc.Contains(Origin, Forward, new Vector3(0f, 0f, 1f), 1.6f, 100f));
        }

        [Test]
        public void Contains_PointBeyondRange_IsRejected()
        {
            Assert.IsFalse(SwordArc.Contains(Origin, Forward, new Vector3(0f, 0f, 2f), 1.6f, 100f));
        }

        [Test]
        public void Contains_PointOutsideCone_IsRejected()
        {
            Assert.IsFalse(SwordArc.Contains(Origin, Forward, new Vector3(1.5f, 0f, 0.2f), 1.6f, 100f));
        }

        [Test]
        public void Contains_PointAtConeEdge_IsAccepted()
        {
            var angle = 49f * Mathf.Deg2Rad;
            var point = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Assert.IsTrue(SwordArc.Contains(Origin, Forward, point, 1.6f, 100f));
        }
    }
}
