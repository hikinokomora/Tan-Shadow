using UnityEngine;

namespace TanShadow.MotionMatchingLab
{
    public static class MotionTwoBone
    {
        public static void Reach(Transform upper, Transform lower, Transform tip, Vector3 target, Vector3 bend)
        {
            Vector3 origin = upper.position;
            float upperLength = Vector3.Distance(origin, lower.position);
            float lowerLength = Vector3.Distance(lower.position, tip.position);
            Vector3 axis = (target - origin).normalized;
            if (axis.sqrMagnitude < .01f || upperLength < .01f || lowerLength < .01f)
                return;

            float distance = Mathf.Clamp(Vector3.Distance(origin, target),
                Mathf.Abs(upperLength - lowerLength) + .005f, upperLength + lowerLength - .005f);
            Vector3 pole = Vector3.ProjectOnPlane(bend, axis).normalized;
            if (pole.sqrMagnitude < .01f)
                pole = Vector3.ProjectOnPlane(Mathf.Abs(axis.y) > .9f ? Vector3.forward : Vector3.up, axis).normalized;
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 joint = origin + axis * along + pole * height;

            Quaternion tipRotation = tip.rotation;
            upper.rotation = Quaternion.FromToRotation(lower.position - origin, joint - origin) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(tip.position - lower.position,
                origin + axis * distance - lower.position) * lower.rotation;
            tip.rotation = tipRotation;
        }
    }
}
