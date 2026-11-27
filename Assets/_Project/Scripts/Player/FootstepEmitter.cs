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
        public RaycastHit LastContact { get; private set; }

        void Awake()
        {
            if (probe == null) probe = transform;
        }

        public bool TryGetContact(out RaycastHit hit)
        {
            var origin = probe != null ? probe : transform;
            return Physics.Raycast(origin.position + Vector3.up * 0.1f, Vector3.down, out hit,
                rayLength, groundMask, QueryTriggerInteraction.Ignore);
        }

        public void OnFootstep()
        {
            if (!TryGetContact(out var hit))
            {
                return;
            }

            LastContact = hit;
            var tag = hit.collider.GetComponentInParent<SurfaceTag>();
            FootstepDetected?.Invoke(tag != null ? tag.Surface : null);
        }
    }
}
