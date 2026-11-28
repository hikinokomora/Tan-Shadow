using UnityEngine;

namespace ShadowTan.Gameplay.Autumn
{
    [DisallowMultipleComponent]
    public sealed class AutumnVegetationImpulse : MonoBehaviour
    {
        [SerializeField] AutumnWindController wind;
        [SerializeField] Renderer[] legacyStems;

        const int Limit = 6;
        readonly Renderer[] stems = new Renderer[Limit];
        readonly float[] distances = new float[Limit];
        readonly Vector3[] initialPosition = new Vector3[Limit];
        readonly Quaternion[] initialRotation = new Quaternion[Limit];
        readonly Vector3[] pivot = new Vector3[Limit];
        readonly Vector3[] axis = new Vector3[Limit];
        readonly float[] weight = new float[Limit];
        int activeCount;
        float elapsed;
        float duration;
        float angle;

        public void Rustle(Vector3 point) => Trigger(point, Vector3.zero, 2.7f, 0.76f, 0.48f, 7f);
        public void Leap(Vector3 point, Vector3 direction) => Trigger(point, direction, 3f, 0.93f, 1.2f, 16f);

        void Trigger(Vector3 point, Vector3 direction, float radius, float length, float strength, float degrees)
        {
            Stop();
            if (wind) wind.TriggerImpulse(point, direction, radius, length, strength);
            if (legacyStems == null || legacyStems.Length == 0) return;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;
            float maxDistance = radius * radius;
            foreach (var candidate in legacyStems)
            {
                if (!candidate || !candidate.enabled || !candidate.gameObject.activeInHierarchy) continue;
                var bounds = candidate.bounds;
                var closest = bounds.ClosestPoint(new Vector3(point.x, bounds.center.y, point.z));
                float edgeDistance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(closest.x, closest.z));
                float centerDistance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(bounds.center.x, bounds.center.z));
                // _Game merges each planting into one renderer. Compare its footprint to the cue,
                // then favor the nearest plantings when several bounds overlap.
                float reach = edgeDistance + Mathf.Min(centerDistance * .13f, 1.15f);
                float distance = reach * reach;
                if (distance >= maxDistance || (activeCount == Limit && distance >= distances[Limit - 1])) continue;
                int insertion = Mathf.Min(activeCount, Limit - 1);
                while (insertion > 0 && distance < distances[insertion - 1])
                {
                    stems[insertion] = stems[insertion - 1];
                    distances[insertion] = distances[insertion - 1];
                    insertion--;
                }
                stems[insertion] = candidate;
                distances[insertion] = distance;
                activeCount = Mathf.Min(Limit, activeCount + 1);
            }
            for (int i = 0; i < activeCount; i++)
            {
                var t = stems[i].transform;
                var localBase = stems[i].bounds.center;
                var radial = localBase - point;
                radial.y = 0f;
                localBase.y = stems[i].bounds.min.y;
                pivot[i] = t.InverseTransformPoint(localBase);
                initialRotation[i] = t.localRotation;
                initialPosition[i] = t.localPosition;
                Vector3 away = side.sqrMagnitude > 0.1f
                    ? side * Mathf.Sign(Vector3.Dot(side, radial))
                    : radial.normalized;
                Vector3 worldAxis = Vector3.Cross(Vector3.up, away);
                axis[i] = t.parent ? t.parent.InverseTransformDirection(worldAxis) : worldAxis;
                weight[i] = 1f - Mathf.Sqrt(distances[i]) / radius;
            }
            duration = length;
            angle = degrees;
        }

        void LateUpdate()
        {
            if (activeCount == 0) return;
            elapsed += Time.deltaTime;
            if (elapsed >= duration) { Restore(); return; }
            float t = elapsed / duration;
            float envelope = Mathf.SmoothStep(0, 1, Mathf.Min(t * 9, (1f - t) * 1.35f));
            for (int i = 0; i < activeCount; i++)
            {
                if (!stems[i]) continue;
                var stem = stems[i].transform;
                Vector3 localPivot = Vector3.Scale(pivot[i], stem.localScale);
                var rotation = Quaternion.AngleAxis(angle * weight[i] * envelope, axis[i]) * initialRotation[i];
                stem.localRotation = rotation;
                stem.localPosition = initialPosition[i] + initialRotation[i] * localPivot - rotation * localPivot;
            }
        }

        public void Stop()
        {
            if (wind) wind.ClearImpulse();
            Restore();
        }

        void Restore()
        {
            for (int i = 0; i < activeCount; i++)
            {
                if (!stems[i]) continue;
                var stem = stems[i].transform;
                stem.localPosition = initialPosition[i];
                stem.localRotation = initialRotation[i];
            }
            activeCount = 0;
            elapsed = 0;
        }

        void OnDisable() => Stop();
    }
}
