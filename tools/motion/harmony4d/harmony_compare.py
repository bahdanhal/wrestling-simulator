"""Real (Harmony4D) against keyed clips for the checked segments, plus statistics over all real shots.

usage: harmony_compare.py REPO POSES_DIR EVENTS_JSON
POSES_DIR holds harmony_fetch.py output as train/<archive>/<sequence>.npz and test/...;
EVENTS_JSON is harmony_events.py output. Segments come from segments.json next to this file.
Heights are shares of the person's stature (keyed clips on the 1.75 m reference body), so bodies
of different sizes compare. Keyed values use the same formulas as ClipKinematics.
"""
import glob
import json
import math
import os
import sys

import numpy as np

repo, poses, events_path = sys.argv[1:4]
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import harmony_world as hw  # noqa: E402
from harmony_take import height  # noqa: E402  (module prints nothing on import)

H, LEG = 1.75, 1.75 * 0.47
THIGH = LEG * 0.52


def keyed_hip(key):
    g = key["ground"]
    if g == "standing":
        return min(0.07 + LEG * (0.4 + 0.5 * key["level"]), 0.07 + LEG * 0.985)
    if g in ("oneKnee", "bothKnees"):
        return 0.06 + THIGH * (0.55 + 0.42 * min(1.0, key["level"] / 0.6))
    return 0.114


def keyed_lean(key):
    if key["ground"] == "standing":
        return (62 + (24 - 62) * key["posture"]) + (1 - key["level"]) * 14
    return 80 + (8 - 80) * key["posture"]


def keyed(clip_id, ticks):
    for path in glob.glob(os.path.join(repo, "data/animation/*.json")):
        for clip in json.load(open(path))["clips"]:
            if clip["id"] == clip_id:
                keys = clip["keys"]
                out = []
                for k in keys:
                    out.append((k["t"], keyed_hip(k["executor"]) / H, keyed_hip(k["opponent"]) / H, keyed_lean(k["executor"]),
                                abs(k["opponent"]["x"] - k["executor"]["x"])))
                return out
    raise KeyError(clip_id)


def lean(p):
    spine = (p[:, hw.L_SH] + p[:, hw.R_SH]) / 2 - (p[:, hw.L_HIP] + p[:, hw.R_HIP]) / 2
    return np.degrees(np.arccos(np.clip(spine[:, 1] / np.linalg.norm(spine, axis=1), -1, 1)))


def pelvis(p):
    return (p[:, hw.L_HIP] + p[:, hw.R_HIP]) / 2


phases = json.load(open(os.path.join(here, "segments.json")))["segments"]
techniques = {}
for path in glob.glob(os.path.join(repo, "data/techniques/*.json")):
    for ph in json.load(open(path))["phases"]:
        techniques[ph["animation"]] = (ph["minimumTicks"], ph["maximumTicks"])

for ph in phases:
    a, b, _, _ = hw.load(os.path.join(poses, ph["poses"] + ".npz"))
    e, o = (a, b) if ph["executor"] == 0 else (b, a)
    he, ho = height(e), height(o)
    lo, hi = ph["fromFrame"], ph["toFrame"]
    seconds = (hi - lo) / 20.0
    lo_t, hi_t = techniques[ph["clip"]]
    print(f"\n{ph['clip']}  (real {seconds:.2f} s; phase {lo_t}-{hi_t} ticks = {lo_t / 60:.2f}-{hi_t / 60:.2f} s: plays {seconds / (hi_t / 60):.1f}-{seconds / (lo_t / 60):.1f}x fast)")
    k = keyed(ph["clip"], lo_t)
    print("   t    | executor hips (share of height) real / keyed | opponent hips real / keyed | executor trunk lean deg real / keyed | pelvis gap m real / keyed")
    gap = np.linalg.norm((pelvis(e) - pelvis(o))[:, [0, 2]], axis=1)
    for t in (0.0, 0.25, 0.5, 0.75, 1.0):
        f = int(round(lo + t * (hi - lo)))
        # keyed values eased between keys (smoothstep), as MoveClip.Sample does
        i = max(j for j in range(len(k)) if k[j][0] <= t) if t < 1 else len(k) - 2
        j = min(i + 1, len(k) - 1)
        u = 0 if k[j][0] == k[i][0] else (t - k[i][0]) / (k[j][0] - k[i][0])
        s = u * u * (3 - 2 * u)
        kv = [k[i][c] + (k[j][c] - k[i][c]) * s for c in range(1, 5)]
        print(f"  {t:4.2f}  | {pelvis(e)[f, 1] / he:5.2f} / {kv[0]:5.2f}                     | {pelvis(o)[f, 1] / ho:5.2f} / {kv[1]:5.2f}              | {lean(e)[f]:5.1f} / {kv[2]:5.1f}                      | {gap[f]:4.2f} / {kv[3]:4.2f}")
    v = np.diff(pelvis(e)[lo:hi + 1, 1]) * 20.0
    print(f"   fastest real executor hip drop {-v.min():.2f} m/s; keyed drop over the phase at its fastest {(k[0][1] - min(x[1] for x in k)) * H / (lo_t / 60):.2f} m/s average")

# All real shots and level changes: depth, core time, peak hip speed.
events = json.load(open(events_path))
rows = []
for seq, info in events.items():
    a, b, _, _ = hw.load(info["path"])
    for ev in info["events"]:
        if ev["kind"] not in ("shot", "level_change"):
            continue
        p = (a, b)[ev["actor"]]
        y = pelvis(p)[:, 1]
        lo, hi = ev["from"], ev["to"] - 1
        top, bottom = y[lo], y[lo:hi + 1].min()
        drop = top - bottom
        seg = y[lo:hi + 1]
        start = lo + int(np.argmax(seg < top - 0.1 * drop))
        end = lo + int(np.argmax(seg < top - 0.9 * drop))
        speed = -np.diff(seg).min() * 20.0
        rows.append((drop, (end - start) / 20.0, speed))
rows = np.array(rows)
print(f"\nAll {len(rows)} real shots and level changes: hip drop median {np.median(rows[:, 0]):.2f} m (p10-p90 {np.percentile(rows[:, 0], 10):.2f}-{np.percentile(rows[:, 0], 90):.2f}),"
      f" core time (10-90% of the drop) median {np.median(rows[:, 1]):.2f} s (p10 {np.percentile(rows[:, 1], 10):.2f}), peak hip speed median {np.median(rows[:, 2]):.2f} m/s (max {rows[:, 2].max():.2f})")
fastest = rows[np.argmin(rows[:, 1] / np.maximum(rows[:, 0], 1e-3))]
print(f"Free fall over the median drop would take {math.sqrt(2 * np.median(rows[:, 0]) / 9.81):.2f} s; the quickest real one dropped {fastest[0]:.2f} m in {fastest[1]:.2f} s (core).")
