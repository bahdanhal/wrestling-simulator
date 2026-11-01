"""Deterministic source and phase coverage inventory; detections remain candidates.

Usage: python harmony_inventory.py [REPO] [--events EVENTS.json ...] [--out REPORT.md]
"""
import argparse
import glob
import json
import math
import os
import sys


def load(path):
    with open(path, encoding="utf-8") as stream:
        return json.load(stream)


def take_fps(take_id, takes):
    take = takes.get(take_id)
    if take is None:
        raise ValueError(f"Reviewed segment references missing take {take_id!r}; native timing is unavailable.")
    fps = take.get("framesPerSecond")
    if not isinstance(fps, (int, float)) or not math.isfinite(fps) or fps <= 0:
        raise ValueError(f"Take {take_id!r} has invalid framesPerSecond {fps!r}; native timing is unavailable.")
    return fps


def segment_seconds(segment, takes):
    return (segment["toFrame"] - segment["fromFrame"]) / take_fps(segment.get("take"), takes)


def audit_provenance(seg, takes):
    take = takes.get(seg.get("take"))
    title = take.get("source", {}).get("title", "") if take else ""
    archive = title.split(", sequence ", 1)[0].removesuffix(".zip")
    archive = archive.removeprefix("Harmony4D ")
    sequence = title.split(", sequence ", 1)[1] if ", sequence " in title else ""
    expected_pose = archive + "/" + sequence if archive and sequence else ""
    frames = take.get("frames", []) if take else []
    try:
        frame_range_valid = bool(take and 0 <= seg["fromFrame"] < seg["toFrame"] <= len(frames) - 1)
    except (KeyError, TypeError):
        frame_range_valid = False
    return {"clip": seg.get("clip", ""), "take_exists": take is not None,
            "pose_source_matches_take_title": bool(expected_pose and seg.get("poses") == expected_pose),
            "frame_range_valid": frame_range_valid}


def inventory(repo, event_paths=()):
    take_paths = sorted(glob.glob(os.path.join(repo, "data/motion/takes/*.json")))
    takes = {}
    for path in take_paths:
        take = load(path)
        takes[take["id"]] = take
    index = load(os.path.join(repo, "data/motion/index.json"))
    techniques = []
    for path in sorted(glob.glob(os.path.join(repo, "data/techniques/*.json"))):
        technique = load(path)
        techniques.extend((technique["id"], phase) for phase in technique["phases"])
    segments = load(os.path.join(os.path.dirname(__file__), "segments.json"))["segments"]
    events = []
    for path in event_paths:
        for sequence, data in load(path).items():
            for event in data.get("events", []):
                events.append({"poses": sequence, **event})
    # Candidate event kinds are only broad cues. These associations never establish phase validity.
    kind_hints = {
        "penetration": {"shot", "level_change"}, "snap": {"snap_down"},
        "lock": {"body_lock"}, "finish": {"takedown"}, "landing": {"takedown"},
    }
    reviewed = {seg["clip"]: seg for seg in segments}
    assigned = {p["clip"]: p for p in index.get("phases", [])}
    rows = []
    for technique_id, phase in techniques:
        clip = phase["animation"]
        seg = reviewed.get(clip)
        phase_name = phase["id"].lower()
        kinds = next((v for key, v in kind_hints.items() if key in phase_name), set())
        cues = [e for e in events if e.get("kind") in kinds]
        native_seconds = None
        if seg:
            native_seconds = segment_seconds(seg, takes)
        rows.append({"technique": technique_id, "phase": phase["id"], "clip": clip,
                     "reviewed": bool(seg), "candidate_cues": len(cues),
                     "assigned": clip in assigned,
                     "reviewed_segment": seg, "assignment": assigned.get(clip),
                     "native_seconds": native_seconds,
                     "engine_min_seconds": phase["minimumTicks"] / 60.0,
                     "engine_max_seconds": phase["maximumTicks"] / 60.0})
    provenance = []
    for seg in segments:
        provenance.append(audit_provenance(seg, takes))
    take_rows = []
    for take_id in sorted(takes):
        take = takes[take_id]
        count = len(take["frames"])
        fps = take_fps(take_id, takes)
        take_rows.append({"id": take_id, "fps": fps, "frames": count,
                          "endpoint_seconds": (count - 1) / fps,
                          "hold_last_seconds": count / fps,
                          "source": take["source"].get("title", ""), "license": take["source"].get("license", "")})
    return {"takes": take_rows, "rows": rows, "provenance": provenance,
            "candidate_event_count": len(events), "event_files": list(event_paths)}


def render(report):
    reviewed_count = len(report["provenance"])
    out = ["# Harmony4D source and phase coverage", "",
           f"The {reviewed_count} camera-reviewed segment records are reviewed source windows; their clip labels are provenance annotations, not accepted phase matches. Candidate detections are automated and are not camera-validated.", "",
           "## Local imported takes", "", "| Take | Source | Licence | FPS | Frames | Endpoint span (s) | Hold-last span (s) |", "|---|---|---:|---:|---:|---:|---:|"]
    for t in report["takes"]:
        out.append(f"| {t['id']} | {t['source']} | {t['license']} | {t['fps']:g} | {t['frames']} | {t['endpoint_seconds']:.2f} | {t['hold_last_seconds']:.2f} |")
    reviewed = sum(r["reviewed"] for r in report["rows"])
    assigned = sum(r["assigned"] for r in report["rows"])
    out += ["", f"Phase coverage: {len(report['rows'])} total; {reviewed} have a camera-reviewed source window labelled with the clip; {assigned} have an accepted motion-index assignment.",
            f"Candidate cue events supplied: {report['candidate_event_count']} (broad detections; not validated).", "",
            "## Per-phase coverage", "", "| Technique | Phase | Clip | Reviewed source window | Candidate cues* | Assigned | Native window (s) | Engine min–max (s) |", "|---|---|---|---:|---:|---:|---:|---:|"]
    for r in report["rows"]:
        native = f"{r['native_seconds']:.2f}" if r['native_seconds'] is not None else "—"
        out.append(f"| {r['technique']} | {r['phase']} | {r['clip']} | {'yes' if r['reviewed'] else 'no'} | {r['candidate_cues']} | {'yes' if r['assigned'] else 'no'} | {native} | {r['engine_min_seconds']:.2f}–{r['engine_max_seconds']:.2f} |")
    out += ["", "*Candidate cue counts are broad event-kind hints and are not evidence that the named phase occurred.", "", "## Reviewed segment provenance audit", "",
            "| Clip | Take exists | Pose source matches title | Frame range valid |", "|---|---:|---:|---:|"]
    for p in report["provenance"]:
        out.append(f"| {p['clip']} | {'yes' if p['take_exists'] else 'no'} | {'yes' if p['pose_source_matches_take_title'] else 'no'} | {'yes' if p['frame_range_valid'] else 'no'} |")
    return "\n".join(out) + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repo", nargs="?", default=os.path.abspath(os.path.join(os.path.dirname(__file__), "../../..")))
    parser.add_argument("--events", action="append", default=[], help="optional harmony_events JSON; repeatable")
    parser.add_argument("--out", help="write Markdown report (default: stdout)")
    args = parser.parse_args(argv)
    report = render(inventory(args.repo, args.events))
    if args.out:
        with open(args.out, "w", encoding="utf-8") as stream:
            stream.write(report)
    else:
        sys.stdout.write(report)


if __name__ == "__main__":
    main()
