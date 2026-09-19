// Terrain_DebugDataOverlay.shader
// Editor-oriented overlay used by Infinity's world-data debugger.
// Projects a generated diagnostic texture directly onto Unity Terrain geometry.
// The source Terrain is never modified; editor tooling renders a temporary Terrain copy.

Shader "Hidden/InfinityProject/Terrain/DebugDataOverlay"
{
    Properties
    {
        [NoScaleOffset] _DebugMap ("Debug Map", 2D) = "white" {}
        _Opacity ("Opacity", Range(0,1)) = 0.86
        _ReliefShading ("Relief Shading", Range(0,1)) = 0.38
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent+100"
        }

        Pass
        {
            Name "DebugOverlay"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Opacity;
                float _ReliefShading;
            CBUFFER_END

            TEXTURE2D(_DebugMap);
            SAMPLER(sampler_DebugMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.texcoord;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 map = SAMPLE_TEXTURE2D(_DebugMap, sampler_DebugMap, input.uv);

                // Stable synthetic lighting keeps the terrain's relief readable even when
                // the Scene has no useful directional light. Set Relief Shading to 0 to
                // see the raw diagnostic colours exactly.
                float3 normalWS = normalize(input.normalWS);
                float3 debugLightDirection = normalize(float3(0.38, 0.86, 0.34));
                float lambert = saturate(dot(normalWS, debugLightDirection));
                float relief = lerp(1.0, 0.42 + lambert * 0.58, saturate(_ReliefShading));

                map.rgb *= relief;
                map.a *= saturate(_Opacity);
                return map;
            }
            ENDHLSL
        }
    }
}
