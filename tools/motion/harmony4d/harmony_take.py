"""Writes a fetched Harmony4D sequence as a keypoint JSON for `motion-import --keypoints`.

usage: harmony_take.py NPZ OUT_JSON
Prints each person's standing height (m), estimated from their own bone lengths.

Joints: COCO wrists become hands, the mid-shoulder point the neck and the mid-ear point the
head. COCO has no toes, so each toe is placed one foot-length (0.15 of the person's height)
ahead of the ankle along the pelvis's facing, on the mat when the ankle is low.
"""
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import harmony_world as hw  # noqa: E402

NAMES = {
    "LeftShoulder": hw.L_SH, "RightShoulder": hw.R_SH, "LeftElbow": hw.L_EL, "RightElbow": hw.R_EL,
    "LeftWrist": hw.L_WR, "RightWrist": hw.R_WR, "LeftHip": hw.L_HIP, "RightHip": hw.R_HIP,
    "LeftKnee": hw.L_KN, "RightKnee": hw.R_KN, "LeftAnkle": hw.L_AN, "RightAnkle": hw.R_AN,
}


def height(p):
    """Standing height from bone lengths: ankle height, shin, thigh, hip to shoulder line, shoulder line to ears, crown."""
    thigh, shin, _, _, _ = hw.bones(p)
    trunk = np.median(np.linalg.norm((p[:, hw.L_SH] + p[:, hw.R_SH]) / 2 - (p[:, hw.L_HIP] + p[:, hw.R_HIP]) / 2, axis=1))
    neck_to_ears = np.median(np.linalg.norm((p[:, hw.L_EAR] + p[:, hw.R_EAR]) / 2 - (p[:, hw.L_SH] + p[:, hw.R_SH]) / 2, axis=1))
    return float(0.08 + shin + thigh + trunk + neck_to_ears + 0.12)


def actor(p, name, stature):
    joints = {key: p[:, index] for key, index in NAMES.items()}
    joints["Neck"] = (p[:, hw.L_SH] + p[:, hw.R_SH]) / 2
    joints["Head"] = (p[:, hw.L_EAR] + p[:, hw.R_EAR]) / 2
    left = p[:, hw.L_HIP] - p[:, hw.R_HIP]
    front = np.cross(left, [0.0, 1.0, 0.0])
    front[:, 1] = 0
    front /= np.maximum(np.linalg.norm(front, axis=1, keepdims=True), 1e-6)
    for side, ankle in (("Left", hw.L_AN), ("Right", hw.R_AN)):
        toe = p[:, ankle] + front * (0.15 * stature)
        toe[:, 1] = np.where(p[:, ankle, 1] < 0.15, 0.03, p[:, ankle, 1] - 0.04)
        joints[side + "Toe"] = toe
    return {"name": name, "joints": {k: [[round(float(c), 4) for c in v] for v in values] for k, values in joints.items()}}


if __name__ == "__main__":
    a, b, _, data = hw.load(sys.argv[1])
    names = [str(n) for n in data["names"]]
    heights = [height(a), height(b)]
    out = {"fps": 20.0, "actors": [actor(a, names[0], heights[0]), actor(b, names[1], heights[1])]}
    with open(sys.argv[2], "w") as handle:
        json.dump(out, handle)
    print(f"{heights[0]:.2f} {heights[1]:.2f} {len(a)}")
