// Water_River.shader
// URP river shader for spline-driven WaterRiver meshes.
// UV.y flows along the river (set by WaterRiver ribbon builder: u=0->1 cross-river, v=0->1 along river).
// Animated flow, edge foam, depth colour, turbulence noise.
// Naturally handles curves — flow direction encoded in UV gradient.

Shader "InfinityProject/Water/River"
{
    Properties
    {
        // - Colour ------------------------------
        [HDR] _ShallowColor ("Shallow Color", Color) = (0.28, 0.62, 0.55, 0.80)
        [HDR] _DeepColor    ("Deep Color",    Color) = (0.05, 0.22, 0.35, 0.92)
        _DepthFade          ("Depth Fade",    Float) = 1.5

        // - Flow -------------------------------
        _FlowSpeed       ("Flow Speed",        Float) = 0.8
        _FlowTurbulence  ("Flow Turbulence",   Range(0,1)) = 0.3
        _FlowNoiseScale  ("Flow Noise Scale",  Float) = 3.0
        // Flow distortion: offsets UV sample to simulate eddies
        _FlowDistort     ("Flow Distortion",   Float) = 0.12

        // - Surface -----------------------------─
        _Smoothness  ("Smoothness",  Range(0,1)) = 0.85
        _NormalScale ("Normal Bump", Float)      = 0.5
        _FresnelPower("Fresnel",     Float)      = 4.0

        // - Edge / Shore Foam ------------------------─
        _FoamDistance   ("Edge Foam Width",  Range(0,0.5)) = 0.12
        _FoamColor      ("Foam Color",       Color)        = (0.92, 0.95, 0.98, 0.9)
        _FoamSpeed      ("Foam Anim Speed",  Float)        = 0.5
        _FoamNoiseScale ("Foam Noise Scale", Float)        = 8.0

        // - Caustics (cheap projected pattern) ----------------
        _CausticsStrength ("Caustics Strength", Range(0,1)) = 0.25
        _CausticsScale    ("Caustics Scale",    Float)      = 8.0
        _CausticsSpeed    ("Caustics Speed",    Float)      = 0.4

        // - Runtime -----------------------------─
        _WaterTime      ("_WaterTime (runtime)", Float) = 0.0
        _ShallowColor2  ("ShallowColor (legacy)", Color) = (0.28, 0.62, 0.55, 0.80)
        _DeepColor2     ("DeepColor (legacy)", Color)   = (0.05, 0.22, 0.35, 0.92)
        _FoamDistance2  ("FoamDistance (legacy)", Float) = 0.6
        _BodyType       ("BodyType (runtime)", Float)   = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off          // Double-sided; river banks are sometimes viewed from below

            HLSLPROGRAM
            #pragma vertex   RiverVert
            #pragma fragment RiverFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthFade;
                float _FlowSpeed, _FlowTurbulence, _FlowNoiseScale, _FlowDistort;
                float _Smoothness, _NormalScale, _FresnelPower;
                float _FoamDistance, _FoamSpeed, _FoamNoiseScale;
                float _CausticsStrength, _CausticsScale, _CausticsSpeed;
                float _WaterTime;
                half4 _ShallowColor2, _DeepColor2;
                float _FoamDistance2, _BodyType;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;   // u=cross-river [0,1], v=along-river [0,1]
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;   // original ribbon UV
                float4 screenPos  : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            // - Noise ----------------------------─
            float Hash2(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }
            float VN(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f*f*(3-2*f);
                return lerp(lerp(Hash2(i), Hash2(i+float2(1,0)), u.x),
                            lerp(Hash2(i+float2(0,1)), Hash2(i+float2(1,1)), u.x), u.y);
            }
            // Layered flow noise: two octaves scrolled in flow direction
            float FlowNoise(float2 uv, float t)
            {
                float2 off1 = float2(0, -t * _FlowSpeed);
                float2 off2 = float2(0.3, -t * _FlowSpeed * 1.4 + 0.7);
                float n1 = VN((uv + off1) * _FlowNoiseScale);
                float n2 = VN((uv + off2) * _FlowNoiseScale * 1.8);
                return n1 * 0.65 + n2 * 0.35;
            }

            // Cheap caustics pattern
            float Caustics(float2 worldXZ, float t)
            {
                float2 uv1 = worldXZ * _CausticsScale + t * _CausticsSpeed;
                float2 uv2 = worldXZ * _CausticsScale * 0.7 - t * _CausticsSpeed * 0.6 + 1.3;
                float  c1  = pow(VN(uv1), 2.0);
                float  c2  = pow(VN(uv2), 2.0);
                return (c1 + c2) * 0.5;
            }

            // Procedural normal from flow noise
            float3 FlowNormal(float2 uv, float t)
            {
                float eps = 0.004;
                float h0  = FlowNoise(uv, t);
                float hx  = FlowNoise(uv + float2(eps, 0), t);
                float hy  = FlowNoise(uv + float2(0, eps), t);
                float dx  = (hx - h0) / eps * _NormalScale * 0.5;
                float dy  = (hy - h0) / eps * _NormalScale * 0.5;
                return normalize(float3(-dx, 1.0, -dy));
            }

            float GetSceneDepth(float4 sp)
            {
                float2 uv = sp.xy / sp.w;
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            Varyings RiverVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 wp = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.positionWS = wp;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.uv         = input.uv;
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 RiverFrag(Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                float2 uv       = input.uv;  // u=cross, v=along
                float  t        = _WaterTime;

                // - Flow distortion: warp UV before noise lookup -------
                float  flowN    = FlowNoise(uv, t);
                float2 distortedUV = uv + (flowN - 0.5) * _FlowDistort;

                // - Procedural normal --------------------─
                float3 normalWS = FlowNormal(distortedUV, t);
                // Blend with geometry normal near edges
                float  edgeFade = smoothstep(0.0, 0.15, uv.x) * smoothstep(1.0, 0.85, uv.x);
                normalWS = normalize(lerp(input.normalWS, normalWS, edgeFade));
                float3 viewDir  = normalize(GetCameraPositionWS() - worldPos);

                // - Depth colour -----------------------
                float  sceneD   = GetSceneDepth(input.screenPos);
                float  surfD    = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
                float  depthD   = max(0.0, sceneD - surfD);
                float  depthT   = saturate(depthD / _DepthFade);
                half4  color    = lerp(_ShallowColor, _DeepColor, depthT);

                // - Flow colour variation (darker in troughs, lighter on crests) 
                float  flowV    = FlowNoise(distortedUV + float2(0, t * _FlowSpeed), t);
                color.rgb       = lerp(color.rgb * 0.8, color.rgb * 1.1,
                                       flowV * _FlowTurbulence + (1 - _FlowTurbulence) * 0.5);

                // - Fresnel -------------------------─
                float  fresnel  = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                color.rgb       = lerp(color.rgb, float3(0.85, 0.90, 0.95), fresnel * 0.3);

                // - Specular -------------------------
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 halfDir  = normalize(viewDir + lightDir);
                float  spec     = pow(saturate(dot(normalWS, halfDir)), lerp(16, 128, _Smoothness));
                color.rgb      += _MainLightColor.rgb * spec * _Smoothness * 0.5;

                // - Caustics (visible in shallow clear water) --------─
                if (_CausticsStrength > 0.01)
                {
                    float caus   = Caustics(worldPos.xz, t);
                    float causticsDepthMask = 1.0 - depthT;
                    color.rgb   += _MainLightColor.rgb * caus * _CausticsStrength * causticsDepthMask;
                }

                // - Edge foam ------------------------─
                {
                    float2 fuv  = worldPos.xz * _FoamNoiseScale * 0.08;
                    float  fn   = VN(fuv + t * _FoamSpeed) * 0.6 + VN(fuv * 1.7 - t * _FoamSpeed * 0.4) * 0.4;
                    // u-space edge mask (river banks)
                    float  edge = 1.0 - smoothstep(0.0, _FoamDistance, uv.x)
                                      * smoothstep(1.0, 1.0 - _FoamDistance, uv.x);
                    // shore depth mask
                    float  shore = 1.0 - saturate(depthD / max(_FoamDistance * 2.0, 0.01));
                    float  mask  = saturate((edge + shore * 0.5) * (fn + 0.4));
                    color.rgb    = lerp(color.rgb, _FoamColor.rgb, mask * _FoamColor.a);
                    color.a      = lerp(color.a,   1.0, mask * 0.4);
                }

                // - Ambient + fog ----------------------─
                color.rgb += unity_AmbientSky.rgb * 0.04;
                color.rgb  = MixFog(color.rgb, input.fogFactor);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _FoamColor, _ShallowColor2, _DeepColor2;
                float _DepthFade, _FlowSpeed, _FlowTurbulence, _FlowNoiseScale, _FlowDistort;
                float _Smoothness, _NormalScale, _FresnelPower;
                float _FoamDistance, _FoamSpeed, _FoamNoiseScale;
                float _CausticsStrength, _CausticsScale, _CausticsSpeed;
                float _WaterTime, _FoamDistance2, _BodyType;
            CBUFFER_END
            struct A { float4 p:POSITION; };
            struct V { float4 p:SV_POSITION; };
            V DV(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);return o;}
            half4 DF(V i):SV_Target{return 0;}
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
