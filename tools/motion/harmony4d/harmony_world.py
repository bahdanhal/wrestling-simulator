"""Shared: load a fetched Harmony4D sequence into a Y-up, metre, floor-at-zero world.

COCO-17 joints: 0 nose, 1/2 eyes, 3/4 ears, 5/6 shoulders, 7/8 elbows, 9/10 wrists, 11/12 hips,
13/14 knees, 15/16 ankles (left first). Each sequence has its own reconstruction axes, so the
floor is fitted to the ankles: the direction in which the ankle points spread least (after
trimming the lifted ones) is up, signed so heads are above ankles, and the floor is the low
ankle level along it.
"""
import numpy as np

NOSE, L_EAR, R_EAR = 0, 3, 4
L_SH, R_SH, L_EL, R_EL, L_WR, R_WR = 5, 6, 7, 8, 9, 10
L_HIP, R_HIP, L_KN, R_KN, L_AN, R_AN = 11, 12, 13, 14, 15, 16


def floor_frame(points_a, points_b):
    """Rotation (rows = right, up, forward) and floor height that put the floor at y = 0."""
    ankles = np.concatenate([points_a[:, [L_AN, R_AN]], points_b[:, [L_AN, R_AN]]]).reshape(-1, 3)
    heads = np.concatenate([points_a[:, NOSE], points_b[:, NOSE]])
    keep = np.ones(len(ankles), bool)
    for _ in range(5):
        centre = ankles[keep].mean(0)
        _, _, vt = np.linalg.svd(ankles[keep] - centre, full_matrices=False)
        up = vt[2]
        if (heads - centre).dot(up).mean() < 0:
            up = -up
        height = (ankles - centre).dot(up)
        # Drop lifted feet (well above the low band) and refit.
        keep = height < np.percentile(height[keep], 60) + 0.04
    right = np.cross(up, [0.0, 0.0, 1.0]) if abs(up[2]) < 0.9 else np.cross(up, [1.0, 0.0, 0.0])
    right /= np.linalg.norm(right)
    forward = np.cross(right, up)
    rotation = np.stack([right, up, forward])
    floor = np.percentile((ankles - centre).dot(up), 10) + centre.dot(up)
    return rotation, floor, keep.mean()


def load(path):
    """Both people's joints (frames, 17, 3) in the floor frame (x right, y up, z towards the viewer), and the fit's inlier share."""
    data = np.load(path, allow_pickle=False)
    a, b = data["a"][:, :, :3], data["b"][:, :, :3]
    rotation, floor, inliers = floor_frame(a, b)
    def place(p):
        world = p @ rotation.T
        world[..., 1] -= floor
        return world
    return place(a), place(b), inliers, data


def bones(p):
    """Median thigh, shin, upper arm, forearm, shoulder width (m)."""
    def length(i, j):
        return np.median(np.linalg.norm(p[:, i] - p[:, j], axis=-1))
    return (length(L_HIP, L_KN) + length(R_HIP, R_KN)) / 2, (length(L_KN, L_AN) + length(R_KN, R_AN)) / 2, \
        (length(L_SH, L_EL) + length(R_SH, R_EL)) / 2, (length(L_EL, L_WR) + length(R_EL, R_WR)) / 2, length(L_SH, R_SH)
