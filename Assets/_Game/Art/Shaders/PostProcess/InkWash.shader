// Живописный грейд на весь кадр. Фактура (мазки, зерно) привязана к МИРУ, а не к экрану:
// по глубине восстанавливается мировая позиция, и шум лежит на поверхностях, как нарисованная текстура.
// Плюс масляный фильтр, резкость в фокусе, тонкий рисованный контур, цветной грейд.
Shader "Hidden/Shader/TanInkWash"
{
    HLSLINCLUDE
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/FXAA.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/PostProcessing/Shaders/RTUpscale.hlsl"

    struct Attributes
    {
        uint vertexID : SV_VertexID;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 texcoord : TEXCOORD0;
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

    float _Intensity;
    float _Paint;           // сила масляного фильтра
    float _FocusDistance;   // расстояние (м) до резких ключевых объектов
    float _FocusRange;      // ширина зоны резкости (м)
    float _Sharpen;         // резкость и локальный контраст в фокусе
    float _Grain;           // сила зерна на поверхностях
    float _GrainWorld;      // размер зерна в мире (м); вдали не мельче пикселя
    float _Stroke;          // сила мазков кисти по поверхностям
    float _StrokeWorld;     // длина крупного мазка в мире (м)
    float _Outline;         // сила контура
    float _OutlineWidth;    // толщина контура в пикселях
    float _Contrast;
    float _Saturation;
    float _DepthTint;       // насколько дальние объекты уходят в цвет дымки
    float _Vignette;
    float _FocusLift;
    float4 _ShadowTint;
    float4 _HighlightTint;
    float4 _FogTint;
    float4 _OutlineColor;

    TEXTURE2D_X(_InputTexture);

    float Hash12(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float Hash13(float3 p3)
    {
        p3 = frac(p3 * 0.1031);
        p3 += dot(p3, p3.zyx + 31.32);
        return frac((p3.x + p3.y) * p3.z);
    }

    float ValueNoise2(float2 p)
    {
        float2 i = floor(p), f = frac(p);
        f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
        return lerp(lerp(Hash12(i), Hash12(i + float2(1, 0)), f.x), lerp(Hash12(i + float2(0, 1)), Hash12(i + 1), f.x), f.y);
    }

    float ValueNoise3(float3 p)
    {
        float3 i = floor(p), f = frac(p);
        f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
        return lerp(lerp(lerp(Hash13(i + float3(0, 0, 0)), Hash13(i + float3(1, 0, 0)), f.x),
                         lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x), f.y),
                    lerp(lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x),
                         lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x), f.y), f.z);
    }

    float3 Load(int2 ss)
    {
        return LOAD_TEXTURE2D_X(_InputTexture, clamp(ss, 0, int2(_ScreenSize.xy) - 1)).rgb;
    }

    float LinearDepthAt(int2 ss)
    {
        return LinearEyeDepth(LoadCameraDepth(clamp(ss, 0, int2(_ScreenSize.xy) - 1)), _ZBufferParams);
    }

    float3 WorldRWSAt(int2 ss)
    {
        int2 c = clamp(ss, 0, int2(_ScreenSize.xy) - 1);
        float2 uv = (c + 0.5) * _ScreenSize.zw;
        return ComputeWorldSpacePosition(uv, LoadCameraDepth(c), UNITY_MATRIX_I_VP);
    }

    // Нормаль по глубине (берём меньший из двух перепадов, чтобы не ломаться на краях силуэтов).
    float3 NormalFromDepth(int2 ss, float3 p0)
    {
        float3 px = WorldRWSAt(ss + int2(1, 0)) - p0, nx = p0 - WorldRWSAt(ss - int2(1, 0));
        float3 py = WorldRWSAt(ss + int2(0, 1)) - p0, ny = p0 - WorldRWSAt(ss - int2(0, 1));
        float3 dx = dot(px, px) < dot(nx, nx) ? px : nx;
        float3 dy = dot(py, py) < dot(ny, ny) ? py : ny;
        float3 n = normalize(cross(dy, dx));
        return dot(n, p0) > 0 ? -n : n;      // нормаль смотрит к камере
    }

    // Мазок: шум, вытянутый вдоль направления мазка в плоскости, с мягко-жёсткой границей.
    float Strokes2D(float2 p, float2 stretch)
    {
        float a = ValueNoise2(p * stretch);
        float b = ValueNoise2(p * stretch * 2.3 + 7.1);
        float v = a * 0.65 + b * 0.35;
        return smoothstep(0.1, 0.9, v);                  // мягкий переход: маслянистый мазок, без жёсткой границы
    }

    // Мазки по поверхности: триплан по доминирующей оси нормали, мазки вытянуты вертикально на стенах
    // и вдоль X на земле. Поле привязано к мировым координатам, поэтому едет вместе с объектом.
    float StrokeField(float3 p, float3 n, float cell)
    {
        float3 w = pow(abs(n), 4.0);
        w /= (w.x + w.y + w.z + 1e-4);
        float sx = Strokes2D(float2(p.z, p.y) / cell, float2(1.0, 0.22));    // стена, нормаль по X
        float sy = Strokes2D(float2(p.x, p.z) / cell, float2(0.22, 1.0));    // пол, нормаль по Y
        float sz = Strokes2D(float2(p.x, p.y) / cell, float2(1.0, 0.22));    // стена, нормаль по Z
        return sx * w.x + sy * w.y + sz * w.z;
    }

    // Kuwahara: мазки масла.
    float3 Kuwahara(int2 ss, int r)
    {
        float3 mean[4];
        float3 sq[4];
        [unroll] for (int k = 0; k < 4; k++) { mean[k] = 0; sq[k] = 0; }
        int cnt = (r + 1) * (r + 1);
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                float3 c = Load(ss + int2(x, y));
                if (x <= 0 && y <= 0) { mean[0] += c; sq[0] += c * c; }
                if (x >= 0 && y <= 0) { mean[1] += c; sq[1] += c * c; }
                if (x <= 0 && y >= 0) { mean[2] += c; sq[2] += c * c; }
                if (x >= 0 && y >= 0) { mean[3] += c; sq[3] += c * c; }
            }
        }
        float best = 1e9; float3 result = 0;
        [unroll] for (int j = 0; j < 4; j++)
        {
            float3 m = mean[j] / cnt;
            float3 v = abs(sq[j] / cnt - m * m);
            float var = v.r + v.g + v.b;
            if (var < best) { best = var; result = m; }
        }
        return result;
    }

    float4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = input.texcoord;
        int2 ss = int2(uv * _ScreenSize.xy);

        float3 src = Load(ss);
        float rawDepth = LoadCameraDepth(ss);
        float depth = LinearEyeDepth(rawDepth, _ZBufferParams);
        float sky = step(0.99999, rawDepth);

        // мировая позиция и нормаль поверхности
        float3 rws = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
        float3 pw = GetAbsolutePositionWS(rws);
        float3 nrm = sky > 0.5 ? float3(0, 1, 0) : NormalFromDepth(ss, rws);
        float pixelWorld = 2.0 * depth / (UNITY_MATRIX_P._m11 * _ScreenSize.y);   // размер пикселя в мире на этой глубине

        // фокус: ключевые объекты резче, остальное мазками крупнее
        float focus = 1.0 - smoothstep(_FocusRange * 0.5, _FocusRange * 1.6, abs(depth - _FocusDistance));
        focus = lerp(focus, 0.0, sky);
        float paint = saturate(lerp(0.5, 1.0, 1.0 - focus) * _Paint);

        float3 col = src;
        if (paint > 0.02)
        {
            float3 oil = Kuwahara(ss, focus > 0.5 ? 3 : 4);
            col = lerp(src, oil, paint);
        }

        // резкость и локальный контраст на ключевых объектах
        float3 blur = (Load(ss + int2(2, 0)) + Load(ss - int2(2, 0)) + Load(ss + int2(0, 2)) + Load(ss - int2(0, 2))) * 0.25;
        float3 wide = (Load(ss + int2(7, 0)) + Load(ss - int2(7, 0)) + Load(ss + int2(0, 7)) + Load(ss - int2(0, 7))) * 0.25;
        col += ((src - blur) * _Sharpen + (src - wide) * _Sharpen * 0.6) * focus;

        // мазки кисти, лежащие на поверхности: яркость и лёгкий сдвиг оттенка
        if (sky < 0.5)
        {
            float cellBig = max(_StrokeWorld, pixelWorld * 6.0);
            float cellSmall = max(_StrokeWorld * 0.25, pixelWorld * 3.0);
            float s1 = StrokeField(pw, nrm, cellBig);
            float s2 = StrokeField(pw + 5.3, nrm, cellSmall);
            float strokes = s1 * 0.6 + s2 * 0.4 - 0.5;
            col *= 1.0 + strokes * _Stroke;
            // масляный блик: мазки чуть светлеют на освещённых местах
            col += col * saturate(s1 - 0.45) * smoothstep(0.15, 0.7, Luminance(col)) * _Stroke * 0.6;
            col = lerp(col, col * float3(1.06, 1.0, 0.94), saturate(strokes * 2.0) * _Stroke * 0.4);
        }

        // дальше — глубже в цвет дымки (воздушная перспектива)
        float far = smoothstep(_FocusDistance, _FocusDistance + _FocusRange * 4.0, depth) * (1.0 - sky);
        col = lerp(col, col * _FogTint.rgb * 1.1, far * _DepthTint);

        // грейд
        float lum = Luminance(col);
        float3 graded = lerp(lum.xxx, max(col, 0.0), _Saturation);
        float s = smoothstep(0.0, 1.0, saturate(lum));
        graded = lerp(graded, graded * _ShadowTint.rgb * 1.8, (1.0 - s) * 0.55);
        graded = lerp(graded, graded * _HighlightTint.rgb * 1.3, s * 0.45);
        graded = max((graded - 0.25) * _Contrast + 0.25, 0.0);
        graded += _ShadowTint.rgb * 0.006 * (1.0 - s);
        graded *= 1.0 + focus * _FocusLift;

        // зерно: лежит на поверхностях (мировой шум), вдали не мельче пикселя; небо — фиксированное экранное
        float grainCell = max(_GrainWorld, pixelWorld * 1.1);
        float n3 = ValueNoise3(pw / grainCell) * 0.6 + ValueNoise3(pw / (grainCell * 2.3) + 9.1) * 0.4;
        float nSky = ValueNoise2(uv * _ScreenSize.xy * 0.8) * 0.6 + ValueNoise2(uv * _ScreenSize.xy * 0.35 + 3.0) * 0.4;
        float n = (lerp(n3, nSky, sky) - 0.5) * 2.0 * 2.4;
        float lumNow = Luminance(max(graded, 0.0));
        float grainAmp = _Grain * smoothstep(0.0, 0.18, lumNow) * (1.0 - smoothstep(0.6, 1.0, lumNow) * 0.5);
        graded = graded * (1.0 + n * grainAmp) + n * _Grain * 0.012;

        // тонкий рисованный контур по перепаду глубины, толщина плавает как мазок
        float wob = 0.7 + 0.6 * ValueNoise3(pw * 3.0 / max(pixelWorld * 40.0, 0.2));
        int2 o = int2(max(1.0, round(_OutlineWidth * wob)), 0);
        float d0 = depth;
        float dl = LinearDepthAt(ss - o), dr = LinearDepthAt(ss + o), du = LinearDepthAt(ss + o.yx), dd = LinearDepthAt(ss - o.yx);
        float edge = (abs(dl - dr) + abs(du - dd) + abs(dl + dr + du + dd - 4.0 * d0) * 0.5) / max(d0, 0.1);
        edge = saturate(edge * 22.0 - 0.12) * (1.0 - sky);
        graded = lerp(graded, _OutlineColor.rgb, edge * _Outline);

        // виньетка
        float2 c = uv - 0.5;
        float vig = smoothstep(0.35, 0.85, length(c * float2(1.0, 0.85)));
        graded *= 1.0 - vig * _Vignette;

        return float4(lerp(src, max(graded, 0.0), _Intensity), 1);
    }
    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "TanInkWash"
            ZWrite Off ZTest Always Blend Off Cull Off
            HLSLPROGRAM
                #pragma fragment Frag
                #pragma vertex Vert
            ENDHLSL
        }
    }
    Fallback Off
}
