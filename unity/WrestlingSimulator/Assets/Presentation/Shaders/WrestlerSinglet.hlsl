#ifndef WRESTLER_SINGLET_INCLUDED
#define WRESTLER_SINGLET_INCLUDED

// Wrestling singlet, from the data the importer bakes into the body mesh (CharacterImport,
// SingletSeam): the seam distance is the signed distance in metres to the singlet's edge along
// the skin (positive on the fabric), measured to a smooth curve and exact on the subdivided
// seam triangles, so the edge is crisp at any zoom. Body positions are bind-pose positions
// normalised by height (feet at y = 0, +x the wrestler's left, +z the front).

#define SINGLET_TRIM_WIDTH 0.008

// 1 on the fabric, 0 on skin, anti-aliased over one pixel.
half SingletCoverage(float seam)
{
    float width = max(fwidth(seam), 1e-5);
    return saturate(0.5 + seam / width);
}

// The elastic binding along the neckline, straps, armholes and leg openings.
half SingletTrim(float seam)
{
    float width = max(fwidth(seam), 1e-5);
    return saturate(0.5 + seam / width) * saturate(0.5 + (SINGLET_TRIM_WIDTH - seam) / width);
}

// Two coverstitch rows holding the binding, as thin recessed lines.
half SingletStitches(float seam)
{
    float width = max(fwidth(seam), 2e-4);
    float row1 = abs(seam - 0.0016);
    float row2 = abs(seam - (SINGLET_TRIM_WIDTH - 0.0016));
    float nearest = min(row1, row2);
    return saturate(1.0 - (nearest - 0.00018) / width);
}

// Darker side panel along the ribcage, hip and outer thigh.
half SingletSidePanel(float3 position, half armShare)
{
    float x = abs(position.x);
    float y = position.y;
    float seamX = 0.078 + 0.012 * saturate((y - 0.42) / 0.28);
    float d = x - seamX;
    float w = max(fwidth(d), 0.001);
    float verticalFade = smoothstep(0.38, 0.41, y) * smoothstep(0.72, 0.67, y) * (1.0 - smoothstep(0.10, 0.35, armShare));
    return saturate(0.5 + d / w) * verticalFade;
}

// One family of parallel folds: phase runs across them, envelope is a soft ellipse.
float FoldFamily(float2 p, float2 centre, float2 across, float2 radius, float wavelength)
{
    float2 d = p - centre;
    float2 along = float2(-across.y, across.x);
    float u = dot(d, across);
    float v = dot(d, along);
    float envelope = exp(-(u * u) / (radius.x * radius.x) - (v * v) / (radius.y * radius.y));
    return sin(u * (6.2831853 / wavelength)) * envelope;
}

// Height of the fabric's tension folds (metres) from how far each hip and shoulder is bent
// (drive = hip left, hip right, shoulder left, shoulder right): crease folds in the groin and
// across the lower belly when the hips flex, and fan folds under the armpit when the arm lifts.
float SingletFolds(float3 position, float4 drive, float depth)
{
    float side = position.x >= 0.0 ? 1.0 : -1.0;
    float hip = side > 0.0 ? drive.x : drive.y;
    float shoulder = side > 0.0 ? drive.z : drive.w;
    float2 p = float2(abs(position.x), position.y);
    float frontFace = smoothstep(0.0, 0.035, position.z);
    float sideFace = 1.0 - smoothstep(0.03, 0.07, abs(position.z));

    float groin = FoldFamily(p, float2(0.062, 0.468), float2(0.6, 0.8), float2(0.03, 0.05), 0.011) * frontFace * hip;
    float belly = FoldFamily(p, float2(0.0, 0.545), float2(0.0, 1.0), float2(0.03, 0.075), 0.013) * frontFace * min(drive.x, drive.y);
    float armpit = FoldFamily(p, float2(0.098, 0.672), float2(0.86, -0.5), float2(0.028, 0.045), 0.012) * sideFace * shoulder;
    return depth * (groin + 0.8 * belly + armpit);
}

#endif
