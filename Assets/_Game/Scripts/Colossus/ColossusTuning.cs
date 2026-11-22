using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Colossus
{
    public sealed class ColossusTuning : ScriptableObject
    {
        public AttackData slam;
        public float pursuitSpeed = 1.65f;
        public float attackDistance = 4.8f;
        public float firstGripWindow = 3.8f;
        public float secondGripWindow = 2.7f;
        public float riseDuration = 1.1f;
        public float lateGripGrace = 0.18f;
        public float jumpReach = 2.2f;
        public float jumpDuration = 0.43f;
        public float climbSpeed = 0.41f;
        public float shakeDelay = 4.4f;
        public float shakeWarning = 0.95f;
        public float impactRadius = 2.2f;
        public float resetDelay = 1.25f;
    }
}
