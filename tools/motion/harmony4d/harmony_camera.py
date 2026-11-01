"""Harmony4D calibrated camera geometry for manually marked image points.

Fetch calibration: python harmony_camera.py fetch ARCHIVE SEQUENCE OUTPUT
Triangulate: python harmony_camera.py triangulate CALIB_DIR --frame 177 --landmarks marks.json --output result.json

marks.json: {"uncertainty": "manual annotation; unknown pixel uncertainty", "landmarks":
 {"left_heel": {"cam01": [1234, 567], "cam05": [2100, 800]}}}
Coordinates are original 3840x2160 image pixels. Results are estimates in Harmony4D's
metric world coordinates, not proof of mat contact. No model-derived landmarks are used.
"""
import argparse
import io
import json
import re
import zipfile
from pathlib import Path

import numpy as np

from harmony_images import BASE, RangeReader

MEMBERS = ("cameras.txt", "images.txt", "scale.npy")


def fetch(archive_name, sequence, output, include_points=False):
    if not re.fullmatch(r"(?:train|test)/[A-Za-z0-9_-]+\.zip", archive_name) or not re.fullmatch(r"[A-Za-z0-9_-]+", sequence):
        raise ValueError("Invalid archive or sequence")
    if output.exists() and any(output.iterdir()):
        raise ValueError("Calibration output directory must be empty")
    output.mkdir(parents=True, exist_ok=True)
    prefix = f"{Path(archive_name).stem}/{sequence}/colmap/workplace/"
    members = {}
    with RangeReader(BASE + archive_name) as remote, zipfile.ZipFile(remote) as archive:
        for name in MEMBERS + (("points3D.txt",) if include_points else ()):
            info = archive.getinfo(prefix + name)
            if info.file_size > 64 * 1024 * 1024 or info.compress_size > 64 * 1024 * 1024:
                raise ValueError(f"Calibration member too large: {info.filename}")
            data = archive.read(info)
            if name == "scale.npy":
                scale = np.load(io.BytesIO(data), allow_pickle=False)
                if scale.shape != (4, 4) or not np.isfinite(scale).all():
                    raise ValueError("Invalid scale transform")
            (output / name).write_bytes(data)
            members[name] = info.filename
    (output / "manifest.json").write_text(json.dumps({"archive": archive_name, "archive_url": BASE + archive_name,
        "sequence": sequence, "members": members, "source": {"title": "Harmony4D", "url": "https://huggingface.co/datasets/Jyun-Ting/Harmony4D", "license": "MIT", "authors": "Rawal Khirodkar, Jyun-Ting Song, Jinkun Cao, Zhengyi Luo, Kris Kitani"}}, indent=2, allow_nan=False) + "\n")


def quaternion_rotation(q):
    q = np.asarray(q, dtype=float)
    if q.shape != (4,) or not np.isfinite(q).all() or np.linalg.norm(q) < 1e-12:
        raise ValueError("Invalid COLMAP quaternion")
    q = q / np.linalg.norm(q)
    w, x, y, z = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])


def validate_affine(matrix, label):
    matrix = np.asarray(matrix, dtype=float)
    if (matrix.shape != (4, 4) or not np.isfinite(matrix).all()
            or not np.allclose(matrix[3], [0, 0, 0, 1], atol=1e-10, rtol=0)
            or abs(np.linalg.det(matrix[:3, :3])) < 1e-12):
        raise ValueError(f"Invalid or singular {label} affine transform")
    return matrix


class Camera:
    def __init__(self, name, params, transform, calibration_frame):
        self.name, self.params, self.transform = name, np.asarray(params, float), np.asarray(transform, float)
        self.calibration_frame = calibration_frame
        if (self.params.shape != (8,) or not np.isfinite(self.params).all()
                or self.params[0] <= 0 or self.params[1] <= 0):
            raise ValueError("Invalid fisheye camera parameters")
        validate_affine(self.transform, "camera")

    def project(self, point):
        point = np.asarray(point, dtype=float)
        if point.shape != (3,) or not np.isfinite(point).all():
            raise ValueError("World point must be three finite coordinates")
        p = self.transform @ np.r_[point, 1.0]
        if not np.isfinite(p).all() or p[2] <= 0:
            raise ValueError("Point is behind camera")
        x, y = p[:2] / p[2]
        radius = np.hypot(x, y)
        theta = np.arctan(radius)
        fx, fy, cx, cy, k1, k2, k3, k4 = self.params
        t2 = theta * theta
        distorted = theta * (1 + k1*t2 + k2*t2**2 + k3*t2**3 + k4*t2**4)
        scale = distorted / radius if radius > 1e-12 else 1.0
        result = np.array([fx*x*scale+cx, fy*y*scale+cy])
        if not np.isfinite(result).all():
            raise ValueError("Nonfinite projection")
        return result

    def ray(self, pixel):
        fx, fy, cx, cy, k1, k2, k3, k4 = self.params
        pixel = np.asarray(pixel, dtype=float)
        if pixel.shape != (2,) or not np.isfinite(pixel).all():
            raise ValueError("Image point must be two finite pixel coordinates")
        xy = (pixel - [cx, cy]) / [fx, fy]
        rd = np.linalg.norm(xy)
        theta = rd
        for _ in range(30):
            t2 = theta * theta
            value = theta * (1 + k1*t2 + k2*t2**2 + k3*t2**3 + k4*t2**4) - rd
            derivative = 1 + 3*k1*t2 + 5*k2*t2**2 + 7*k3*t2**3 + 9*k4*t2**4
            if abs(derivative) < 1e-12:
                raise ValueError("Fisheye inverse is singular")
            step = value / derivative
            theta -= step
            if abs(step) < 1e-12:
                break
        else:
            raise ValueError("Fisheye inverse did not converge")
        if theta < 0 or theta >= np.pi / 2:
            raise ValueError("Pixel is outside the supported forward fisheye field")
        radial = np.tan(theta) / rd if rd > 1e-12 else 1.0
        direction_camera = np.array([*(xy * radial), 1.0])
        inverse = np.linalg.inv(self.transform)
        origin = inverse[:3, 3]
        direction = inverse[:3, :3] @ direction_camera
        length = np.linalg.norm(direction)
        if not np.isfinite(origin).all() or not np.isfinite(length) or length < 1e-12:
            raise ValueError("Invalid unprojected ray")
        return origin, direction / length


def load_cameras(directory, frame, names, max_calibration_frame_gap=25):
    if frame < 1 or not names or any(not re.fullmatch(r"cam\d+", name) for name in names):
        raise ValueError("Invalid frame or camera names")
    if max_calibration_frame_gap is not None and max_calibration_frame_gap < 0:
        raise ValueError("Maximum calibration frame gap must be nonnegative")
    scale = validate_affine(np.load(directory / "scale.npy", allow_pickle=False), "scale")
    calibrations = {}
    for line in (directory / "cameras.txt").read_text().splitlines():
        parts = line.split()
        if not parts or parts[0].startswith("#"):
            continue
        if parts[1] == "OPENCV_FISHEYE":
            calibrations[int(parts[0])] = (int(parts[2]), int(parts[3]), list(map(float, parts[4:])))
    records = {name: [] for name in names}
    # COLMAP images.txt alternates pose and potentially very long feature-observation lines.
    lines = (directory / "images.txt").read_text().splitlines()
    rows = [line for line in lines if not line.startswith("#")]
    for line in rows[::2]:
        fields = line.split()
        if len(fields) != 10:
            raise ValueError("Malformed COLMAP image pose")
        match = re.fullmatch(r"(cam\d+)/(\d+)\.jpg", fields[9])
        if not match or match.group(1) not in records:
            continue
        timestamp, camera_id = int(match.group(2)), int(fields[8])
        if camera_id not in calibrations:
            raise ValueError("Missing fisheye calibration")
        raw = np.eye(4)
        raw[:3, :3] = quaternion_rotation(list(map(float, fields[1:5])))
        raw[:3, 3] = list(map(float, fields[5:8]))
        validate_affine(raw, "COLMAP extrinsic")
        rotation = raw[:3, :3]
        if not np.allclose(rotation.T @ rotation, np.eye(3), atol=1e-8) or not np.isclose(np.linalg.det(rotation), 1.0, atol=1e-8):
            raise ValueError("COLMAP rotation is not rigid")
        records[match.group(1)].append((timestamp, camera_id, raw))
    result = {}
    for name, values in records.items():
        if not values:
            raise ValueError(f"No extrinsics for {name}")
        timestamp, camera_id, raw = min(values, key=lambda row: (abs(row[0] - frame), row[0]))
        available = [row[0] for row in values]
        gap = abs(timestamp - frame)
        if max_calibration_frame_gap is not None:
            if not min(available) <= frame <= max(available):
                raise ValueError(f"{name} frame {frame} is outside the calibration frame range")
            if gap > max_calibration_frame_gap:
                raise ValueError(f"{name} calibration gap {gap} exceeds {max_calibration_frame_gap} frames")
        width, height, params = calibrations[camera_id]
        if width <= 0 or height <= 0:
            raise ValueError("Invalid calibration image dimensions")
        result[name] = Camera(name, params, raw @ np.linalg.inv(scale), timestamp)
        result[name].image_size = (width, height)
        result[name].calibration_range = (min(available), max(available))
        result[name].calibration_frame_gap = gap
    return result


def triangulate(cameras, observations):
    if len(observations) < 2:
        raise ValueError("At least two camera observations required")
    matrix = np.zeros((3, 3))
    rhs = np.zeros(3)
    for name, pixel in observations.items():
        if name not in cameras:
            raise ValueError(f"Missing camera {name}")
        pixel = np.asarray(pixel, dtype=float)
        if pixel.shape != (2,) or not np.isfinite(pixel).all():
            raise ValueError(f"Invalid finite pixel coordinates for {name}")
        width, height = cameras[name].image_size if hasattr(cameras[name], "image_size") else (None, None)
        if width is not None and (not 0 <= pixel[0] < width or not 0 <= pixel[1] < height):
            raise ValueError(f"Point outside image bounds for {name}")
        origin, direction = cameras[name].ray(pixel)
        normal = np.eye(3) - np.outer(direction, direction)
        matrix += normal
        rhs += normal @ origin
    if not np.isfinite(matrix).all() or not np.isfinite(rhs).all() or np.linalg.cond(matrix) > 1e8:
        raise ValueError("Rays are too nearly parallel to triangulate reliably")
    point = np.linalg.solve(matrix, rhs)
    if not np.isfinite(point).all():
        raise ValueError("Nonfinite triangulated point")
    residuals = {name: {"observed_pixel": list(pixel), "projected_pixel": cameras[name].project(point).tolist(),
                         "residual_pixels": float(np.linalg.norm(cameras[name].project(point) - pixel))}
                 for name, pixel in observations.items()}
    if any(not np.isfinite(view["residual_pixels"]) for view in residuals.values()):
        raise ValueError("Nonfinite reprojection residual")
    return point, residuals


def fit_plane(points, up):
    """Return an up-oriented least-squares plane and signed point residuals."""
    points = np.asarray(points, dtype=float)
    up = np.asarray(up, dtype=float)
    if points.ndim != 2 or points.shape[1:] != (3,) or len(points) < 3 or not np.isfinite(points).all():
        raise ValueError("At least three finite 3D points are required")
    if up.shape != (3,) or not np.isfinite(up).all() or np.linalg.norm(up) < 1e-12:
        raise ValueError("Up must be a finite nonzero 3D direction")
    centre = np.mean(points, axis=0)
    _, singular_values, axes = np.linalg.svd(points - centre, full_matrices=False)
    if not np.isfinite(singular_values).all() or singular_values[1] <= max(1e-10, singular_values[0] * 1e-8):
        raise ValueError("Plane points are collinear or degenerate")
    normal = axes[-1]
    alignment = float(normal @ up)
    if abs(alignment) <= np.linalg.norm(up) * 1e-10:
        raise ValueError("Up direction is perpendicular to the fitted normal")
    if alignment < 0:
        normal = -normal
    offset = -float(normal @ centre)
    residuals = points @ normal + offset
    if not np.isfinite(normal).all() or not np.isfinite(offset) or not np.isfinite(residuals).all():
        raise ValueError("Nonfinite fitted plane")
    return normal, offset, singular_values, residuals


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    get = sub.add_parser("fetch")
    get.add_argument("archive"); get.add_argument("sequence"); get.add_argument("output", type=Path)
    get.add_argument("--include-points", action="store_true", help="Also fetch COLMAP sparse 3D points")
    run = sub.add_parser("triangulate")
    run.add_argument("calibration", type=Path); run.add_argument("--frame", type=int, required=True)
    run.add_argument("--landmarks", type=Path, required=True); run.add_argument("--output", type=Path, required=True)
    run.add_argument("--max-calibration-frame-gap", type=int, default=25,
                     help="Maximum distance to nearest calibration frame (default 25); -1 explicitly disables the limit")
    plane = sub.add_parser("fit-plane", help="Fit a diagnostic plane to selected triangulated landmarks")
    plane.add_argument("triangulation", type=Path)
    plane.add_argument("--points", nargs="+", required=True, help="At least three selected landmark names")
    plane.add_argument("--up", nargs=3, type=float, required=True, metavar=("X", "Y", "Z"))
    plane.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "fit-plane":
        source = json.loads(args.triangulation.read_text())
        if len(set(args.points)) != len(args.points):
            raise ValueError("Plane landmark names must be unique")
        coordinates = [source["landmarks"][name]["metric_world_xyz"] for name in args.points]
        normal, offset, singular, residuals = fit_plane(coordinates, args.up)
        result = {
            "triangulation_path": str(args.triangulation.resolve()),
            "triangulation_provenance": source,
            "selected_landmarks": args.points,
            "up_direction": args.up,
            "plane": {"normal": normal.tolist(), "offset": offset, "equation": "normal dot metric_world_xyz + offset = 0"},
            "singular_values": singular.tolist(),
            "signed_point_residuals_metres": dict(zip(args.points, residuals.tolist())),
            "warning": "Three noncollinear points define an exact plane and provide no independent validation; image marking, calibration and reconstruction errors remain. This is a diagnostic estimate, not an accepted source floor.",
        }
        args.output.write_text(json.dumps(result, indent=2, allow_nan=False) + "\n")
        return
    if args.command == "fetch":
        fetch(args.archive, args.sequence, args.output, args.include_points)
        return
    marks = json.loads(args.landmarks.read_text())
    if not isinstance(marks.get("uncertainty"), str) or not marks["uncertainty"].strip():
        raise ValueError("Supply a description of annotation uncertainty")
    landmarks = marks["landmarks"]
    if not landmarks:
        raise ValueError("No landmarks")
    names = sorted({name for obs in landmarks.values() for name in obs})
    if args.max_calibration_frame_gap < -1:
        raise ValueError("Maximum calibration gap must be nonnegative or -1 to disable")
    limit = None if args.max_calibration_frame_gap == -1 else args.max_calibration_frame_gap
    cameras = load_cameras(args.calibration, args.frame, names, limit)
    provenance = json.loads((args.calibration / "manifest.json").read_text())
    results = {}
    for name, observations in landmarks.items():
        point, residuals = triangulate(cameras, observations)
        results[name] = {"metric_world_xyz": point.tolist(), "views": residuals}
    meta = {name: {"image_member": f"{Path(provenance['archive']).stem}/{provenance['sequence']}/exo/{name}/images/{args.frame:05d}.jpg",
                   "calibration_image": f"{name}/{camera.calibration_frame:05d}.jpg", "image_size": camera.image_size,
                   "calibration_frame_gap": camera.calibration_frame_gap, "calibration_frame_range": camera.calibration_range}
            for name, camera in cameras.items()}
    output = {"calibration": provenance, "frame": args.frame, "input_landmarks": str(args.landmarks.resolve()),
              "uncertainty": marks["uncertainty"], "max_calibration_frame_gap": limit, "cameras": meta, "landmarks": results,
              "note": "Manual point triangulation; residuals are not confidence or evidence of sole contact. Metric frame uses COLMAP-to-metric scale.npy."}
    args.output.write_text(json.dumps(output, indent=2, allow_nan=False) + "\n")


if __name__ == "__main__":
    main()
