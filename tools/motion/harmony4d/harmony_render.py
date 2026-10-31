"""Stick-figure contact sheets of Harmony4D segments, seen side-on to the line between the bodies.

usage: harmony_render.py EVENTS_JSON KIND OUT_PNG [MAX_SEGMENTS] [SEQ:FROM:TO ...]
Each row is one segment, 8 frames across it; actor 0 red, actor 1 blue (the event's actor has a
thicker line). With explicit SEQ:FROM:TO items, those segments are drawn instead.
"""
import json
import os
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import harmony_world as hw  # noqa: E402

BONES = [(5, 6), (5, 7), (7, 9), (6, 8), (8, 10), (5, 11), (6, 12), (11, 12), (11, 13), (13, 15), (12, 14), (14, 16)]

report = json.load(open(sys.argv[1]))
kind, out = sys.argv[2], sys.argv[3]
limit = int(sys.argv[4]) if len(sys.argv) > 4 else 8
segments = []
for item in sys.argv[5:]:
    seq, lo, hi = item.split(":")
    segments.append((seq, {"kind": kind, "actor": -1, "from": int(lo), "to": int(hi)}))
if not segments:
    for seq, info in report.items():
        for ev in info["events"]:
            if ev["kind"] == kind:
                segments.append((seq, ev))
    segments = segments[:limit]

fig, axes = plt.subplots(len(segments), 8, figsize=(16, 2.2 * len(segments)), squeeze=False)
for row, (seq, ev) in enumerate(segments):
    a, b, _, _ = hw.load(report[seq]["path"])
    lo, hi = ev["from"], min(ev["to"], len(a) - 1)
    mid = (lo + hi) // 2
    line = ((a[mid, 11] + a[mid, 12]) - (b[mid, 11] + b[mid, 12])) / 2
    line[1] = 0
    line /= max(np.linalg.norm(line), 1e-6)
    for col, f in enumerate(np.linspace(lo, hi, 8).astype(int)):
        ax = axes[row, col]
        for i, (p, colour) in enumerate(((a, "red"), (b, "blue"))):
            x = p[f] @ line
            y = p[f, :, 1]
            width = 2.6 if i == ev["actor"] else 1.4
            for j, k in BONES:
                ax.plot([x[j], x[k]], [y[j], y[k]], color=colour, lw=width)
            ax.plot(x[0], y[0], "o", color=colour, ms=3)
        centre = ((a[f, 11] + b[f, 11]) / 2) @ line
        ax.set_xlim(centre - 1.2, centre + 1.2)
        ax.set_ylim(0, 2.0)
        ax.axhline(0, color="k", lw=0.5)
        ax.set_xticks([])
        ax.set_yticks([])
        ax.set_aspect("equal")
        if col == 0:
            ax.set_title(f"{seq} {lo}-{hi} actor {ev['actor']}", fontsize=7, loc="left")
plt.tight_layout()
plt.savefig(out, dpi=70)
print("wrote", out, len(segments), "rows")
