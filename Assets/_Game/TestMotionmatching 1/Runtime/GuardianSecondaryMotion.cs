using UnityEngine;

/// <summary>Add to the Guardian model root. Drives the two unanimated glute deform bones.</summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public sealed class GuardianSecondaryMotion : MonoBehaviour
{
    private const float Frequency = 4.2f;
    private const float Damping = 0.77f;
    [SerializeField, Range(0f, 0.025f)] private float maxOffsetMetres = 0.012f;
    [SerializeField, Range(0f, 1f)] private float response = 0.42f;
    private readonly Spring[] springs = new Spring[2];

    private sealed class Spring
    {
        public Transform bone;
        public Vector3 restLocal, lastAnchor, lastVelocity, offset, velocity;
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
            springs[side] = new Spring { bone = bone, restLocal = bone.localPosition };
        }
    }

    private void OnDisable()
    {
        foreach (var s in springs)
            if (s != null && s.bone != null)
                s.bone.localPosition = s.restLocal;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float omega = 2f * Mathf.PI * Frequency;
        foreach (var s in springs)
        {
            Vector3 anchor = s.bone.parent.TransformPoint(s.restLocal);
            Vector3 delta = anchor - s.lastAnchor;
            if (s.samples == 0 || dt > 0.1f || delta.sqrMagnitude > 0.25f)
            {
                s.samples = 1;
                s.lastAnchor = anchor;
                s.lastVelocity = s.offset = s.velocity = Vector3.zero;
                s.bone.localPosition = s.restLocal;
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
                    - 2f * Damping * omega * s.velocity) * step;
                s.offset = Vector3.ClampMagnitude(s.offset + s.velocity * step, maxOffsetMetres);
            }
            s.bone.position = anchor + s.offset;
        }
    }
}
