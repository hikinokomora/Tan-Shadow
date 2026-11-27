#ifndef SHADOWTAN_AUTUMN_WIND_INCLUDED
#define SHADOWTAN_AUTUMN_WIND_INCLUDED

float4 _AutumnWindDirection;
float _AutumnWindStrength;
float _AutumnWindSpeed;
float _AutumnWindFrequency;
float _AutumnWindGust;
float4 _AutumnWindEditorClock;
float4 _AutumnImpulseCenter;
float4 _AutumnImpulseDirection;

float AutumnWindWave(float2 worldXZ, float noise, float time, float speedMultiplier)
{
    float clock = lerp(time, _AutumnWindEditorClock.x, _AutumnWindEditorClock.y);
    float phase = dot(worldXZ, _AutumnWindDirection.xz) * _AutumnWindFrequency
                + clock * _AutumnWindSpeed * speedMultiplier + noise + _AutumnWindGust * 0.45;
    return (sin(phase) + 0.35 * sin(phase * 2.3 + 1.7)) / 1.35;
}

float3 AutumnWindDisplace(float3 positionOS, float sway)
{
    float activation = smoothstep(0.0, 0.25, _AutumnWindStrength);
    float3 offsetWS = _AutumnWindDirection.xyz * (sway * activation);
    return positionOS + TransformWorldToObjectDir(offsetWS, false);
}

float3 AutumnBambooImpulse(float3 positionOS, float2 plantPoint, float heightMask)
{
    if (_AutumnImpulseCenter.w <= 0.0)
        return positionOS;

    float2 delta = plantPoint - _AutumnImpulseCenter.xy;
    float distance = length(delta);
    float falloff = 1.0 - smoothstep(_AutumnImpulseCenter.z * 0.18, _AutumnImpulseCenter.z, distance);
    float2 heading = _AutumnImpulseDirection.xy;
    float2 radial = delta / max(distance, 0.05);
    float2 side = float2(-heading.y, heading.x);
    float leap = step(0.01, dot(heading, heading));
    float2 axis = lerp(radial, side * (dot(side, delta) < 0.0 ? -1.0 : 1.0), leap);
    float rhythm = lerp(0.5 + 0.5 * sin(_AutumnImpulseDirection.z * 18.0), 1.0, leap);
    float2 offset = axis * falloff * heightMask * rhythm * _AutumnImpulseCenter.w;
    return positionOS + TransformWorldToObjectDir(float3(offset.x, 0, offset.y), false);
}

void AutumnGrass_float(float3 PositionOS, float4 Color, float Time, out float3 BentPositionOS)
{
    // Red stores normalized blade height, and alpha stores height above the root in 1.6 m units.
    // Keep enough movement on the short grass that makes up the banks beside the path.
    float heightMask = saturate(Color.r * 0.8 + Color.a * 0.2);
    float2 worldXZ = GetAbsolutePositionWS(TransformObjectToWorld(PositionOS)).xz;
    float phaseOffset = dot(Color.gb, float2(1.8, 1.2));
    float wave = AutumnWindWave(worldXZ, phaseOffset, Time, 1.4);
    float amplitude = lerp(0.06, 0.10, _AutumnWindStrength);
    BentPositionOS = AutumnWindDisplace(PositionOS, wave * heightMask * amplitude);
}

float3 AutumnBamboo(float3 positionOS, float time, bool hasLeaves)
{
    // All three LOD meshes share one root and pose, including at LOD transitions.
    float2 rootXZ = GetAbsolutePositionWS(TransformObjectToWorld(float3(0, 0, 0))).xz;
    float phaseOffset = frac(dot(rootXZ, float2(0.7548777, 0.5698403))) * 1.4;
    float wave = AutumnWindWave(rootXZ, phaseOffset, time, 0.7);
    float heightMask = saturate(positionOS.y / 15.0);
    float bend = heightMask * lerp(1.65, 1.0, heightMask);
    float amplitude = lerp(0.19, 0.37, _AutumnWindStrength);
    float sway = wave * bend * amplitude;

    if (hasLeaves)
    {
        float clock = lerp(time, _AutumnWindEditorClock.x, _AutumnWindEditorClock.y);
        float flutterPhase = clock * _AutumnWindSpeed * 2.65 + dot(rootXZ, float2(0.24, 0.18))
                           + dot(positionOS.xz, float2(1.25, 1.8)) + positionOS.y * 0.38;
        float flexibility = smoothstep(0.005, 0.13, heightMask);
        float flutterAmplitude = lerp(0.035, 0.08, _AutumnWindStrength);
        sway += sin(flutterPhase) * flexibility * flutterAmplitude;
    }

    return AutumnBambooImpulse(AutumnWindDisplace(positionOS, sway), rootXZ, bend);
}

void AutumnBambooLeaf_float(float3 PositionOS, float Time, out float3 BentPositionOS)
{
    BentPositionOS = AutumnBamboo(PositionOS, Time, true);
}

void AutumnBambooCulm_float(float3 PositionOS, float Time, out float3 BentPositionOS)
{
    BentPositionOS = AutumnBamboo(PositionOS, Time, false);
}

float AutumnCardHeight(float v)
{
    // VegetationAtlas.Card uses a 2-pixel gutter on each end of a 256-pixel tile.
    return saturate((v * 256.0 - 2.0) / 252.0);
}

void AutumnDenseGrass_float(float3 PositionOS, float2 UV, float Time, out float3 BentPositionOS)
{
    float2 worldXZ = GetAbsolutePositionWS(TransformObjectToWorld(PositionOS)).xz;
    float wave = AutumnWindWave(worldXZ, floor(UV.x * 4.0) * 0.28, Time, 1.15);
    float amplitude = lerp(0.06, 0.10, _AutumnWindStrength);
    BentPositionOS = AutumnWindDisplace(PositionOS, wave * AutumnCardHeight(UV.y) * amplitude);
}

float AutumnBakedBambooSway(float2 worldXZ, float heightMask, float time)
{
    float wave = AutumnWindWave(worldXZ, 0.0, time, 0.7);
    float bend = heightMask * lerp(1.9, 1.0, heightMask);
    return wave * bend * lerp(0.19, 0.36, _AutumnWindStrength);
}

void AutumnForestCulm_float(float3 PositionOS, float2 UV, float3 NormalOS, float Time,
    out float3 BentPositionOS)
{
    // Each culm starts in atlas V=[0,1). The cap uses its own UVs and faces up.
    // The merged renderers are static-batchable, so use absolute height across draw paths.
    float cap = step(0.75, NormalOS.y);
    float aboveRoot = max(step(1.0, UV.y), cap);
    float3 world = GetAbsolutePositionWS(TransformObjectToWorld(PositionOS));
    float height = lerp(max(world.y, (UV.y - 0.5) * 0.49), world.y, cap);
    float heightMask = saturate(height / 15.0) * aboveRoot;
    BentPositionOS = AutumnBambooImpulse(AutumnWindDisplace(PositionOS, AutumnBakedBambooSway(world.xz, heightMask, Time)), world.xz, heightMask);
}

void AutumnForestLeaves_float(float3 PositionOS, float2 UV, float Time, out float3 BentPositionOS)
{
    float3 world = GetAbsolutePositionWS(TransformObjectToWorld(PositionOS));
    float heightMask = saturate(max(world.y, 1.3) / 15.0);
    float bend = AutumnBakedBambooSway(world.xz, heightMask, Time);
    float clock = lerp(Time, _AutumnWindEditorClock.x, _AutumnWindEditorClock.y);
    float phase = dot(world.xz, float2(1.45, 0.88)) + world.y * 0.52 + clock * _AutumnWindSpeed * 2.45;
    float flutter = sin(phase) * AutumnCardHeight(UV.y) * lerp(0.04, 0.09, _AutumnWindStrength);
    BentPositionOS = AutumnBambooImpulse(AutumnWindDisplace(PositionOS, bend + flutter), world.xz, heightMask);
}

void AutumnBakedFoliage_float(float2 UV, UnityTexture2D BaseMap, float4 Tint,
    float4 EmissiveTint, out float3 BaseColor, out float Alpha, out float3 Emission)
{
    float4 texel = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, BaseMap.GetTransformedUV(UV));
    BaseColor = texel.rgb * Tint.rgb;
    Alpha = texel.a * Tint.a;
    Emission = texel.rgb * EmissiveTint.rgb;
}

void AutumnFoliageBase_float(float2 UV, UnityTexture2D BaseMap, float4 Tint,
    out float3 BaseColor, out float Alpha)
{
    float4 texel = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, BaseMap.GetTransformedUV(UV)) * Tint;
    BaseColor = texel.rgb;
    Alpha = texel.a;
}

void AutumnFoliageSurface_float(float2 UV, UnityTexture2D BaseMap, UnityTexture2D NormalMap,
    UnityTexture2D MaskMap, float NormalScale, float MetallicMin, float MetallicMax, float OcclusionMin,
    float OcclusionMax, float SmoothnessMin, float SmoothnessMax,
    out float3 NormalTS, out float Metallic, out float Occlusion, out float Smoothness)
{
    // HDRP Lit's UV0 mapping shares the Base Map scale and offset with the normal and mask maps.
    float2 uv = BaseMap.GetTransformedUV(UV);
    float4 mask = SAMPLE_TEXTURE2D(MaskMap.tex, MaskMap.samplerstate, uv);
    NormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, uv), NormalScale);
    Metallic = lerp(MetallicMin, MetallicMax, mask.r);
    Occlusion = lerp(OcclusionMin, OcclusionMax, mask.g);
    Smoothness = lerp(SmoothnessMin, SmoothnessMax, mask.a);
}

#endif
