#ifndef WRESTLER_SINGLET_INCLUDED
#define WRESTLER_SINGLET_INCLUDED

// Wrestling singlet, evaluated per pixel from the importer's data (CharacterImport):
// position = bind-pose position normalised by height (feet at y = 0, +z the front);
// regions  = skin-weight share of (Torso, ShoulderGirdle, Thigh, Arm).
// The cut uses a single C1-continuous arch curve from the chest/back scoop up over the
// shoulder straps and down into the underarm crease (finishing before the hanging upper arm),
// so the straps are unbroken over the shoulders and never leave islands on the deltoid or armpit.

#define SINGLET_OUTSIDE 0.06

float SingletCut(float3 p, float4 regions)
{
    float x = abs(p.x);
    float y = p.y;
    float front = smoothstep(-0.035, 0.035, p.z);

    float strapInner = 0.062;
    float strapOuter = 0.092;
    float scoopY = lerp(0.752, 0.726, front);
    float armpitY = 0.706;
    float peakY = 0.892;

    float u = saturate(x / strapInner);
    float su = u * u * (3.0 - 2.0 * u);
    float dsu = 6.0 * u * (1.0 - u) / strapInner;

    float armSpan = 0.118 - strapOuter;
    float v = saturate((x - strapOuter) / armSpan);
    float sv = v * v * (3.0 - 2.0 * v);
    float dsv = -6.0 * v * (1.0 - v) / armSpan;

    float upperY = x < strapInner ? lerp(scoopY, peakY, su) : (x <= strapOuter ? peakY : lerp(peakY, armpitY, sv));
    float slopeX = x < strapInner ? (peakY - scoopY) * dsu : (x <= strapOuter ? 0.0 : (peakY - armpitY) * dsv);

    float bodyWeight = regions.x + regions.y + regions.z;
    float otherWeight = saturate(1.0 - bodyWeight - regions.w);
    float headWeight = otherWeight * smoothstep(0.865, 0.895, y);
    float armThreshold = lerp(0.20, 0.62, smoothstep(0.76, 0.82, y));
    float armMask = smoothstep(armThreshold, armThreshold + 0.22, regions.w) * smoothstep(0.075, 0.092, x);
    upperY -= 0.44 * armMask + 0.35 * smoothstep(0.08, 0.38, headWeight);

    float upper = (upperY - y) * rsqrt(1.0 + slopeX * slopeX);

    float legY = 0.382 + 0.014 * smoothstep(0.025, 0.095, x);
    float lowerLegWeight = otherWeight * smoothstep(0.36, 0.26, y);
    float lower = (y - legY) - 0.25 * lowerLegWeight;

    return min(upper, lower);
}

float SingletCut(float3 p)
{
    return SingletCut(p, float4(1.0, 0.0, 0.0, 0.0));
}

half SingletMask(float3 position, float4 regions)
{
    float distance = SingletCut(position, regions);
    float width = max(fwidth(distance), 1e-5);
    return saturate(0.5 + distance / width);
}

// Stitched border piping along the neckline, shoulder straps, armholes and leg openings.
half SingletTrimMask(float3 position, float4 regions)
{
    float distance = SingletCut(position, regions);
    float width = max(fwidth(distance), 1e-5);
    half inside = saturate(0.5 + distance / width);
    half innerEdge = saturate(0.5 + (0.0038 - distance) / max(width, 0.0007));
    return inside * innerEdge;
}

// Subtle ergonomic side panel along the ribcage, hip and outer thigh.
half2 SingletSidePanel(float3 position, float4 regions)
{
    float x = abs(position.x);
    float y = position.y;
    float seamX = 0.078 + 0.012 * saturate((y - 0.42) / 0.28);
    float d = x - seamX;
    float w = max(fwidth(d), 0.001);
    float verticalFade = smoothstep(0.38, 0.41, y) * smoothstep(0.72, 0.67, y) * (1.0 - smoothstep(0.10, 0.35, regions.w));
    half panel = saturate(0.5 + d / w) * verticalFade;
    return half2(panel, 0.0h);
}

#endif
