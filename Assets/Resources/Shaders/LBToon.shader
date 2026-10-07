// Iluminacion "plastico brillante" al estilo BombSquad: difuso suave envolvente,
// especular marcado y luz de borde. Built-in Render Pipeline.
Shader "LB/Toon"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Spec ("Specular", Range(0,1.5)) = 0.45
        _Gloss ("Gloss", Range(0,1)) = 0.55
        _Rim ("Rim", Range(0,1.5)) = 0.35
        _RimColor ("Rim Color", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (0,0,0,0)
        _Wrap ("Diffuse Wrap", Range(0,1)) = 0.45
        _CrackTex ("Cracks", 2D) = "black" {}
        _Damage ("Damage", Range(0,1)) = 0
        _DamageColor ("Damage Color", Color) = (0.12,0.08,0.07,1)
        _SootTex ("Soot", 2D) = "black" {}
        _Soot ("Soot", Range(0,1)) = 0
        [Toggle] _VertexDamage ("Damage from vertex colors (r=soot, g=cracks)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf LBToon fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Spec;
        half _Gloss;
        half _Rim;
        fixed4 _RimColor;
        fixed4 _Emission;
        half _Wrap;
        sampler2D _CrackTex;
        half _Damage;
        fixed4 _DamageColor;
        sampler2D _SootTex;
        half _Soot;
        half _VertexDamage;

        struct Input
        {
            float2 uv_MainTex;
            float3 viewDir;
            float4 color : COLOR;
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
            // Piezas del cuerpo: hollin y grietas vienen por vertice (r = hollin, g = grietas).
            half dmg = _Damage;
            half soot = _Soot;
            if (_VertexDamage > 0.5)
            {
                soot = IN.color.r;
                dmg = IN.color.g;
            }
            // Desgaste: las grietas/rasgunos aparecen de mas fuertes a mas finos segun el dano.
            if (dmg > 0.001)
            {
                half cr = tex2D(_CrackTex, IN.uv_MainTex * 3.0).r;
                half mask = saturate((cr - (1.0 - dmg)) * 7.0);
                c.rgb *= 1.0 - dmg * 0.12;
                c.rgb = lerp(c.rgb, _DamageColor.rgb, mask * 0.85);
            }
            // Hollin / chamuscado: manchas que crecen; muy quemado -> brasas que parpadean.
            half3 ember = 0;
            if (soot > 0.001)
            {
                half sn = tex2D(_SootTex, IN.uv_MainTex * 1.7).r;
                half sm = saturate((sn - (1.0 - soot)) * 3.0 + soot * 0.3);
                c.rgb = lerp(c.rgb, half3(0.07, 0.06, 0.055), sm * 0.88);
                half e = saturate((soot - 0.7) * 3.3) * smoothstep(0.8, 0.95, sn);
                ember = half3(1.0, 0.35, 0.08) * e * (0.6 + 0.4 * sin(_Time.y * 5.0 + sn * 20.0));
            }
            o.Albedo = c.rgb;
            half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
            o.Emission = _RimColor.rgb * pow(rim, 3.0) * _Rim * (1.0 - soot * 0.7) + _Emission.rgb + ember;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
