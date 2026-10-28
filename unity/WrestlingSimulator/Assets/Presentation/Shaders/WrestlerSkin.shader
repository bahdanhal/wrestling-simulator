// Wrestler skin: MakeHuman skin textures (albedo, normal) lit like URP Lit, plus a wrap-lighting
// subsurface approximation (light bleeding warm into the shadow side, as skin does) that is cheap
// enough for phones. The skin-tight singlet is drawn on the body from UV2, which the importer
// and UV3 the importer fills with the bind-pose position and body-region weights.
Shader "Wrestling/Wrestler Skin"
{
    Properties
    {
        _BaseMap ("Skin albedo", 2D) = "white" {}
        _BumpMap ("Skin normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _SkinTint ("Skin tint", Color) = (1, 1, 1, 1)
        _SkinSmoothness ("Skin smoothness", Range(0, 1)) = 0.38
        _ScatterColor ("Subsurface colour", Color) = (0.85, 0.28, 0.18, 1)
        _ScatterWrap ("Subsurface wrap", Range(0, 1)) = 0.45
        _ScatterStrength ("Subsurface strength", Range(0, 1)) = 0.5
        _SingletColor ("Singlet", Color) = (0.8, 0.08, 0.1, 1)
        _SingletSmoothness ("Singlet smoothness", Range(0, 1)) = 0.55
        [Toggle] _SingletOn ("Singlet", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _SkinTint;
            half _BumpScale;
            half _SkinSmoothness;
            half4 _ScatterColor;
            half _ScatterWrap;
            half _ScatterStrength;
            half4 _SingletColor;
            half _SingletSmoothness;
            half _SingletOn;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WrestlerSinglet.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 body : TEXCOORD2;
                float4 regions : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float4 body : TEXCOORD4;
                float4 regions : TEXCOORD6;
                half fogFactor : TEXCOORD5;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = normal.normalWS;
                output.tangentWS = float4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.body = input.body;
                output.regions = input.regions;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half singlet = _SingletOn * SingletMask(input.body.xyz, input.regions);
                half3 skin = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _SkinTint.rgb;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), singlet * 0.85h));
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                // Lycra: fabric colour with a little of the skin's shading so muscle shapes still read.
                half3 fabric = _SingletColor.rgb * lerp(1.0h, saturate(Luminance(skin) * 2.2h), 0.25h);
                half3 albedo = lerp(skin, fabric, singlet);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0;
                surface.smoothness = lerp(_SkinSmoothness, _SingletSmoothness, singlet);
                surface.normalTS = normalTS;
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 colour = UniversalFragmentPBR(inputData, surface);

                // Subsurface approximation: wrapped diffuse beyond the terminator, tinted warm.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half ndotl = dot(normalWS, mainLight.direction);
                half wrapped = saturate((ndotl + _ScatterWrap) / (1.0h + _ScatterWrap)) - saturate(ndotl);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, 0.5h);
                colour.rgb += albedo * _ScatterColor.rgb * wrapped * _ScatterStrength * (1.0h - singlet) * mainLight.color * shadow;

                colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
                return colour;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            float4 ShadowVertex(float4 positionOS : POSITION, float3 normalOS : NORMAL) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 ShadowFragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            float4 DepthVertex(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half DepthFragment() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target { return half4(NormalizeNormalPerPixel(input.normalWS), 0.0); }
            ENDHLSL
        }
    }
}
