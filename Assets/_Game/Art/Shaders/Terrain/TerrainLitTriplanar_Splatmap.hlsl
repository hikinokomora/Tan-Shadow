#if defined(_NORMALMAP) && defined(SURFACE_GRADIENT)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/NormalSurfaceGradient.hlsl"
#endif

TEXTURE2D(_Control0);

#define DECLARE_TERRAIN_LAYER_TEXS(n)   \
    TEXTURE2D(_Splat##n);               \
    TEXTURE2D(_Normal##n);              \
    TEXTURE2D(_Mask##n)

DECLARE_TERRAIN_LAYER_TEXS(0);
DECLARE_TERRAIN_LAYER_TEXS(1);
DECLARE_TERRAIN_LAYER_TEXS(2);
DECLARE_TERRAIN_LAYER_TEXS(3);
#ifdef _TERRAIN_8_LAYERS
    DECLARE_TERRAIN_LAYER_TEXS(4);
    DECLARE_TERRAIN_LAYER_TEXS(5);
    DECLARE_TERRAIN_LAYER_TEXS(6);
    DECLARE_TERRAIN_LAYER_TEXS(7);
    TEXTURE2D(_Control1);
#endif

#undef DECLARE_TERRAIN_LAYER_TEXS

SAMPLER(sampler_Splat0);
SAMPLER(sampler_Control0);


// ---- TanShadow: трипланар на крутых склонах + макровариация
static float3 g_TriPos = 0;  // позиция в пространстве террейна, м
static float3 g_TriNrm = float3(0, 1, 0); // нормаль поверхности
static float  g_TriMetersToUV = 0; // 1 / ширина террейна в метрах (0 — трипланар выключен)
static float3 g_TriDpx = 0, g_TriDpy = 0;

float3 TriplanarWeights(float3 n)
{
    float3 w = pow(abs(n), 6.0);
    return w / (w.x + w.y + w.z);
}

#define TRI_STEEP_START 0.9   // доля вертикальной проекции, ниже которой включается трипланар

float GetSumHeight(float4 heights0, float4 heights1)
{
    float sumHeight = heights0.x;
    sumHeight += heights0.y;
    sumHeight += heights0.z;
    sumHeight += heights0.w;
    #ifdef _TERRAIN_8_LAYERS
        sumHeight += heights1.x;
        sumHeight += heights1.y;
        sumHeight += heights1.z;
        sumHeight += heights1.w;
    #endif
    return sumHeight;
}

#ifdef _NORMALMAP
float3 SampleNormalGrad(TEXTURE2D_PARAM(textureName, samplerName), float2 uv, float2 dxuv, float2 dyuv, float scale)
{
    float4 nrm = SAMPLE_TEXTURE2D_GRAD(textureName, samplerName, uv, dxuv, dyuv);
#ifdef SURFACE_GRADIENT
    #ifdef UNITY_NO_DXT5nm
        return float3(UnpackDerivativeNormalRGB(nrm, scale), 0);
    #else
        return float3(UnpackDerivativeNormalRGorAG(nrm, scale), 0);
    #endif
#else
    #ifdef UNITY_NO_DXT5nm
        return UnpackNormalRGB(nrm, scale);
    #else
        return UnpackNormalMapRGorAG(nrm, scale);
    #endif
#endif
}
#endif

float4 RemapMasks(float4 masks, float blendMask, float4 remapOffset, float4 remapScale)
{
    float4 ret = masks;
    ret.b *= blendMask; // height needs to be weighted before remapping
    ret = ret * remapScale + remapOffset;
    return ret;
}

#ifdef OVERRIDE_SPLAT_SAMPLER_NAME
    #define sampler_Splat0 OVERRIDE_SPLAT_SAMPLER_NAME
    SAMPLER(OVERRIDE_SPLAT_SAMPLER_NAME);
#endif

void TerrainSplatBlend(float2 controlUV, float2 splatBaseUV, inout TerrainLitSurfaceData surfaceData)
{
    // TODO: triplanar
    // TODO: POM

    float4 albedo[_LAYER_COUNT];
    float3 normal[_LAYER_COUNT];
    float4 masks[_LAYER_COUNT];

#ifdef _NORMALMAP
    #define SampleNormal(i) SampleNormalGrad(_Normal##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv, _NormalScale##i)
#else
    #define SampleNormal(i) float3(0, 0, 0)
#endif

#define DefaultMask(i) float4(_Metallic##i, _MaskMapRemapOffset##i.y + _MaskMapRemapScale##i.y, _MaskMapRemapOffset##i.z + 0.5 * _MaskMapRemapScale##i.z, albedo[i].a * _Smoothness##i)

#ifdef _MASKMAP
    #define MaskModeMasks(i, blendMask) RemapMasks(SAMPLE_TEXTURE2D_GRAD(_Mask##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv), blendMask, _MaskMapRemapOffset##i, _MaskMapRemapScale##i)
#define SampleMasks(i, blendMask) lerp(DefaultMask(i), MaskModeMasks(i, blendMask), _LayerHasMask##i)
    #define NullMask(i)               float4(0, 1, _MaskMapRemapOffset##i.z, 0) // only height matters when weight is zero.
#else
    #define SampleMasks(i, blendMask) DefaultMask(i)
    #define NullMask(i)               float4(0, 1, 0, 0)
#endif


#ifdef _MASKMAP
    #define TRI_MASKS(i, blendMask, s)                                                                          \
        UNITY_BRANCH if (_LayerHasMask##i > 0)                                                                  \
        {                                                                                                       \
            float4 mx = SAMPLE_TEXTURE2D_GRAD(_Mask##i, sampler_Splat0, g_TriPos.zy * s, g_TriDpx.zy * s, g_TriDpy.zy * s); \
            float4 mz = SAMPLE_TEXTURE2D_GRAD(_Mask##i, sampler_Splat0, g_TriPos.xy * s, g_TriDpx.xy * s, g_TriDpy.xy * s); \
            float4 mt = SAMPLE_TEXTURE2D_GRAD(_Mask##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv);       \
            masks[i] = RemapMasks(mx * triW.x + mt * triW.y + mz * triW.z, blendMask, _MaskMapRemapOffset##i, _MaskMapRemapScale##i); \
        }
#else
    #define TRI_MASKS(i, blendMask, s)
#endif
#ifdef SURFACE_GRADIENT
    #define TRI_FADE_NORMAL(n, k) ((n) * (k))
#else
    #define TRI_FADE_NORMAL(n, k) lerp(float3(0, 0, 1), (n), (k))
#endif

#define SampleResults(i, mask)                                                                                  \
    UNITY_BRANCH if (mask > 0)                                                                                  \
    {                                                                                                           \
        float2 splatuv = splatBaseUV * _Splat##i##_ST.xy + _Splat##i##_ST.zw;                                   \
        float2 splatdxuv = dxuv * _Splat##i##_ST.x;                                                             \
        float2 splatdyuv = dyuv * _Splat##i##_ST.y;                                                             \
        albedo[i] = SAMPLE_TEXTURE2D_GRAD(_Splat##i, sampler_Splat0, splatuv, splatdxuv, splatdyuv);            \
        normal[i] = SampleNormal(i);                                                                            \
        masks[i] = SampleMasks(i, mask);                                                                        \
        float macro##i = dot(SAMPLE_TEXTURE2D_GRAD(_Splat##i, sampler_Splat0, splatuv * 0.071, splatdxuv * 0.071, splatdyuv * 0.071).rgb, float3(0.3, 0.59, 0.11)); \
        UNITY_BRANCH if (g_TriMetersToUV > 0 && triW.y < TRI_STEEP_START)                                       \
        {                                                                                                       \
            float s = _Splat##i##_ST.x * g_TriMetersToUV;                                                       \
            float4 ax = SAMPLE_TEXTURE2D_GRAD(_Splat##i, sampler_Splat0, g_TriPos.zy * s, g_TriDpx.zy * s, g_TriDpy.zy * s); \
            float4 az = SAMPLE_TEXTURE2D_GRAD(_Splat##i, sampler_Splat0, g_TriPos.xy * s, g_TriDpx.xy * s, g_TriDpy.xy * s); \
            albedo[i] = ax * triW.x + albedo[i] * triW.y + az * triW.z;                                         \
            TRI_MASKS(i, mask, s)                                                                               \
            normal[i] = TRI_FADE_NORMAL(normal[i], triW.y);                                                     \
        }                                                                                                       \
        albedo[i].rgb *= _DiffuseRemapScale##i.xyz * lerp(0.78, 1.22, saturate(macro##i * 1.6 - 0.15));        \
    }                                                                                                           \
    else                                                                                                        \
    {                                                                                                           \
        albedo[i] = float4(0, 0, 0, 0);                                                                         \
        normal[i] = float3(0, 0, 0);                                                                            \
        masks[i] = NullMask(i);                                                                                 \
    }

    // Derivatives are not available for ray tracing for now
#if defined(SHADER_STAGE_RAY_TRACING)
    float2 dxuv = 0;
    float2 dyuv = 0;
#else
    float2 dxuv = ddx(splatBaseUV);
    float2 dyuv = ddy(splatBaseUV);
#endif

    float3 triW = TriplanarWeights(g_TriNrm);
    float2 blendUV0 = (controlUV.xy * (_Control0_TexelSize.zw - 1.0f) + 0.5f) * _Control0_TexelSize.xy;
    float4 blendMasks0 = SAMPLE_TEXTURE2D(_Control0, sampler_Control0, blendUV0);
    #ifdef _TERRAIN_8_LAYERS
        float2 blendUV1 = (controlUV.xy * (_Control1_TexelSize.zw - 1.0f) + 0.5f) * _Control1_TexelSize.xy;
        float4 blendMasks1 = SAMPLE_TEXTURE2D(_Control1, sampler_Control0, blendUV1);
    #else
        float4 blendMasks1 = float4(0, 0, 0, 0);
    #endif

    SampleResults(0, blendMasks0.x);
    SampleResults(1, blendMasks0.y);
    SampleResults(2, blendMasks0.z);
    SampleResults(3, blendMasks0.w);
    #ifdef _TERRAIN_8_LAYERS
        SampleResults(4, blendMasks1.x);
        SampleResults(5, blendMasks1.y);
        SampleResults(6, blendMasks1.z);
        SampleResults(7, blendMasks1.w);
    #endif

#undef SampleNormal
#undef SampleMasks
#undef SampleResults
#undef TRI_MASKS
#undef TRI_FADE_NORMAL

    float weights[_LAYER_COUNT];
    ZERO_INITIALIZE_ARRAY(float, weights, _LAYER_COUNT);

    #ifdef _MASKMAP
        #if defined(_TERRAIN_BLEND_HEIGHT)
            // Modify blendMask to take into account the height of the layer. Higher height should be more visible.
            float maxHeight = masks[0].z;
            maxHeight = max(maxHeight, masks[1].z);
            maxHeight = max(maxHeight, masks[2].z);
            maxHeight = max(maxHeight, masks[3].z);
            #ifdef _TERRAIN_8_LAYERS
                maxHeight = max(maxHeight, masks[4].z);
                maxHeight = max(maxHeight, masks[5].z);
                maxHeight = max(maxHeight, masks[6].z);
                maxHeight = max(maxHeight, masks[7].z);
            #endif

            // Make sure that transition is not zero otherwise the next computation will be wrong.
            // The epsilon here also has to be bigger than the epsilon in the next computation.
            float transition = max(_HeightTransition, 1e-5);

            // The goal here is to have all but the highest layer at negative heights, then we add the transition so that if the next highest layer is near transition it will have a positive value.
            // Then we clamp this to zero and normalize everything so that highest layer has a value of 1.
            float4 weightedHeights0 = { masks[0].z, masks[1].z, masks[2].z, masks[3].z };
            weightedHeights0 = weightedHeights0 - maxHeight.xxxx;
            // We need to add an epsilon here for active layers (hence the blendMask again) so that at least a layer shows up if everything's too low.
            weightedHeights0 = (max(0, weightedHeights0 + transition) + 1e-6) * blendMasks0;

            #ifdef _TERRAIN_8_LAYERS
                float4 weightedHeights1 = { masks[4].z, masks[5].z, masks[6].z, masks[7].z };
                weightedHeights1 = weightedHeights1 - maxHeight.xxxx;
                weightedHeights1 = (max(0, weightedHeights1 + transition) + 1e-6) * blendMasks1;
            #else
                float4 weightedHeights1 = { 0, 0, 0, 0 };
            #endif

            // Normalize
            float sumHeight = GetSumHeight(weightedHeights0, weightedHeights1);
            blendMasks0 = weightedHeights0 / sumHeight.xxxx;
            #ifdef _TERRAIN_8_LAYERS
                blendMasks1 = weightedHeights1 / sumHeight.xxxx;
            #endif
        #elif defined(_TERRAIN_BLEND_DENSITY)
            // Denser layers are more visible.
            float4 opacityAsDensity0 = saturate((float4(albedo[0].a, albedo[1].a, albedo[2].a, albedo[3].a) - (float4(1.0, 1.0, 1.0, 1.0) - blendMasks0)) * 20.0); // 20.0 is the number of steps in inputAlphaMask (Density mask. We decided 20 empirically)
            opacityAsDensity0 += 0.001f * blendMasks0;      // if all weights are zero, default to what the blend mask says
            float4 useOpacityAsDensityParam0 = { _DiffuseRemapScale0.w, _DiffuseRemapScale1.w, _DiffuseRemapScale2.w, _DiffuseRemapScale3.w }; // 1 is off
            blendMasks0 = lerp(opacityAsDensity0, blendMasks0, useOpacityAsDensityParam0);
            #ifdef _TERRAIN_8_LAYERS
                float4 opacityAsDensity1 = saturate((float4(albedo[4].a, albedo[5].a, albedo[6].a, albedo[7].a) - (float4(1.0, 1.0, 1.0, 1.0) - blendMasks1)) * 20.0); // 20.0 is the number of steps in inputAlphaMask (Density mask. We decided 20 empirically)
                opacityAsDensity1 += 0.001f * blendMasks1;  // if all weights are zero, default to what the blend mask says
                float4 useOpacityAsDensityParam1 = { _DiffuseRemapScale4.w, _DiffuseRemapScale5.w, _DiffuseRemapScale6.w, _DiffuseRemapScale7.w };
                blendMasks1 = lerp(opacityAsDensity1, blendMasks1, useOpacityAsDensityParam1);
            #endif

            // Normalize
            float sumHeight = GetSumHeight(blendMasks0, blendMasks1);
            blendMasks0 = blendMasks0 / sumHeight.xxxx;
            #ifdef _TERRAIN_8_LAYERS
                blendMasks1 = blendMasks1 / sumHeight.xxxx;
            #endif
        #endif // if _TERRAIN_BLEND_HEIGHT
    #endif // if _MASKMAP

    weights[0] = blendMasks0.x;
    weights[1] = blendMasks0.y;
    weights[2] = blendMasks0.z;
    weights[3] = blendMasks0.w;
    #ifdef _TERRAIN_8_LAYERS
        weights[4] = blendMasks1.x;
        weights[5] = blendMasks1.y;
        weights[6] = blendMasks1.z;
        weights[7] = blendMasks1.w;
    #endif

    surfaceData.albedo = 0;
    surfaceData.normalData = 0;
    float3 outMasks = 0;
    UNITY_UNROLL for (int i = 0; i < _LAYER_COUNT; ++i)
    {
        surfaceData.albedo += albedo[i].rgb * weights[i];
        surfaceData.normalData += normal[i].rgb * weights[i]; // no need to normalize
        outMasks += masks[i].xyw * weights[i];
    }
    surfaceData.smoothness = outMasks.z;
    surfaceData.metallic = outMasks.x;
    surfaceData.ao = outMasks.y;
}

void TerrainLitShade(float2 uv, inout TerrainLitSurfaceData surfaceData)
{
    TerrainSplatBlend(uv, uv, surfaceData);
}

void TerrainLitShadeTriplanar(float2 uv, float3 positionOS, float3 normalOS, inout TerrainLitSurfaceData surfaceData)
{
    g_TriPos = positionOS;
    g_TriNrm = normalOS;
#if defined(UNITY_INSTANCING_ENABLED) && !defined(SHADER_STAGE_RAY_TRACING)
    g_TriMetersToUV = 1.0 / max(1.0, _TerrainHeightmapScale.x * (1.0 / _TerrainHeightmapRecipSize.x - 1.0));
    g_TriDpx = ddx(positionOS);
    g_TriDpy = ddy(positionOS);
#else
    g_TriMetersToUV = 0;
    g_TriDpx = g_TriDpy = 0;
#endif
    TerrainSplatBlend(uv, uv, surfaceData);
}

void TerrainLitDebug(float2 uv, uint2 screenSpaceCoords, out float3 baseColor)
{
#ifdef DEBUG_DISPLAY
    if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_CONTROL)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv, _Control0);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER0)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat0_ST.xy + _Splat0_ST.zw, _Splat0);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER1)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat1_ST.xy + _Splat1_ST.zw, _Splat1);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER2)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat2_ST.xy + _Splat2_ST.zw, _Splat2);
    }
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER3)
    {
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat3_ST.xy + _Splat3_ST.zw, _Splat3);
    }
    #ifdef _TERRAIN_8_LAYERS
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER4)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat4_ST.xy + _Splat4_ST.zw, _Splat4);
        }
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER5)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat5_ST.xy + _Splat5_ST.zw, _Splat5);
        }
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER6)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat6_ST.xy + _Splat6_ST.zw, _Splat6);
        }
        else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER7)
        {
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat7_ST.xy + _Splat7_ST.zw, _Splat7);
        }
    #else
        else
        {
            // User is trying to debug layer 4/5/6/7 but this terrain only has 4 layers: let's try to display some basic "invalid" debug info...
            baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_NO_TEX(screenSpaceCoords, uv);
        }
    #endif
#endif
}
