// Wrestler body: one skinned mesh, regions chosen per pixel from smooth masks in the vertex
// colour (r singlet, g shoe, b hair, encoded as 0.5 + signed distance), and facial features drawn
// from the head-space position in UV1 (xyz normalised by height, w = head weight). Lit like URP Lit.
Shader "Wrestling/Wrestler Body"
{
    Properties
    {
        _SkinColor ("Skin", Color) = (0.86, 0.66, 0.52, 1)
        _SingletColor ("Singlet", Color) = (0.8, 0.08, 0.1, 1)
        _ShoeColor ("Shoe", Color) = (0.08, 0.08, 0.09, 1)
        _HairColor ("Hair", Color) = (0.1, 0.08, 0.06, 1)
        _SkinSmoothness ("Skin smoothness", Range(0, 1)) = 0.42
        _SingletSmoothness ("Singlet smoothness", Range(0, 1)) = 0.6
        _ShoeSmoothness ("Shoe smoothness", Range(0, 1)) = 0.3
        _HairSmoothness ("Hair smoothness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _SkinColor;
            half4 _SingletColor;
            half4 _ShoeColor;
            half4 _HairColor;
            half _SkinSmoothness;
            half _SingletSmoothness;
            half _ShoeSmoothness;
            half _HairSmoothness;
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float4 head : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 masks : TEXCOORD2;
                float4 head : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.masks = input.color;
                output.head = input.head;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // Antialiased step of a mask encoded as 0.5 + signed distance.
            half Region(half value)
            {
                half width = max(fwidth(value), 1e-4h);
                return saturate((value - 0.5h) / width + 0.5h);
            }

            // 1 inside an ellipse in the face plane, antialiased.
            half Ellipse(float2 p, float2 centre, float2 radius)
            {
                float d = length((p - centre) / radius) - 1.0;
                float width = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / width);
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half singlet = Region(input.masks.r);
                half shoe = Region(input.masks.g);
                half hair = Region(input.masks.b);

                half3 albedo = _SkinColor.rgb;
                half smoothness = _SkinSmoothness;
                albedo = lerp(albedo, _SingletColor.rgb, singlet);
                smoothness = lerp(smoothness, _SingletSmoothness, singlet);
                albedo = lerp(albedo, _ShoeColor.rgb, shoe);
                smoothness = lerp(smoothness, _ShoeSmoothness, shoe);
                albedo = lerp(albedo, _HairColor.rgb, hair);
                smoothness = lerp(smoothness, _HairSmoothness, hair);

                // Face: head-space position normalised by height; +z is the front of the face.
                float3 h = input.head.xyz;
                half face = saturate(input.head.w) * step(0.03, h.z) * (1.0h - hair);
                float2 p = float2(abs(h.x), h.y);
                half sclera = Ellipse(p, float2(0.0175, 0.006), float2(0.0062, 0.0036));
                half iris = Ellipse(p, float2(0.0172, 0.0058), float2(0.0028, 0.0028));
                half brow = Ellipse(p, float2(0.018, 0.0132), float2(0.0095, 0.0017));
                half lips = Ellipse(float2(h.x, h.y), float2(0.0, -0.028), float2(0.0115, 0.0026));
                albedo = lerp(albedo, half3(0.93h, 0.92h, 0.9h), sclera * face);
                albedo = lerp(albedo, half3(0.12h, 0.08h, 0.06h), iris * face);
                albedo = lerp(albedo, _HairColor.rgb * 0.8h, brow * face);
                albedo = lerp(albedo, albedo * half3(0.78h, 0.58h, 0.55h), lips * face);
                smoothness = lerp(smoothness, 0.8h, (sclera + iris) * face);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.specular = half3(0, 0, 0);
                surface.metallic = 0;
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 colour = UniversalFragmentPBR(inputData, surface);
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVertex(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
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

            float4 DepthVertex(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
