Shader "OffshoreWake/BladeDeflection"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.9, 0.92, 0.95, 1)
        _TipColor ("Tip Color", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.75
        _Metallic ("Metallic", Range(0, 1)) = 0.05

        [Header(Blade Shape)]
        _BladeLength ("Blade Length (m)", Float) = 63.0
        _ChordRoot ("Root Chord (m)", Float) = 4.5
        _ChordTip ("Tip Chord (m)", Float) = 1.2
        _BladeThickness ("Max Thickness", Float) = 0.8
        _TwistRoot ("Root Twist (deg)", Float) = 12.0
        _TwistTip ("Tip Twist (deg)", Float) = -3.0

        [Header(Aeroelastic Deflection)]
        _FlapwiseTip ("Flapwise Tip Deflection (m)", Float) = 0.0
        _EdgewiseTip ("Edgewise Tip Deflection (m)", Float) = 0.0
        _TorsionTip ("Tip Torsion (deg)", Float) = 0.0
        _DeflectionSmooth ("Deflection Smoothness", Range(1, 10)) = 4.0
        _DynamicScale ("Dynamic Visual Scale", Float) = 15.0

        [Header(Vibration)]
        _VibFreq1 ("1st Mode Freq (Hz)", Float) = 0.65
        _VibFreq2 ("2nd Mode Freq (Hz)", Float) = 2.1
        _VibDamping ("Vibration Damping", Range(0, 1)) = 0.95
        _GustNoise ("Gust Noise Amplitude", Float) = 0.3
        _Time ("Time", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow vertex:vert
        #pragma target 5.0

        struct Input
        {
            float2 uv_MainTex;
            float3 color;
            float bladeSpan;
        };

        fixed4 _BaseColor;
        fixed4 _TipColor;
        half _Glossiness;
        half _Metallic;

        float _BladeLength;
        float _ChordRoot;
        float _ChordTip;
        float _BladeThickness;
        float _TwistRoot;
        float _TwistTip;

        float _FlapwiseTip;
        float _EdgewiseTip;
        float _TorsionTip;
        float _DeflectionSmooth;
        float _DynamicScale;

        float _VibFreq1;
        float _VibFreq2;
        float _VibDamping;
        float _GustNoise;
        float _Time;

        float3 CubicBezier(float3 p0, float3 p1, float3 p2, float3 p3, float t)
        {
            float u = 1.0 - t;
            return u*u*u*p0 + 3*u*u*t*p1 + 3*u*t*t*p2 + t*t*t*p3;
        }

        float3 CatmullRom(float3 p0, float3 p1, float3 p2, float3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5 * ((2*p1) +
                (-p0 + p2) * t +
                (2*p0 - 5*p1 + 4*p2 - p3) * t2 +
                (-p0 + 3*p1 - 3*p2 + p3) * t3);
        }

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            float bladeSpan = (v.vertex.y + 0.5);
            bladeSpan = clamp(bladeSpan, 0.0, 1.0);
            o.bladeSpan = bladeSpan;

            float spanN = bladeSpan;
            float spanPow = pow(spanN, _DeflectionSmooth);
            float spanQuad = spanN * spanN;
            float spanCubic = spanQuad * spanN;

            float dynFlap = _FlapwiseTip * _DynamicScale;
            float dynEdge = _EdgewiseTip * _DynamicScale * 0.3;
            float dynTorsion = _TorsionTip;

            float flapShape = spanCubic;
            float edgeShape = spanCubic;
            float torsionShape = spanN;

            float flapBend = dynFlap * flapShape;
            float edgeBend = dynEdge * edgeShape;
            float twist = dynTorsion * torsionShape * 3.14159 / 180.0;

            float vibPhase1 = _Time * _VibFreq1 * 6.28318;
            float vibPhase2 = _Time * _VibFreq2 * 6.28318;
            float envelope = exp(-(1.0 - _VibDamping) * spanN * 5.0);
            float vib1 = sin(vibPhase1 + spanN * 3.0) * spanQuad * envelope;
            float vib2 = sin(vibPhase2 + spanN * 7.0) * spanCubic * envelope * 0.3;
            float gust = sin(_Time * 3.7 + spanN * 5.0) * cos(_Time * 2.3) * _GustNoise * spanQuad;

            float totalFlap = flapBend + vib1 * dynFlap * 0.01 + gust * dynFlap * 0.005;
            float totalEdge = edgeBend + vib2 * dynEdge * 0.01;
            float totalTwist = twist + vib1 * 0.02;

            float chordTaper = _ChordRoot + (_ChordTip - _ChordRoot) * spanN;
            float thicknessTaper = _BladeThickness * (1.0 - 0.5 * spanQuad);
            float twistTaper = lerp(_TwistRoot, _TwistTip, spanN) * 3.14159 / 180.0;
            totalTwist += twistTaper;

            float3 localPos = v.vertex.xyz;

            float3 originalSpan = float3(0, spanN * _BladeLength - _BladeLength * 0.5, 0);

            float3 deformedSpan = float3(
                totalEdge,
                originalSpan.y,
                -totalFlap
            );

            float bendAngle1 = atan2(totalFlap, _BladeLength * 0.9 + 0.001);
            float bendAngle2 = atan2(totalEdge, _BladeLength * 0.9 + 0.001);

            float sinF, cosF, sinE, cosE;
            sincos(bendAngle1 * spanN, sinF, cosF);
            sincos(bendAngle2 * spanN * 0.3, sinE, cosE);

            float3x3 flapBendMat = float3x3(
                1, 0, 0,
                0, cosF, -sinF,
                0, sinF, cosF
            );

            float3x3 edgeBendMat = float3x3(
                cosE, 0, sinE,
                0, 1, 0,
                -sinE, 0, cosE
            );

            float3x3 torsionMat;
            float sinT, cosT;
            sincos(totalTwist, sinT, cosT);
            torsionMat = float3x3(
                cosT, 0, sinT,
                0, 1, 0,
                -sinT, 0, cosT
            );

            float3 chordOffset = float3(localPos.x * chordTaper, 0, localPos.z * thicknessTaper);

            chordOffset = mul(torsionMat, chordOffset);
            chordOffset = mul(edgeBendMat, chordOffset);
            chordOffset = mul(flapBendMat, chordOffset);

            float3 finalPos = deformedSpan + chordOffset;

            float3x3 totalRot = mul(mul(flapBendMat, edgeBendMat), torsionMat);
            float3 newNormal = normalize(mul(totalRot, v.normal));

            v.vertex.xyz = finalPos;
            v.normal = newNormal;

            o.color = lerp(_BaseColor.rgb, _TipColor.rgb, spanN).rgb;

            float stressStain = abs(totalFlap) / max(1.0, dynFlap + 0.001);
            o.color += float3(stressStain * 0.1, -stressStain * 0.05, -stressStain * 0.05);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = fixed4(IN.color, 1.0);
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
