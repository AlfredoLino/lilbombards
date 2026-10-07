// Agua estilizada con olas en el vertice y brillos animados.
Shader "LB/Water"
{
    Properties
    {
        _DeepColor ("Deep", Color) = (0.05,0.3,0.55,1)
        _ShallowColor ("Shallow", Color) = (0.2,0.6,0.8,1)
        _SparkleColor ("Sparkle", Color) = (0.85,0.95,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            fixed4 _DeepColor, _ShallowColor, _SparkleColor;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                float4 wp = mul(unity_ObjectToWorld, v.vertex);
                wp.y += sin(wp.x * 0.15 + _Time.y) * 0.3 + cos(wp.z * 0.12 + _Time.y * 0.8) * 0.3;
                o.wp = wp.xyz;
                o.pos = mul(UNITY_MATRIX_VP, wp);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float w = sin(i.wp.x * 0.6 + _Time.y * 1.3) * sin(i.wp.z * 0.5 - _Time.y * 1.1);
                w += sin((i.wp.x + i.wp.z) * 0.9 + _Time.y * 2.0) * 0.5;
                float s = smoothstep(1.05, 1.4, w);
                fixed4 c = lerp(_DeepColor, _ShallowColor, saturate(w * 0.25 + 0.5));
                c = lerp(c, _SparkleColor, s * 0.6);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
