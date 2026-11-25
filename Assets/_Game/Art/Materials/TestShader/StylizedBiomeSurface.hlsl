// Unity Shader Graph custom functions for an opaque HDRP Lit surface.
#ifndef TAN_SHADOW_STYLIZED_BIOME_INCLUDED
#define TAN_SHADOW_STYLIZED_BIOME_INCLUDED

float BiomeHash(float3 cell)
{
    float3 p = frac(cell * float3(0.1031, 0.11369, 0.13787));
    p += dot(p, p.yzx + 19.19);
    return frac((p.x + p.y) * p.z);
}

float BiomeValueNoise(float3 position)
{
    float3 cell = floor(position);
    float3 t = frac(position);
    t = t * t * (3.0 - 2.0 * t);

    float z0y0 = lerp(BiomeHash(cell), BiomeHash(cell + float3(1, 0, 0)), t.x);
    float z0y1 = lerp(BiomeHash(cell + float3(0, 1, 0)), BiomeHash(cell + float3(1, 1, 0)), t.x);
    float z1y0 = lerp(BiomeHash(cell + float3(0, 0, 1)), BiomeHash(cell + float3(1, 0, 1)), t.x);
    float z1y1 = lerp(BiomeHash(cell + float3(0, 1, 1)), BiomeHash(cell + float3(1, 1, 1)), t.x);
    return lerp(lerp(z0y0, z0y1, t.y), lerp(z1y0, z1y1, t.y), t.z);
}

float3 BiomeScaleNormal(float3 normalTS, float strength)
{
    return normalize(float3(normalTS.xy * strength, max(normalTS.z, 0.001)));
}

void ScanSurface_float(
    float2 UV, UnityTexture2D Albedo, UnityTexture2D ScanNormal,
    UnityTexture2D Mask, UnityTexture2D RoughnessMap,
    float AlbedoMipBias, float NormalStrength, float MaskInfluence, float RoughnessMapInfluence,
    float BaseRoughness, float RoughnessOffset, float BaseMetallic, float OcclusionStrength,
    float4 DarkTone, float4 MidTone, float4 LightTone, float PaletteBlend,
    out float3 Color, out float3 Normal, out float3 Response)
{
    float3 albedo = SAMPLE_TEXTURE2D_BIAS(Albedo.tex, Albedo.samplerstate,
        Albedo.GetTransformedUV(UV), AlbedoMipBias).rgb;
    float4 mask = SAMPLE_TEXTURE2D(Mask.tex, Mask.samplerstate, Mask.GetTransformedUV(UV));
    float scanRoughness = SAMPLE_TEXTURE2D(RoughnessMap.tex, RoughnessMap.samplerstate,
        RoughnessMap.GetTransformedUV(UV)).r;
    float4 normalPacked = SAMPLE_TEXTURE2D(ScanNormal.tex, ScanNormal.samplerstate,
        ScanNormal.GetTransformedUV(UV));

    // Shape the palette in perceptual luminance so existing large scan features survive.
    float tone = sqrt(saturate(dot(albedo, float3(0.2126, 0.7152, 0.0722))));
    float3 low = lerp(DarkTone.rgb, MidTone.rgb, smoothstep(0.05, 0.52, tone));
    float3 palette = lerp(low, LightTone.rgb, smoothstep(0.48, 0.95, tone));
    Color = saturate(lerp(albedo, palette, PaletteBlend));
    Normal = BiomeScaleNormal(UnpackNormal(normalPacked), NormalStrength);

    // HDRP Mask Map uses R = metal, G = occlusion, B = detail mask, A = smoothness.
    // A standalone roughness map takes precedence when its influence is one.
    float roughness = lerp(BaseRoughness, 1.0 - mask.a, MaskInfluence);
    roughness = saturate(lerp(roughness, scanRoughness, RoughnessMapInfluence) + RoughnessOffset);
    float occlusion = lerp(1.0, mask.g, MaskInfluence * OcclusionStrength);
    float metallic = lerp(BaseMetallic, mask.r, MaskInfluence);
    Response = float3(roughness, occlusion, metallic);
}

void BiomeWorld_float(
    float3 AbsolutePosition, float3 GeometricNormal, float MacroSize, float PatchSize,
    float4 MacroTintA, float4 MacroTintB, float MacroStrength,
    float3 ScanColor, out float3 Color, out float4 Fields)
{
    float3 p = AbsolutePosition + float3(11.7, 23.6, -41.2);
    float macro = BiomeValueNoise(p / max(MacroSize, 0.05));
    float3 patchPosition = p / max(PatchSize, 0.05);
    float patch = BiomeValueNoise(patchPosition + macro * float3(0.65, -0.42, 0.53));
    float fine = BiomeValueNoise(patchPosition * 3.1 + float3(2.5, 5.7, 13.8));
    float tint = smoothstep(0.18, 0.82, macro);
    float3 multiplyTint = lerp(MacroTintA.rgb, MacroTintB.rgb, tint);
    Color = saturate(ScanColor * lerp(1.0, multiplyTint, MacroStrength));
    float pigment = lerp(fine, macro, 0.32);
    Fields = float4(lerp(patch, fine, 0.23), normalize(GeometricNormal).y, AbsolutePosition.y, pigment);
}

void BiomeCover_float(
    float3 ColorIn, float3 NormalIn, float3 ResponseIn, float4 Fields,
    float4 Tint, float4 AccentTint, float Amount, float PatchFill, float MaxSlope,
    float Height, float HeightBias, float SlopeFeather, float HeightFeather,
    float SurfaceRoughness, float NormalRetention,
    out float3 Color, out float3 Normal, out float3 Response)
{
    float noise = Fields.x;
    float patchThreshold = lerp(1.14, -0.14, PatchFill);
    float patch = smoothstep(patchThreshold - 0.12, patchThreshold + 0.12, noise);

    // Slope uses the mesh normal so heavily textured scans cannot create speckled masks.
    float softAngle = min(MaxSlope + 0.5 * SlopeFeather, 180.0);
    float hardAngle = max(MaxSlope - 0.5 * SlopeFeather, 0.0);
    float lowDot = cos(radians(softAngle));
    float highDot = cos(radians(hardAngle));
    float up = Fields.y + (noise - 0.5) * 0.20;
    float slope = lerp(smoothstep(lowDot, max(highDot, lowDot + 0.0001), up),
        1.0, smoothstep(158.0, 180.0, MaxSlope));

    float halfHeightFade = max(HeightFeather * 0.5, 0.005);
    float elevation = Fields.z + (noise - 0.5) * halfHeightFade;
    float above = smoothstep(Height - halfHeightFade, Height + halfHeightFade, elevation);
    float heightMask = lerp(1.0, HeightBias >= 0.0 ? above : 1.0 - above, abs(HeightBias));
    float coverage = saturate(Amount * patch * slope * heightMask);

    float pigment = smoothstep(0.20, 0.83, Fields.w);
    float3 paint = lerp(Tint.rgb, AccentTint.rgb, pigment);
    float tonalVariation = lerp(0.75, 1.12, smoothstep(0.14, 0.86, noise));
    float baseLuminance = dot(ColorIn, float3(0.2126, 0.7152, 0.0722));
    float materialDetail = lerp(0.86, 1.13, sqrt(saturate(baseLuminance)));
    Color = lerp(ColorIn, saturate(paint * tonalVariation * materialDetail), coverage);
    Normal = BiomeScaleNormal(NormalIn, lerp(1.0, NormalRetention, coverage));
    Response = lerp(ResponseIn, float3(SurfaceRoughness, lerp(ResponseIn.y, 1.0, 0.25), 0.0), coverage);
}

void BiomeLitOutputs_float(float3 Response, out float Smoothness, out float Occlusion, out float Metallic)
{
    Smoothness = 1.0 - saturate(Response.x);
    Occlusion = saturate(Response.y);
    Metallic = saturate(Response.z);
}

#endif
