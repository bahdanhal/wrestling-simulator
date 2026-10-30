// Soft contact shadows on the mat under both wrestlers. Each body is passed as up to 16 capsules
// (feet, shins, thighs, pelvis, trunk, head, arms, hands; ends at the joints). Every capsule darkens
// the mat by the share of the sky it hides from that point: a sphere's cosine-weighted solid angle,
// r^2 / d^2 times the cosine above the surface, faded out at a few radii. Drawn as a multiply over
// the lit mat on a quad that only covers the wrestlers, so it is cheap enough for phones.
Shader "Wrestling/MatContactShadow"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 0.72
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define CAPSULES_PER_BODY 16
            #define BODIES 2

            // How many radii from a capsule its shadow reaches (it has faded to nothing there).
            #define REACH 4.0

            CBUFFER_START(UnityPerMaterial)
            half _Strength;
            CBUFFER_END

            // Capsule ends: xyz end A and radius in w; xyz end B.
            float4 _ContactA[CAPSULES_PER_BODY * BODIES];
            float4 _ContactB[CAPSULES_PER_BODY * BODIES];

            // Per body: xz centre and, in w, the radius beyond which none of its capsules shades the mat.
            float4 _ContactBounds[BODIES];

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Shade the mat's surface itself, not the overlay a few millimetres above it.
                float3 p = float3(input.positionWS.x, 0.0, input.positionWS.z);
                float visibility = 1.0;

                [unroll]
                for (int body = 0; body < BODIES; body++)
                {
                    float4 bounds = _ContactBounds[body];
                    float2 fromCentre = p.xz - bounds.xz;
                    if (dot(fromCentre, fromCentre) > bounds.w * bounds.w)
                    {
                        continue;
                    }

                    [loop]
                    for (int i = 0; i < CAPSULES_PER_BODY; i++)
                    {
                        int index = (body * CAPSULES_PER_BODY) + i;
                        float4 a = _ContactA[index];
                        float radius = a.w;
                        float3 ab = _ContactB[index].xyz - a.xyz;
                        float t = saturate(dot(p - a.xyz, ab) / max(dot(ab, ab), 1e-6));
                        float3 toward = a.xyz + (ab * t) - p;
                        float d2 = max(dot(toward, toward), max(radius * radius, 1e-6));
                        float d = sqrt(d2);
                        float occlusion = (radius * radius / d2) * saturate(toward.y / d);
                        occlusion *= saturate(((REACH * radius) - d) / max(radius, 1e-4));
                        visibility *= 1.0 - occlusion;
                    }
                }

                half shade = lerp(1.0h, (half)visibility, _Strength);
                return half4(shade, shade, shade, 1.0h);
            }
            ENDHLSL
        }
    }
}
