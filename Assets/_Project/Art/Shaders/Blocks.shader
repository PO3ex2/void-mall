// Блоки мира: Texture2DArray (слой и AO в UV0.zw), полное освещение URP: Forward+ (_CLUSTER_LIGHT_LOOP),
// тени основного и дополнительных источников, SSAO, туман. Прозрачный вариант — для стекла.
Shader "VoidMall/Blocks"
{
    Properties
    {
        [NoScaleOffset] _BlockTextures ("Block Textures", 2DArray) = "" {}
        _AOStrength ("Vertex AO Strength", Range(0, 1)) = 1
        _TransparentSmoothness ("Transparent Smoothness", Range(0, 1)) = 0.92
        [Toggle(_BLOCKS_TRANSPARENT)] _Transparent ("Transparent", Float) = 0
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 1
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 0
        [HideInInspector] _ZWrite ("Z Write", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half _AOStrength;
            half _TransparentSmoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local_fragment _BLOCKS_TRANSPARENT
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D_ARRAY(_BlockTextures);
            SAMPLER(sampler_BlockTextures);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 uv : TEXCOORD0; // xy — мировые UV, z — слой, w — AO
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 uv : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                nointerpolation float layer : TEXCOORD4; // < 0 — гладкая поверхность (трипланар), слой = −layer − 1
            };

            half4 SampleBlockTexture(float2 uv, float layer, float3 positionWS, float3 normalWS)
            {
                if (layer >= 0)
                    return SAMPLE_TEXTURE2D_ARRAY(_BlockTextures, sampler_BlockTextures, uv, layer);

                // Трипланар для гладких вокселей: три проекции, веса — по нормали.
                float slice = -layer - 1;
                float3 w = pow(abs(normalWS), 4);
                w /= max(w.x + w.y + w.z, 1e-4);
                half4 x = SAMPLE_TEXTURE2D_ARRAY(_BlockTextures, sampler_BlockTextures, positionWS.zy, slice);
                half4 y = SAMPLE_TEXTURE2D_ARRAY(_BlockTextures, sampler_BlockTextures, positionWS.xz, slice);
                half4 z = SAMPLE_TEXTURE2D_ARRAY(_BlockTextures, sampler_BlockTextures, positionWS.xy, slice);
                return x * w.x + y * w.y + z * w.z;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.layer = input.uv.z;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 texel = SampleBlockTexture(input.uv.xy, input.layer, input.positionWS, input.normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = texel.rgb;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = lerp(1.0h, (half)input.uv.w, _AOStrength);
            #if defined(_BLOCKS_TRANSPARENT)
                surface.smoothness = _TransparentSmoothness;
                surface.alpha = texel.a;
            #else
                surface.smoothness = texel.a;
                surface.alpha = 1;
            #endif

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
