// Water_Lake.shader
// URP inland water shader.
// No forced waves — gentle procedural ripple only.
// Can be placed at any Y (above or below sea level).
// Depth-based colour, soft reflective sheen, optional shoreline edge.
// No foam by default (toggleable for shallow water near rocks etc).

Shader "InfinityProject/Water/Lake"
{
    Properties
    {
        // - Colour ------------------------------
        [HDR] _ShallowColor  ("Shallow Color",  Color) = (0.20, 0.58, 0.55, 0.75)
        [HDR] _DeepColor     ("Deep Color",     Color) = (0.04, 0.18, 0.28, 0.92)
        _DepthFade           ("Depth Fade",     Float) = 3.0

        // - Ripple (tiny procedural waves, no geometry displacement) -----
        _RippleAmplitude  ("Ripple Amplitude", Float)       = 0.04
        _RippleSpeed      ("Ripple Speed",     Float)       = 0.30
        _RippleScale      ("Ripple Scale",     Float)       = 4.0

        // - Reflection ----------------------------
        _Smoothness  ("Smoothness",  Range(0,1)) = 0.88
        _Metallic    ("Metallic",    Range(0,1)) = 0.0
        _FresnelPower("Fresnel Power",   Float)  = 5.0
        _ReflectColor("Reflect Tint",   Color)   = (0.80, 0.88, 0.94, 1.0)

        // - Optional shoreline foam ---------------------─
        _FoamEnabled    ("Foam Enabled",  Float) = 0.0
        _FoamColor      ("Foam Color",   Color)  = (0.92, 0.95, 0.98, 0.85)
        _FoamDistance   ("Foam Dist",    Float)  = 0.6
        _FoamSpeed      ("Foam Speed",   Float)  = 0.3
        _FoamNoiseScale ("Foam Scale",   Float)  = 6.0

        // - Runtime (set by WaterBody.cs) ------------------─
        _WaterTime   ("_WaterTime (runtime)", Float) = 0.0
        _WaveAmplitude("WaveAmplitude (legacy)", Float) = 0.0
        _WaveFrequency("WaveFrequency (legacy)", Float) = 0.8
        _FoamDistance2("FoamDistance (legacy)", Float) = 0.6
        _FoamSpeed2   ("FoamSpeed (legacy)",    Float) = 0.3
        _BodyType     ("BodyType (runtime)",    Float) = 1.0
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
            Cull Back

            HLSLPROGRAM
            #pragma vertex   LakeVert
            #pragma fragment LakeFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _ReflectColor;
                half4 _FoamColor;
                float _DepthFade, _RippleAmplitude, _RippleSpeed, _RippleScale;
                float _Smoothness, _Metallic, _FresnelPower;
                float _FoamEnabled, _FoamDistance, _FoamSpeed, _FoamNoiseScale;
                float _WaterTime;
                float _WaveAmplitude, _WaveFrequency, _FoamDistance2, _FoamSpeed2, _BodyType;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            // - Noise helpers ------------------------─
            float Hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i+float2(1,0)), u.x),
                            lerp(Hash(i+float2(0,1)), Hash(i+float2(1,1)), u.x), u.y);
            }
            float FBM2(float2 p)
            {
                return VNoise(p) * 0.6 + VNoise(p * 2.1 + 1.7) * 0.4;
            }

            // Procedural normal from ripple noise (no texture required)
            float3 RippleNormal(float2 worldXZ, float t)
            {
                float2 uv1 = worldXZ * _RippleScale + float2(t * _RippleSpeed, t * _RippleSpeed * 0.7);
                float2 uv2 = worldXZ * _RippleScale * 1.4 - float2(t * _RippleSpeed * 0.5, t * _RippleSpeed * 1.1);
                float h0   = VNoise(uv1);
                float hx   = VNoise(uv1 + float2(0.01, 0));
                float hz   = VNoise(uv1 + float2(0, 0.01));
                float h0b  = VNoise(uv2);
                float hxb  = VNoise(uv2 + float2(0.01, 0));
                float hzb  = VNoise(uv2 + float2(0, 0.01));
                float dx   = ((hx - h0) + (hxb - h0b)) * 0.5 * _RippleAmplitude * 80.0;
                float dz   = ((hz - h0) + (hzb - h0b)) * 0.5 * _RippleAmplitude * 80.0;
                return normalize(float3(-dx, 1.0, -dz));
            }

            float GetSceneDepth(float4 sp)
            {
                float2 uv = sp.xy / sp.w;
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            Varyings LakeVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                // Lake surface: no vertex displacement (calm water)
                // Only a tiny Y offset from ripple for visual believability
                float3 wp  = TransformObjectToWorld(input.positionOS.xyz);
                float  t   = _WaterTime;
                float2 ruv = wp.xz * _RippleScale + t * _RippleSpeed;
                float  rDisp = (VNoise(ruv) - 0.5) * _RippleAmplitude;
                wp.y += rDisp;
                o.positionCS = TransformWorldToHClip(wp);
                o.positionWS = wp;
                o.uv         = input.uv;
                o.screenPos  = ComputeScreenPos(o.positionCS);
                o.fogFactor  = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 LakeFrag(Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                float  t        = _WaterTime;

                // - Procedural normal --------------------─
                float3 normalWS = RippleNormal(worldPos.xz, t);
                float3 viewDir  = normalize(GetCameraPositionWS() - worldPos);

                // - Depth colour -----------------------
                float sceneDepth = GetSceneDepth(input.screenPos);
                float surfDepth  = LinearEyeDepth(input.positionCS.z / input.positionCS.w, _ZBufferParams);
                float depthDiff  = max(0.0, sceneDepth - surfDepth);
                float depthT     = saturate(depthDiff / _DepthFade);
                half4 color      = lerp(_ShallowColor, _DeepColor, depthT);

                // - Fresnel reflection tint -----------------─
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _FresnelPower);
                color.rgb = lerp(color.rgb, _ReflectColor.rgb, fresnel * 0.45);

                // - Specular -------------------------
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 halfDir  = normalize(viewDir + lightDir);
                float  spec     = pow(saturate(dot(normalWS, halfDir)), lerp(16, 256, _Smoothness));
                color.rgb      += _MainLightColor.rgb * spec * _Smoothness * 0.6;

                // - Optional foam ----------------------─
                if (_FoamEnabled > 0.5)
                {
                    float2 fuv    = worldPos.xz * _FoamNoiseScale * 0.1;
                    float  fn     = FBM2(fuv + t * _FoamSpeed);
                    float  shore  = 1.0 - saturate(depthDiff / _FoamDistance);
                    float  mask   = saturate(shore * 1.4) * saturate(fn + shore - 0.35);
                    color.rgb     = lerp(color.rgb, _FoamColor.rgb, mask * _FoamColor.a);
                    color.a       = lerp(color.a, 1.0, mask * 0.5);
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
            Cull Back
            HLSLPROGRAM
            #pragma vertex DV
            #pragma fragment DF
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _ReflectColor, _FoamColor;
                float _DepthFade, _RippleAmplitude, _RippleSpeed, _RippleScale;
                float _Smoothness, _Metallic, _FresnelPower;
                float _FoamEnabled, _FoamDistance, _FoamSpeed, _FoamNoiseScale;
                float _WaterTime, _WaveAmplitude, _WaveFrequency, _FoamDistance2, _FoamSpeed2, _BodyType;
            CBUFFER_END
            struct A { float4 p : POSITION; };
            struct V { float4 p : SV_POSITION; };
            V DV(A i) { V o; o.p = TransformObjectToHClip(i.p.xyz); return o; }
            half4 DF(V i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
