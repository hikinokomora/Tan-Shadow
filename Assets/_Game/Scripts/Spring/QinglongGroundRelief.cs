using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public sealed class QinglongGroundRelief : MonoBehaviour
{
    [SerializeField] private float strength = 0.43f;
    [SerializeField] private float hillSize = 1f;
    [SerializeField, HideInInspector] private float[] baseHeights;

    public float Strength => strength;
    public float HillSize => hillSize;
    public bool HasBaseHeights(int vertexCount) => baseHeights != null && baseHeights.Length == vertexCount;

    public float GetHeight(int vertex, Vector3 worldPosition, float height, float size)
    {
        return baseHeights[vertex] + GetNoiseHeight(worldPosition, height, size) / transform.lossyScale.y;
    }

    public static float GetNoiseHeight(Vector3 position, float height, float size)
    {
        float x = position.x / size;
        float z = position.z / size;
        float broad = Mathf.PerlinNoise(x * 0.073f + 26.7f, z * 0.08f + 13.2f);
        float detail = Mathf.PerlinNoise(x * 0.147f + 109.4f, z * 0.151f + 71.6f);
        float noise = 0.72f * broad + 0.28f * detail;
        float entrance = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 17f, position.z));
        return entrance * Mathf.SmoothStep(0f, height, Mathf.InverseLerp(0.26f, 0.82f, noise));
    }
}
