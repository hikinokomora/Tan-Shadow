using System;
using ShadowTan.Combat;
using UnityEngine;

namespace ShadowTan.Player
{
    // По Animation Event Footstep определяет поверхность под ногой.
    // Проигрывание Cue_Player_Footstep_<Surface> подключит модуль Audio (§16.8), когда появится.
    public class FootstepEmitter : MonoBehaviour
    {
        [SerializeField] Transform probe;
        [SerializeField] LayerMask groundMask = ~0;
        [SerializeField] float rayLength = 1f;

        public event Action<SurfaceDefinition> FootstepDetected;

        void Awake()
        {
            if (probe == null) probe = transform;
        }

        public void OnFootstep()
        {
            if (!Physics.Raycast(probe.position + Vector3.up * 0.1f, Vector3.down, out var hit,
                    rayLength, groundMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            var tag = hit.collider.GetComponentInParent<SurfaceTag>();
            FootstepDetected?.Invoke(tag != null ? tag.Surface : null);
        }
    }
}
