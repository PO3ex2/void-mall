// Прототипный материал: мировая сетка 1 м (жирная линия каждые 4 м), основной свет с тенями и SH-окружение.
Shader "VoidMall/Greybox"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.6, 0.6, 0.6, 1)
        _LineColor ("Line Color", Color) = (0.25, 0.25, 0.25, 1)
        _CellSize ("Cell Size (m)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LineColor;
                float _CellSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // 1 на линии сетки, 0 вне её. Ширина линии в пикселях, без алиасинга.
            float GridLine(float2 uv, float widthPx)
            {
                float2 distancePx = abs(frac(uv + 0.5) - 0.5) / max(fwidth(uv), 1e-5);
                return 1.0 - saturate(min(distancePx.x, distancePx.y) - widthPx + 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                // Ось проекции выбираем с запасом: на гранях ровно 45° компоненты нормали равны,
                // и без запаса проекция попиксельно «прыгает» (шум, похожий на z-fighting).
                float3 an = abs(normal);
                const float bias = 0.01;
                float2 uv = an.y + bias >= max(an.x, an.z) ? input.positionWS.xz
                          : an.x + bias >= an.z ? input.positionWS.zy
                          : input.positionWS.xy;
                uv /= max(_CellSize, 1e-3);

                float minor = GridLine(uv, 1.0);
                float major = GridLine(uv * 0.25, 1.5);
                half3 albedo = lerp(_BaseColor.rgb, _LineColor.rgb, saturate(minor * 0.45 + major * 0.85));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(normal, mainLight.direction));
                half3 lighting = SampleSH(normal) + mainLight.color * ndl * mainLight.shadowAttenuation;
                half3 color = albedo * lighting;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
