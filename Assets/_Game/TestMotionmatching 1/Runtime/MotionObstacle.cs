using UnityEngine;

namespace TanShadow.MotionMatchingLab
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class MotionObstacle : MonoBehaviour
    {
        public MotionKind kind;
        public bool airCatchOnly;
        public string caption;

        public BoxCollider Volume => GetComponent<BoxCollider>();
        public bool LandsOnTop => kind == MotionKind.Mantle || kind == MotionKind.Climb;

        public void GetRoute(Vector3 from, Vector3 approach, float radius, out Vector3 forward,
            out Vector3 contact, out Vector3 landing, out float distance)
        {
            BoxCollider box = Volume;
            Vector3 center = transform.TransformPoint(box.center);
            Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
            Vector3 across = transform.right;
            forward = Vector3.Dot(approach, transform.forward) >= 0f ? transform.forward : -transform.forward;
            float lateralRoom = Mathf.Max(0f, size.x * .5f - radius - .10f);
            float lateral = Mathf.Clamp(Vector3.Dot(from - center, across), -lateralRoom, lateralRoom);
            Vector3 front = center + across * lateral - forward * (size.z * .5f);
            distance = Vector3.Dot(front - from, forward);

            if (LandsOnTop)
            {
                contact = front - forward * (radius * .30f);
                landing = front + forward * Mathf.Min(size.z - radius, radius + .32f);
                contact.y = landing.y = box.bounds.max.y + .035f;
            }
            else
            {
                contact = front + forward * (size.z * .5f);
                contact.y = box.bounds.max.y + .08f;
                landing = front + forward * (size.z + radius + .46f);
                landing.y = from.y;
            }
        }
    }
}
