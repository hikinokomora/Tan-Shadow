using UnityEngine;

namespace ShadowTan.Player
{
    // Геометрия дуги удара. Вынесена отдельно, чтобы проверяться тестом без сцены.
    public static class SwordArc
    {
        public static bool Contains(Vector3 origin, Vector3 forward, Vector3 point, float range, float arcDegrees)
        {
            var toPoint = point - origin;
            if (toPoint.magnitude > range) return false;
            if (toPoint.sqrMagnitude < 0.0001f) return true;
            return Vector3.Angle(forward, toPoint) <= arcDegrees * 0.5f;
        }
    }
}
