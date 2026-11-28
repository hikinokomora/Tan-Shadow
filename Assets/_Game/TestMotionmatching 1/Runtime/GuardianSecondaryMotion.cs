using UnityEngine;

/// <summary>Adds a damped offset to the two glute deform bones after animation evaluation.</summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class GuardianSecondaryMotion : MonoBehaviour
{
    private const float Frequency = 3.4f;
    // Underdamped so the bones rebound even after the animated anchor comes to rest.
    private const float DampingRatio = 0.19f;
    [SerializeField, Range(0f, 2f), InspectorName("Сила мягкой физики ягодиц")]
    [Tooltip("0 — выключено, 1 — исходная сила, 2 — вдвое сильнее.")]
    private float strength = 1f;

    [SerializeField, HideInInspector] private float maxOffsetMetres = 0.006f;
    [SerializeField, HideInInspector] private float response = 0.31f;
    private readonly Spring[] springs = new Spring[2];

    private sealed class Spring
    {
        public Transform bone;
        public Vector3 authoredLocal, appliedLocal, lastAnchor, lastVelocity, offset, velocity;
        public bool applied;
        public int samples;
    }

    private void OnEnable()
    {
        var bones = GetComponentsInChildren<Transform>(true);
        for (int side = 0; side < springs.Length; side++)
        {
            string nameToFind = side == 0 ? "DEF-glute.L" : "DEF-glute.R";
            var bone = System.Array.Find(bones, t => t.name == nameToFind);
            if (bone == null || bone.parent == null)
            {
                Debug.LogWarning("Guardian requires an exposed bone named " + nameToFind, this);
                enabled = false;
                return;
            }
            springs[side] = new Spring { bone = bone };
        }
    }

    private void OnDisable()
    {
        foreach (var s in springs)
            if (s != null && s.bone && s.applied && s.bone.localPosition.Equals(s.appliedLocal))
                s.bone.localPosition = s.authoredLocal;
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    private void Step(float dt)
    {
        if (dt <= 0f) return;
        float omega = 2f * Mathf.PI * Frequency;
        foreach (var s in springs)
        {
            Vector3 local = s.bone.localPosition;
            if (!s.applied || !local.Equals(s.appliedLocal))
                s.authoredLocal = local;
            Vector3 anchor = s.bone.parent.TransformPoint(s.authoredLocal);
            Vector3 delta = anchor - s.lastAnchor;
            if (s.samples == 0 || dt > 0.1f || delta.sqrMagnitude > 0.25f)
            {
                s.samples = 1;
                s.lastAnchor = anchor;
                s.lastVelocity = s.offset = s.velocity = Vector3.zero;
                s.bone.localPosition = s.authoredLocal;
                s.applied = false;
                continue;
            }
            Vector3 anchorVelocity = delta / dt;
            Vector3 acceleration = s.samples == 1 ? Vector3.zero :
                Vector3.ClampMagnitude((anchorVelocity - s.lastVelocity) / dt, 32f);
            s.samples = 2;
            s.lastAnchor = anchor;
            s.lastVelocity = anchorVelocity;
            int steps = Mathf.CeilToInt(dt * 120f);
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                s.velocity += (-response * acceleration - omega * omega * s.offset
                    - 2f * DampingRatio * omega * s.velocity) * step;
                Vector3 nextOffset = s.offset + s.velocity * step;
                s.offset = Vector3.ClampMagnitude(nextOffset, maxOffsetMetres);
                if (nextOffset.sqrMagnitude > maxOffsetMetres * maxOffsetMetres)
                {
                    Vector3 direction = nextOffset.normalized;
                    s.velocity -= direction * Mathf.Max(0f, Vector3.Dot(s.velocity, direction));
                }
            }
            s.bone.position = anchor + s.offset * strength;
            s.appliedLocal = s.bone.localPosition;
            s.applied = true;
        }
    }
}
