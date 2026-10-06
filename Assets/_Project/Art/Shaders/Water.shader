// Вода (MaterialKind.Liquid): отдельный меш чанка без коллизии. Мутная сине-зелёная полупрозрачная гладь с рябью
// (нормаль колышется по мировым координатам и времени) и бликами ламп — полное освещение URP, как у блоков.
// Двусторонняя: изнутри водного чанка видна та же стенка воды.
Shader "VoidMall/Water"
{
    Properties
    {
        _Color ("Color", Color) = (0.12, 0.33, 0.32, 0.62)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.75
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Smoothness;
                half _RippleStrength;
            CBUFFER_END

            float _VM_Time;

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
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // Рябь: сумма бегущих синусов, наклон нормали — по их производным.
            float2 Ripple(float2 p, float t)
            {
                float2 g = 0;
                g += float2(0.8, 0.3) * cos(dot(p, float2(0.8, 0.3)) * 2.1 + t * 1.3);
                g += float2(-0.4, 0.9) * cos(dot(p, float2(-0.4, 0.9)) * 3.3 + t * 1.7);
                g += float2(0.6, -0.7) * cos(dot(p, float2(0.6, -0.7)) * 5.1 - t * 2.3) * 0.5;
                return g;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                float3 normal = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                float3 p = input.positionWS;
                // Плоскость грани: рябь по двум её осям.
                float3 a = abs(normal);
                float2 plane = a.y > 0.5 ? p.xz : a.x > 0.5 ? p.zy : p.xy;
                float2 slope = Ripple(plane, _VM_Time) * _RippleStrength * 0.25;
                float3 tangent = a.y > 0.5 ? float3(1, 0, 0) : float3(0, 1, 0);
                float3 bitangent = normalize(cross(normal, tangent));
                tangent = cross(bitangent, normal);
                normal = normalize(normal + tangent * slope.x + bitangent * slope.y);

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normal;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SampleSH(normal);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _Color.rgb;
                surface.smoothness = _Smoothness;
                surface.occlusion = 1;
                surface.alpha = _Color.a;
                surface.normalTS = half3(0, 0, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                // Под скользящим углом вода плотнее (меньше видно сквозь).
                float facing = saturate(abs(dot(normal, inputData.viewDirectionWS)));
                color.a = saturate(_Color.a + (1.0 - facing) * 0.3);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
}
