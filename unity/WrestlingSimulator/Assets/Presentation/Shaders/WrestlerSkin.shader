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
        _SingletSmoothness ("Singlet smoothness", Range(0, 1)) = 0.26
        _HairColor ("Hair colour", Color) = (0.12, 0.09, 0.07, 1)
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
            half4 _HairColor;
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
                float3 bodyPos = input.body.xyz;
                half singlet = _SingletOn * SingletMask(bodyPos, input.regions);
                half trim = _SingletOn * SingletTrimMask(bodyPos, input.regions);
                half2 sidePanel = SingletSidePanel(bodyPos, input.regions);

                half3 skin = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _SkinTint.rgb;

                // Shade the cranial scalp under the hair cap with the wrestler's hair root colour so
                // head blend shapes and semi-transparent hair strands never flash bright bare skin.
                float hairlineY = 0.910 + 0.058 * smoothstep(-0.025, 0.035, bodyPos.z) + 0.010 * smoothstep(0.030, 0.046, abs(bodyPos.x));
                half scalp = smoothstep(0.0, 0.012, bodyPos.y - hairlineY) * smoothstep(0.88, 0.91, bodyPos.y);
                half3 scalpRoot = _HairColor.rgb * lerp(0.55h, 0.95h, saturate(Luminance(skin) * 1.8h));
                skin = lerp(skin, scalpRoot, scalp * 0.88h);

                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normalTS = normalize(lerp(normalTS, half3(0, 0, 1), singlet * 0.94h));
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS)));

                // Subtle anatomical muscle relief and ambient crease shading along the torso, deltoids, and thighs.
                float3 geomNormalWS = normalize(input.normalWS);
                half muscleRelief = saturate(dot(normalWS, float3(0, 1, 0)) * 0.5h + 0.5h) - saturate(dot(geomNormalWS, float3(0, 1, 0)) * 0.5h + 0.5h);
                half centerlineGroove = exp(-pow(abs(bodyPos.x) / 0.018, 2.0)) * saturate(input.regions.x) * smoothstep(0.55, 0.80, bodyPos.y);
                half muscleTone = clamp(1.0h + muscleRelief * 0.35h - centerlineGroove * 0.07h, 0.88h, 1.12h);
                skin *= lerp(muscleTone, 1.0h, scalp);

                // Opaque compression lycra: pure singlet colour shaded by 3D body contour, darker ergonomic side panel,
                // and crisp white/accent border piping (never bleeding bare-skin pigment spots through the fabric).
                half fabricContour = clamp(1.0h - centerlineGroove * 0.04h, 0.94h, 1.03h);
                half3 baseFabric = _SingletColor.rgb * fabricContour;
                half3 panelFabric = baseFabric * 0.78h;
                half3 trimColour = lerp(_SingletColor.rgb, half3(0.95h, 0.96h, 0.98h), 0.72h);
                half3 fabric = lerp(baseFabric, panelFabric, sidePanel.x * (1.0h - trim));
                fabric = lerp(fabric, trimColour, saturate(sidePanel.y * 0.65h + trim * 0.85h));
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
                half athleticSheen = smoothstep(0.45, 0.88, bodyPos.y) * (1.0h - scalp) * 0.06h;
                half skinSmoothness = lerp(_SkinSmoothness + athleticSheen, 0.22h, scalp);
                surface.smoothness = lerp(skinSmoothness, _SingletSmoothness, singlet);
                surface.normalTS = normalTS;
                surface.occlusion = lerp(1.0h, 0.92h, centerlineGroove);
                surface.alpha = 1;
                half4 colour = UniversalFragmentPBR(inputData, surface);

                // Subsurface approximation: wrapped diffuse beyond the terminator, tinted warm, plus subtle grazing rim.
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half ndotl = dot(normalWS, mainLight.direction);
                half wrapped = saturate((ndotl + _ScatterWrap) / (1.0h + _ScatterWrap)) - saturate(ndotl);
                half shadow = lerp(1.0h, mainLight.shadowAttenuation, 0.5h);
                colour.rgb += albedo * _ScatterColor.rgb * wrapped * _ScatterStrength * (1.0h - singlet) * (1.0h - scalp * 0.8h) * mainLight.color * shadow;
                half fresnel = pow(1.0h - saturate(dot(normalWS, inputData.viewDirectionWS)), 4.0h) * saturate(normalWS.y * 0.5h + 0.5h);
                colour.rgb += mainLight.color * fresnel * lerp(0.055h, 0.04h, singlet) * (1.0h - scalp) * shadow;

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
