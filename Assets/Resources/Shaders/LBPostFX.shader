// Post-procesado ligero para el Built-in Render Pipeline:
// bloom (prefiltro con umbral suave + cadena de down/upsample), gradacion de color y vinieta.
Shader "Hidden/LB/PostFX"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;
    float4 _BloomTex_TexelSize;
    sampler2D _BlurTex;
    float4 _BlurTex_TexelSize;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
    half _FocusDist;
    half _FocusStart;
    half _FocusRange;
    half _DofAmount;
    half4 _Filter;
    half _Intensity;
    half _Saturation;
    half _Contrast;
    half _Vignette;
    half3 _Tint;
    half3 _ShadowTint;

    half3 Prefilter(half3 c)
    {
        half brightness = max(c.r, max(c.g, c.b));
        half soft = brightness - _Filter.y;
        soft = clamp(soft, 0, _Filter.z);
        soft = soft * soft * _Filter.w;
        half contribution = max(soft, brightness - _Filter.x);
        contribution /= max(brightness, 0.00001);
        return c * contribution;
    }

    half3 Box(sampler2D tex, float4 texel, float2 uv, float delta)
    {
        float4 o = texel.xyxy * float2(-delta, delta).xxyy;
        half3 s = tex2D(tex, uv + o.xy).rgb + tex2D(tex, uv + o.zy).rgb +
                  tex2D(tex, uv + o.xw).rgb + tex2D(tex, uv + o.zw).rgb;
        return s * 0.25;
    }
    ENDCG

    SubShader
    {
        Cull Off ZTest Always ZWrite Off

        Pass // 0: prefiltro + primer downsample
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag (v2f_img i) : SV_Target
            {
                return half4(Prefilter(Box(_MainTex, _MainTex_TexelSize, i.uv, 1)), 1);
            }
            ENDCG
        }

        Pass // 1: downsample
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag (v2f_img i) : SV_Target
            {
                return half4(Box(_MainTex, _MainTex_TexelSize, i.uv, 1), 1);
            }
            ENDCG
        }

        Pass // 2: upsample aditivo
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag (v2f_img i) : SV_Target
            {
                return half4(Box(_MainTex, _MainTex_TexelSize, i.uv, 0.5), 1);
            }
            ENDCG
        }

        Pass // 3: composicion final + color + vinieta
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            half4 frag (v2f_img i) : SV_Target
            {
                float2 uvB = i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) uvB.y = 1 - uvB.y;
                #endif
                half3 c = tex2D(_MainTex, i.uv).rgb;

                // Profundidad de campo: lo que esta mas lejos que la accion se desenfoca (efecto maqueta).
                float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uvB));
                half coc = saturate((depth - _FocusDist - _FocusStart) / _FocusRange) * _DofAmount;
                c = lerp(c, Box(_BlurTex, _BlurTex_TexelSize, uvB, 1), coc);

                c += Box(_BloomTex, _BloomTex_TexelSize, uvB, 0.5) * _Intensity;

                half lum = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(c * _ShadowTint, c, saturate(lum * 2.0));   // sombras ligeramente azuladas
                c *= _Tint;
                lum = dot(c, half3(0.299, 0.587, 0.114));
                c = lerp(lum.xxx, c, _Saturation);
                c = (c - 0.5) * _Contrast + 0.5;

                float2 d = i.uv - 0.5;
                c *= 1.0 - _Vignette * saturate(dot(d, d) * 2.2);
                return half4(saturate(c), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
