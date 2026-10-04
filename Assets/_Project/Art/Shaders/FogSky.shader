// Небо мола: выше горизонта — цвет тумана (даль коридоров растворяется в дымке), внизу — чёрная пустота
// (в бездну и вырезы смотреть — темнота). LightingDirector подкрашивает _TopColor цветом тумана по времени суток.
Shader "VoidMall/FogSky"
{
    Properties
    {
        _TopColor ("Top (fog)", Color) = (0.32, 0.31, 0.29, 1)
        _BottomColor ("Bottom (void)", Color) = (0, 0, 0, 1)
        _FadeStart ("Fade start (dir.y)", Float) = -0.12
        _FadeEnd ("Fade end (dir.y)", Float) = -0.35
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
                float _FadeStart;
                float _FadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float y = normalize(input.direction).y;
                float t = saturate((y - _FadeEnd) / (_FadeStart - _FadeEnd));
                return lerp(_BottomColor, _TopColor, t);
            }
            ENDHLSL
        }
    }
}
