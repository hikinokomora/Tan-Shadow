using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnDebrisFx : MonoBehaviour
    {
        [SerializeField] ParticleSystem stones;
        [SerializeField] Collider slope;
        public bool Running { get; set; } = true;

        public void Play(Vector3 point)
        {
            if (!Running || !slope || !slope.Raycast(new Ray(point + Vector3.up * 0.85f, Vector3.down), out var hit, 3f))
                return;
            Vector3 downslope = Vector3.ProjectOnPlane(Vector3.down, hit.normal);
            if (downslope.sqrMagnitude < 0.006f) return;
            AutumnParticles.Emit(stones, hit.point + hit.normal * 0.035f, downslope.normalized,
                Random.Range(3, 7), 0.85f, 0.052f, 1.0f, 0.25f);
        }
    }
}
