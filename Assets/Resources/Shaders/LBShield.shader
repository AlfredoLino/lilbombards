// Burbuja de escudo / hielo: transparente con fresnel.
Shader "LB/Shield"
{
    Properties
    {
        _Color ("Color", Color) = (0.7,0.4,1,0.25)
        _EdgeColor ("Edge Color", Color) = (0.9,0.7,1,0.9)
        _Power ("Fresnel Power", Range(0.5,8)) = 2.5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _EdgeColor;
            half _Power;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.v = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half f = pow(1.0 - saturate(dot(normalize(i.n), normalize(i.v))), _Power);
                return lerp(_Color, _EdgeColor, f);
            }
            ENDCG
        }
    }
}
