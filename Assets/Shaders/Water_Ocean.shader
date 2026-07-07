// Water_Ocean.shader
// URP-compatible ocean water shader.
// Physical Gerstner waves (up to 4 summed), depth-based colour,
// screen-space foam at shorelines using scene depth, animated normal map
// computed procedurally from wave derivatives.
//
// Drop this in Assets/Shaders/Water/ and assign to the Ocean WaterBody's material.

Shader "InfinityProject/Water/Ocean"
{
    Properties
    {
        // - Colour ------------------------------
        [HDR] _ShallowColor  ("Shallow Color",  Color) = (0.15, 0.55, 0.72, 0.82)
        [HDR] _DeepColor     ("Deep Color",     Color) = (0.02, 0.12, 0.32, 0.96)
        _DepthFade           ("Depth Fade Distance", Float) = 4.0
        _HorizonColor        ("Horizon Color",  Color) = (0.60, 0.80, 0.90, 1.0)

        // - Gerstner Waves (4 independent waves) --------------─
        // Each wave: xy = direction (world XZ), z = amplitude, w = frequency
        _Wave0   ("Wave 0  (dir.xy | amp | freq)", Vector) = (1.0, 0.0,  0.35, 0.7)
        _Wave1   ("Wave 1  (dir.xy | amp | freq)", Vector) = (0.7, 0.7,  0.20, 1.1)
        _Wave2   ("Wave 2  (dir.xy | amp | freq)", Vector) = (0.0, 1.0,  0.12, 1.8)
        _Wave3   ("Wave 3  (dir.xy | amp | freq)", Vector) = (-0.6,0.8,  0.08, 2.6)
        _WaveSpeed    ("Wave Speed",   Float) = 1.2
        _WaveCount    ("Wave Count",   Int)   = 3      // 1–4

        // - Foam -------------------------------
        _FoamEnabled     ("Foam Enabled",      Float) = 1.0
        _FoamColor       ("Foam Color",        Color) = (0.92, 0.95, 0.98, 0.9)
        _FoamDistance    ("Foam Shore Dist",   Float) = 1.5
        _FoamSpeed       ("Foam Anim Speed",   Float) = 0.6
        _FoamNoiseScale  ("Foam Noise Scale",  Float) = 8.0

        // - Surface -----------------------------─
        _Smoothness  ("Smoothness",  Range(0,1)) = 0.92
        _Metallic    ("Metallic",    Range(0,1)) = 0.0
        _NormalScale ("Normal Scale",Float)      = 0.6

        // - Subsurface / Fresnel -----------------------
        _FresnelPower ("Fresnel Power", Float) = 4.0
        _SSSColor     ("Subsurface Color", Color) = (0.05, 0.65, 0.55, 1.0)
        _SSSStrength  ("Subsurface Strength", Range(0,1)) = 0.35

        // - Runtime (set by WaterBody.cs) ------------------─
        _WaterTime       ("_WaterTime (runtime)", Float)   = 0.0
        _WaveAmplitude   ("WaveAmplitude (legacy)", Float) = 0.4
        _WaveFrequency   ("WaveFrequency (legacy)", Float) = 0.8
        _BodyType        ("BodyType (runtime)",    Float)  = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        // - Forward lit pass -------------------------
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   WaterVert
            #pragma fragment WaterFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // - Properties -------------------------─
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _HorizonColor;
                half4 _FoamColor, _SSSColor;
                float4 _Wave0, _Wave1, _Wave2, _Wave3;
                float _WaveSpeed, _WaterTime;
                int   _WaveCount;
                float _FoamEnabled, _FoamDistance, _FoamSpeed, _FoamNoiseScale;
                float _DepthFade, _Smoothness, _Metallic, _NormalScale;
                float _FresnelPower, _SSSStrength;
                float _WaveAmplitude, _WaveFrequency, _BodyType;
            CBUFFER_END

            // - Structs ---------------------------─
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float4 screenPos   : TEXCOORD3;
                float  fogFactor   : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // --------------------------------─
            // Gerstner wave: returns (displacement.xyz, normal_contribution.xz)
            // wave = (dir.x, dir.z, amplitude, frequency)
            void GerstnerWave(float4 wave, float3 worldPos, float time,
                              inout float3 disp, inout float3 normal)
            {
                float2 dir   = normalize(wave.xy);
                float  amp   = wave.z;
                float  freq  = wave.w;
                float  speed = _WaveSpeed;
                float  phase = dot(dir, worldPos.xz) * freq + time * speed;
                float  s     = sin(phase);
                float  c     = cos(phase);

                // Gerstner displacement
                disp.x += dir.x * amp * c;
                disp.z += dir.y * amp * c;
                disp.y += amp * s;

                // Approximate normal from wave derivative
                float ka = amp * freq;
                normal.x -= dir.x * ka * c;
                normal.z -= dir.y * ka * c;
            }

            // --------------------------------─
            // Value noise hash (no texture needed)
            float Hash21(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash21(i),             Hash21(i + float2(1,0)), u.x),
                    lerp(Hash21(i + float2(0,1)),Hash21(i + float2(1,1)), u.x),
                    u.y);
            }

            float FBMNoise(float2 p, int oct)
            {
                float v = 0, a = 0.5;
                for (int o = 0; o < oct; o++)
                { v += a * ValueNoise(p); p *= 2.1; a *= 0.5; }
                return v;
            }

            // --------------------------------─
            // Reconstruct linear depth from scene depth buffer
            float GetSceneDepth(float4 screenPos)
            {
                float2 uv = screenPos.xy / screenPos.w;
                float  rawDepth = SampleSceneDepth(uv);
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // --------------------------------─
            Varyings WaterVert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float  t        = _WaterTime;

                // Sum Gerstner waves
                float3 disp   = float3(0,0,0);
                float3 normal = float3(0,1,0);

                GerstnerWave(_Wave0, worldPos, t, disp, normal);
                if (_WaveCount >= 2) GerstnerWave(_Wave1, worldPos, t, disp, normal);
                if (_WaveCount >= 3) GerstnerWave(_Wave2, worldPos, t, disp, normal);
                if (_WaveCount >= 4) GerstnerWave(_Wave3, worldPos, t, disp, normal);

                worldPos += disp;
                normal    = normalize(normal);

                output.positionCS = TransformWorldToHClip(worldPos);
                output.positionWS = worldPos;
                output.normalWS   = TransformObjectToWorldNormal(float3(normal.x, normal.y, normal.z));
                output.uv         = input.uv;
                output.screenPos  = ComputeScreenPos(output.positionCS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // --------------------------------─
            half4 WaterFrag(Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir  = normalize(GetCameraPositionWS() - worldPos);

                // - Depth-based colour --------------------
                float sceneDepth  = GetSceneDepth(input.screenPos);
                float surfDepth   = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
                float depthDiff   = max(0.0, sceneDepth - surfDepth);
                float depthFactor = saturate(depthDiff / _DepthFade);
                half4 waterColor  = lerp(_ShallowColor, _DeepColor, depthFactor);

                // - Fresnel -------------------------─
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                waterColor.rgb = lerp(waterColor.rgb, _HorizonColor.rgb, fresnel * 0.6);

                // - Subsurface scattering approximation -----------─
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float  sss      = pow(saturate(dot(viewDir, -lightDir)), 3.0);
                waterColor.rgb += _SSSColor.rgb * sss * _SSSStrength * depthFactor;

                // - Specular (Blinn-Phong on top of PBR) ----------─
                float3 halfDir  = normalize(viewDir + lightDir);
                float  spec     = pow(saturate(dot(normalWS, halfDir)), lerp(32, 512, _Smoothness));
                waterColor.rgb += _MainLightColor.rgb * spec * _Smoothness * 0.8;

                // - Foam ---------------------------
                if (_FoamEnabled > 0.5)
                {
                    float t       = _WaterTime * _FoamSpeed;
                    float2 foamUV = worldPos.xz * _FoamNoiseScale * 0.1;
                    float noise1  = FBMNoise(foamUV + float2(t * 0.3, t * 0.1), 3);
                    float noise2  = FBMNoise(foamUV * 1.7 - float2(t * 0.2, t * 0.4), 3);
                    float foamN   = (noise1 + noise2) * 0.5;

                    // Shore foam: driven by depth
                    float shoreFactor = 1.0 - saturate(depthDiff / _FoamDistance);
                    float foamMask    = saturate(shoreFactor * 1.5) * saturate(foamN + shoreFactor - 0.3);
                    // Crest foam: driven by wave height
                    float crestMask   = saturate((worldPos.y - 0.2) * 2.0) * saturate(foamN - 0.3);
                    float totalFoam   = saturate(foamMask + crestMask * 0.4);
                    waterColor.rgb    = lerp(waterColor.rgb, _FoamColor.rgb, totalFoam * _FoamColor.a);
                    waterColor.a      = lerp(waterColor.a,  1.0, totalFoam * 0.6);
                }

                // - Ambient -------------------------─
                waterColor.rgb += unity_AmbientSky.rgb * 0.05;

                // - Fog ---------------------------─
                waterColor.rgb = MixFog(waterColor.rgb, input.fogFactor);

                return waterColor;
            }
            ENDHLSL
        }

        // Depth-only pass (needed for shadows and depth prepass)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Wave0, _Wave1, _Wave2, _Wave3;
                float  _WaveSpeed, _WaterTime;
                int    _WaveCount;
                float  _WaveAmplitude, _WaveFrequency, _BodyType;
                half4  _ShallowColor, _DeepColor, _HorizonColor, _FoamColor, _SSSColor;
                float  _FoamEnabled, _FoamDistance, _FoamSpeed, _FoamNoiseScale;
                float  _DepthFade, _Smoothness, _Metallic, _NormalScale;
                float  _FresnelPower, _SSSStrength;
            CBUFFER_END

            struct Attribs { float4 pos : POSITION; };
            struct Vout    { float4 pos : SV_POSITION; };

            void GerstnerWaveD(float4 wave, float3 wp, float t, inout float3 d, inout float3 n)
            {
                float2 dir = normalize(wave.xy); float amp = wave.z, freq = wave.w;
                float phase = dot(dir, wp.xz) * freq + t * _WaveSpeed;
                d.x += dir.x * amp * cos(phase); d.z += dir.y * amp * cos(phase);
                d.y += amp * sin(phase);
                float ka = amp * freq;
                n.x -= dir.x * ka * cos(phase); n.z -= dir.y * ka * cos(phase);
            }

            Vout DepthVert(Attribs i)
            {
                Vout o;
                float3 wp = TransformObjectToWorld(i.pos.xyz);
                float3 d = 0, n = float3(0,1,0);
                GerstnerWaveD(_Wave0, wp, _WaterTime, d, n);
                if (_WaveCount >= 2) GerstnerWaveD(_Wave1, wp, _WaterTime, d, n);
                if (_WaveCount >= 3) GerstnerWaveD(_Wave2, wp, _WaterTime, d, n);
                if (_WaveCount >= 4) GerstnerWaveD(_Wave3, wp, _WaterTime, d, n);
                wp += d;
                o.pos = TransformWorldToHClip(wp);
                return o;
            }
            half4 DepthFrag(Vout i) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
