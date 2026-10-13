using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TanShadow.Rendering
{
    // Живописный грейд на весь кадр. Мазки и зерно лежат на поверхностях (по мировой позиции из глубины), а не на экране.
    [Serializable, VolumeComponentMenu("Post-processing/Tan Shadow/Painterly Grade")]
    public sealed class InkWashEffect : CustomPostProcessVolumeComponent, IPostProcessComponent
    {
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);
        [Header("Фокус и кисть")]
        public ClampedFloatParameter paint = new ClampedFloatParameter(0.8f, 0f, 1f);
        public MinFloatParameter focusDistance = new MinFloatParameter(7f, 0.5f);
        public MinFloatParameter focusRange = new MinFloatParameter(9f, 0.5f);
        public ClampedFloatParameter sharpen = new ClampedFloatParameter(0.9f, 0f, 2f);
        [Header("Мазки и зерно на поверхностях")]
        public ClampedFloatParameter stroke = new ClampedFloatParameter(0.35f, 0f, 1f);
        public MinFloatParameter strokeWorld = new MinFloatParameter(0.35f, 0.02f);
        public ClampedFloatParameter grain = new ClampedFloatParameter(0.22f, 0f, 0.6f);
        public MinFloatParameter grainWorld = new MinFloatParameter(0.015f, 0.002f);
        [Header("Контур")]
        public ClampedFloatParameter outline = new ClampedFloatParameter(0.6f, 0f, 1f);
        public ClampedFloatParameter outlineWidth = new ClampedFloatParameter(1.4f, 0.5f, 4f);
        public ColorParameter outlineColor = new ColorParameter(new Color(0.03f, 0.04f, 0.05f), false, false, true);
        [Header("Цвет")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(1.15f, 0.6f, 1.8f);
        public ClampedFloatParameter saturation = new ClampedFloatParameter(0.85f, 0f, 2f);
        public ClampedFloatParameter depthTint = new ClampedFloatParameter(0.8f, 0f, 1f);
        public ClampedFloatParameter vignette = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter focusLift = new ClampedFloatParameter(0.35f, 0f, 1f);
        public ColorParameter shadowTint = new ColorParameter(new Color(0.45f, 0.58f, 0.62f), false, false, true);
        public ColorParameter highlightTint = new ColorParameter(new Color(1.0f, 0.86f, 0.62f), false, false, true);
        public ColorParameter fogTint = new ColorParameter(new Color(0.52f, 0.66f, 0.66f), false, false, true);

        Material material;

        public bool IsActive() => material != null && intensity.value > 0f;

        public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.BeforePostProcess;

        public override void Setup()
        {
            var shader = Shader.Find("Hidden/Shader/TanInkWash");
            if (shader != null) material = new Material(shader);
        }

        public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
        {
            if (material == null) return;
            material.SetFloat("_Intensity", intensity.value);
            material.SetFloat("_Paint", paint.value);
            material.SetFloat("_FocusDistance", focusDistance.value);
            material.SetFloat("_FocusRange", focusRange.value);
            material.SetFloat("_Sharpen", sharpen.value);
            material.SetFloat("_Stroke", stroke.value);
            material.SetFloat("_StrokeWorld", strokeWorld.value);
            material.SetFloat("_Grain", grain.value);
            material.SetFloat("_GrainWorld", grainWorld.value);
            material.SetFloat("_Outline", outline.value);
            material.SetFloat("_OutlineWidth", outlineWidth.value);
            material.SetColor("_OutlineColor", outlineColor.value);
            material.SetFloat("_Contrast", contrast.value);
            material.SetFloat("_Saturation", saturation.value);
            material.SetFloat("_DepthTint", depthTint.value);
            material.SetFloat("_Vignette", vignette.value);
            material.SetFloat("_FocusLift", focusLift.value);
            material.SetColor("_ShadowTint", shadowTint.value);
            material.SetColor("_HighlightTint", highlightTint.value);
            material.SetColor("_FogTint", fogTint.value);
            material.SetTexture("_InputTexture", source);
            HDUtils.DrawFullScreen(cmd, material, destination, shaderPassId: 0);
        }

        public override void Cleanup() => CoreUtils.Destroy(material);
    }
}
