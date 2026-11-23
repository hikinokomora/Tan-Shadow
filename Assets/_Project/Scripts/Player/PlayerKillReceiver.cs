using ShadowTan.Core.Contracts;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    // Принимает «убить», блокирует ввод и публикует PlayerKilled. Смерть никогда не показывается (I-5).
    public class PlayerKillReceiver : MonoBehaviour
    {
        [SerializeField] PlayerController player;

        public bool IsDead { get; private set; }

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
        }

        public void ResetDead() => IsDead = false;

        public void Kill(IKillSource source)
        {
            Kill(source != null ? source.Cause : DeathCause.Other, source);
        }

        public void Kill(DeathCause cause)
        {
            Kill(cause, null);
        }

        void Kill(DeathCause cause, IKillSource source)
        {
            if (IsDead) return;
            if (player != null && player.IsInvulnerable) return;

            IsDead = true;
            player?.EnterDead();
            EventBus.Publish(new PlayerKilled(cause, source));
        }
    }
}
