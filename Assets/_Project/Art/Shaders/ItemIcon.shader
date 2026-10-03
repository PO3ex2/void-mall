// Отрисовка иконок предметов в редакторе (ItemIconRenderer): цвет из вершин, два направленных света в пространстве вида.
// Матрица задаётся вручную (_IconMatrix), чтобы не зависеть от камер и пайплайна.
Shader "Hidden/VoidMall/ItemIcon"
{
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4x4 _IconMatrix;
            float4x4 _IconNormalMatrix;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = mul(_IconMatrix, float4(v.vertex.xyz, 1));
                o.normal = mul((float3x3)_IconNormalMatrix, v.normal);
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float key = saturate(dot(n, normalize(float3(-0.45, 0.65, 0.6))));
                float fill = saturate(dot(n, normalize(float3(0.7, -0.1, 0.4))));
                float3 albedo = i.color.rgb;
                float3 lit = albedo * (0.38 + key * 0.8 + fill * 0.22);
                return float4(saturate(lit), 1);
            }
            ENDCG
        }
    }
}
