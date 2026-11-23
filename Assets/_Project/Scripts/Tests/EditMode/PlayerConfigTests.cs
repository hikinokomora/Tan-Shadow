using NUnit.Framework;
using ShadowTan.Player;
using UnityEngine;

namespace ShadowTan.Tests
{
    public class PlayerConfigTests
    {
        PlayerConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<PlayerConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(config);
        }

        [Test]
        public void RollSpeed_FlatCurve_EqualsDistanceOverDuration()
        {
            Assert.AreEqual(config.rollDistance / config.rollDuration, config.RollSpeed(0.5f), 0.0001f);
        }

        [Test]
        public void RollSpeed_ClampsNormalizedTime()
        {
            Assert.AreEqual(config.RollSpeed(1f), config.RollSpeed(2f), 0.0001f);
            Assert.AreEqual(config.RollSpeed(0f), config.RollSpeed(-1f), 0.0001f);
        }
    }
}
