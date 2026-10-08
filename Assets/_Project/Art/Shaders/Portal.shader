// Проём портала (этап 10): картинка второй камеры (мир по ту сторону) по экранным координатам — так в проёме виден
// параллакс, как в настоящем окне. Пока портал закрыт (_Open = 0) — тёмная рябь; открывается волной от центра;
// по краю — бледное свечение. _Collapse схлопывает проём к центру.
Shader "VoidMall/Portal"
{
    Properties
    {
        _MainTex ("View", 2D) = "black" {}
        _Open ("Open", Range(0, 1)) = 0
        _Collapse ("Collapse", Range(0, 1)) = 0
        _EdgeColor ("Edge Color", Color) = (0.75, 0.85, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Portal"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half _Open;
                half _Collapse;
                half4 _EdgeColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centered = input.uv * 2 - 1;
                // Проём — прямоугольник со скруглёнными углами; схлопывание сжимает его к центру.
                float shrink = 1 - _Collapse;
                float2 q = abs(centered) - (shrink - 0.12);
                float box = length(max(q, 0)) + min(max(q.x, q.y), 0) - 0.12;
                clip(-box);
                float radius = length(centered * float2(1, 0.8));

                float t = _Time.y;
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                // Чуть «дышит» по краю — жидкое стекло.
                float edge = saturate(1 + box * 8);
                screenUV += centered * edge * 0.01 * sin(t * 2 + radius * 12);
                half3 view = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, screenUV).rgb;

                // Закрыт: тёмная рябь; открывается кольцом от центра.
                float ripple = 0.5 + 0.5 * sin(radius * 22 - t * 3);
                half3 closed = half3(0.03, 0.035, 0.05) + _EdgeColor.rgb * ripple * 0.06;
                float open = saturate((_Open * 1.8 - radius) * 4);
                half3 color = lerp(closed, view, open);
                color += _EdgeColor.rgb * edge * (0.35 + 0.25 * sin(t * 3));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
