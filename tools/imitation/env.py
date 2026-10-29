"""Imitation environment: two physically simulated wrestlers learning to reproduce a reference.

One controller drives both wrestlers' muscles (PD targets = reference joint angles + action). The
physics is plain: gravity, contact with the mat and each other, muscle force limits. Nothing pushes
a body except muscles, contact and gravity, so whatever the controller learns is physically possible.

Reward (DeepMimic-style, averaged over both wrestlers):
  pose      exp(-2   · Σ joint angle error²)
  velocity  exp(-0.1 · Σ joint velocity error²)
  keypoints exp(-10  · mean keypoint distance²)
  root      exp(-10  · pelvis distance²)
Episodes start at a random reference frame (reference state initialisation) and end early when a
body drifts far from the reference.
"""
from __future__ import annotations

import mujoco
import numpy as np

from reference import CONTROL_HZ, Reference, keypoint_positions
from wrestlers import model_xml

ACTION_SCALE = 0.35  # radians of PD-target offset at action ±1
TERMINATE_DISTANCE = 0.45  # mean keypoint error (m) that ends an episode


class WrestlingImitation:
    def __init__(self, reference: Reference, masses=(75.0, 75.0), seed: int = 0):
        self.reference = reference
        self.model = mujoco.MjModel.from_xml_string(model_xml(reference.heights, masses))
        self.data = mujoco.MjData(self.model)
        self.substeps = int(round(1.0 / (CONTROL_HZ * self.model.opt.timestep)))
        self.rng = np.random.default_rng(seed)
        # Actuated joints: their qpos/qvel addresses, to compare with the reference.
        self.act_qpos = np.array([self.model.jnt_qposadr[self.model.actuator_trnid[i, 0]] for i in range(self.model.nu)])
        self.act_qvel = np.array([self.model.jnt_dofadr[self.model.actuator_trnid[i, 0]] for i in range(self.model.nu)])
        self.ctrl_low = self.model.actuator_ctrlrange[:, 0]
        self.ctrl_high = self.model.actuator_ctrlrange[:, 1]
        self.roots = [self.model.jnt_qposadr[self.model.joint(f"{n}_root").id] for n in ("a", "b")]
        self.frame = 0
        self.observation_size = self._observe().shape[0]
        self.action_size = self.model.nu

    def reset(self, frame: int | None = None) -> np.ndarray:
        last = self.reference.frames - 2
        self.frame = int(self.rng.integers(0, max(1, last))) if frame is None else frame
        mujoco.mj_resetData(self.model, self.data)
        self.data.qpos[:] = self.reference.qpos[self.frame]
        self.data.qvel[:] = self.reference.qvel[self.frame]
        mujoco.mj_forward(self.model, self.data)
        return self._observe()

    def step(self, action: np.ndarray):
        target = self.reference.qpos[min(self.frame + 1, self.reference.frames - 1), self.act_qpos] + ACTION_SCALE * np.clip(action, -1.0, 1.0)
        self.data.ctrl[:] = np.clip(target, self.ctrl_low, self.ctrl_high)
        for _ in range(self.substeps):
            mujoco.mj_step(self.model, self.data)
        self.frame += 1
        reward, error = self._reward()
        done = self.frame >= self.reference.frames - 1 or error > TERMINATE_DISTANCE or not np.all(np.isfinite(self.data.qpos))
        return self._observe(), reward, done, {"keypoint_error": error}

    def keypoints(self) -> np.ndarray:
        return keypoint_positions(self.model, self.data)

    def _reward(self):
        f = min(self.frame, self.reference.frames - 1)
        ref_q = self.reference.qpos[f]
        ref_v = self.reference.qvel[f]
        pose = np.sum((self.data.qpos[self.act_qpos] - ref_q[self.act_qpos]) ** 2)
        vel = np.sum((self.data.qvel[self.act_qvel] - ref_v[self.act_qvel]) ** 2)
        distances = np.linalg.norm(self.keypoints() - self.reference.keypoints[f], axis=-1)
        error = float(np.mean(distances))
        root = sum(float(np.sum((self.data.qpos[r:r + 3] - ref_q[r:r + 3]) ** 2)) for r in self.roots)
        reward = (0.5 * np.exp(-2.0 * pose / 2) + 0.05 * np.exp(-0.1 * vel / 2)
                  + 0.3 * np.exp(-10.0 * error ** 2) + 0.15 * np.exp(-10.0 * root / 2))
        return float(reward), error

    def _observe(self) -> np.ndarray:
        """Both bodies relative to wrestler A's pelvis, plus the reference's next pose and the phase."""
        origin = self.data.qpos[self.roots[0]:self.roots[0] + 3].copy()
        origin[2] = 0.0
        q = self.data.qpos.copy()
        for r in self.roots:
            q[r:r + 3] -= origin
        f = min(self.frame + 1, self.reference.frames - 1)
        ref = self.reference.qpos[f, self.act_qpos]
        phase = self.frame / max(1, self.reference.frames - 1)
        return np.concatenate([q, np.clip(self.data.qvel, -20, 20) * 0.1, ref, [np.sin(np.pi * phase), np.cos(np.pi * phase)]]).astype(np.float32)
