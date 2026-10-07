// Igual que LB/Toon pero semitransparente (alpha del color). Para la baranda delantera.
Shader "LB/ToonFade"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.35)
        _MainTex ("Texture", 2D) = "white" {}
        _Spec ("Specular", Range(0,1.5)) = 0.45
        _Gloss ("Gloss", Range(0,1)) = 0.55
        _Rim ("Rim", Range(0,1.5)) = 0.35
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        _Wrap ("Diffuse Wrap", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 200
        ZWrite Off

        CGPROGRAM
        #pragma surface surf LBToon alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Spec;
        half _Gloss;
        half _Rim;
        fixed4 _RimColor;
        fixed4 _Emission;
        half _Wrap;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
        };

        half4 LightingLBToon (SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
            half diff = saturate((ndl + _Wrap) / (1.0 + _Wrap));
            diff = diff * diff * (3.0 - 2.0 * diff);
            half3 h = normalize(lightDir + viewDir);
            half nh = saturate(dot(s.Normal, h));
            half spec = pow(nh, 8.0 + _Gloss * 120.0) * _Spec * saturate(ndl * 4.0);
            half4 c;
            c.rgb = (s.Albedo * _LightColor0.rgb * diff + _LightColor0.rgb * spec) * atten;
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            o.Emission = (_RimColor.rgb * pow(rim, 3.0) * _Rim + _Emission.rgb) * c.a;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Transparent/Diffuse"
}
