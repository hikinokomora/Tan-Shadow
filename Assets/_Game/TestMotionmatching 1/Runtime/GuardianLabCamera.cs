using UnityEngine;

namespace TanShadow.MotionMatchingLab
{
    public sealed class GuardianLabCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        static readonly Vector3 Offset = new Vector3(.65f, 2.21f, -4.47f);
        Vector3 velocity;

        public void Configure(Transform player)
        {
            target = player;
            Place(true);
        }

        void OnEnable() => Place(true);
        void LateUpdate() => Place(false);

        void Place(bool immediate)
        {
            if (!target)
                return;
            Vector3 desired = target.position + Offset;
            transform.position = immediate ? desired : Vector3.SmoothDamp(transform.position, desired, ref velocity, .105f);
            transform.rotation = Quaternion.LookRotation(target.position + Vector3.up * 1.39f + Vector3.forward * .24f - transform.position);
        }
    }
}
