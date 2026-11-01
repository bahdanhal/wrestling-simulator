"""Combine source camera images and Unity review frames at the recorded frame rate."""
import argparse
import json
import math
import shutil
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps

WIDTH, HEIGHT = 1920, 820
CAMERA_BOX = (0, 80, 640, 800)
REVIEW_BOX = (640, 80, 1920, 800)


def read_manifest(directory):
    return json.loads((directory / "manifest.json").read_text())


def safe_file(directory, name):
    if not name or Path(name).name != name:
        raise ValueError(f"Unsafe image filename: {name!r}")
    path = directory / name
    if not path.is_file():
        raise ValueError(f"Missing image: {path}")
    return path


def validate(camera_dir, review_dir):
    """Reject mismatched, incomplete or stale inputs before any rendering."""
    camera = read_manifest(camera_dir)
    review = read_manifest(review_dir)
    start, end = review["fromFrame"], review["toFrame"]
    fps = review["fps"]
    if not isinstance(start, int) or not isinstance(end, int) or start < 0 or end < start:
        raise ValueError("Invalid review frame range")
    if not isinstance(fps, (int, float)) or not math.isfinite(fps) or fps <= 0:
        raise ValueError("Invalid native fps")
    count = end - start + 1
    if review.get("encodedFrameCount") != count:
        raise ValueError("Review frame count does not match range")
    if not math.isclose(review["frameIntervalSeconds"], 1 / fps, rel_tol=1e-5):
        raise ValueError("Review timing does not match its fps")
    cam_source = {k.lower(): v for k, v in camera["source"].items()}
    review_source = {k.lower(): v for k, v in review["source"].items()}
    if any(cam_source.get(k) != review_source.get(k) for k in ("title", "url", "license")):
        raise ValueError("Camera and review source attribution does not match")
    images = {}
    for entry in camera["images"]:
        index = entry["pose_index"]
        if index in images:
            raise ValueError(f"Duplicate camera pose index: {index}")
        images[index] = entry
    expected = set(range(start, end + 1))
    if not expected.issubset(images):
        raise ValueError("Camera manifest does not cover the review range")
    frame_dir = review_dir / "frames"
    expected_names = {f"{i:06d}.png" for i in range(count)}
    actual_names = {p.name for p in frame_dir.iterdir() if p.is_file()}
    if actual_names != expected_names:
        raise ValueError("Unity frame directory contains missing or stale frames")
    for index in expected:
        entry = images[index]
        safe_file(camera_dir, entry["file"])
        original = entry["original_frame"]
        expected_member = f"/{camera['sequence']}/exo/{camera['camera']}/images/{original:05d}.jpg"
        if not ("/" + entry["archive_member"].lstrip("/")).endswith(expected_member):
            raise ValueError(f"Camera member does not match frame mapping for pose {index}")
    selected = review["contactSheetFrames"]
    if not selected or len(set(selected)) != len(selected) or any(i not in expected for i in selected):
        raise ValueError("Invalid contact sheet frame selection")
    return camera, review, images


def font(size):
    for path in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        if Path(path).exists():
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def paste_fit(canvas, path, box):
    with Image.open(path) as source:
        image = ImageOps.contain(source.convert("RGB"), (box[2] - box[0], box[3] - box[1]), Image.Resampling.LANCZOS)
        canvas.paste(image, (box[0] + (box[2] - box[0] - image.width) // 2,
                             box[1] + (box[3] - box[1] - image.height) // 2))


def assemble(camera_dir, review_dir, output):
    camera, review, images = validate(camera_dir, review_dir)
    if output.exists() and any(output.iterdir()):
        raise ValueError(f"Output directory is not empty: {output}")
    output.mkdir(parents=True, exist_ok=True)
    frames_dir = output / "frames"
    frames_dir.mkdir()
    start, end, fps = review["fromFrame"], review["toFrame"], review["fps"]
    selected = review["contactSheetFrames"]
    tile_width = 960
    tile_height = HEIGHT // 2
    sheet = Image.new("RGB", (tile_width * 2, tile_height * ((len(selected) + 1) // 2)), "#17191e")
    heading, details = font(23), font(18)
    for index in range(start, end + 1):
        entry = images[index]
        canvas = Image.new("RGB", (WIDTH, HEIGHT), "#17191e")
        draw = ImageDraw.Draw(canvas)
        draw.text((16, 8), f"CAMERA {camera['camera']} — ORIGINAL FRAME {entry['original_frame']}", fill="white", font=heading)
        draw.text((656, 8), "UNITY — SOURCE JOINTS / CHARACTER RIG", fill="white", font=heading)
        draw.text((16, 42), f"Pose {index}  |  {(index - start) / fps:.3f} s from start  |  {fps:g} fps", fill="#c9d3df", font=details)
        draw.text((656, 42), "Different viewpoint; no pixel alignment implied", fill="#c9d3df", font=details)
        paste_fit(canvas, camera_dir / entry["file"], CAMERA_BOX)
        paste_fit(canvas, review_dir / "frames" / f"{index - start:06d}.png", REVIEW_BOX)
        canvas.save(frames_dir / f"{index - start:06d}.png")
        if index in selected:
            position = selected.index(index)
            sheet.paste(canvas.resize((tile_width, tile_height), Image.Resampling.LANCZOS),
                        ((position % 2) * tile_width, (position // 2) * tile_height))
    sheet.save(output / "contact-sheet.png")
    ffmpeg = shutil.which("ffmpeg")
    video = None
    if ffmpeg:
        video = "native-rate.mp4"
        subprocess.run([ffmpeg, "-y", "-loglevel", "error", "-framerate", str(fps), "-i", str(frames_dir / "%06d.png"),
                        "-frames:v", str(end - start + 1), "-c:v", "libx264", "-pix_fmt", "yuv420p", "-crf", "20", str(output / video)], check=True)
    manifest = {
        "camera_manifest": str((camera_dir / "manifest.json").resolve()),
        "review_manifest": str((review_dir / "manifest.json").resolve()),
        "camera_provenance": camera,
        "review_provenance": review,
        "fps": fps, "from_pose_index": start, "to_pose_index": end,
        "contact_sheet_pose_indices": selected, "video": video,
        "notes": "Camera images and Unity frames are resized preserving aspect ratio and padded; no crop or image reconstruction. Camera view differs from the aligned model side view; no pixel alignment is implied.",
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--camera", type=Path, required=True)
    parser.add_argument("--review", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    assemble(args.camera, args.review, args.output)
    print(f"Wrote comparison to {args.output}")


if __name__ == "__main__":
    main()
