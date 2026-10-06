using UnityEngine;

namespace TanShadow.Characters
{
    // Капсула между двумя костями, о которую отталкиваются пружинные цепочки (подол о бёдра).
    public class SpringCollider : MonoBehaviour
    {
        public Transform from;
        [Tooltip("Если пусто — сфера в точке from")]
        public Transform to;
        [Min(0)] public float radius = 0.09f;

        public Vector3 A => from.position;
        public Vector3 B => to != null ? to.position : from.position;

        // Вытолкнуть точку (с её радиусом) за поверхность капсулы.
        public Vector3 PushOut(Vector3 point, float pointRadius)
        {
            Vector3 a = A, ab = B - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 closest = a + ab * t;
            Vector3 d = point - closest;
            float min = radius + pointRadius;
            float len = d.magnitude;
            if (len >= min || len < 1e-6f) return point;
            return closest + d / len * min;
        }

        void OnDrawGizmosSelected()
        {
            if (from == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(A, radius);
            Gizmos.DrawWireSphere(B, radius);
            Gizmos.DrawLine(A, B);
        }
    }
}
