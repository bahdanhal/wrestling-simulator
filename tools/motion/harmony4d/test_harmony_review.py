"""Offline checks for mismatched provenance, image mappings and stale Unity frames."""
import json
import tempfile
import unittest
from pathlib import Path

from harmony_review import validate


class ValidationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.camera = self.root / "camera"
        self.review = self.root / "review"
        self.camera.mkdir()
        (self.review / "frames").mkdir(parents=True)
        self.source = {"title": "source", "url": "https://example.org", "license": "MIT"}
        self.camera_manifest = {
            "sequence": "seq", "camera": "cam01", "source": self.source,
            "images": [{"pose_index": 4, "original_frame": 10,
                        "archive_member": "archive/seq/exo/cam01/images/00010.jpg", "file": "photo.jpg"}],
        }
        self.review_manifest = {
            "fromFrame": 4, "toFrame": 4, "fps": 20, "encodedFrameCount": 1,
            "frameIntervalSeconds": 0.05, "contactSheetFrames": [4], "source": self.source,
        }
        (self.camera / "photo.jpg").touch()
        (self.review / "frames" / "000000.png").touch()
        self.write_manifests()

    def tearDown(self):
        self.temp.cleanup()

    def write_manifests(self):
        (self.camera / "manifest.json").write_text(json.dumps(self.camera_manifest))
        (self.review / "manifest.json").write_text(json.dumps(self.review_manifest))

    def test_rejects_mismatched_source_or_mapping(self):
        self.camera_manifest["source"] = dict(self.source, title="different")
        self.write_manifests()
        with self.assertRaisesRegex(ValueError, "source attribution"):
            validate(self.camera, self.review)
        self.camera_manifest["source"] = self.source
        self.camera_manifest["images"][0]["original_frame"] = 11
        self.write_manifests()
        with self.assertRaisesRegex(ValueError, "frame mapping"):
            validate(self.camera, self.review)

    def test_rejects_stale_frame(self):
        (self.review / "frames" / "000001.png").touch()
        with self.assertRaisesRegex(ValueError, "stale"):
            validate(self.camera, self.review)


if __name__ == "__main__":
    unittest.main()
