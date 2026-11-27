#ifndef QINGLONG_SURFACE_INCLUDED
#define QINGLONG_SURFACE_INCLUDED

#if defined(_QINGLONG_GROUND)

float3 QinglongTileHash(float2 cell)
{
    float3 hash = frac(cell.xyx * float3(0.1031, 0.1030, 0.0973));
    hash += dot(hash, hash.yxz + 33.33);
    return frac((hash.xxy + hash.yzz) * hash.zyx);
}

float3 QinglongGroundNoise(float2 position)
{
    const float2 cell = floor(position);
    float2 blend = frac(position);
    blend = blend * blend * (3.0 - 2.0 * blend);
    return lerp(lerp(QinglongTileHash(cell), QinglongTileHash(cell + float2(1, 0)), blend.x),
                lerp(QinglongTileHash(cell + float2(0, 1)), QinglongTileHash(cell + 1.0), blend.x), blend.y);
}

// Use the same rotated coordinates for all three maps so their details stay aligned.
void QinglongSampleTile(float2 uv, float2 dx, float2 dy, float2 cell,
                        out float3 albedo, out float2 gradient, out float2 aoSmoothness)
{
    const float3 variation = QinglongTileHash(cell);
    float sine, cosine;
    sincos(variation.z * TWO_PI, sine, cosine);
    const float scale = lerp(0.88, 1.12, variation.y);
    const float2x2 rotation = float2x2(cosine, -sine, sine, cosine);
    const float2 tileUV = mul(rotation, uv - cell) * scale + variation.xy;

    #if defined(SHADER_STAGE_RAY_TRACING)
        #define QINGLONG_SAMPLE(map) SAMPLE_TEXTURE2D_LOD(map, sampler##map, tileUV, 0)
    #else
        const float2 tileDx = mul(rotation, dx) * scale;
        const float2 tileDy = mul(rotation, dy) * scale;
        #define QINGLONG_SAMPLE(map) SAMPLE_TEXTURE2D_GRAD(map, sampler##map, tileUV, tileDx, tileDy)
    #endif
    albedo = QINGLONG_SAMPLE(_BaseColorMap).rgb;
    const float3 normal = UnpackNormalScale(QINGLONG_SAMPLE(_NormalMap), _NormalScale);
    gradient = mul(transpose(rotation), -normal.xy / max(normal.z, 0.25));
    aoSmoothness = QINGLONG_SAMPLE(_MaskMap).ga;
    #undef QINGLONG_SAMPLE
}

void QinglongApplySurface(FragInputs input, inout SurfaceData surface, inout float3 normalTS, inout float3 bentNormalTS)
{
    const float2 uv = GetAbsolutePositionWS(input.positionRWS).xz * _BaseColorMap_ST.xy + _BaseColorMap_ST.zw;
    // Split a regular grid into equilateral triangles. Across each edge, the two
    // shared vertices retain their samples while the third one's weight reaches zero.
    const float3 variation = QinglongGroundNoise(uv * 0.21);
    const float2 grid = mul(float2x2(1.0, -0.57735027, 0.0, 1.15470054), uv * 0.64) + (variation.xy - 0.5) * 0.72;
    const float2 cell = floor(grid);
    const float2 f = frac(grid);
    const float lastWeight = 1.0 - f.x - f.y;
    const bool upper = lastWeight < 0.0;
    float3 weights = upper ? float3(1.0 - f.y, 1.0 - f.x, -lastWeight) : float3(f.x, f.y, lastWeight);
    weights = weights * weights;
    weights /= dot(weights, float3(1, 1, 1));

    #if defined(SHADER_STAGE_RAY_TRACING)
        const float2 dx = 0.0;
        const float2 dy = 0.0;
    #else
        const float2 dx = ddx(uv);
        const float2 dy = ddy(uv);
    #endif

    float3 a, b, c;
    float2 ga, gb, gc, ma, mb, mc;
    QinglongSampleTile(uv, dx, dy, cell + float2(1, 0), a, ga, ma);
    QinglongSampleTile(uv, dx, dy, cell + float2(0, 1), b, gb, mb);
    QinglongSampleTile(uv, dx, dy, cell + (upper ? 1.0 : 0.0), c, gc, mc);

    const float2 gradient = ga * weights.x + gb * weights.y + gc * weights.z;
    const float2 mask = ma * weights.x + mb * weights.y + mc * weights.z;
    const float3 vertexNormal = input.tangentToWorld[2];
    const float3 worldGradient = SurfaceGradientFromVolumeGradient(vertexNormal, float3(gradient.x, 0, gradient.y));
    #if defined(SURFACE_GRADIENT)
        normalTS = worldGradient;
    #else
        normalTS = TransformWorldToTangent(normalize(vertexNormal - worldGradient), input.tangentToWorld);
    #endif

    surface.baseColor = (a * weights.x + b * weights.y + c * weights.z) * _BaseColor.rgb * lerp(0.82, 1.09, variation.z);
    surface.metallic = 0;
    surface.ambientOcclusion = lerp(_AORemapMin, _AORemapMax, mask.x);
    surface.perceptualSmoothness = lerp(_SmoothnessRemapMin, _SmoothnessRemapMax, mask.y);
    bentNormalTS = normalTS;
}

#else

float _QinglongRockToneStrength;

void QinglongApplySurface(FragInputs input, inout SurfaceData surface, inout float3 normalTS, inout float3 bentNormalTS)
{
    const float3 color = surface.baseColor;
    const float luminance = dot(color, float3(0.2126, 0.7152, 0.0722));
    const float peak = max(Max3(color.r, color.g, color.b), 0.001);
    const float greenVsRed = (color.g - color.r) / peak;
    const float greenVsBlue = (color.g - color.b) / peak;
    const float moss = smoothstep(-0.14, 0.10, greenVsRed) * smoothstep(0.065, 0.27, greenVsBlue);

    const float slateLuminance = 0.025 + 0.68 * luminance;
    const float3 slate = slateLuminance * float3(0.96, 1.0, 1.035);
    const float3 graded = max(0.0, slate + 0.26 * (color - luminance));
    surface.baseColor = lerp(color, graded, saturate(_QinglongRockToneStrength) * (1.0 - 0.97 * moss));
}

#endif

#endif
