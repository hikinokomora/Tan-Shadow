using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Dark fantasy look as a Volume override: crimson grade, contrast, glow + red halation,
// oil paint (anisotropic Kuwahara + impasto relief + canvas), chromatic aberration, vignette, film grain.
// Toggle it with the checkbox on the override header (or the Volume_DarkFantasy object).
// All defaults are "off"; the tuned values live in VP_DarkFantasy.asset.
[Serializable, VolumeComponentMenu("Post-processing/Custom/Dark Fantasy")]
[SupportedOnRenderPipeline(typeof(HDRenderPipelineAsset))]
public sealed class DarkFantasyPostProcess : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    [Header("Master")]
    [Tooltip("Общая сила эффекта. 0 = выключен, 1 = полностью.")]
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f);

    [Header("Grade: tone")]
    [Tooltip("Экспозиция в стопах.")]
    public ClampedFloatParameter exposure = new ClampedFloatParameter(0f, -2f, 2f);
    [Tooltip("S-кривая контраста.")]
    public ClampedFloatParameter contrast = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Точка, вокруг которой крутится контраст (средние тона).")]
    public ClampedFloatParameter contrastPivot = new ClampedFloatParameter(0.45f, 0.1f, 0.9f);
    [Tooltip("Задавить чёрные: всё ниже этого значения уходит в чистый чёрный.")]
    public ClampedFloatParameter blackCrush = new ClampedFloatParameter(0f, 0f, 0.3f);
    [Tooltip("Плёночный подъём чёрных, окрашенный в цвет теней.")]
    public ClampedFloatParameter blackLift = new ClampedFloatParameter(0f, 0f, 0.3f);

    [Header("Grade: color (Chinese red palette)")]
    [Tooltip("Общая насыщенность.")]
    public ClampedFloatParameter saturation = new ClampedFloatParameter(1f, 0f, 2f);
    [Tooltip("Насколько обесцветить все оттенки, кроме сохраняемого (красного).")]
    public ClampedFloatParameter nonRedDesaturation = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Дополнительная насыщенность сохраняемого оттенка.")]
    public ClampedFloatParameter redBoost = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Какой оттенок сохранять (0 = красный, 0.08 = киноварь/оранжевый, 0.15 = золото).")]
    public ClampedFloatParameter keepHue = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Ширина диапазона сохраняемого оттенка.")]
    public ClampedFloatParameter keepHueWidth = new ClampedFloatParameter(0.08f, 0.01f, 0.3f);
    [Tooltip("Цвет теней (и поднятых чёрных).")]
    public ColorParameter shadowTint = new ColorParameter(new Color(0.55f, 0.1f, 0.08f), false, false, true);
    public ClampedFloatParameter shadowTintStrength = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Цвет светов.")]
    public ColorParameter highlightTint = new ColorParameter(new Color(1f, 0.82f, 0.62f), false, false, true);
    public ClampedFloatParameter highlightTintStrength = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Граница между тенями и светами для тонировки.")]
    public ClampedFloatParameter toneBalance = new ClampedFloatParameter(0.5f, 0f, 1f);
    [Tooltip("Общий цветовой фильтр (overlay).")]
    public ColorParameter colorFilter = new ColorParameter(new Color(0.75f, 0.2f, 0.15f), false, false, true);
    public ClampedFloatParameter colorFilterStrength = new ClampedFloatParameter(0f, 0f, 1f);

    [Header("Moonlight")]
    [Tooltip("Оттенок лунного света, который не трогает красный грейд (0.6 = синий, 0.5 = бирюзовый).")]
    public ClampedFloatParameter moonHue = new ClampedFloatParameter(0.6f, 0f, 1f);
    [Tooltip("Ширина диапазона «лунных» оттенков.")]
    public ClampedFloatParameter moonHueWidth = new ClampedFloatParameter(0.08f, 0.01f, 0.3f);
    [Tooltip("Насколько лунный синий защищён от обесцвечивания (Non Red Desaturation).")]
    public ClampedFloatParameter moonPreserve = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Насколько лунный свет защищён от красной тонировки теней и цветового фильтра.")]
    public ClampedFloatParameter moonTintProtect = new ClampedFloatParameter(0f, 0f, 1f);


    [Header("Glow")]
    [Tooltip("Мягкое свечение светлых участков.")]
    public ClampedFloatParameter bloomIntensity = new ClampedFloatParameter(0f, 0f, 3f);
    [Tooltip("С какой яркости начинается свечение.")]
    public ClampedFloatParameter bloomThreshold = new ClampedFloatParameter(0.6f, 0f, 1f);
    public ClampedFloatParameter bloomKnee = new ClampedFloatParameter(0.5f, 0f, 1f);
    [Tooltip("Насколько широко расползается свечение.")]
    public ClampedFloatParameter bloomScatter = new ClampedFloatParameter(0.7f, 0f, 1f);
    public ColorParameter bloomTint = new ColorParameter(Color.white, false, false, true);
    [Tooltip("Красный ореол вокруг светов, как на старой плёнке.")]
    public ClampedFloatParameter halationIntensity = new ClampedFloatParameter(0f, 0f, 2f);
    public ColorParameter halationColor = new ColorParameter(new Color(1f, 0.15f, 0.04f), false, false, true);

    [Header("Oil paint")]
    [Tooltip("Сила масляной живописи: картинка распадается на мазки, которые идут вдоль форм.")]
    public ClampedFloatParameter painterlyStrength = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Размер мазка в пикселях (в половинном разрешении, если Half Resolution включён). Дорого на больших значениях.")]
    public ClampedIntParameter painterlyRadius = new ClampedIntParameter(6, 2, 12);
    [Tooltip("Чёткость границ между мазками.")]
    public ClampedFloatParameter paintSharpness = new ClampedFloatParameter(8f, 1f, 18f);
    [Tooltip("Насколько сильно мазок «выбирает» самую ровную сторону. Больше = плоские пятна краски.")]
    public ClampedFloatParameter paintHardness = new ClampedFloatParameter(8f, 1f, 100f);
    [Tooltip("Вытянутость мазков вдоль форм. Меньше = длиннее мазки.")]
    public ClampedFloatParameter paintAnisotropy = new ClampedFloatParameter(1f, 0.1f, 5f);
    [Tooltip("Считать краску в половинном разрешении: в 4 раза дешевле и мазки крупнее.")]
    public BoolParameter paintHalfResolution = new BoolParameter(true);
    [Tooltip("Импасто: рельеф густой краски, подсвеченный сверху-слева.")]
    public ClampedFloatParameter impastoStrength = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Следы щетины кисти вдоль мазка.")]
    public ClampedFloatParameter brushBristles = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Длина следов щетины в пикселях.")]
    public ClampedFloatParameter strokeLength = new ClampedFloatParameter(10f, 2f, 30f);
    [Tooltip("Фактура холста.")]
    public ClampedFloatParameter canvasStrength = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Размер плетения холста в пикселях.")]
    public ClampedFloatParameter canvasScale = new ClampedFloatParameter(3f, 1f, 8f);

    [Header("Lens")]
    [Tooltip("Цветная кайма к краям кадра.")]
    public ClampedFloatParameter chromaticAberration = new ClampedFloatParameter(0f, 0f, 1f);
    public ClampedFloatParameter vignetteIntensity = new ClampedFloatParameter(0f, 0f, 1f);
    public ClampedFloatParameter vignetteSmoothness = new ClampedFloatParameter(0.5f, 0f, 1f);
    public ColorParameter vignetteColor = new ColorParameter(new Color(0.06f, 0f, 0f), false, false, true);

    [Header("Film grain")]
    public ClampedFloatParameter grainIntensity = new ClampedFloatParameter(0f, 0f, 1f);
    [Tooltip("Размер зерна в пикселях.")]
    public ClampedFloatParameter grainSize = new ClampedFloatParameter(1.5f, 0.5f, 4f);
    [Tooltip("0 = зерно везде одинаковое, 1 = в основном в тенях.")]
    public ClampedFloatParameter grainResponse = new ClampedFloatParameter(0.6f, 0f, 1f);
    [Tooltip("0 = монохромное зерно, 1 = цветное.")]
    public ClampedFloatParameter grainColor = new ClampedFloatParameter(0.15f, 0f, 1f);
    [Tooltip("Частота смены зерна. 24 = как плёнка, 0 = статичное.")]
    public ClampedFloatParameter grainFps = new ClampedFloatParameter(24f, 0f, 60f);

    const string kShaderName = "Hidden/Shader/DarkFantasy";
    const int kPassPrefilter = 0, kPassDown = 1, kPassUp = 2, kPassComposite = 3;
    const int kPassTensor = 4, kPassTensorBlur = 5, kPassPaint = 6;
    const int kMaxBloomLevels = 6;

    Material m_Material;
    readonly int[] m_DownIds = new int[kMaxBloomLevels];
    readonly int[] m_UpIds = new int[kMaxBloomLevels];
    readonly Vector2Int[] m_Sizes = new Vector2Int[kMaxBloomLevels];

    static class Ids
    {
        public static readonly int MainTex = Shader.PropertyToID("_MainTex");
        public static readonly int BlitTexelSize = Shader.PropertyToID("_DF_BlitTexelSize");
        public static readonly int BlitTex = Shader.PropertyToID("_DF_BlitTex");
        public static readonly int HighTex = Shader.PropertyToID("_DF_HighTex");
        public static readonly int BloomTex = Shader.PropertyToID("_DF_BloomTex");
        public static readonly int TensorTex = Shader.PropertyToID("_DF_TensorTex");
        public static readonly int PaintTex = Shader.PropertyToID("_DF_PaintTex");
        public static readonly int PaintTexelSize = Shader.PropertyToID("_DF_PaintTexelSize");
        public static readonly int BlurDir = Shader.PropertyToID("_DF_BlurDir");
        public static readonly int TensorA = Shader.PropertyToID("_DF_TensorA");
        public static readonly int TensorB = Shader.PropertyToID("_DF_TensorB");
        public static readonly int PaintRT = Shader.PropertyToID("_DF_PaintRT");
    }

    public bool IsActive() => m_Material != null && intensity.value > 0f;

    // After HDRP's own post: we grade the tonemapped image, like a colorist would.
    public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterPostProcess;

    public override void Setup()
    {
        var shader = Shader.Find(kShaderName);
        if (shader == null)
        {
            Debug.LogError($"DarkFantasyPostProcess: shader '{kShaderName}' not found.");
            return;
        }
        m_Material = CoreUtils.CreateEngineMaterial(shader);
        for (int i = 0; i < kMaxBloomLevels; i++)
        {
            m_DownIds[i] = Shader.PropertyToID("_DF_Down" + i);
            m_UpIds[i] = Shader.PropertyToID("_DF_Up" + i);
        }
    }

    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
    {
        if (m_Material == null) return;

        PushParameters(source);
        int width = camera.actualWidth, height = camera.actualHeight;

        int levels = 0;
        if (bloomIntensity.value > 0f || halationIntensity.value > 0f)
            levels = RenderBloom(cmd, width, height);
        if (levels == 0)
            cmd.SetGlobalTexture(Ids.BloomTex, Texture2D.blackTexture);

        bool paint = painterlyStrength.value > 0f || impastoStrength.value > 0f || brushBristles.value > 0f;
        if (paint)
        {
            RenderPaint(cmd, width, height);
        }
        else
        {
            cmd.SetGlobalTexture(Ids.TensorTex, Texture2D.blackTexture);
            cmd.SetGlobalTexture(Ids.PaintTex, Texture2D.blackTexture);
        }

        HDUtils.DrawFullScreen(cmd, m_Material, destination, null, kPassComposite);

        for (int i = 0; i < levels; i++)
        {
            cmd.ReleaseTemporaryRT(m_DownIds[i]);
            cmd.ReleaseTemporaryRT(m_UpIds[i]);
        }
        if (paint)
        {
            cmd.ReleaseTemporaryRT(Ids.TensorA);
            cmd.ReleaseTemporaryRT(Ids.TensorB);
            cmd.ReleaseTemporaryRT(Ids.PaintRT);
        }
    }

    // Classic mip-chain bloom: bright-pass, downsample, then tent-upsample back up.
    int RenderBloom(CommandBuffer cmd, int width, int height)
    {
        int levels = 0;
        int w = width / 2, h = height / 2;
        while (levels < kMaxBloomLevels && w >= 4 && h >= 4)
        {
            cmd.GetTemporaryRT(m_DownIds[levels], w, h, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float);
            cmd.GetTemporaryRT(m_UpIds[levels], w, h, 0, FilterMode.Bilinear, RenderTextureFormat.RGB111110Float);
            m_Sizes[levels] = new Vector2Int(w, h);
            w /= 2; h /= 2;
            levels++;
        }
        if (levels == 0) return 0;

        DrawTo(cmd, m_DownIds[0], kPassPrefilter);
        for (int i = 1; i < levels; i++)
        {
            SetBlitSource(cmd, m_DownIds[i - 1], m_Sizes[i - 1]);
            DrawTo(cmd, m_DownIds[i], kPassDown);
        }

        int low = m_DownIds[levels - 1];
        for (int i = levels - 2; i >= 0; i--)
        {
            SetBlitSource(cmd, low, m_Sizes[i + 1]);
            cmd.SetGlobalTexture(Ids.HighTex, m_DownIds[i]);
            DrawTo(cmd, m_UpIds[i], kPassUp);
            low = m_UpIds[i];
        }
        cmd.SetGlobalTexture(Ids.BloomTex, low);
        return levels;
    }

    // Oil paint: structure tensor -> blur -> anisotropic Kuwahara.
    void RenderPaint(CommandBuffer cmd, int width, int height)
    {
        int div = paintHalfResolution.value ? 2 : 1;
        var size = new Vector2Int(Mathf.Max(1, width / div), Mathf.Max(1, height / div));
        cmd.GetTemporaryRT(Ids.TensorA, size.x, size.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
        cmd.GetTemporaryRT(Ids.TensorB, size.x, size.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
        cmd.GetTemporaryRT(Ids.PaintRT, size.x, size.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
        cmd.SetGlobalVector(Ids.PaintTexelSize, new Vector4(1f / size.x, 1f / size.y, size.x, size.y));

        DrawTo(cmd, Ids.TensorA, kPassTensor);

        SetBlitSource(cmd, Ids.TensorA, size);
        cmd.SetGlobalVector(Ids.BlurDir, new Vector4(1f, 0f, 0f, 0f));
        DrawTo(cmd, Ids.TensorB, kPassTensorBlur);

        SetBlitSource(cmd, Ids.TensorB, size);
        cmd.SetGlobalVector(Ids.BlurDir, new Vector4(0f, 1f, 0f, 0f));
        DrawTo(cmd, Ids.TensorA, kPassTensorBlur);
        cmd.SetGlobalTexture(Ids.TensorTex, Ids.TensorA);

        if (painterlyStrength.value > 0f)
        {
            DrawTo(cmd, Ids.PaintRT, kPassPaint);
            cmd.SetGlobalTexture(Ids.PaintTex, Ids.PaintRT);
        }
        else
        {
            cmd.SetGlobalTexture(Ids.PaintTex, Texture2D.blackTexture);
        }
    }

    void SetBlitSource(CommandBuffer cmd, int id, Vector2Int size)
    {
        cmd.SetGlobalTexture(Ids.BlitTex, id);
        cmd.SetGlobalVector(Ids.BlitTexelSize, new Vector4(1f / size.x, 1f / size.y, size.x, size.y));
    }

    void DrawTo(CommandBuffer cmd, int id, int pass)
    {
        cmd.SetRenderTarget(new RenderTargetIdentifier(id), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
        cmd.DrawProcedural(Matrix4x4.identity, m_Material, pass, MeshTopology.Triangles, 3, 1);
    }

    void PushParameters(RTHandle source)
    {
        var m = m_Material;
        m.SetTexture(Ids.MainTex, source);
        m.SetFloat("_Intensity", intensity.value);

        m.SetFloat("_Exposure", exposure.value);
        m.SetFloat("_Contrast", contrast.value);
        m.SetFloat("_ContrastPivot", contrastPivot.value);
        m.SetFloat("_BlackCrush", blackCrush.value);
        m.SetFloat("_BlackLift", blackLift.value);
        m.SetFloat("_Saturation", saturation.value);
        m.SetFloat("_NonRedDesat", nonRedDesaturation.value);
        m.SetFloat("_RedBoost", redBoost.value);
        m.SetFloat("_KeepHue", keepHue.value);
        m.SetFloat("_KeepHueWidth", keepHueWidth.value);
        // Grading happens in sRGB space, so these colors go in as-is (SetVector skips linear conversion)
        m.SetVector("_ShadowTint", (Vector4)shadowTint.value);
        m.SetFloat("_ShadowTintStrength", shadowTintStrength.value);
        m.SetVector("_HighlightTint", (Vector4)highlightTint.value);
        m.SetFloat("_HighlightTintStrength", highlightTintStrength.value);
        m.SetFloat("_ToneBalance", toneBalance.value);
        m.SetVector("_ColorFilter", (Vector4)colorFilter.value);
        m.SetFloat("_ColorFilterStrength", colorFilterStrength.value);
        m.SetFloat("_MoonHue", moonHue.value);
        m.SetFloat("_MoonHueWidth", moonHueWidth.value);
        m.SetFloat("_MoonPreserve", moonPreserve.value);
        m.SetFloat("_MoonTintProtect", moonTintProtect.value);


        m.SetFloat("_BloomIntensity", bloomIntensity.value);
        m.SetFloat("_BloomThreshold", bloomThreshold.value);
        m.SetFloat("_BloomKnee", bloomKnee.value);
        m.SetFloat("_BloomScatter", bloomScatter.value);
        m.SetColor("_BloomTint", bloomTint.value);       // added in linear space
        m.SetFloat("_HalationIntensity", halationIntensity.value);
        m.SetColor("_HalationColor", halationColor.value);

        m.SetFloat("_PainterlyStrength", painterlyStrength.value);
        m.SetFloat("_PainterlyRadius", painterlyRadius.value);
        m.SetFloat("_PaintSharpness", paintSharpness.value);
        m.SetFloat("_PaintHardness", paintHardness.value);
        m.SetFloat("_PaintAnisotropy", paintAnisotropy.value);
        m.SetFloat("_ImpastoStrength", impastoStrength.value);
        m.SetFloat("_BrushBristles", brushBristles.value);
        m.SetFloat("_StrokeLength", strokeLength.value);
        m.SetFloat("_CanvasStrength", canvasStrength.value);
        m.SetFloat("_CanvasScale", canvasScale.value);

        m.SetFloat("_ChromaticAberration", chromaticAberration.value);
        m.SetFloat("_VignetteIntensity", vignetteIntensity.value);
        m.SetFloat("_VignetteSmoothness", vignetteSmoothness.value);
        m.SetVector("_VignetteColor", (Vector4)vignetteColor.value);

        m.SetFloat("_GrainIntensity", grainIntensity.value);
        m.SetFloat("_GrainSize", grainSize.value);
        m.SetFloat("_GrainResponse", grainResponse.value);
        m.SetFloat("_GrainColor", grainColor.value);
        float seed = grainFps.value > 0f ? Mathf.Floor(Time.realtimeSinceStartup * grainFps.value) % 97f : 0f;
        m.SetFloat("_GrainSeed", seed);
    }

    public override void Cleanup()
    {
        CoreUtils.Destroy(m_Material);
    }
}
