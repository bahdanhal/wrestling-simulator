"""Diagnostic fit of COLMAP scene points selected solely by visible-image polygons.

python harmony_scene_floor.py CALIB_DIR masks.json OUTPUT --up 0 0 1 --threshold 0.02
Masks specify frame, fit_cameras, heldout_camera, image paths, and per-camera polygons
in original pixel coordinates. Candidate selection uses no prior floor plane.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from harmony_camera import fit_plane, load_cameras, validate_affine


def in_polygon(point, polygon):
    """Ray-crossing test of a pixel against a simple polygon."""
    x, y = point
    inside = False
    previous = polygon[-1]
    for current in polygon:
        x1, y1 = previous
        x2, y2 = current
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            inside = not inside
        previous = current
    return inside


def robust_fit(points, up, threshold, trials=3000):
    """Deterministic three-point RANSAC, followed by SVD fit of its consensus."""
    points = np.asarray(points, float)
    if len(points) < 3 or not np.isfinite(threshold) or threshold <= 0:
        raise ValueError("At least three points and a positive finite threshold required")
    random = np.random.default_rng(20261031)
    best = None
    for _ in range(trials):
        try:
            normal, offset, _, _ = fit_plane(points[random.choice(len(points), 3, replace=False)], up)
        except ValueError:
            continue
        residual = np.abs(points @ normal + offset)
        mask = residual <= threshold
        score = (int(mask.sum()), -float(np.sum(residual[mask])))
        if best is None or score > best[0]:
            best = (score, mask)
    if best is None or best[0][0] < 3:
        raise ValueError("No nondegenerate plane consensus")
    mask = best[1]
    normal, offset, singular, _ = fit_plane(points[mask], up)
    return normal, offset, singular, mask


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("calibration", type=Path)
    parser.add_argument("masks", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--up", nargs=3, type=float, required=True)
    parser.add_argument("--threshold", type=float, required=True, help="RANSAC distance in metric-world metres")
    args = parser.parse_args()
    config = json.loads(args.masks.read_text())
    names = config["fit_cameras"] + [config["heldout_camera"]]
    if len(set(names)) != len(names):
        raise ValueError("Fit and held-out cameras must be distinct")
    for name in names:
        polygon = np.asarray(config["polygons"][name], dtype=float)
        if polygon.ndim != 2 or polygon.shape[1] != 2 or len(polygon) < 3 or not np.isfinite(polygon).all():
            raise ValueError(f"Invalid polygon for {name}")
    if args.output.exists() and any(args.output.iterdir()):
        raise ValueError("Output directory must be empty")
    cameras = load_cameras(args.calibration, config["frame"], names)
    transform = validate_affine(np.load(args.calibration / "scale.npy", allow_pickle=False), "scale")
    lines = [line for line in (args.calibration / "images.txt").read_text().splitlines() if not line.startswith("#")]
    tracks = {}
    for line in lines[::2]:
        fields = line.split()
        if len(fields) == 10:
            parts = fields[9].split("/")
            if len(parts) == 2 and parts[0].startswith("cam") and parts[1].endswith(".jpg"):
                tracks[int(fields[0])] = (parts[0], int(parts[1][:-4]))
    records = []
    for line in (args.calibration / "points3D.txt").read_text().splitlines():
        if not line or line.startswith("#"):
            continue
        fields = line.split()
        if len(fields) < 8 or (len(fields) - 8) % 2:
            raise ValueError("Invalid COLMAP point record")
        point = (transform @ np.array([*map(float, fields[1:4]), 1.0]))[:3]
        error = float(fields[7])
        if not np.isfinite(point).all() or not np.isfinite(error):
            raise ValueError("Nonfinite COLMAP point or source error")
        observations = [tracks[int(i)] for i in fields[8::2] if int(i) in tracks]
        observed_cameras = sorted({camera for camera, _ in observations})
        timestamps = sorted({stamp for _, stamp in observations})
        if len(observed_cameras) < 3 or len(timestamps) < 2:
            continue
        pixels = {}
        for name, camera in cameras.items():
            try:
                pixel = camera.project(point)
                if in_polygon(pixel, config["polygons"][name]):
                    pixels[name] = pixel.tolist()
            except ValueError:
                continue
        if pixels:
            records.append({"id": int(fields[0]), "xyz": point.tolist(), "colmap_error_pixels": error,
                            "track_observations": len(observations), "track_cameras": observed_cameras,
                            "track_timestamps": timestamps, "mask_pixels": pixels})
    fit = [r for r in records if any(c in r["mask_pixels"] for c in config["fit_cameras"])]
    heldout = [r for r in records if config["heldout_camera"] in r["mask_pixels"] and not any(c in r["mask_pixels"] for c in config["fit_cameras"])]
    points = np.array([r["xyz"] for r in fit])
    normal, offset, singular, included = robust_fit(points, args.up, args.threshold)
    for group in (fit, heldout):
        for record in group:
            record["signed_distance_metres"] = float(np.dot(normal, record["xyz"]) + offset)
    for record, kept in zip(fit, included):
        record["fit_inlier"] = bool(kept)
    args.output.mkdir(parents=True, exist_ok=True)
    for name in names:
        image_path = args.masks.parent / config["images"][name]
        with Image.open(image_path) as source:
            if source.size != cameras[name].image_size:
                raise ValueError(f"{name} image size must match its original calibration: {cameras[name].image_size}")
            image = source.convert("RGB")
        draw = ImageDraw.Draw(image)
        polygon = [tuple(p) for p in config["polygons"][name]]
        draw.line(polygon + [polygon[0]], fill="yellow", width=6)
        try:
            font = ImageFont.truetype("/System/Library/Fonts/Supplemental/Arial.ttf", 25)
        except OSError:
            font = ImageFont.load_default()
        for record in records:
            if name not in record["mask_pixels"]:
                continue
            x, y = record["mask_pixels"][name]
            color = "red" if record in fit and not record.get("fit_inlier") else "lime"
            draw.ellipse((x-9, y-9, x+9, y+9), fill=color, outline="black", width=2)
            draw.text((x+10, y-15), str(record["id"]), font=font, fill=color, stroke_width=2, stroke_fill="black")
        image.thumbnail((1920, 1080))
        image.save(args.output / f"{name}-mask-points.jpg", quality=95)
    calibration_provenance = json.loads((args.calibration / "manifest.json").read_text())
    points_member = calibration_provenance.get("members", {}).get("points3D.txt")
    if not points_member:
        points_member = f"{Path(calibration_provenance['archive']).stem}/{calibration_provenance['sequence']}/colmap/workplace/points3D.txt"
    result = {"masks_path": str(args.masks.resolve()), "masks": config,
              "calibration_path": str(args.calibration.resolve()),
              "calibration_provenance": calibration_provenance,
              "points_member": points_member, "selection": "Image polygon projection only; >=3 distinct exo cameras and >=2 distinct track timestamps. No prior plane filter.",
              "threshold_metres": args.threshold, "normal": normal.tolist(), "offset": float(offset),
              "inlier_singular_values_metres": singular.tolist(), "fit_points": fit, "heldout_unique_points": heldout,
              "excluded_fit_ids": [r["id"] for r in fit if not r["fit_inlier"]],
              "warning": "Image-region membership is not proof of floor membership. RANSAC and camera tracks share calibration; held-out points are separate IDs, not independent reconstruction. No floor accepted."}
    (args.output / "floor-diagnostic.json").write_text(json.dumps(result, indent=2, allow_nan=False) + "\n")
    print(f"fit {len(fit)} points, {sum(included)} inliers, heldout {len(heldout)} unique points")


if __name__ == "__main__":
    main()
