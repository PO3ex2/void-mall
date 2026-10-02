// Трещины на повреждённом блоке: процедурные (ячейки Вороного), количество растёт с _Damage (0…1).
Shader "VoidMall/BlockCracks"
{
    Properties
    {
        _Damage ("Damage", Range(0, 1)) = 0
        _Color ("Crack Color", Color) = (0.05, 0.04, 0.03, 0.9)
        _Scale ("Cell Scale", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Cracks"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Damage;
                half4 _Color;
                float _Scale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 normalOS : TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                return output;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = abs(input.normalOS);
                float2 uv = n.y > 0.5 ? input.positionOS.xz : (n.x > 0.5 ? input.positionOS.zy : input.positionOS.xy);
                uv = (uv + 0.5) * _Scale;

                // F1/F2 Вороного: трещины — на границах ячеек.
                float2 cell = floor(uv);
                float f1 = 8, f2 = 8;
                float2 nearest = 0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + float2(x, y);
                    float d = length(c + Hash2(c) - uv);
                    if (d < f1) { f2 = f1; f1 = d; nearest = c; }
                    else if (d < f2) { f2 = d; }
                }
                float edge = f2 - f1;
                float width = 0.03 + 0.06 * _Damage;
                float visible = step(Hash2(nearest).x, _Damage * 1.15);
                float crack = (1 - smoothstep(width * 0.5, width, edge)) * visible;
                return half4(_Color.rgb, _Color.a * crack * step(0.001, _Damage));
            }
            ENDHLSL
        }
    }
}
