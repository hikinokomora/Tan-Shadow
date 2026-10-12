// Dark Fantasy fullscreen post process for HDRP (After Post Process injection).
// Driven by DarkFantasyPostProcess.cs volume override. Lives in Resources so Shader.Find works in builds.
Shader "Hidden/Shader/DarkFantasy"
{
    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord   : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
        output.texcoord = GetFullScreenTriangleTexCoord(input.vertexID);
        return output;
    }

    // Camera color (texture array in HDRP) and intermediate plain 2D temp RTs
    TEXTURE2D_X(_MainTex);
    TEXTURE2D(_DF_BlitTex);
    TEXTURE2D(_DF_HighTex);
    TEXTURE2D(_DF_BloomTex);
    TEXTURE2D(_DF_TensorTex);
    TEXTURE2D(_DF_PaintTex);
    float4 _DF_BlitTexelSize;
    float4 _DF_PaintTexelSize;
    float4 _DF_BlurDir;

    float  _Intensity;
    // Grade
    float  _Exposure;
    float  _Contrast;
    float  _ContrastPivot;
    float  _BlackCrush;
    float  _BlackLift;
    float  _Saturation;
    float  _NonRedDesat;
    float  _RedBoost;
    float  _KeepHue;
    float  _KeepHueWidth;
    float4 _ShadowTint;      // sRGB values (passed as vector, no linear conversion)
    float  _ShadowTintStrength;
    float4 _HighlightTint;   // sRGB
    float  _HighlightTintStrength;
    float  _ToneBalance;
    float4 _ColorFilter;     // sRGB
    float  _ColorFilterStrength;
    // Moonlight protection
    float  _MoonHue;
    float  _MoonHueWidth;
    float  _MoonPreserve;
    float  _MoonTintProtect;
    // Glow
    float  _BloomIntensity;
    float  _BloomThreshold;
    float  _BloomKnee;
    float  _BloomScatter;
    float4 _BloomTint;       // linear
    float  _HalationIntensity;
    float4 _HalationColor;   // linear
    // Oil paint
    float  _PainterlyStrength;
    float  _PainterlyRadius;
    float  _PaintSharpness;
    float  _PaintHardness;
    float  _PaintAnisotropy;
    float  _ImpastoStrength;
    float  _BrushBristles;
    float  _StrokeLength;
    float  _CanvasStrength;
    float  _CanvasScale;
    // Lens
    float  _ChromaticAberration;
    float  _VignetteIntensity;
    float  _VignetteSmoothness;
    float4 _VignetteColor;   // sRGB
    // Grain
    float  _GrainIntensity;
    float  _GrainSize;
    float  _GrainResponse;
    float  _GrainColor;
    float  _GrainSeed;

    float3 SampleSource(float2 uv)
    {
        return SAMPLE_TEXTURE2D_X_LOD(_MainTex, s_linear_clamp_sampler, ClampAndScaleUVForBilinearPostProcessTexture(uv), 0).rgb;
    }

    float3 SampleSourceSRGB(float2 uv)
    {
        return LinearToSRGB(saturate(SampleSource(uv)));
    }

    float3 SampleBlit(float2 uv)
    {
        return SAMPLE_TEXTURE2D_LOD(_DF_BlitTex, s_linear_clamp_sampler, uv, 0).rgb;
    }

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float ValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float a = Hash12(i);
        float b = Hash12(i + float2(1, 0));
        float c = Hash12(i + float2(0, 1));
        float d = Hash12(i + float2(1, 1));
        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    float3 ContrastCurve(float3 x, float k, float p)
    {
        float3 lo = p * pow(saturate(x / p), k);
        float3 hi = 1.0 - (1.0 - p) * pow(saturate((1.0 - x) / (1.0 - p)), k);
        return lerp(hi, lo, step(x, p));
    }

    float3 Overlay(float3 a, float3 b)
    {
        return lerp(2.0 * a * b, 1.0 - 2.0 * (1.0 - a) * (1.0 - b), step(0.5, a));
    }

    // Direction along which the image changes least (edge flow), from the smoothed structure tensor
    float2 FlowDirection(float3 t)
    {
        float disc = sqrt(max(0.0, (t.y - t.x) * (t.y - t.x) + 4.0 * t.z * t.z));
        float l1 = 0.5 * (t.y + t.x + disc);
        float2 v = float2(l1 - t.x, -t.z);
        return dot(v, v) > 1e-10 ? normalize(v) : float2(0.0, 1.0);
    }

    // ---------------- Bloom chain ----------------

    // Pass 0: bright-pass + downsample from camera color into half res
    float4 FragPrefilter(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float2 uv = i.texcoord;
        float2 d = _PostProcessScreenSize.zw;
        float3 c = SampleSource(uv + d * float2(-1, -1)) + SampleSource(uv + d * float2(1, -1))
                 + SampleSource(uv + d * float2(-1,  1)) + SampleSource(uv + d * float2(1,  1));
        c = min(c * 0.25, 8.0);

        // threshold measured in perceptual space so the slider feels linear
        float3 cs = LinearToSRGB(saturate(c));
        float br = max(cs.r, max(cs.g, cs.b));
        float knee = _BloomThreshold * _BloomKnee + 1e-5;
        float soft = clamp(br - _BloomThreshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee);
        float contrib = max(soft, br - _BloomThreshold) / max(br, 1e-4);
        return float4(c * contrib, 1.0);
    }

    // Pass 1: downsample
    float4 FragDownsample(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float2 t = _DF_BlitTexelSize.xy;
        float3 c = SampleBlit(uv) * 0.5;
        c += SampleBlit(uv + t * float2(-1, -1)) * 0.125;
        c += SampleBlit(uv + t * float2( 1, -1)) * 0.125;
        c += SampleBlit(uv + t * float2(-1,  1)) * 0.125;
        c += SampleBlit(uv + t * float2( 1,  1)) * 0.125;
        return float4(c, 1.0);
    }

    // Pass 2: tent upsample of the lower level, blended with the current level
    float4 FragUpsample(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float2 t = _DF_BlitTexelSize.xy;
        float3 low = SampleBlit(uv) * 4.0;
        low += (SampleBlit(uv + t * float2(-1, 0)) + SampleBlit(uv + t * float2(1, 0))
              + SampleBlit(uv + t * float2(0, -1)) + SampleBlit(uv + t * float2(0, 1))) * 2.0;
        low += SampleBlit(uv + t * float2(-1, -1)) + SampleBlit(uv + t * float2(1, -1))
             + SampleBlit(uv + t * float2(-1,  1)) + SampleBlit(uv + t * float2(1,  1));
        low /= 16.0;
        float3 high = SAMPLE_TEXTURE2D_LOD(_DF_HighTex, s_linear_clamp_sampler, uv, 0).rgb;
        return float4(lerp(high, low, _BloomScatter), 1.0);
    }

    // ---------------- Oil paint chain (anisotropic Kuwahara, Kyprianidis et al.) ----------------

    // Pass 4: structure tensor from Sobel gradients
    float4 FragTensor(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float2 d = _DF_PaintTexelSize.xy;
        float3 c00 = SampleSourceSRGB(uv + d * float2(-1, -1));
        float3 c10 = SampleSourceSRGB(uv + d * float2( 0, -1));
        float3 c20 = SampleSourceSRGB(uv + d * float2( 1, -1));
        float3 c01 = SampleSourceSRGB(uv + d * float2(-1,  0));
        float3 c21 = SampleSourceSRGB(uv + d * float2( 1,  0));
        float3 c02 = SampleSourceSRGB(uv + d * float2(-1,  1));
        float3 c12 = SampleSourceSRGB(uv + d * float2( 0,  1));
        float3 c22 = SampleSourceSRGB(uv + d * float2( 1,  1));
        float3 sx = (c20 + 2.0 * c21 + c22 - c00 - 2.0 * c01 - c02) * 0.25;
        float3 sy = (c02 + 2.0 * c12 + c22 - c00 - 2.0 * c10 - c20) * 0.25;
        return float4(dot(sx, sx), dot(sy, sy), dot(sx, sy), 1.0);
    }

    // Pass 5: separable gaussian blur of the tensor (direction in _DF_BlurDir)
    float4 FragTensorBlur(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float2 stepUV = _DF_BlurDir.xy * _DF_BlitTexelSize.xy;
        float w[5] = { 0.2270, 0.1945, 0.1216, 0.0540, 0.0162 };
        float3 t = SampleBlit(uv) * w[0];
        [unroll] for (int k = 1; k < 5; k++)
            t += (SampleBlit(uv + stepUV * k) + SampleBlit(uv - stepUV * k)) * w[k];
        return float4(t, 1.0);
    }

    // Pass 6: anisotropic Kuwahara - elliptical, edge-aligned 8-sector filter.
    // Produces flat brush patches that follow the shapes of the image, like oil strokes.
    float4 FragPaint(Varyings i) : SV_Target
    {
        float2 uv = i.texcoord;
        float2 texel = _DF_PaintTexelSize.xy;
        float3 t = SAMPLE_TEXTURE2D_LOD(_DF_TensorTex, s_linear_clamp_sampler, uv, 0).xyz;

        float disc = sqrt(max(0.0, (t.y - t.x) * (t.y - t.x) + 4.0 * t.z * t.z));
        float l1 = 0.5 * (t.y + t.x + disc);
        float l2 = 0.5 * (t.y + t.x - disc);
        float2 dir = FlowDirection(t);
        float phi = -atan2(dir.y, dir.x);
        float A = (l1 + l2 > 1e-6) ? (l1 - l2) / (l1 + l2) : 0.0;

        float radius = _PainterlyRadius;
        float alpha = _PaintAnisotropy;
        float a = radius * clamp((alpha + A) / alpha, 0.1, 2.0);
        float b = radius * clamp(alpha / (alpha + A), 0.1, 2.0);

        float cosPhi = cos(phi);
        float sinPhi = sin(phi);
        float2x2 R = float2x2(cosPhi, -sinPhi, sinPhi, cosPhi);
        float2x2 S = float2x2(0.5 / a, 0.0, 0.0, 0.5 / b);
        float2x2 SR = mul(S, R);

        int maxX = (int)sqrt(a * a * cosPhi * cosPhi + b * b * sinPhi * sinPhi);
        int maxY = (int)sqrt(a * a * sinPhi * sinPhi + b * b * cosPhi * cosPhi);

        float zeta = 2.0 / radius;
        float zeroCross = 0.58;
        float sinZc = sin(zeroCross);
        float eta = (zeta + cos(zeroCross)) / (sinZc * sinZc);

        float4 m[8];
        float3 s[8];
        int k;
        [unroll] for (k = 0; k < 8; k++) { m[k] = 0; s[k] = 0; }

        [loop] for (int y = -maxY; y <= maxY; y++)
        {
            [loop] for (int x = -maxX; x <= maxX; x++)
            {
                float2 v = mul(SR, float2(x, y));
                if (dot(v, v) <= 0.25)
                {
                    float3 c = SampleSourceSRGB(uv + float2(x, y) * texel);
                    float w[8];
                    float z, vxx, vyy;

                    vxx = zeta - eta * v.x * v.x;
                    vyy = zeta - eta * v.y * v.y;
                    z = max(0.0,  v.y + vxx); w[0] = z * z;
                    z = max(0.0, -v.x + vyy); w[2] = z * z;
                    z = max(0.0, -v.y + vxx); w[4] = z * z;
                    z = max(0.0,  v.x + vyy); w[6] = z * z;

                    float2 r = 0.70710678 * float2(v.x - v.y, v.x + v.y);
                    vxx = zeta - eta * r.x * r.x;
                    vyy = zeta - eta * r.y * r.y;
                    z = max(0.0,  r.y + vxx); w[1] = z * z;
                    z = max(0.0, -r.x + vyy); w[3] = z * z;
                    z = max(0.0, -r.y + vxx); w[5] = z * z;
                    z = max(0.0,  r.x + vyy); w[7] = z * z;

                    float sum = 0.0;
                    [unroll] for (k = 0; k < 8; k++) sum += w[k];
                    float g = exp(-3.125 * dot(v, v)) / max(sum, 1e-6);

                    [unroll] for (k = 0; k < 8; k++)
                    {
                        float wk = w[k] * g;
                        m[k] += float4(c * wk, wk);
                        s[k] += c * c * wk;
                    }
                }
            }
        }

        float4 result = 0;
        [unroll] for (k = 0; k < 8; k++)
        {
            float wsum = max(m[k].w, 1e-6);
            float3 mean = m[k].rgb / wsum;
            float3 var = abs(s[k] / wsum - mean * mean);
            float sigma2 = var.r + var.g + var.b;
            float wk = 1.0 / (1.0 + pow(max(_PaintHardness * 1000.0 * sigma2, 0.0), 0.5 * _PaintSharpness));
            result += float4(mean * wk, wk);
        }
        float3 painted = result.rgb / max(result.w, 1e-6);
        return float4(SRGBToLinear(saturate(painted)), 1.0);
    }

    // ---------------- Composite helpers ----------------

    float3 PaintedColor(float2 uv)
    {
        float3 c = SampleSource(uv);
        if (_PainterlyStrength > 0.0)
            c = lerp(c, SAMPLE_TEXTURE2D_LOD(_DF_PaintTex, s_linear_clamp_sampler, uv, 0).rgb, _PainterlyStrength);
        return c;
    }

    // Bristle streaks stretched along the stroke direction
    float Bristles(float2 p, float2 dir)
    {
        float2 q = float2(dot(p, dir), dot(p, float2(-dir.y, dir.x)));
        return ValueNoise(float2(q.x / _StrokeLength, q.y / 1.3));
    }

    // Procedural linen weave
    float Canvas(float2 p)
    {
        float2 c = p / _CanvasScale;
        float weave = (sin(c.x * PI) * 0.5 + 0.5) * (sin(c.y * PI) * 0.5 + 0.5);
        return weave * 0.7 + ValueNoise(c * 2.7) * 0.3;
    }

    // Height of the paint layer: thick where bright, plus bristles and canvas tooth
    float PaintHeight(float2 uv, float2 p, float2 dir)
    {
        float h = Luminance(LinearToSRGB(saturate(PaintedColor(uv))));
        h += Bristles(p, dir) * _BrushBristles * 0.5;
        h += Canvas(p) * _CanvasStrength * 0.5;
        return h;
    }

    // Pass 3: final look
    float4 FragComposite(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float2 uv = i.texcoord;
        float2 p = i.positionCS.xy;
        float3 orig = SampleSource(uv);
        float3 col = PaintedColor(uv);

        // Radial chromatic aberration (old lens)
        if (_ChromaticAberration > 0.0)
        {
            float2 off = (uv - 0.5) * _ChromaticAberration * 0.012;
            col.r += SampleSource(uv - off).r - orig.r;
            col.b += SampleSource(uv + off).b - orig.b;
            col = max(col, 0.0);
        }

        // Dreamy glow + red film halation around highlights
        float3 bloom = SAMPLE_TEXTURE2D_LOD(_DF_BloomTex, s_linear_clamp_sampler, uv, 0).rgb;
        col += bloom * _BloomTint.rgb * _BloomIntensity;
        col += Luminance(bloom) * _HalationColor.rgb * _HalationIntensity;
        col *= exp2(_Exposure);

        // Grade in perceptual (sRGB) space: sliders behave like in a photo editor
        float3 g = LinearToSRGB(saturate(col));

        g = saturate((g - _BlackCrush) / max(1.0 - _BlackCrush, 1e-3));
        g = ContrastCurve(g, 1.0 + _Contrast * 1.5, _ContrastPivot);

        // Saturation that keeps the chosen hue (red) and mutes the rest
        float3 hsv = RgbToHsv(g);
        float hueDist = abs(frac(hsv.x - _KeepHue + 0.5) - 0.5);
        float keepMask = (1.0 - smoothstep(_KeepHueWidth, _KeepHueWidth * 2.0, hueDist)) * smoothstep(0.05, 0.25, hsv.y);
        float sat = _Saturation * lerp(1.0 - _NonRedDesat, 1.0 + _RedBoost, keepMask);
        // Cold moonlight hues are exempt from the red grade, so the night stays blue against red candles
        float moonDist = abs(frac(hsv.x - _MoonHue + 0.5) - 0.5);
        float moonMask = (1.0 - smoothstep(_MoonHueWidth, _MoonHueWidth * 2.0, moonDist)) * smoothstep(0.03, 0.2, hsv.y) * (1.0 - keepMask);
        sat = lerp(sat, _Saturation, moonMask * _MoonPreserve);
        float moonProtect = 1.0 - moonMask * _MoonTintProtect;
        float l = Luminance(g);
        g = max(lerp(l.xxx, g, sat), 0.0);

        // Split toning: crimson shadows, gold highlights
        float t = smoothstep(_ToneBalance - 0.35, _ToneBalance + 0.35, l);
        float3 sh = _ShadowTint.rgb / max(Luminance(_ShadowTint.rgb), 0.05);
        float3 hl = _HighlightTint.rgb / max(Luminance(_HighlightTint.rgb), 0.05);
        // luminance-preserving: tint shifts hue, never darkens the image
        float3 gs = g * sh; gs *= l / max(Luminance(gs), 1e-4);
        float3 gh = g * hl; gh *= l / max(Luminance(gh), 1e-4);
        g = lerp(g, gs, _ShadowTintStrength * (1.0 - t) * moonProtect);
        g = lerp(g, gh, _HighlightTintStrength * t);
        g = saturate(g);

        // Overall color cast
        g = lerp(g, Overlay(g, _ColorFilter.rgb), _ColorFilterStrength * moonProtect);

        // Faded film blacks, tinted with the shadow color
        g = g * (1.0 - _BlackLift) + _BlackLift * _ShadowTint.rgb;

        // Impasto: light the paint relief from the top-left, so strokes and canvas catch light
        if (_ImpastoStrength > 0.0 || _CanvasStrength > 0.0)
        {
            float2 dir = FlowDirection(SAMPLE_TEXTURE2D_LOD(_DF_TensorTex, s_linear_clamp_sampler, uv, 0).xyz);
            float2 du = float2(_PostProcessScreenSize.z, 0.0) * 1.5;
            float2 dv = float2(0.0, _PostProcessScreenSize.w) * 1.5;
            float dx = PaintHeight(uv + du, p + float2(1.5, 0.0), dir) - PaintHeight(uv - du, p - float2(1.5, 0.0), dir);
            float dy = PaintHeight(uv + dv, p + float2(0.0, 1.5), dir) - PaintHeight(uv - dv, p - float2(0.0, 1.5), dir);
            float emboss = (dx + dy) * 0.70710678;
            g = saturate(g + emboss * _ImpastoStrength * 2.0 * (0.25 + g));
            g *= 1.0 - _CanvasStrength * 0.15 * (1.0 - Canvas(p));
        }

        // Vignette
        float vd = length(uv - 0.5) * 1.41421;
        float vm = pow(saturate(vd), lerp(5.0, 1.0, _VignetteSmoothness));
        g = lerp(g, _VignetteColor.rgb, vm * _VignetteIntensity);

        // Film grain, animated at film frame rate, stronger in shadows
        if (_GrainIntensity > 0.0)
        {
            float2 gp = p / _GrainSize + _GrainSeed * float2(127.1, 311.7);
            float n = ValueNoise(gp);
            float3 nc = float3(ValueNoise(gp + 17.0), n, ValueNoise(gp + 41.0));
            float3 grain = lerp(n.xxx, nc, _GrainColor) - 0.5;
            float resp = lerp(1.0, 1.0 - Luminance(g), _GrainResponse);
            g += grain * _GrainIntensity * 0.35 * resp;
        }

        float3 result = SRGBToLinear(saturate(g));
        return float4(lerp(orig, result, _Intensity), 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass // 0
        {
            Name "Prefilter"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragPrefilter
            ENDHLSL
        }
        Pass // 1
        {
            Name "Downsample"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragDownsample
            ENDHLSL
        }
        Pass // 2
        {
            Name "Upsample"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragUpsample
            ENDHLSL
        }
        Pass // 3
        {
            Name "Composite"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragComposite
            ENDHLSL
        }
        Pass // 4
        {
            Name "Tensor"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragTensor
            ENDHLSL
        }
        Pass // 5
        {
            Name "TensorBlur"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragTensorBlur
            ENDHLSL
        }
        Pass // 6
        {
            Name "Paint"
            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragPaint
            ENDHLSL
        }
    }
    Fallback Off
}
