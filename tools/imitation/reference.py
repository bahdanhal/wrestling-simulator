"""Reference motion for imitation: a game motion take turned into simulator targets.

Takes store 17 joint positions per actor per frame, right-handed and Y up. The simulator is Z up.
Joint angles are fitted by physics-based inverse kinematics: a weightless copy of the two wrestlers
(no gravity, no muscles) is pulled onto the recorded positions by soft constraints, frame after
frame, warm-started from the previous one.
"""
from __future__ import annotations

import json
from dataclasses import dataclass

import mujoco
import numpy as np

from wrestlers import JOINTS, model_xml

CONTROL_HZ = 30.0


def to_sim(xyz_yup: np.ndarray) -> np.ndarray:
    """Right-handed Y up to right-handed Z up: (x, y, z) -> (x, -z, y)."""
    out = np.empty_like(xyz_yup)
    out[..., 0] = xyz_yup[..., 0]
    out[..., 1] = -xyz_yup[..., 2]
    out[..., 2] = xyz_yup[..., 1]
    return out


def to_take(xyz_zup: np.ndarray) -> np.ndarray:
    out = np.empty_like(xyz_zup)
    out[..., 0] = xyz_zup[..., 0]
    out[..., 1] = xyz_zup[..., 2]
    out[..., 2] = -xyz_zup[..., 1]
    return out


@dataclass
class Reference:
    take_id: str
    heights: tuple
    keypoints: np.ndarray  # frames × 2 actors × 17 × 3, Z up, at CONTROL_HZ
    qpos: np.ndarray  # frames × nq, fitted joint angles
    qvel: np.ndarray  # frames × nv, finite differences

    @property
    def frames(self) -> int:
        return self.keypoints.shape[0]


def load_take(path: str):
    with open(path, encoding="utf-8") as handle:
        take = json.load(handle)
    frames = np.asarray(take["frames"], dtype=np.float64)  # F × 2 × 51
    frames = frames.reshape(frames.shape[0], 2, len(JOINTS), 3)
    return take, to_sim(frames)


def resample(keypoints: np.ndarray, fps: float, hz: float = CONTROL_HZ) -> np.ndarray:
    duration = (keypoints.shape[0] - 1) / fps
    count = max(2, int(round(duration * hz)) + 1)
    times = np.linspace(0.0, duration, count)
    source = np.arange(keypoints.shape[0]) / fps
    flat = keypoints.reshape(keypoints.shape[0], -1)
    out = np.stack([np.interp(times, source, flat[:, i]) for i in range(flat.shape[1])], axis=1)
    return out.reshape((count,) + keypoints.shape[1:])


def fit(take_path: str, masses=(75.0, 75.0), settle_steps: int = 400, frame_steps: int = 60) -> Reference:
    take, keypoints = load_take(take_path)
    heights = tuple(float(a["heightMetres"]) for a in take["actors"])
    keypoints = resample(keypoints, float(take["framesPerSecond"]))
    model = mujoco.MjModel.from_xml_string(model_xml(heights, masses, ik=True))
    data = mujoco.MjData(model)
    model.dof_damping[:] = np.maximum(model.dof_damping, 5.0)
    mocap_ids = [[model.body(f"target_{name}_{joint}").mocapid[0] for joint in JOINTS] for name in ("a", "b")]

    # Start near frame 0: put each pelvis on its recorded pelvis.
    for actor, name in enumerate(("a", "b")):
        adr = model.jnt_qposadr[model.joint(f"{name}_root").id]
        data.qpos[adr:adr + 3] = keypoints[0, actor, 0]
    qpos = np.zeros((keypoints.shape[0], model.nq))
    for f in range(keypoints.shape[0]):
        for actor in range(2):
            for j in range(len(JOINTS)):
                data.mocap_pos[mocap_ids[actor][j]] = keypoints[f, actor, j]
        for _ in range(settle_steps if f == 0 else frame_steps):
            mujoco.mj_step(model, data)
        data.qvel[:] = 0.0
        qpos[f] = data.qpos
    qvel = np.zeros((keypoints.shape[0], model.nv))
    for f in range(1, keypoints.shape[0]):
        mujoco.mj_differentiatePos(model, qvel[f], 1.0 / CONTROL_HZ, qpos[f - 1], qpos[f])
    qvel[0] = qvel[1] if keypoints.shape[0] > 1 else 0.0
    return Reference(take["id"], heights, keypoints, qpos, qvel)


def fit_error(reference: Reference, masses=(75.0, 75.0)) -> float:
    """Mean distance (m) between the fitted bodies' keypoints and the recording."""
    model = mujoco.MjModel.from_xml_string(model_xml(reference.heights, masses))
    data = mujoco.MjData(model)
    total = 0.0
    for f in range(reference.frames):
        data.qpos[:] = reference.qpos[f]
        mujoco.mj_kinematics(model, data)
        sites = keypoint_positions(model, data)
        total += float(np.mean(np.linalg.norm(sites - reference.keypoints[f], axis=-1)))
    return total / reference.frames


def keypoint_positions(model, data) -> np.ndarray:
    return np.stack([np.stack([data.site(f"{name}_{joint}").xpos for joint in JOINTS]) for name in ("a", "b")])
