#ifndef WRESTLER_SINGLET_INCLUDED
#define WRESTLER_SINGLET_INCLUDED

// Wrestling singlet, evaluated per pixel from the importer's data (CharacterImport):
// position = bind-pose position normalised by height (feet at y = 0, +z the front);
// The cut is purely geometric, so it is smooth across joints: from mid-thigh up to a scoop
// neckline under the collarbones, with straps over the shoulders.

#define SINGLET_OUTSIDE 0.06

// Signed distance (positive inside) of the singlet's cut, in units of height. The upper edge is
// the union of a scoop between the straps, the straps over the shoulders, and the armholes that
// curve from the straps down to the side; below that it reaches mid-thigh.
float SingletCut(float3 p)
{
    float x = abs(p.x);
    float y = p.y;
    float front = smoothstep(-0.03, 0.03, p.z);

    float scoopLine = lerp(0.778, 0.765, front) + 0.03 * (x / 0.045) * (x / 0.045);
    float scoop = min(scoopLine - y, 0.06 - x);
    float strap = min(min(0.828 - y, 0.07 - x), x - 0.043);
    float armholeLine = 0.80 - 0.09 * smoothstep(0.07, 0.105, x);
    float armhole = min(armholeLine - y, x - 0.062);
    float upper = max(max(scoop, strap), armhole);

    float lower = y - 0.385;
    // In the A-pose bind, arms hang clear of the torso: everything this far out is arm.
    float body = 0.135 - x;
    return min(min(upper, lower), body);
}

half SingletMask(float3 position, float4 regions)
{
    float distance = SingletCut(position);
    float width = max(fwidth(distance), 1e-5);
    return saturate(0.5 + distance / width);
}

#endif
