// Terrain_Surface.shader
// URP terrain shader. Replaces HeightColor.shader.
// 4 layers: Sand, Grass, Rock, Snow - blended by normalised height and slope.
// Each layer: assignable albedo texture + procedural fallback colour.
// Water simulation removed - handled by dedicated water shaders.

Shader "InfinityProject/Terrain/Surface"
{
    Properties
    {
        // ── Layer colours (used when no texture assigned) ─────────────────────
        _SandColor    ("Sand Color",    Color) = (0.76, 0.70, 0.50, 1)
        _GrassColor   ("Grass Color",   Color) = (0.30, 0.52, 0.22, 1)
        _RockColor    ("Rock Color",    Color) = (0.45, 0.40, 0.35, 1)
        _SnowColor    ("Snow Color",    Color) = (0.92, 0.94, 0.96, 1)

        // ── Layer textures (optional - leave empty for procedural) ────────────
        [NoScaleOffset] _SandTex  ("Sand Texture  (optional)", 2D) = "white" {}
        [NoScaleOffset] _GrassTex ("Grass Texture (optional)", 2D) = "white" {}
        [NoScaleOffset] _RockTex  ("Rock Texture  (optional)", 2D) = "white" {}
        [NoScaleOffset] _SnowTex  ("Snow Texture  (optional)", 2D) = "white" {}
        _TexScale ("Texture Scale (m)", Float) = 8.0

        // 1 = texture assigned, 0 = procedural (set automatically by C# if desired, or manually)
        _SandTexEnabled  ("", Float) = 0
        _GrassTexEnabled ("", Float) = 0
        _RockTexEnabled  ("", Float) = 0
        _SnowTexEnabled  ("", Float) = 0

        // ── Height thresholds (normalised 0-1 relative to terrain height) ────
        _SandMaxHeight  ("Sand Max Height",  Range(0,1)) = 0.32
        _GrassMaxHeight ("Grass Max Height", Range(0,1)) = 0.62
        _RockMaxHeight  ("Rock Max Height",  Range(0,1)) = 0.84
        // Above RockMax -> Snow

        // ── Slope thresholds (dot(normal,up), 1=flat, 0=vertical) ────────────
        _RockSlopeMin ("Rock Slope Threshold", Range(0,1)) = 0.72
        // Below this -> Rock regardless of height (cliffs)

        // ── Blend sharpness ───────────────────────────────────────────────────
        _BlendSharpness ("Blend Sharpness", Range(1,16)) = 6.0

        // ── Generated world-data mask (R wetness, G biomass, B flow, A retention) ──
        [NoScaleOffset] _WorldDataTex ("World Data (auto)", 2D) = "gray" {}
        _WorldDataEnabled ("World Data Enabled", Float) = 0
        _WetnessDarken ("Wetness Darken", Range(0,0.5)) = 0.18
        _BiomassGreenBoost ("Biomass Green Boost", Range(0,0.5)) = 0.22
        _FlowDarken ("Flow Darken", Range(0,0.5)) = 0.10

        // ── Lighting ─────────────────────────────────────────────────────────
        _Smoothness ("Smoothness", Range(0,1)) = 0.12
        _Metallic   ("Metallic",   Range(0,1)) = 0.0

        // ── Micro-detail: procedural noise overlay ────────────────────────────
        _DetailStrength ("Detail Strength", Range(0,1)) = 0.12
        _DetailScale    ("Detail Size (m)", Float)      = 32.0

        // ── Terrain world-space height range (set by C# after generation) ────
        _TerrainBaseY  ("Terrain Base Y  (auto)",  Float) = 0.0
        _TerrainHeight ("Terrain Height  (auto)",  Float) = 150.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-100" "TerrainCompatible"="True" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   TerrainVert
            #pragma fragment TerrainFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SandColor, _GrassColor, _RockColor, _SnowColor;
                float _SandMaxHeight, _GrassMaxHeight, _RockMaxHeight;
                float _RockSlopeMin;
                float _BlendSharpness;
                float _Smoothness, _Metallic;
                float _DetailStrength, _DetailScale;
                float _TexScale;
                float _SandTexEnabled, _GrassTexEnabled, _RockTexEnabled, _SnowTexEnabled;
                float _TerrainBaseY, _TerrainHeight;
                float _WorldDataEnabled, _WetnessDarken, _BiomassGreenBoost, _FlowDarken;
            CBUFFER_END

            TEXTURE2D(_SandTex);  SAMPLER(sampler_SandTex);
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_RockTex);  SAMPLER(sampler_RockTex);
            TEXTURE2D(_SnowTex);  SAMPLER(sampler_SnowTex);
            TEXTURE2D(_WorldDataTex); SAMPLER(sampler_WorldDataTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;  // heightmap UV
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Noise for procedural detail ────────────────────────────────────
            float Hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p.yx + 19.19);
                return frac(p.x * p.y);
            }
            float VNoise(float2 p)
            {
                float2 i=floor(p), f=frac(p);
                float2 u=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),u.x),
                            lerp(Hash(i+float2(0,1)),Hash(i+float2(1,1)),u.x),u.y);
            }
            float Detail(float2 worldXZ)
            {
                float size = max(_DetailScale, 1.0);
                float broad = VNoise(worldXZ / size);
                float fine = VNoise(worldXZ / (size * 0.45) + 0.7);
                return broad * 0.65 + fine * 0.35;
            }

            float4 HeightLayerWeights(float height01)
            {
                float sharp01 = saturate((_BlendSharpness - 1.0) / 15.0);
                float blend = lerp(0.10, 0.035, sharp01);

                float sandToGrass = smoothstep(_SandMaxHeight - blend, _SandMaxHeight + blend, height01);
                float grassToRock = smoothstep(_GrassMaxHeight - blend, _GrassMaxHeight + blend, height01);
                float rockToSnow = smoothstep(_RockMaxHeight - blend, _RockMaxHeight + blend, height01);

                float4 weights;
                weights.x = 1.0 - sandToGrass;
                weights.y = sandToGrass * (1.0 - grassToRock);
                weights.z = grassToRock * (1.0 - rockToSnow);
                weights.w = rockToSnow;
                return weights;
            }

            Varyings TerrainVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings o = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.uv         = input.texcoord;
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 TerrainFrag(Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                float3 normalWS = normalize(input.normalWS);

                // ── Normalised height [0,1] ────────────────────────────────────
                // Derive from world Y: (worldY - terrainBase) / terrainMaxHeight
                // _TerrainBaseY and _TerrainHeight are set by C# after generation.
                float normH = saturate((worldPos.y - _TerrainBaseY) / max(_TerrainHeight, 0.01));

                // ── Slope (0=cliff, 1=flat) ────────────────────────────────────
                float slope = saturate(dot(normalWS, float3(0,1,0)));

                // World-space triplanar sampling avoids stretched bands on steep slopes.
                float3 triWeights = abs(normalWS);
                triWeights *= triWeights;
                triWeights *= triWeights;
                triWeights /= max(triWeights.x + triWeights.y + triWeights.z, 0.0001);

                #define SampleLayer(tex, sampl, enabled, col) \
                    (enabled > 0.5 ? (half3)(( \
                        SAMPLE_TEXTURE2D(tex, sampl, worldPos.zy / _TexScale).rgb * triWeights.x + \
                        SAMPLE_TEXTURE2D(tex, sampl, worldPos.xz / _TexScale).rgb * triWeights.y + \
                        SAMPLE_TEXTURE2D(tex, sampl, worldPos.xy / _TexScale).rgb * triWeights.z) * col.rgb * 1.55) : (half3)col.rgb)

                half3 sandC  = SampleLayer(_SandTex,  sampler_SandTex,  _SandTexEnabled,  _SandColor);
                half3 grassC = SampleLayer(_GrassTex, sampler_GrassTex, _GrassTexEnabled, _GrassColor);
                half3 rockC  = SampleLayer(_RockTex,  sampler_RockTex,  _RockTexEnabled,  _RockColor);
                half3 snowC  = SampleLayer(_SnowTex,  sampler_SnowTex,  _SnowTexEnabled,  _SnowColor);

                float4 worldData = _WorldDataEnabled > 0.5
                    ? SAMPLE_TEXTURE2D(_WorldDataTex, sampler_WorldDataTex, input.uv)
                    : float4(0.5, 0.5, 0.0, 0.5);
                float wetness = saturate(worldData.r);
                float biomass = saturate(worldData.g);
                float flowSignal = saturate(worldData.b);

                // ── Layer blend weights ───────────────────────────────────────
                // Sand:  low heights
                // Grass: mid heights
                // Rock:  high heights OR steep slope
                // Snow:  very high heights

                float4 heightWeights = HeightLayerWeights(normH);
                float wSand = heightWeights.x;
                float wGrass = heightWeights.y;
                float wRock = heightWeights.z;
                float wSnow = heightWeights.w;

                // Slope override: blend Rock in on cliffs regardless of height
                float slopeMask = 1.0 - saturate((slope - _RockSlopeMin) / max(1.0-_RockSlopeMin, 0.01));
                wRock = saturate(wRock + slopeMask * (wSand + wGrass + wSnow) * 0.85);
                wSand  *= (1.0 - slopeMask);
                wGrass *= (1.0 - slopeMask);
                wSnow  *= (1.0 - slopeMask * 0.6);

                // Generated ecological state nudges the material without replacing geology.
                wGrass *= lerp(0.72, 1.35, biomass);
                wSand  *= lerp(1.15, 0.62, wetness);

                // Normalise weights so they sum to 1
                float wSum = wSand + wGrass + wRock + wSnow + 1e-6;
                wSand/=wSum; wGrass/=wSum; wRock/=wSum; wSnow/=wSum;

                // ── Blend ─────────────────────────────────────────────────────
                half3 albedo = sandC*wSand + grassC*wGrass + rockC*wRock + snowC*wSnow;
                albedo *= 1.0 - wetness * _WetnessDarken;
                albedo = lerp(albedo, albedo * half3(0.88, 1.12, 0.86), biomass * _BiomassGreenBoost);
                albedo *= 1.0 - flowSignal * _FlowDarken;

                // ── Procedural detail overlay ─────────────────────────────────
                float det = Detail(worldPos.xz) * 2.0 - 1.0;
                albedo = saturate(albedo + det * _DetailStrength * (1.0 - wSnow));

                // ── Lighting (URP SimpleLit-style) ────────────────────────────
                InputData id = (InputData)0;
                id.positionWS    = worldPos;
                id.normalWS      = normalWS;
                id.viewDirectionWS = normalize(GetCameraPositionWS() - worldPos);
                id.shadowCoord   = TransformWorldToShadowCoord(worldPos);
                id.fogCoord      = input.fogFactor;
                id.bakedGI       = SampleSH(normalWS);

                SurfaceData sd = (SurfaceData)0;
                sd.albedo        = albedo;
                sd.smoothness    = _Smoothness * (1.0 - wRock * 0.5);
                sd.metallic      = _Metallic;
                sd.occlusion     = 1.0;
                sd.alpha         = 1.0;
                sd.normalTS      = float3(0,0,1);

                half4 color = UniversalFragmentPBR(id, sd);
                color.rgb = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        // Shadow caster - manual implementation, no ShadowCasterPass.hlsl dependency
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   ShadowCasterVert
            #pragma fragment ShadowCasterFrag
            #pragma multi_compile_shadowcaster
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Only the properties actually used in this pass need to be in the CBUFFER
            CBUFFER_START(UnityPerMaterial)
                // Dummy entries - keeps the CBUFFER valid; values unused in shadow pass
                half4 _SandColor, _GrassColor, _RockColor, _SnowColor;
                float _SandMaxHeight, _GrassMaxHeight, _RockMaxHeight;
                float _RockSlopeMin;
                float _BlendSharpness;
                float _Smoothness, _Metallic;
                float _DetailStrength, _DetailScale;
                float _TexScale;
                float _SandTexEnabled, _GrassTexEnabled, _RockTexEnabled, _SnowTexEnabled;
                float _TerrainBaseY, _TerrainHeight;
                float _WorldDataEnabled, _WetnessDarken, _BiomassGreenBoost, _FlowDarken;
            CBUFFER_END

            struct ShadowAttribs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowCasterVert(ShadowAttribs input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS    = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // Simple constant shadow bias - robust across URP versions
                float3 lightDir = normalize(_MainLightPosition.xyz);
                posWS += lightDir * 0.004 + normalWS * 0.002;

                output.positionCS = TransformWorldToHClip(posWS);

                // Clamp depth to avoid objects behind near plane dropping out of shadow map
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                return output;
            }

            half4 ShadowCasterFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Depth only - used by SSAO, depth of field, depth prepass
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthOnlyVert
            #pragma fragment DepthOnlyFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SandColor, _GrassColor, _RockColor, _SnowColor;
                float _SandMaxHeight, _GrassMaxHeight, _RockMaxHeight;
                float _RockSlopeMin;
                float _BlendSharpness;
                float _Smoothness, _Metallic;
                float _DetailStrength, _DetailScale;
                float _TexScale;
                float _SandTexEnabled, _GrassTexEnabled, _RockTexEnabled, _SnowTexEnabled;
                float _TerrainBaseY, _TerrainHeight;
                float _WorldDataEnabled, _WetnessDarken, _BiomassGreenBoost, _FlowDarken;
            CBUFFER_END

            struct DepthAttribs   { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVaryings  { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO  };

            DepthVaryings DepthOnlyVert(DepthAttribs input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DepthVaryings output = (DepthVaryings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthOnlyFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
