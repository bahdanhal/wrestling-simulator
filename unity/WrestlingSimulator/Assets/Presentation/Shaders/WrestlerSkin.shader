// Wrestler skin and singlet in one pass, cheap enough for phones.
// Skin: MakeHuman albedo and normal maps; curvature-aware pre-integrated subsurface scattering
// (per-channel wrapped diffuse that widens with surface curvature, softer normals and shadow
// edges for red, light through thin parts) and a dual-lobe specular over pore micro-relief that
// appears in close-ups (fading out where it would alias, and where the skin's own normal map
// already has fine detail). Exertion builds a sweat film, first on the brow, neck, deltoids and
// forearms, with droplet micro-highlights.
// Singlet: compression fabric with anisotropic sheen along the knit, tension folds where the hips
// and shoulders bend, a raised rolled binding with coverstitching, and damp patches as it soaks.
// Per-vertex data from the importer (CharacterImport):
//   UV2 = bind position / height (xyz; +x left, +z front), sweat affinity (w)
//   UV3 = torso share, arm share, signed seam distance in metres (+ on the singlet), curvature 1/m
Shader "Wrestling/Wrestler Skin"
{
    Properties
    {
        _BaseMap ("Skin albedo", 2D) = "white" {}
        _BumpMap ("Skin normal", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0, 2)) = 1
        _SkinTint ("Skin tint", Color) = (1, 1, 1, 1)
        _SkinSmoothness ("Skin smoothness", Range(0, 1)) = 0.38
        _PoreStrength ("Pore micro-relief", Range(0, 2)) = 1
        _ScatterColor ("Subsurface colour", Color) = (0.85, 0.28, 0.18, 1)
        _ScatterStrength ("Subsurface strength", Range(0, 1)) = 0.6
        _CurvatureScale ("Curvature to scatter (m)", Range(0, 0.1)) = 0.03
        _SingletColor ("Singlet", Color) = (0.8, 0.08, 0.1, 1)
        _SingletSmoothness ("Singlet smoothness", Range(0, 1)) = 0.26
        _FoldDepth ("Singlet fold depth (m)", Range(0, 0.006)) = 0.0018
        _HairColor ("Hair colour", Color) = (0.12, 0.09, 0.07, 1)
        _Exertion ("Exertion", Range(0, 1)) = 0
        _FoldDrive ("Fold drive (hip L, hip R, shoulder L, shoulder R)", Vector) = (0, 0, 0, 0)
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
            half _PoreStrength;
            half4 _ScatterColor;
            half _ScatterStrength;
            float _CurvatureScale;
            half4 _SingletColor;
            half _SingletSmoothness;
            float _FoldDepth;
            half4 _HairColor;
            half _Exertion;
            float4 _FoldDrive;
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
            #include "WrestlerSkinLighting.hlsl"

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
                float4 surface : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float4 body : TEXCOORD4;
                float4 surface : TEXCOORD6;
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
                // Curvature in world units: the character is scaled uniformly to the wrestler's height.
                float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                output.surface = float4(input.surface.xyz, input.surface.w / max(scale, 1e-4));
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 bodyPos = input.body.xyz;
                half sweatAffinity = input.body.w;
                half torsoShare = input.surface.x;
                half armShare = input.surface.y;
                float seam = input.surface.z;
                float curvature = input.surface.w;

                half singlet = _SingletOn * SingletCoverage(seam);
                half trim = _SingletOn * SingletTrim(seam);
                half stitches = trim * SingletStitches(seam);
                half sidePanel = SingletSidePanel(bodyPos, armShare);

                half3 skin = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _SkinTint.rgb;

                // Shade the cranial scalp under the hair cap with the wrestler's hair root colour so
                // head blend shapes and semi-transparent hair strands never flash bright bare skin.
                float hairlineY = 0.910 + 0.058 * smoothstep(-0.025, 0.035, bodyPos.z) + 0.010 * smoothstep(0.030, 0.046, abs(bodyPos.x));
                half scalp = smoothstep(0.0, 0.012, bodyPos.y - hairlineY) * smoothstep(0.88, 0.91, bodyPos.y);
                half3 scalpRoot = _HairColor.rgb * lerp(0.55h, 0.95h, saturate(Luminance(skin) * 1.8h));
                skin = lerp(skin, scalpRoot, scalp * 0.88h);

                float3 geometricNormal = normalize(input.normalWS);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                // How much fine detail the skin's own normal map has here (against a blurred read of
                // it): the procedural pores make way for real ones.
                half3 coarseTS = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_BumpMap, sampler_BumpMap, input.uv, 3.0), _BumpScale);
                half poreRoom = 1.0h - smoothstep(0.03h, 0.15h, length(normalTS.xy - coarseTS.xy));
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), singlet * 0.94h));
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                // Muscle relief and the centreline groove of the torso.
                half muscleRelief = saturate(dot(normalWS, float3(0, 1, 0)) * 0.5h + 0.5h) - saturate(dot(geometricNormal, float3(0, 1, 0)) * 0.5h + 0.5h);
                half centerlineGroove = exp(-pow(abs(bodyPos.x) / 0.018, 2.0)) * saturate(torsoShare) * smoothstep(0.55, 0.80, bodyPos.y);
                half muscleTone = clamp(1.0h + muscleRelief * 0.35h - centerlineGroove * 0.07h, 0.88h, 1.12h);
                skin *= lerp(muscleTone, 1.0h, scalp);

                float3 dpdx = ddx(input.positionWS);
                float3 dpdy = ddy(input.positionWS);

                // Sweat: zones with the highest affinity wet first; the film darkens skin slightly.
                half sweat = saturate((_Exertion - (1.0h - sweatAffinity) * 0.8h) / 0.35h) * (1.0h - singlet) * (1.0h - scalp);
                float droplets = SweatDroplets(bodyPos * 1.75, dpdx, dpdy);
                skin *= 1.0h - 0.08h * sweat;

                // Singlet: base colour, darker side panel, light binding with recessed stitching,
                // and damp patches where sweat soaks through.
                half damp = saturate((_Exertion - (1.0h - sweatAffinity) * 0.9h) / 0.4h) * singlet;
                half fabricContour = clamp(1.0h - centerlineGroove * 0.04h, 0.94h, 1.03h);
                half3 fabric = _SingletColor.rgb * fabricContour;
                fabric = lerp(fabric, fabric * 0.78h, sidePanel * (1.0h - trim));
                half3 trimColour = lerp(_SingletColor.rgb, half3(0.95h, 0.96h, 0.98h), 0.72h);
                fabric = lerp(fabric, trimColour, trim * 0.85h);
                fabric *= 1.0h - 0.32h * stitches;
                fabric *= 1.0h - 0.22h * damp;

                // Tension folds bend the fabric's normal and turn its sheen along the folds.
                float folds = SingletFolds(bodyPos, _FoldDrive, _FoldDepth * (0.85 + 0.3 * _Exertion)) * singlet * (1.0h - trim);
                float3 foldGradient = SurfaceGradient(geometricNormal, dpdx, dpdy, ddx(folds), ddy(folds));
                float3 fabricNormal = normalize(lerp(geometricNormal, normalWS, 0.25) - foldGradient);
                float3 knit = SurfaceGradient(fabricNormal, dpdx, dpdy, ddx(bodyPos.y), ddy(bodyPos.y));
                float3 ridge = cross(fabricNormal, foldGradient);
                float foldStrength = saturate(length(foldGradient) * 6.0);
                ridge *= dot(ridge, knit) < 0.0 ? -1.0 : 1.0;
                float3 fabricTangent = SafeNormalize(lerp(SafeNormalize(knit), SafeNormalize(ridge), foldStrength));

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalize(lerp(normalWS, fabricNormal, singlet));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                WrestlerSurface surface;
                surface.skinAlbedo = skin;
                surface.fabricAlbedo = fabric;
                surface.singlet = singlet;
                // Pores bump only the detail normal (the specular lobes); scattered light sees the
                // blurred one. Cavities are rougher than the oily ridges between them.
                float3 pores = SkinMicroPores(bodyPos * 1.75, dpdx, dpdy);
                half poreAmount = _PoreStrength * poreRoom * (1.0h - singlet) * (1.0h - scalp);
                float poreHeight = pores.x * poreAmount;
                surface.skinNormal = normalize(normalWS - SurfaceGradient(normalWS, dpdx, dpdy, ddx(poreHeight), ddy(poreHeight)));
                surface.blurredNormal = normalize(lerp(normalWS, geometricNormal, 0.65));
                surface.fabricNormal = fabricNormal;
                surface.fabricTangent = fabricTangent;
                surface.skinRoughness = saturate(PerceptualSmoothnessToPerceptualRoughness(lerp(_SkinSmoothness, 0.22h, scalp)) + (0.06h * ((2.0h * pores.y) - pores.z) * saturate(poreAmount)));
                surface.fabricRoughness = PerceptualSmoothnessToPerceptualRoughness(saturate(_SingletSmoothness + 0.12h * damp + 0.06h * trim));
                surface.scatter = saturate(max(curvature * _CurvatureScale, 0.12) * _ScatterStrength * 1.6) * (1.0h - scalp * 0.8h);
                surface.thin = saturate((curvature * _CurvatureScale - 0.9) * 2.0) * (1.0h - scalp);
                surface.scatterColor = _ScatterColor.rgb;
                surface.sweat = sweat;
                surface.droplets = droplets * sweat;
                surface.dropletNormal = normalize(normalWS - SurfaceGradient(normalWS, dpdx, dpdy, ddx(droplets), ddy(droplets)) * 0.0006 * sweat);
                surface.occlusion = lerp(1.0h, 0.92h, centerlineGroove) * (1.0h - 0.35h * stitches);

                half3 colour = ShadeWrestler(inputData, surface);
                colour = MixFog(colour, inputData.fogCoord);
                return half4(colour, 1);
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
