using UnityEngine;

namespace ShadowTan.Player
{
    // Запоминает высоту начала падения, чтобы Fall понял, насколько высоко героиня была (§9.6.7).
    public class FallTracker : MonoBehaviour
    {
        float startHeight;
        bool falling;

        public bool IsFalling => falling;

        public void BeginFall() => BeginFall(transform.position.y);

        public void BeginFall(float lastGroundedHeight)
        {
            startHeight = Mathf.Max(transform.position.y, lastGroundedHeight);
            falling = true;
        }

        public float EndFall()
        {
            if (!falling) return 0f;
            falling = false;
            return Mathf.Max(0f, startHeight - transform.position.y);
        }
    }
}
