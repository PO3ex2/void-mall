// Блоки мира: Texture2DArray (слой и AO в UV0.zw), полное освещение URP: Forward+ (_CLUSTER_LIGHT_LOOP),
// тени основного и дополнительных источников, SSAO, туман. Прозрачный вариант — для стекла.
// Эмиссия ламп считается по LampRules.hlsl (те же правила, что у LightingDirector на CPU).
// Аномалии «Искажение» (смещение вершин) и «Глитч» (подмена текстур, полосы) — AnomalyFx.hlsl.
// Проходы глубины и теней свои: смещение вершин должно совпадать во всех проходах.
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

        #include "AnomalyFx.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma target 4.5
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

            float4 _VM_LayerEmission[64]; // rgb — цвет × яркость, a — LampKind

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
                float3 cellPositionWS : TEXCOORD5;       // позиция до искажения — для клетки лампы и глитча
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
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.cellPositionWS = positionWS;
                positionWS = VM_Distort(positionWS);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.layer = input.uv.z;
                // Глубина из w, а не из z: у камеры портала наклонная ближняя плоскость портит z (туман в портале пропадал).
                output.fogFactor = ComputeFogFactorZ0ToFar(output.positionCS.w);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float layer = input.layer;
                float2 uv = input.uv.xy;
                int3 cell = (int3)floor(input.cellPositionWS - input.normalWS * 0.01);

                // Глитч: блоки меняют текстуру, полосы сдвигаются, изображение рвётся.
                float glitch = VM_Glitch(input.cellPositionWS);
                float glitchFlash = 0;
                if (glitch > 0.001)
                {
                    int tick = (int)floor(_VM_Time * 6.0);
                    uint h = VM_Hash4(_VM_Seed, cell.x, cell.y, cell.z, tick);
                    if (VM_ToFloat01(h) < glitch * 0.45)
                    {
                        float other = (float)((h >> 4) % (uint)max(_VM_LayerCount, 1.0));
                        layer = layer >= 0 ? other : -other - 1;
                        glitchFlash = VM_ToFloat01(h * 747796405u) < 0.15 ? 1 : 0;
                    }
                    int band = (int)floor(input.cellPositionWS.y * 3.0 + _VM_Time * 2.0);
                    if (VM_ToFloat01(VM_Hash1((uint)tick, band)) < glitch * 0.3)
                        uv.x += 0.37;
                }

                half4 texel = SampleBlockTexture(uv, layer, input.cellPositionWS, input.normalWS);

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

                // Эмиссия ламп: слой исходного материала, состояние — по клетке и правилам ламп.
                int baseLayer = (int)(input.layer >= 0 ? input.layer : -input.layer - 1);
                float4 emission = _VM_LayerEmission[baseLayer];
                if (emission.a > 0.5)
                {
                    int lampKind = (int)(emission.a + 0.5);
                    surface.emission = texel.rgb * emission.rgb * VM_LampKindIntensity(cell, lampKind);
                    // Экран «неба» ночью: редкие пиксели-звёзды (16×16 пикселей на блок) мерцают.
                    if (lampKind == 4)
                    {
                        int2 pixel = (int2)floor(frac(uv) * 16.0);
                        uint h = VM_Hash4(_VM_Seed, cell.x * 16 + pixel.x, cell.y, cell.z * 16 + pixel.y, 905);
                        float star = VM_ToFloat01(h) < 0.012 ? 0.6 + 0.4 * sin(_VM_Time * (1.0 + VM_ToFloat01(h * 747796405u) * 3.0)) : 0.0;
                        surface.emission += star * (1.0 - _VM_Skylight) * texel.rgb * 8.0;
                    }
                }
                surface.emission += glitchFlash * glitch * half3(0.6, 0.2, 0.8);

                half4 color = UniversalFragmentPBR(inputData, surface);
                if (glitch > 0.001)
                {
                    float scan = step(0.5, frac(input.positionCS.y * 0.2 + _VM_Time * 13.0));
                    color.rgb *= 1.0 - glitch * 0.18 * scan;
                }
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                float3 positionWS = VM_Distort(TransformObjectToWorld(input.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformWorldToHClip(VM_Distort(TransformObjectToWorld(positionOS.xyz)));
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                output.positionCS = TransformWorldToHClip(VM_Distort(TransformObjectToWorld(input.positionOS.xyz)));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                return half4(PackFloat2To888(remapped), 0.0);
            #else
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
