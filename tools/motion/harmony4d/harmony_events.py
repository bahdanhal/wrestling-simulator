"""Finds wrestling situations in fetched Harmony4D sequences from the 3D joints.

usage: harmony_events.py POSES_GLOB OUT_JSON
Per frame and person: hip height, distance between the two pelvises, a hand on the opponent's
neck (collar tie), both hands round behind the opponent's back with chests together (body lock),
a fast drop of the hips towards the opponent (level change / shot), and takedowns (one goes from
standing to the mat while the other stays over them). Writes the segments found.
"""
import glob
import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import harmony_world as hw  # noqa: E402

FPS = 20.0


def pelvis(p):
    return (p[:, hw.L_HIP] + p[:, hw.R_HIP]) / 2


def neck(p):
    return (p[:, hw.L_SH] + p[:, hw.R_SH]) / 2


def chest_front(p):
    """Unit vector the chest faces, from the shoulder line and the spine."""
    left = p[:, hw.L_SH] - p[:, hw.R_SH]
    spine = neck(p) - pelvis(p)
    # Right-handed, y up: a person facing +z has their left at +x, and left x up = +z, the front.
    front = np.cross(left, spine)
    return front / np.linalg.norm(front, axis=1, keepdims=True)


def runs(mask, minimum):
    """(start, end) frame pairs of True runs at least `minimum` frames long (end exclusive)."""
    out, start = [], None
    for i, v in enumerate(list(mask) + [False]):
        if v and start is None:
            start = i
        elif not v and start is not None:
            if i - start >= minimum:
                out.append((start, i))
            start = None
    return out


def analyse(path):
    a, b, _, _ = hw.load(path)
    people = (a, b)
    hips = [pelvis(p)[:, 1] for p in people]
    flat = [pelvis(p)[:, [0, 2]] for p in people]
    gap = np.linalg.norm(flat[0] - flat[1], axis=1)
    events = []
    for me in (0, 1):
        p, o = people[me], people[1 - me]
        # Collar tie: a wrist within 15 cm of the opponent's neck line (shoulders to ears).
        o_neck = (neck(o) + (o[:, hw.L_EAR] + o[:, hw.R_EAR]) / 2) / 2
        tie = np.minimum(np.linalg.norm(p[:, hw.L_WR] - o_neck, axis=1), np.linalg.norm(p[:, hw.R_WR] - o_neck, axis=1)) < 0.15
        for s, e in runs(tie, 8):
            events.append({"kind": "collar_tie", "actor": me, "from": s, "to": e})
        # Body lock: chests within 35 cm, both wrists behind the opponent's chest (on their back half).
        o_front = chest_front(o)
        o_mid = (neck(o) + pelvis(o)) / 2
        behind = lambda w: ((p[:, w] - o_mid) * o_front).sum(1) < 0.0
        chests = np.linalg.norm((neck(p) + pelvis(p)) / 2 - o_mid, axis=1) < 0.45
        lock = chests & behind(hw.L_WR) & behind(hw.R_WR)
        for s, e in runs(lock, 6):
            events.append({"kind": "body_lock", "actor": me, "from": s, "to": e})
        # Level change: the hips drop at least 20 cm within 0.8 s while closing on the opponent.
        window = int(0.8 * FPS)
        for i in range(len(hips[me]) - window):
            seg = hips[me][i:i + window]
            if hips[me][i] > 0.65 and seg.min() < hips[me][i] - 0.2:
                j = i + int(np.argmin(seg))
                # The shooter's own hips travel towards where the opponent was (not pushed down in place).
                towards = flat[1 - me][i] - flat[me][i]
                towards /= max(np.linalg.norm(towards), 1e-6)
                travel = float((flat[me][j] - flat[me][i]) @ towards)
                if travel < 0.15:
                    continue
                head_low = p[j, hw.NOSE, 1] < neck(o)[j, 1] - 0.15
                hands = min(np.linalg.norm(p[j, w] - o[j, k]) for w in (hw.L_WR, hw.R_WR) for k in (hw.L_KN, hw.R_KN, hw.L_HIP, hw.R_HIP))
                events.append({"kind": "shot" if head_low and hands < 0.3 else "level_change", "actor": me, "from": i, "to": j + 1})
        # Snap down: my hand on their neck, then their head pulled down at least 30 cm within 1 s while my hips stay up.
        window = int(1.0 * FPS)
        o_head = o[:, hw.NOSE, 1]
        for i in np.where(tie)[0]:
            if i + window >= len(o_head):
                break
            j = i + int(np.argmin(o_head[i:i + window]))
            if o_head[i] - o_head[j] > 0.3 and hips[me][j] > 0.6 and hips[1 - me][j] > 0.35:
                events.append({"kind": "snap_down", "actor": me, "from": int(i), "to": j + 1})
        # Takedown: my opponent's hips go from standing (> 0.6 m) to the mat (< 0.35 m) within 2 s
        # while mine stay above theirs.
        window = int(2.0 * FPS)
        o_hips, my_hips = hips[1 - me], hips[me]
        for i in range(len(o_hips) - 1):
            if o_hips[i] <= 0.6:
                continue
            ahead = np.where(o_hips[i:i + window] < 0.35)[0]
            if len(ahead):
                j = i + int(ahead[0])
                if my_hips[j] > o_hips[j] + 0.1:
                    events.append({"kind": "takedown", "actor": me, "from": i, "to": j + 1})
    # Merge overlapping events of the same kind and actor.
    merged = []
    for ev in sorted(events, key=lambda e: (e["kind"], e["actor"], e["from"])):
        last = merged[-1] if merged else None
        if last and last["kind"] == ev["kind"] and last["actor"] == ev["actor"] and ev["from"] <= last["to"]:
            last["to"] = max(last["to"], ev["to"])
        else:
            merged.append(dict(ev))
    for ev in merged:
        ev["seconds"] = round((ev["to"] - ev["from"]) / FPS, 2)
    stance = runs((hips[0] > 0.65) & (hips[1] > 0.65) & (gap > 0.9), int(1.0 * FPS))
    merged += [{"kind": "stance", "actor": -1, "from": s, "to": e, "seconds": round((e - s) / FPS, 2)} for s, e in stance]
    return merged, len(a)


report = {}
for path in sorted(glob.glob(sys.argv[1])):
    name = os.path.basename(os.path.dirname(path)) + "/" + os.path.basename(path)[:-4]
    events, frames = analyse(path)
    report[name] = {"path": path, "frames": frames, "events": events}

with open(sys.argv[2], "w") as handle:
    json.dump(report, handle, indent=1, default=int)

totals = {}
for seq in report.values():
    for ev in seq["events"]:
        totals.setdefault(ev["kind"], []).append(ev["seconds"])
for kind, secs in sorted(totals.items()):
    print(f"{kind:13s} {len(secs):4d} segments, {sum(secs):6.1f} s")
