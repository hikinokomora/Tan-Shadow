using UnityEngine;

namespace ShadowTan.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class SeasonEntranceView : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] LayerMask obstacles;
        [SerializeField] float distance = 3.8f;
        [SerializeField] float height = 1.45f;
        [SerializeField] float pitch = 12f;

        void LateUpdate()
        {
            if (target == null) return;

            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            Vector3 focus = target.position + Vector3.up * height;
            Vector3 back = rotation * Vector3.back;
            float offset = distance;
            if (Physics.SphereCast(focus, 0.14f, back, out RaycastHit hit, distance, obstacles, QueryTriggerInteraction.Ignore))
                offset = Mathf.Max(0.18f, hit.distance - 0.08f);
            transform.SetPositionAndRotation(focus + back * offset, rotation);
        }
    }
}
