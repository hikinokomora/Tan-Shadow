using UnityEngine;

/// <summary>Optional gravity and inertia for the SwordRig tassel. Add to the imported FBX root.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class SwordRopePhysics : MonoBehaviour
{
    private const int SegmentCount = 6;
    private const int SolverIterations = 12;

    [SerializeField] private bool simulationEnabled = true;
    [SerializeField, Min(0f)] private float gravityScale = 1f;
    [SerializeField, Min(0f)] private float damping = 4f;
    [SerializeField, Range(0f, 1f)] private float bendStiffness = 0.12f;

    private readonly Transform[] joints = new Transform[SegmentCount + 1];
    private readonly Quaternion[] restRotation = new Quaternion[SegmentCount];
    private readonly Vector3[] points = new Vector3[SegmentCount + 1];
    private readonly Vector3[] previous = new Vector3[SegmentCount + 1];
    private readonly float[] lengths = new float[SegmentCount];
    private Vector3 lastAnchor;
    private float totalLength;
    private bool bound;
    private bool seeded;

    public bool SimulationEnabled
    {
        get => simulationEnabled;
        set
        {
            if (simulationEnabled == value)
                return;

            simulationEnabled = value;
            RestorePose();
        }
    }

    private void OnEnable()
    {
        bound = Bind();
        if (!bound)
            Debug.LogWarning("SwordRopePhysics requires the exported Sword_Grip, Rope_01–06 and Rope_Tip transforms.", this);
    }

    private void OnDisable() => RestorePose();

    private bool Bind()
    {
        Transform grip = System.Array.Find(GetComponentsInChildren<Transform>(true), bone => bone.name == "Sword_Grip");
        if (!grip)
            return false;

        Transform parent = grip;
        for (int i = 0; i <= SegmentCount; i++)
        {
            string name = i == SegmentCount ? "Rope_Tip" : $"Rope_{i + 1:00}";
            parent = parent.Find(name);
            if (!parent)
                return false;

            joints[i] = parent;
            if (i < SegmentCount)
                restRotation[i] = parent.localRotation;
        }

        return true;
    }

    private void RestorePose()
    {
        if (seeded)
        {
            for (int i = 0; i < SegmentCount; i++)
                if (joints[i])
                    joints[i].localRotation = restRotation[i];
        }

        seeded = false;
    }

    private void Seed(Vector3 anchor)
    {
        totalLength = 0f;
        for (int i = 0; i <= SegmentCount; i++)
        {
            points[i] = previous[i] = joints[i].position;
            if (i == 0)
                continue;

            lengths[i - 1] = Vector3.Distance(points[i - 1], points[i]);
            totalLength += lengths[i - 1];
            // A slight lateral impulse lets the chain fall even when the grip points straight up.
            previous[i] -= joints[0].right * (0.001f * i / SegmentCount);
        }

        lastAnchor = anchor;
        seeded = true;
    }

    private void LateUpdate()
    {
        if (!bound)
            return;
        if (!simulationEnabled)
        {
            RestorePose();
            return;
        }

        Step(Time.deltaTime);
    }

    private void Step(float deltaTime)
    {
        if (deltaTime <= 0f)
            return;

        Vector3 anchor = joints[0].position;
        float liveFirstLength = Vector3.Distance(anchor, joints[1].position);
        if (!seeded || Vector3.Distance(anchor, lastAnchor) > totalLength * 1.5f
            || Mathf.Abs(liveFirstLength - lengths[0]) > Mathf.Max(0.0001f, lengths[0] * 0.05f))
        {
            RestorePose();
            Seed(anchor);
        }

        if (totalLength < 0.0001f)
            return;

        float duration = Mathf.Min(deltaTime, 0.05f);
        int steps = Mathf.CeilToInt(duration * 120f);
        float step = duration / steps;
        Vector3 acceleration = Physics.gravity * (gravityScale * step * step);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 pin = Vector3.Lerp(lastAnchor, anchor, (float)i / steps);
            Advance(pin, acceleration, Mathf.Exp(-damping * step));
        }

        lastAnchor = anchor;
        ApplyPose();
    }

    private void Advance(Vector3 anchor, Vector3 acceleration, float retention)
    {
        points[0] = anchor;
        for (int i = 1; i <= SegmentCount; i++)
        {
            Vector3 current = points[i];
            points[i] += (current - previous[i]) * retention + acceleration;
            previous[i] = current;
        }

        for (int iteration = 0; iteration < SolverIterations; iteration++)
        {
            for (int i = 0; i < SegmentCount; i++)
            {
                Vector3 delta = points[i + 1] - points[i];
                float distance = delta.magnitude;
                if (distance < 0.000001f)
                    continue;

                Vector3 correction = delta * ((distance - lengths[i]) / distance);
                if (i == 0)
                    points[i + 1] -= correction;
                else
                {
                    points[i] += correction * 0.5f;
                    points[i + 1] -= correction * 0.5f;
                }
            }

            if (iteration == 0)
                for (int i = 1; i < SegmentCount; i++)
                {
                    float t = lengths[i - 1] / Mathf.Max(0.0001f, lengths[i - 1] + lengths[i]);
                    points[i] = Vector3.Lerp(points[i], Vector3.Lerp(points[i - 1], points[i + 1], t), bendStiffness);
                }
        }

        // The skeleton has rigid bone lengths. Match the solver positions to that same constraint.
        for (int i = 0; i < SegmentCount; i++)
        {
            Vector3 delta = points[i + 1] - points[i];
            Vector3 direction = delta.sqrMagnitude > 1e-12f
                ? delta.normalized : (joints[i + 1].position - joints[i].position).normalized;
            Vector3 corrected = points[i] + direction * lengths[i];
            previous[i + 1] += corrected - points[i + 1];
            points[i + 1] = corrected;
        }
    }

    private void ApplyPose()
    {
        for (int i = 0; i < SegmentCount; i++)
        {
            Transform bone = joints[i];
            bone.localRotation = restRotation[i];
            Vector3 restDirection = joints[i + 1].position - bone.position;
            Vector3 direction = points[i + 1] - points[i];
            if (restDirection.sqrMagnitude > 1e-12f && direction.sqrMagnitude > 1e-12f)
                bone.rotation = Quaternion.FromToRotation(restDirection, direction) * bone.rotation;
        }
    }
}

