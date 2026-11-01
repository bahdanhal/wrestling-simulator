"""Fetch selected Harmony4D camera images through bounded HTTP byte ranges.

Example:
  python harmony_images.py test/03_grappling2.zip 043_grappling2 poses.npz \
      --from-index 176 --to-index 204 --indices 176 182 187 193 198 204 \
      --camera cam01 --take data/motion/takes/harmony4d.test-grappling2-043.json --output review

Pose indices address the NPZ's frames array, whose values name the original image frames.
"""
import argparse
import io
import json
import re
import zipfile
from pathlib import Path

import numpy as np
import requests

BASE = "https://huggingface.co/datasets/Jyun-Ting/Harmony4D/resolve/main/"
MAX_RANGE = 64 * 1024 * 1024
MAX_IMAGE = 20 * 1024 * 1024


class RangeReader(io.RawIOBase):
    """Seekable HTTPS reader which rejects whole-archive or oversized responses."""

    def __init__(self, url):
        self.url = url
        self.session = requests.Session()
        self.position = 0
        response = self.session.get(url, headers={"Range": "bytes=0-0", "Accept-Encoding": "identity"}, stream=True, timeout=60)
        try:
            match = re.fullmatch(r"bytes 0-0/(\d+)", response.headers.get("Content-Range", ""))
            if response.status_code != 206 or not match:
                raise ValueError("Server did not honour the initial byte range")
            self.size = int(match.group(1))
            if len(response.raw.read(2)) != 1:
                raise ValueError("Invalid initial byte-range response")
        finally:
            response.close()

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.position

    def seek(self, offset, whence=0):
        position = offset if whence == 0 else self.position + offset if whence == 1 else self.size + offset
        if position < 0:
            raise ValueError("Negative seek")
        self.position = position
        return position

    def read(self, size=-1):
        if size < 0:
            size = self.size - self.position
        size = min(size, max(0, self.size - self.position))
        if size > MAX_RANGE:
            raise ValueError(f"Refusing a {size}-byte range")
        if not size:
            return b""
        start = self.position
        end = start + size - 1
        response = self.session.get(self.url, headers={"Range": f"bytes={start}-{end}", "Accept-Encoding": "identity"}, stream=True, timeout=60)
        try:
            expected = f"bytes {start}-{end}/{self.size}"
            if response.status_code != 206 or response.headers.get("Content-Range") != expected:
                raise ValueError(f"Server did not honour byte range {start}-{end}")
            data = response.raw.read(size + 1)
            if len(data) != size:
                raise ValueError("Short or oversized byte-range response")
        finally:
            response.close()
        self.position += size
        return data

    def close(self):
        self.session.close()
        super().close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", help="Dataset-relative archive, e.g. test/03_grappling2.zip")
    parser.add_argument("sequence")
    parser.add_argument("npz", type=Path)
    parser.add_argument("--from-index", type=int, required=True)
    parser.add_argument("--to-index", type=int, required=True)
    parser.add_argument("--indices", type=int, nargs="+", help="Optional selected pose indices within the range")
    parser.add_argument("--camera", default="cam01")
    parser.add_argument("--take", type=Path, help="Take JSON containing source attribution")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists() and (not args.output.is_dir() or any(args.output.iterdir())):
        parser.error("output must be an empty directory so image evidence is not overwritten or mixed")
    if not re.fullmatch(r"(?:train|test)/[A-Za-z0-9_-]+\.zip", args.archive):
        parser.error("archive must be a train/ or test/ dataset-relative ZIP path")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", args.sequence) or not re.fullmatch(r"cam\d+", args.camera):
        parser.error("invalid sequence or camera")
    with np.load(args.npz, allow_pickle=False) as poses:
        numbers = poses["frames"]
        if numbers.ndim != 1 or numbers.dtype.kind not in "iu":
            raise ValueError("NPZ frames must be a one-dimensional integer array")
        numbers = numbers.copy()
    if args.from_index < 0 or args.to_index < args.from_index or args.to_index >= len(numbers):
        parser.error("invalid pose-index range")
    indices = args.indices if args.indices is not None else list(range(args.from_index, args.to_index + 1))
    if len(set(indices)) != len(indices) or any(i < args.from_index or i > args.to_index for i in indices):
        parser.error("selected indices must be unique and inside the range")
    source = json.loads(args.take.read_text())["source"] if args.take else {
        "title": "Harmony4D", "url": "https://huggingface.co/datasets/Jyun-Ting/Harmony4D", "license": "MIT",
        "notes": "Dataset attribution; see the dataset for terms and details."
    }
    url = BASE + args.archive
    args.output.mkdir(parents=True, exist_ok=True)
    entries = []
    with RangeReader(url) as remote, zipfile.ZipFile(remote) as archive:
        infos = archive.infolist()
        for index in indices:
            frame = int(numbers[index])
            suffix = f"/{args.sequence}/exo/{args.camera}/images/{frame:05d}.jpg"
            matches = [info for info in infos if ("/" + info.filename.lstrip("/")).endswith(suffix)]
            if len(matches) != 1:
                raise ValueError(f"Expected one image for pose {index}, frame {frame}; found {len(matches)}")
            info = matches[0]
            if info.file_size > MAX_IMAGE or info.compress_size > MAX_IMAGE:
                raise ValueError(f"Image too large: {info.filename}")
            data = archive.read(info)
            if len(data) != info.file_size or not data.startswith(b"\xff\xd8\xff"):
                raise ValueError(f"Invalid JPEG: {info.filename}")
            name = f"pose-{index:06d}-frame-{frame:06d}.jpg"
            (args.output / name).write_bytes(data)
            entries.append({"pose_index": index, "original_frame": frame, "archive_member": info.filename, "file": name})
    manifest = {"archive": args.archive, "archive_url": url, "sequence": args.sequence, "camera": args.camera,
                "npz": str(args.npz), "source": source, "images": entries}
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Fetched {len(entries)} images to {args.output}")


if __name__ == "__main__":
    main()
