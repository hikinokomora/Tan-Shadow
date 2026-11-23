using UnityEngine;

namespace ZhunqueWideArena
{
    [DisallowMultipleComponent]
    public sealed class LavaTextureFlow : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float speedMetersPerSecond = 4.2f;
        [SerializeField, Min(0.01f)] private float metersPerTextureRepeat = 18f;

        private static readonly int ColorTransform = Shader.PropertyToID("_UnlitColorMap_ST");
        private static readonly int EmissionTransform = Shader.PropertyToID("_EmissiveColorMap_ST");

        private MeshRenderer[] renderers;
        private MaterialPropertyBlock properties;
        private float offset;

        private void OnEnable()
        {
            ApplyTextureOffset(0f);
        }

        private void LateUpdate()
        {
            float textureSpeed = speedMetersPerSecond / metersPerTextureRepeat;
            ApplyTextureOffset(offset - Time.deltaTime * textureSpeed);
        }

        public void ApplyTextureOffset(float offsetInRepeats)
        {
            renderers ??= GetComponentsInChildren<MeshRenderer>(true);
            properties ??= new MaterialPropertyBlock();
            offset = Mathf.Repeat(offsetInRepeats, 1f);
            foreach (MeshRenderer target in renderers)
            {
                target.GetPropertyBlock(properties);
                Vector4 textureTransform = new Vector4(1f, 1f, 0f, offset);
                properties.SetVector(ColorTransform, textureTransform);
                properties.SetVector(EmissionTransform, textureTransform);
                target.SetPropertyBlock(properties);
            }
        }
    }
}
