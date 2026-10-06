// Объёмные эффекты аномалий (AnomalyVisuals, AnomalyPuddles): один шейдер, режим — _Mode.
//   0 — светящаяся лужа: диск на полу (uv −1…1), переливы голубого, бирюзового и жёлтого, шипящие пузыри.
//   1 — вспышка «Смотри на меня»: билборд (uv −1…1), фиолетовое ядро, ореол и лучи.
//   2 — лучи света оазиса: открытый конус (uv.x — по кругу, uv.y — снизу вверх), мягкие края и полосы, вблизи гаснут.
// Смешивание premultiplied: rgb складывается со светом сцены, alpha закрывает то, что за лужей.
Shader "VoidMall/AnomalyVfx"
{
    Properties
    {
        _Mode ("Mode", Float) = 0
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Vfx"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                half4 _Color;
                float _Intensity;
            CBUFFER_END

            float _VM_Time;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 Puddle(Varyings input)
            {
                float t = _VM_Time;
                float2 uv = input.uv;
                float angle = atan2(uv.y, uv.x);
                // Неровный край, медленно «дышит».
                float edge = 0.86 + 0.08 * sin(angle * 5.0 + t * 0.7) + 0.05 * sin(angle * 11.0 - t * 1.1);
                float r = length(uv) / edge;
                if (r > 1.0)
                    return half4(0, 0, 0, 0);
                float2 p = input.positionWS.xz;
                float n1 = Noise(p * 1.7 + float2(t * 0.25, -t * 0.18));
                float n2 = Noise(p * 3.1 - float2(t * 0.31, t * 0.22));
                half3 cyan = half3(0.25, 0.85, 1.0), teal = half3(0.1, 0.95, 0.75), yellow = half3(1.0, 0.92, 0.35);
                half3 color = lerp(cyan, teal, n1);
                color = lerp(color, yellow, smoothstep(0.62, 0.85, n2) * 0.8);

                // Пузыри: в ячейках сетки растут кольца и лопаются.
                float2 grid = p * 4.0;
                float2 cell = floor(grid);
                float phase = frac(t * (0.6 + Hash21(cell) * 0.9) + Hash21(cell + 7.1));
                float2 centre = cell + 0.3 + 0.4 * float2(Hash21(cell + 3.3), Hash21(cell + 5.7));
                float size = phase * 0.28;
                float d = distance(grid, centre);
                float ring = smoothstep(0.05, 0.0, abs(d - size)) * (1.0 - phase) * step(0.35, Hash21(cell + 1.9));
                color += ring * half3(0.9, 1.0, 0.95);

                float rim = smoothstep(1.0, 0.75, r);
                float glow = 0.55 + 0.25 * sin(t * 1.7 + n1 * 6.0);
                half alpha = (half)(0.78 * rim);
                half3 rgb = color * glow * _Intensity * rim;
                return half4(rgb, alpha);
            }

            half4 Flare(Varyings input)
            {
                float t = _VM_Time;
                float2 uv = input.uv;
                float r = length(uv);
                float angle = atan2(uv.y, uv.x);
                float core = exp(-r * r * 30.0);
                float halo = exp(-r * 4.0) * 0.55;
                float rays = pow(abs(cos(angle * 3.0 + t * 0.8)), 24.0) * exp(-r * 2.5) * 0.6;
                float flicker = 0.85 + 0.15 * sin(t * 13.0) * sin(t * 7.3);
                half3 purple = half3(0.72, 0.25, 1.0);
                half3 rgb = (core * half3(1.0, 0.85, 1.0) + (halo + rays) * purple) * flicker * _Intensity;
                return half4(rgb * saturate(1.0 - r), 0);
            }

            half4 Shaft(Varyings input)
            {
                float t = _VM_Time;
                float3 toCamera = GetCameraPositionWS() - input.positionWS;
                float3 view = normalize(toCamera);
                float3 normal = normalize(input.normalWS);
                // Мягкие края конуса: ярче там, где смотрим поперёк стенки.
                float facing = pow(saturate(abs(dot(normal, view))), 1.5);
                float vertical = smoothstep(0.0, 0.25, input.uv.y) * smoothstep(1.0, 0.75, input.uv.y);
                float streak = 0.65 + 0.35 * Noise(float2(input.uv.x * 40.0, input.uv.y * 2.0 - t * 0.15));
                // Вблизи камеры гаснет: изнутри конуса лучи не должны превращаться в белую дымку.
                float nearFade = saturate((length(toCamera) - 1.5) / 6.0);
                half3 rgb = _Color.rgb * (facing * vertical * streak * 0.07 * nearFade) * _Intensity;
                return half4(rgb, 0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color;
                [branch] if (_Mode < 0.5)
                    color = Puddle(input);
                else if (_Mode < 1.5)
                    color = Flare(input);
                else
                    color = Shaft(input);
                // Свечение тонет в тумане (к нулю, а не к цвету тумана: смешивание аддитивное).
                color.rgb = MixFogColor(color.rgb, half3(0, 0, 0), input.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }
}
