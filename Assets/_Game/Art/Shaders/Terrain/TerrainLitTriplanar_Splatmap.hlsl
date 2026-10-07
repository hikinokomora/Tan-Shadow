// Выборка слоёв террейна для TanShadow/TerrainLitTriplanar.
// Основа — HDRP TerrainLit_Splatmap.hlsl (4 или 8 слоёв, смешивание по высоте/плотности).
// Добавлено: hex-тайлинг (случайный поворот и сдвиг тайлов с плавными стыками),
// трипланарная проекция на крутых склонах и макровариация цвета.

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

#ifdef OVERRIDE_SPLAT_SAMPLER_NAME
    #define sampler_Splat0 OVERRIDE_SPLAT_SAMPLER_NAME
    SAMPLER(OVERRIDE_SPLAT_SAMPLER_NAME);
#endif

// ---------------------------------------------------------------------------------------------
// Параметры (подобраны на глаз)
#define HEX_CELL_SCALE      1.35    // размер ячейки hex-сетки в тайлах текстуры
#define HEX_BLEND_CONTRAST  5.0     // чем больше, тем уже полоса смешивания соседних тайлов
#define TRI_STEEP_START     0.92    // доля вертикальной проекции, ниже которой включается трипланар
#define MACRO_UV_SCALE      0.071   // частота крупных пятен цвета относительно тайла

// Трипланар: позиция в пространстве террейна (м), нормаль, производные позиции
static float3 g_TriPos = 0;
static float3 g_TriNrm = float3(0, 1, 0);
static float  g_TriMetersToUV = 0;      // 1 / ширина террейна, м (0 — трипланар выключен)
static float3 g_TriDpx = 0, g_TriDpy = 0;

float GetSumHeight(float4 heights0, float4 heights1)
{
    float sumHeight = heights0.x + heights0.y + heights0.z + heights0.w;
    #ifdef _TERRAIN_8_LAYERS
        sumHeight += heights1.x + heights1.y + heights1.z + heights1.w;
    #endif
    return sumHeight;
}

float4 RemapMasks(float4 masks, float blendMask, float4 remapOffset, float4 remapScale)
{
    float4 ret = masks;
    ret.b *= blendMask; // height needs to be weighted before remapping
    ret = ret * remapScale + remapOffset;
    return ret;
}

float3 TriplanarWeights(float3 n)
{
    float3 w = pow(abs(n), 6.0);
    return w / (w.x + w.y + w.z);
}

// ---------------------------------------------------------------------------------------------
// Hex-тайлинг: точка UV лежит в треугольнике сетки; каждая из трёх вершин даёт свою копию UV
// со случайным поворотом и сдвигом, результаты смешиваются по барицентрическим весам.

struct HexTile
{
    float3 w;           // веса трёх выборок
    float2 uv[3];       // повёрнутые и сдвинутые UV
    float2 cs[3];       // cos/sin поворота каждой выборки (для градиентов и нормалей)
};

float2 HexHash(int2 p, float seed)
{
    uint2 q = uint2(p + 32768) * uint2(1597334673u, 3812015801u) + (uint)(seed * 7919.0);
    uint n = (q.x ^ q.y) * 1597334673u;
    uint2 r = uint2(n, n * 16807u) * uint2(3812015801u, 1597334673u);
    return float2(r & 0xFFFFu) / 65535.0;
}

HexTile ComputeHexTile(float2 uv, float seed)
{
    HexTile t;
    // треугольная сетка в «скошенных» координатах
    float2 g = uv * (2.0 * 1.7320508 / HEX_CELL_SCALE);
    float2 skewed = float2(g.x - 0.57735027 * g.y, 1.15470054 * g.y);
    int2 base = (int2)floor(skewed);
    float3 f = float3(frac(skewed), 0);
    f.z = 1.0 - f.x - f.y;

    int2 v0, v1, v2;
    float3 b;
    if (f.z > 0) { b = float3(f.z, f.y, f.x);              v0 = base;              v1 = base + int2(0, 1); v2 = base + int2(1, 0); }
    else         { b = float3(-f.z, 1.0 - f.y, 1.0 - f.x); v0 = base + int2(1, 1); v1 = base + int2(1, 0); v2 = base + int2(0, 1); }

    float3 w = pow(max(b, 1e-4), HEX_BLEND_CONTRAST);
    t.w = w / (w.x + w.y + w.z);

    int2 v[3] = { v0, v1, v2 };
    [unroll] for (int k = 0; k < 3; k++)
    {
        float2 h = HexHash(v[k], seed);
        float a = h.x * 6.2831853;
        float2 cs = float2(cos(a), sin(a));
        t.cs[k] = cs;
        t.uv[k] = float2(cs.x * uv.x - cs.y * uv.y, cs.y * uv.x + cs.x * uv.y) + h * 17.0;
    }
    return t;
}

float2 RotateCS(float2 v, float2 cs) { return float2(cs.x * v.x - cs.y * v.y, cs.y * v.x + cs.x * v.y); }
float2 RotateCSInv(float2 v, float2 cs) { return float2(cs.x * v.x + cs.y * v.y, -cs.y * v.x + cs.x * v.y); }

float4 HexSample(TEXTURE2D_PARAM(tex, samp), HexTile t, float2 dx, float2 dy)
{
    float4 c = 0;
    [unroll] for (int k = 0; k < 3; k++)
        c += SAMPLE_TEXTURE2D_GRAD(tex, samp, t.uv[k], RotateCS(dx, t.cs[k]), RotateCS(dy, t.cs[k])) * t.w[k];
    return c;
}

#ifdef _NORMALMAP
float3 DecodeLayerNormal(float4 nrm, float scale)
{
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

// Нормаль каждой выборки поворачивается обратно в систему тайла (xy — наклон или градиент)
float3 HexSampleNormal(TEXTURE2D_PARAM(tex, samp), HexTile t, float2 dx, float2 dy, float scale)
{
    float3 n = 0;
    [unroll] for (int k = 0; k < 3; k++)
    {
        float3 s = DecodeLayerNormal(SAMPLE_TEXTURE2D_GRAD(tex, samp, t.uv[k], RotateCS(dx, t.cs[k]), RotateCS(dy, t.cs[k])), scale);
        s.xy = RotateCSInv(s.xy, t.cs[k]);
        n += s * t.w[k];
    }
    return n;
}
#endif

#ifdef SURFACE_GRADIENT
    #define FADE_LAYER_NORMAL(n, k) ((n) * (k))
#else
    #define FADE_LAYER_NORMAL(n, k) lerp(float3(0, 0, 1), (n), (k))
#endif

// ---------------------------------------------------------------------------------------------

void TerrainSplatBlend(float2 controlUV, float2 splatBaseUV, inout TerrainLitSurfaceData surfaceData)
{
    float4 albedo[_LAYER_COUNT];
    float3 normal[_LAYER_COUNT];
    float4 masks[_LAYER_COUNT];

#if defined(SHADER_STAGE_RAY_TRACING)
    float2 dxuv = 0;
    float2 dyuv = 0;
#else
    float2 dxuv = ddx(splatBaseUV);
    float2 dyuv = ddy(splatBaseUV);
#endif

    float3 triW = TriplanarWeights(g_TriNrm);
    bool steep = g_TriMetersToUV > 0 && triW.y < TRI_STEEP_START;

#ifdef _NORMALMAP
    #define SampleNormalTop(i) HexSampleNormal(TEXTURE2D_ARGS(_Normal##i, sampler_Splat0), ht, dxT, dyT, _NormalScale##i)
#else
    #define SampleNormalTop(i) float3(0, 0, 0)
#endif

#define DefaultMask(i) float4(_Metallic##i, _MaskMapRemapOffset##i.y + _MaskMapRemapScale##i.y, _MaskMapRemapOffset##i.z + 0.5 * _MaskMapRemapScale##i.z, albedo[i].a * _Smoothness##i)

#ifdef _MASKMAP
    #define SampleMasks(i, blendMask, rawMask) lerp(DefaultMask(i), RemapMasks(rawMask, blendMask, _MaskMapRemapOffset##i, _MaskMapRemapScale##i), _LayerHasMask##i)
    #define NullMask(i)                        float4(0, 1, _MaskMapRemapOffset##i.z, 0) // only height matters when weight is zero.
    #define RawMaskTop(i)                      HexSample(TEXTURE2D_ARGS(_Mask##i, sampler_Splat0), ht, dxT, dyT)
    #define RawMaskSide(i, t, dx, dy)          HexSample(TEXTURE2D_ARGS(_Mask##i, sampler_Splat0), t, dx, dy)
#else
    #define SampleMasks(i, blendMask, rawMask) DefaultMask(i)
    #define NullMask(i)                        float4(0, 1, 0, 0)
    #define RawMaskTop(i)                      float4(0, 1, 0, 0)
    #define RawMaskSide(i, t, dx, dy)          float4(0, 1, 0, 0)
#endif

#define SampleResults(i, mask)                                                                                          \
    UNITY_BRANCH if (mask > 0)                                                                                          \
    {                                                                                                                   \
        float2 uvT = splatBaseUV * _Splat##i##_ST.xy + _Splat##i##_ST.zw;                                               \
        float2 dxT = dxuv * _Splat##i##_ST.x;                                                                           \
        float2 dyT = dyuv * _Splat##i##_ST.y;                                                                           \
        HexTile ht = ComputeHexTile(uvT, i * 3.0 + 1.0);                                                                \
        albedo[i] = HexSample(TEXTURE2D_ARGS(_Splat##i, sampler_Splat0), ht, dxT, dyT);                                 \
        normal[i] = SampleNormalTop(i);                                                                                 \
        float4 rawMask = RawMaskTop(i);                                                                                 \
        UNITY_BRANCH if (steep)                                                                                         \
        {                                                                                                               \
            float s = _Splat##i##_ST.x * g_TriMetersToUV;                                                               \
            float2 dxX = g_TriDpx.zy * s, dyX = g_TriDpy.zy * s;                                                        \
            float2 dxZ = g_TriDpx.xy * s, dyZ = g_TriDpy.xy * s;                                                        \
            HexTile hx = ComputeHexTile(g_TriPos.zy * s, i * 3.0 + 11.0);                                               \
            HexTile hz = ComputeHexTile(g_TriPos.xy * s, i * 3.0 + 23.0);                                               \
            albedo[i] = HexSample(TEXTURE2D_ARGS(_Splat##i, sampler_Splat0), hx, dxX, dyX) * triW.x                     \
                      + albedo[i] * triW.y                                                                              \
                      + HexSample(TEXTURE2D_ARGS(_Splat##i, sampler_Splat0), hz, dxZ, dyZ) * triW.z;                    \
            rawMask = RawMaskSide(i, hx, dxX, dyX) * triW.x + rawMask * triW.y + RawMaskSide(i, hz, dxZ, dyZ) * triW.z;  \
            normal[i] = FADE_LAYER_NORMAL(normal[i], triW.y);                                                           \
        }                                                                                                               \
        masks[i] = SampleMasks(i, mask, rawMask);                                                                       \
        float macro = dot(SAMPLE_TEXTURE2D_GRAD(_Splat##i, sampler_Splat0, uvT * MACRO_UV_SCALE,                        \
                          dxT * MACRO_UV_SCALE, dyT * MACRO_UV_SCALE).rgb, float3(0.3, 0.59, 0.11));                    \
        albedo[i].rgb *= _DiffuseRemapScale##i.xyz * lerp(0.8, 1.2, saturate(macro * 1.6 - 0.15));                      \
    }                                                                                                                   \
    else                                                                                                                \
    {                                                                                                                   \
        albedo[i] = float4(0, 0, 0, 0);                                                                                 \
        normal[i] = float3(0, 0, 0);                                                                                    \
        masks[i] = NullMask(i);                                                                                         \
    }

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

#undef SampleNormalTop
#undef SampleMasks
#undef NullMask
#undef RawMaskTop
#undef RawMaskSide
#undef DefaultMask
#undef SampleResults

    float weights[_LAYER_COUNT];
    ZERO_INITIALIZE_ARRAY(float, weights, _LAYER_COUNT);

    #ifdef _MASKMAP
        #if defined(_TERRAIN_BLEND_HEIGHT)
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

            float transition = max(_HeightTransition, 1e-5);

            float4 weightedHeights0 = { masks[0].z, masks[1].z, masks[2].z, masks[3].z };
            weightedHeights0 = weightedHeights0 - maxHeight.xxxx;
            weightedHeights0 = (max(0, weightedHeights0 + transition) + 1e-6) * blendMasks0;

            #ifdef _TERRAIN_8_LAYERS
                float4 weightedHeights1 = { masks[4].z, masks[5].z, masks[6].z, masks[7].z };
                weightedHeights1 = weightedHeights1 - maxHeight.xxxx;
                weightedHeights1 = (max(0, weightedHeights1 + transition) + 1e-6) * blendMasks1;
            #else
                float4 weightedHeights1 = { 0, 0, 0, 0 };
            #endif

            float sumHeight = GetSumHeight(weightedHeights0, weightedHeights1);
            blendMasks0 = weightedHeights0 / sumHeight.xxxx;
            #ifdef _TERRAIN_8_LAYERS
                blendMasks1 = weightedHeights1 / sumHeight.xxxx;
            #endif
        #elif defined(_TERRAIN_BLEND_DENSITY)
            float4 opacityAsDensity0 = saturate((float4(albedo[0].a, albedo[1].a, albedo[2].a, albedo[3].a) - (float4(1.0, 1.0, 1.0, 1.0) - blendMasks0)) * 20.0);
            opacityAsDensity0 += 0.001f * blendMasks0;
            float4 useOpacityAsDensityParam0 = { _DiffuseRemapScale0.w, _DiffuseRemapScale1.w, _DiffuseRemapScale2.w, _DiffuseRemapScale3.w };
            blendMasks0 = lerp(opacityAsDensity0, blendMasks0, useOpacityAsDensityParam0);
            #ifdef _TERRAIN_8_LAYERS
                float4 opacityAsDensity1 = saturate((float4(albedo[4].a, albedo[5].a, albedo[6].a, albedo[7].a) - (float4(1.0, 1.0, 1.0, 1.0) - blendMasks1)) * 20.0);
                opacityAsDensity1 += 0.001f * blendMasks1;
                float4 useOpacityAsDensityParam1 = { _DiffuseRemapScale4.w, _DiffuseRemapScale5.w, _DiffuseRemapScale6.w, _DiffuseRemapScale7.w };
                blendMasks1 = lerp(opacityAsDensity1, blendMasks1, useOpacityAsDensityParam1);
            #endif

            float sumHeight = GetSumHeight(blendMasks0, blendMasks1);
            blendMasks0 = blendMasks0 / sumHeight.xxxx;
            #ifdef _TERRAIN_8_LAYERS
                blendMasks1 = blendMasks1 / sumHeight.xxxx;
            #endif
        #endif
    #endif

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
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv, _Control0);
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER0)
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat0_ST.xy + _Splat0_ST.zw, _Splat0);
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER1)
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat1_ST.xy + _Splat1_ST.zw, _Splat1);
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER2)
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat2_ST.xy + _Splat2_ST.zw, _Splat2);
    else if (_DebugMipMapModeTerrainTexture == DEBUGMIPMAPMODETERRAINTEXTURE_LAYER3)
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_TEX(screenSpaceCoords, uv * _Splat3_ST.xy + _Splat3_ST.zw, _Splat3);
    else
        baseColor = GET_TEXTURE_STREAMING_DEBUG_FOR_TERRAIN_NO_TEX(screenSpaceCoords, uv);
#endif
}
