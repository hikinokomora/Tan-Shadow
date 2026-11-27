using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class QinglongRockTone : MonoBehaviour
{
    private static readonly int StrengthId = Shader.PropertyToID("_QinglongRockToneStrength");

    [Tooltip("0 uses the original rock colors. 1 gives the strongest grey grading while protecting moss.")]
    [SerializeField, Range(0f, 1f)] private float strength = 0.72f;

    private float appliedStrength = -1f;

    private void OnEnable() => Apply();

    private void Update()
    {
        if (appliedStrength != strength)
            Apply();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(StrengthId, 0f);
        appliedStrength = -1f;
    }

    private void Apply()
    {
        Shader.SetGlobalFloat(StrengthId, strength);
        appliedStrength = strength;
    }
}
