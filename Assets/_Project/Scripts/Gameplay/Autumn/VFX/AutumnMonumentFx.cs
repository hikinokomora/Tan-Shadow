using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnMonumentFx : MonoBehaviour
    {
        [SerializeField] ParticleSystem impactDust;
        [SerializeField] ParticleSystem shards;
        [SerializeField] LayerMask groundMask = ~0;

        Vector3 Ground(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 0.75f, Vector3.down, out var hit,
                2f, groundMask, QueryTriggerInteraction.Ignore)
                ? hit.point + hit.normal * 0.045f
                : point + Vector3.up * 0.045f;
        }

        public void Landing(Vector3 point)
        {
            AutumnParticles.Emit(impactDust, Ground(point), Vector3.up * 0.3f, 14, .9f, .40f, .55f, 1f);
        }

        public void Takeoff(Vector3 point, Vector3 direction)
        {
            AutumnParticles.Emit(impactDust, Ground(point), -direction + Vector3.up * .23f, 10, 1.65f, .29f, .40f, .6f);
        }

        public void Hit(Vector3 point)
        {
            Landing(point);
            AutumnParticles.Emit(shards, point + Vector3.up * .38f, Vector3.up * .85f, 11, 1.8f, .08f, .75f, 1f);
        }

        public void MonumentLanded(Vector3 point)
        {
            AutumnParticles.Emit(impactDust, Ground(point), Vector3.up * .62f, 28, 1.25f, .58f, .85f, 1f);
        }
    }
}
