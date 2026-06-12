Shader "OffshoreWake/WindFieldVisualize"
{
    Properties
    {
        _MainTex ("Wind Velocity (RG)", 2D) = "white" {}
        _TurbulenceTex ("Turbulence Intensity", 2D) = "black" {}
        _FreeStreamSpeed ("Free Stream Speed", Float) = 10.0
        _VizMode ("Visualization Mode", Int) = 0
        _ColorScale ("Color Scale", Float) = 1.0
        _ShowStreamlines ("Show Streamlines", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _TurbulenceTex;
            float _FreeStreamSpeed;
            int _VizMode;
            float _ColorScale;
            float _ShowStreamlines;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 TurboColormap(float t)
            {
                t = saturate(t);
                const float4 kRedVec4 = float4(0.13572138, 4.61539260, -42.66032258, 132.13108234);
                const float4 kRedVec3 = float4(-152.94239396, 59.28637943, 2.52952459, 0.0);
                const float4 kGreenVec4 = float4(0.09140261, 2.19568610, 4.63306534, -20.77574975);
                const float4 kGreenVec3 = float4(46.72805044, -27.45398565, 3.87304373, 0.0);
                const float4 kBlueVec4 = float4(0.10667330, 12.65924927, -59.87139847, 147.72555892);
                const float4 kBlueVec3 = float4(-159.04227388, 68.82344878, 0.0, 0.0);

                float4 v4 = float4(1.0, t, t*t, t*t*t);
                float4 v3 = float4(v4.xyz * v4.w, 0.0);

                float r = dot(v4, kRedVec4) + dot(v3, kRedVec3);
                float g = dot(v4, kGreenVec4) + dot(v3, kGreenVec3);
                float b = dot(v4, kBlueVec4) + dot(v3, kBlueVec3);
                return float3(r, g, b);
            }

            float3 JetColormap(float t)
            {
                t = saturate(t);
                float r = clamp(1.5 - abs(4.0 * t - 3.0), 0.0, 1.0);
                float g = clamp(1.5 - abs(4.0 * t - 2.0), 0.0, 1.0);
                float b = clamp(1.5 - abs(4.0 * t - 1.0), 0.0, 1.0);
                return float3(r, g, b);
            }

            float3 WakeDeficitColormap(float deficit)
            {
                deficit = saturate(deficit);
                if (deficit < 0.25)
                    return lerp(float3(0.1, 0.1, 0.4), float3(0.0, 0.5, 1.0), deficit * 4.0);
                else if (deficit < 0.5)
                    return lerp(float3(0.0, 0.5, 1.0), float3(0.0, 0.8, 0.3), (deficit - 0.25) * 4.0);
                else if (deficit < 0.75)
                    return lerp(float3(0.0, 0.8, 0.3), float3(1.0, 0.8, 0.0), (deficit - 0.5) * 4.0);
                else
                    return lerp(float3(1.0, 0.8, 0.0), float3(1.0, 0.1, 0.0), (deficit - 0.75) * 4.0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 vel = tex2D(_MainTex, i.uv).rg;
                float speed = length(vel);
                float turb = tex2D(_TurbulenceTex, i.uv).r;
                float normalizedSpeed = speed / max(_FreeStreamSpeed, 0.001) * _ColorScale;
                float wakeDeficit = 1.0 - saturate(normalizedSpeed);

                float3 col = float3(0, 0, 0);

                if (_VizMode == 0)
                {
                    col = TurboColormap(normalizedSpeed);
                }
                else if (_VizMode == 1)
                {
                    col = WakeDeficitColormap(wakeDeficit);
                }
                else if (_VizMode == 2)
                {
                    col = JetColormap(saturate(turb * 10.0));
                }
                else if (_VizMode == 3)
                {
                    col = TurboColormap(normalizedSpeed);
                    float turbOverlay = saturate(turb * 10.0);
                    col = lerp(col, float3(1.0, 0.2, 0.0), turbOverlay * 0.5);
                }

                if (_ShowStreamlines > 0.5)
                {
                    float2 gridUv = i.uv * 512.0;
                    float2 cellFrac = frac(gridUv);

                    float angle = atan2(vel.y, vel.x);
                    float linePattern = sin(angle * 2.0 + cellFrac.x * 20.0) * 0.5 + 0.5;
                    float dashPattern = sin(cellFrac.x * 40.0 + angle * 5.0) * 0.5 + 0.5;
                    float streamline = linePattern * dashPattern;
                    streamline = smoothstep(0.3, 0.7, streamline);

                    float speedFade = saturate(normalizedSpeed * 2.0);
                    col = lerp(col, col * 0.3 + float3(1, 1, 1) * 0.7, streamline * speedFade * 0.15);
                }

                float2 gridLines = abs(frac(i.uv * 64.0) - 0.5);
                float gridMask = 1.0 - smoothstep(0.48, 0.5, max(gridLines.x, gridLines.y));
                col = lerp(col, col * 0.85, gridMask * 0.3);

                return float4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
