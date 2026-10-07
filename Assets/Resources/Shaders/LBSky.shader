// Cielo degradado con nubes procedurales suaves (skybox).
Shader "LB/Sky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.25,0.5,0.95,1)
        _HorizonColor ("Horizon", Color) = (0.75,0.88,1,1)
        _BottomColor ("Bottom", Color) = (0.35,0.6,0.85,1)
        _CloudColor ("Clouds", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor, _HorizonColor, _BottomColor, _CloudColor;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x),
                            lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float v = 0;
                float a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                fixed4 c = y > 0 ? lerp(_HorizonColor, _TopColor, pow(saturate(y), 0.6))
                                 : lerp(_HorizonColor, _BottomColor, pow(saturate(-y), 0.5));
                if (y > 0.0)
                {
                    float2 uv = d.xz / (y + 0.15) * 1.5 + _Time.x * 0.3;
                    float cl = smoothstep(0.5, 0.8, fbm(uv));
                    c = lerp(c, _CloudColor, cl * saturate(y * 4.0) * 0.8);
                }
                return c;
            }
            ENDCG
        }
    }
}
