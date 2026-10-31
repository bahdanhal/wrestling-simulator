#ifndef WRESTLER_SKIN_LIGHTING_INCLUDED
#define WRESTLER_SKIN_LIGHTING_INCLUDED

// Lighting for the wrestler's skin and singlet (WrestlerSkin.shader): URP main and additional
// lights (clustered or per-object), shadows, SSAO, spherical-harmonics ambient and reflection
// probes, with a skin model and a fabric model blended by the singlet's coverage.

struct WrestlerSurface
{
    half3 skinAlbedo;
    half3 fabricAlbedo;
    half singlet;           // 1 on the fabric
    float3 skinNormal;      // detail (normal-mapped) skin normal
    float3 blurredNormal;   // skin normal with the detail mostly removed, for scattered light
    float3 fabricNormal;
    float3 fabricTangent;   // direction of the fabric's sheen (knit or fold ridges)
    half skinRoughness;     // perceptual
    half fabricRoughness;   // perceptual
    half scatter;           // 0..1, how far light bleeds past the terminator (from curvature)
    half thin;              // 0..1, thin parts light shines through (ears, nose, fingers)
    half3 scatterColor;
    half sweat;             // 0..1 sweat film
    half droplets;          // 0..1 droplet coverage (already scaled by sweat)
    float3 dropletNormal;
    half occlusion;
};

#define SKIN_F0 0.028
#define FABRIC_F0 0.04

// Gradient of a height field on the surface from its screen-space derivatives (Mikkelsen's
// surface gradient); subtracting it from the normal bumps the surface by that height.
float3 SurfaceGradient(float3 normal, float3 dpdx, float3 dpdy, float dhdx, float dhdy)
{
    float3 r1 = cross(dpdy, normal);
    float3 r2 = cross(normal, dpdx);
    float det = dot(dpdx, r1);
    float safeDet = abs(det) > 1e-14 ? det : (det >= 0.0 ? 1e-14 : -1e-14);
    return (r1 * dhdx + r2 * dhdy) / safeDet;
}

float WrestlerHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float WrestlerValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(
        lerp(lerp(WrestlerHash(i), WrestlerHash(i + float3(1, 0, 0)), f.x), lerp(WrestlerHash(i + float3(0, 1, 0)), WrestlerHash(i + float3(1, 1, 0)), f.x), f.y),
        lerp(lerp(WrestlerHash(i + float3(0, 0, 1)), WrestlerHash(i + float3(1, 0, 1)), f.x), lerp(WrestlerHash(i + float3(0, 1, 1)), WrestlerHash(i + float3(1, 1, 1)), f.x), f.y),
        f.z);
}

// Sweat droplets about 3 mm across on the skin (bind position in metres), as a 0..1 height;
// faded out where they would be smaller than a pixel, so they never shimmer at a distance.
float SweatDroplets(float3 positionMetres, float3 dpdx, float3 dpdy)
{
    float3 p = positionMetres / 0.0032;
    float footprint = max(length(dpdx), length(dpdy)) / 0.0032;
    float visible = 1.0 - smoothstep(0.35, 0.9, footprint);
    float n = WrestlerValueNoise(p) * 0.65 + WrestlerValueNoise(p * 2.13 + 7.1) * 0.35;
    return smoothstep(0.56, 0.74, n) * visible;
}

// Distance to the nearest of one jittered point per unit cell (cellular / Worley F1).
float WrestlerCellular(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);
    float nearest = 8.0;
    [unroll] for (int z = -1; z <= 1; z++)
    {
        [unroll] for (int y = -1; y <= 1; y++)
        {
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float3 o = float3(x, y, z);
                float3 c = cell + o;
                float3 jitter = float3(WrestlerHash(c), WrestlerHash(c + 17.31), WrestlerHash(c + 41.73));
                float3 d = o + jitter - f;
                nearest = min(nearest, dot(d, d));
            }
        }
    }

    return sqrt(nearest);
}

// Skin micro-relief: pores as small cavities on two scales (about 1.3 mm and 0.6 mm apart), on the
// bind-space position in metres, so they are the same size everywhere and never break at UV
// seams. Returns the height (m, negative in a pore), the pore share (0 on the ridges between them,
// 1 in a pore) and how visible the relief is: each scale fades out where its pores would be
// smaller than a pixel, so it never aliases at broadcast distance (and costs nothing there).
float3 SkinMicroPores(float3 positionMetres, float3 dpdx, float3 dpdy)
{
    float footprint = max(length(dpdx), length(dpdy));
    float coarse = 1.0 - smoothstep(0.25, 0.7, footprint / 0.0013);
    float3 result = 0;
    UNITY_BRANCH
    if (coarse > 0.0)
    {
        float pore = (1.0 - smoothstep(0.08, 0.32, WrestlerCellular(positionMetres / 0.0013))) * coarse * 0.65;
        float fine = 1.0 - smoothstep(0.25, 0.7, footprint / 0.0006);
        UNITY_BRANCH
        if (fine > 0.0)
        {
            pore += (1.0 - smoothstep(0.08, 0.3, WrestlerCellular((positionMetres / 0.0006) + 13.7))) * fine * 0.45;
        }

        pore = saturate(pore);
        result = float3(-pore * 0.00004, pore, coarse);
    }

    return result;
}

float WrestlerGGX(float nh, float alpha)
{
    float a2 = alpha * alpha;
    float d = nh * nh * (a2 - 1.0) + 1.0;
    return a2 / (PI * d * d + 1e-7);
}

// Height-correlated Smith visibility, approximated (Hammon).
float WrestlerVisibility(float nl, float nv, float alpha)
{
    return 0.5 / (lerp(2.0 * nl * nv, nl + nv, alpha) + 1e-5);
}

float WrestlerFresnel(float f0, float lh)
{
    float x = 1.0 - lh;
    float x2 = x * x;
    return f0 + (1.0 - f0) * x2 * x2 * x;
}

// Anisotropic GGX (Burley) with roughness along the tangent and across it.
float WrestlerGGXAniso(float th, float bh, float nh, float alphaT, float alphaB)
{
    float d = (th * th) / (alphaT * alphaT) + (bh * bh) / (alphaB * alphaB) + nh * nh;
    return 1.0 / (PI * alphaT * alphaB * d * d + 1e-7);
}

// Pre-integrated diffuse for one channel: wrapped further past the terminator the more the light
// scatters, and normalised so a light straight on is unchanged.
float3 ScatteredDiffuse(float3 nl, float3 wrap)
{
    float3 w = (nl + wrap) / (1.0 + wrap);
    return pow(saturate(w), 1.0 + wrap);
}

half3 ShadeSkin(Light light, InputData inputData, WrestlerSurface s, half shadow)
{
    float3 l = light.direction;
    float3 v = inputData.viewDirectionWS;
    half3 radiance = light.color * light.distanceAttenuation;

    // Red scatters furthest: it sees the blurred normal and a wider wrap; blue the detail normal.
    float nlDetail = dot(s.skinNormal, l);
    float nlBlurred = dot(s.blurredNormal, l);
    float3 nl = float3(lerp(nlDetail, nlBlurred, 0.85), lerp(nlDetail, nlBlurred, 0.45), nlDetail);
    float3 wrap = s.scatter * float3(1.0, 0.42, 0.26);
    float3 diffuse = ScatteredDiffuse(nl, wrap);

    // Shadow edges soften and redden, as light leaks through the skin under the penumbra.
    float3 shadowed = lerp(shadow.xxx, pow(max(shadow, 1e-4).xxx, float3(0.45, 0.8, 1.0)), s.scatter);
    half3 colour = s.skinAlbedo * diffuse * shadowed;

    // Light through thin parts, seen from the far side.
    float3 through = normalize(l + s.blurredNormal * 0.3);
    float back = pow(saturate(dot(v, -through)), 6.0) * s.thin;
    colour += s.skinAlbedo * s.scatterColor * back * lerp(1.0, shadow, 0.6);

    // Dual-lobe specular: a broad lobe and a tight one; sweat sharpens and strengthens the tight one.
    float3 n = s.skinNormal;
    float3 h = SafeNormalize(l + v);
    float nv = saturate(dot(n, v)) + 1e-4;
    float nls = saturate(dot(n, l));
    float lh = saturate(dot(l, h));
    float fresnel = WrestlerFresnel(SKIN_F0, lh);
    float roughness = max(s.skinRoughness, 0.08);
    float alpha1 = roughness * roughness;
    float alpha2 = max(alpha1 * 0.3, 0.012);
    float broad = WrestlerGGX(saturate(dot(n, h)), alpha1) * WrestlerVisibility(nls, nv, alpha1);
    float tight = WrestlerGGX(saturate(dot(n, h)), alpha2) * WrestlerVisibility(nls, nv, alpha2);
    float tightShare = lerp(0.18, 0.45, s.sweat);
    float specular = lerp(broad, tight, tightShare) * fresnel * nls;

    // The sweat film and its droplets: a near-mirror lobe on the droplets' own normals.
    float3 nd = s.dropletNormal;
    float filmAlpha = lerp(0.05, 0.02, s.droplets);
    float ndl = saturate(dot(nd, l));
    float film = WrestlerGGX(saturate(dot(nd, h)), filmAlpha) * WrestlerVisibility(ndl, saturate(dot(nd, v)) + 1e-4, filmAlpha);
    specular += film * WrestlerFresnel(0.02, lh) * ndl * (s.sweat * 0.35 + s.droplets * 0.9);

    return (colour + specular * shadow) * radiance;
}

half3 ShadeFabric(Light light, InputData inputData, WrestlerSurface s, half shadow)
{
    float3 l = light.direction;
    float3 v = inputData.viewDirectionWS;
    float3 n = s.fabricNormal;
    half3 radiance = light.color * light.distanceAttenuation * shadow;

    // Compression fabric: a slightly wrapped diffuse (fibres scatter a little).
    float nl = dot(n, l);
    half3 colour = s.fabricAlbedo * saturate((nl + 0.12) / 1.12);

    // Anisotropic sheen, stretched along the knit or the fold ridges.
    float3 t = SafeNormalize(s.fabricTangent - n * dot(s.fabricTangent, n));
    float3 b = cross(n, t);
    float3 h = SafeNormalize(l + v);
    float nls = saturate(nl);
    float nv = saturate(dot(n, v)) + 1e-4;
    float alpha = max(s.fabricRoughness * s.fabricRoughness, 0.02);
    float alphaT = alpha * 0.55;
    float alphaB = min(alpha * 1.6, 1.0);
    float d = WrestlerGGXAniso(dot(t, h), dot(b, h), saturate(dot(n, h)), alphaT, alphaB);
    float specular = d * WrestlerVisibility(nls, nv, alpha) * WrestlerFresnel(FABRIC_F0, saturate(dot(l, h))) * nls;
    return (colour + specular) * radiance;
}

half3 ShadeLight(Light light, InputData inputData, WrestlerSurface s)
{
    half shadow = light.shadowAttenuation;
    half3 result = 0;
    UNITY_BRANCH
    if (s.singlet < 0.999h)
    {
        result += ShadeSkin(light, inputData, s, shadow) * (1.0h - s.singlet);
    }

    UNITY_BRANCH
    if (s.singlet > 0.001h)
    {
        result += ShadeFabric(light, inputData, s, shadow) * s.singlet;
    }

    return result;
}

half3 ShadeWrestler(InputData inputData, WrestlerSurface s)
{
    half4 shadowMask = CalculateShadowMask(inputData);
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.occlusion = s.occlusion;
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint meshRenderingLayers = GetMeshRenderingLayer();

    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    half3 bakedGI = inputData.bakedGI;
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, bakedGI);

    // Ambient and reflections, through URP's environment BRDF.
    half3 albedo = lerp(s.skinAlbedo, s.fabricAlbedo, s.singlet);
    half roughness = lerp(lerp(s.skinRoughness, s.skinRoughness * 0.6h, s.sweat), s.fabricRoughness, s.singlet);
    half alpha = 1;
    BRDFData brdf;
    InitializeBRDFData(albedo, 0, 0, 1.0h - roughness, alpha, brdf);
    BRDFData coat = (BRDFData)0;
    half3 colour = GlobalIllumination(brdf, coat, 0, bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
        inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        colour += ShadeLight(mainLight, inputData, s);
    }

#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            colour += ShadeLight(light, inputData, s);
        }
    }
#endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            colour += ShadeLight(light, inputData, s);
        }
    LIGHT_LOOP_END
#endif

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    colour += inputData.vertexLighting * albedo;
#endif

    return colour;
}

#endif
