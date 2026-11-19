Shader "Custom/HubDiffuser"
{
    Properties
    {
        _AllianceColor ("Alliance Color", Color) = (1,0,0,1)
        _SweepColor ("Sweep Color", Color) = (1,1,1,1)
        _SweepProgress ("Sweep Progress", Range(0,1)) = 0
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.2)) = 0.05
        _BandWidth ("Band Width", Range(0.01, 1.0)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _AllianceColor;
                float4 _SweepColor;
                float _SweepProgress;   // 0-1: band center position across panel (left to right)
                float _EdgeSoftness;
                float _BandWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Band centered at _SweepProgress, half-width = _BandWidth/2
                float halfWidth = _BandWidth * 0.5;
                float dist = abs(IN.uv.x - _SweepProgress);

                // 1.0 inside band, 0.0 outside, soft edges
                float band = 1.0 - smoothstep(halfWidth - _EdgeSoftness, halfWidth + _EdgeSoftness, dist);

                half4 col = lerp(_AllianceColor, _SweepColor, band);
                col.a = 1;
                return col;
            }
            ENDHLSL
        }
    }
}