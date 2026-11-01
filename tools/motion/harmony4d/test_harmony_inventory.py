import json
import os
import tempfile
import unittest

import harmony_inventory as hi


ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../.."))


class HarmonyInventoryTests(unittest.TestCase):
    def test_repository_inventory_audits_local_takes_and_review_windows(self):
        report = hi.inventory(ROOT)
        self.assertEqual(74, len(report["rows"]))
        self.assertEqual(3, sum(row["reviewed"] for row in report["rows"]))
        self.assertEqual(0, sum(row["assigned"] for row in report["rows"]))
        self.assertEqual(["yes"] * 3, ["yes" if x["take_exists"] and x["pose_source_matches_take_title"] and x["frame_range_valid"] else "no" for x in report["provenance"]])
        takes = {take["id"]: take for take in report["takes"]}
        self.assertEqual((601, 30.0, 30.05), (takes["harmony4d.train-grappling-001"]["frames"], takes["harmony4d.train-grappling-001"]["endpoint_seconds"], takes["harmony4d.train-grappling-001"]["hold_last_seconds"]))
        self.assertIn("not accepted phase matches", hi.render(report))

    def test_event_cues_are_counted_as_unvalidated_hints(self):
        with tempfile.NamedTemporaryFile(mode="w", suffix=".json", encoding="utf-8") as stream:
            json.dump({"train/02_grappling/001_grappling": {"events": [
                {"kind": "shot", "actor": 0, "from": 4, "to": 10},
                {"kind": "snap_down", "actor": 1, "from": 20, "to": 29},
            ]}}, stream)
            stream.flush()
            report = hi.inventory(ROOT, [stream.name])
        penetration = next(row for row in report["rows"] if row["clip"] == "anim.double_leg_blast.penetration")
        snap = next(row for row in report["rows"] if row["clip"] == "anim.snap_down_go_behind.snap")
        self.assertEqual(2, report["candidate_event_count"])
        self.assertEqual(1, penetration["candidate_cues"])
        self.assertEqual(1, snap["candidate_cues"])
        self.assertIn("not validated", hi.render(report))

    def test_segment_timing_uses_referenced_take_fps(self):
        takes = {"take-25": {"framesPerSecond": 25}}
        segment = {"take": "take-25", "fromFrame": 10, "toFrame": 35}
        self.assertEqual(1.0, hi.segment_seconds(segment, takes))
        with self.assertRaisesRegex(ValueError, "missing take"):
            hi.segment_seconds({**segment, "take": "missing"}, takes)
        with self.assertRaisesRegex(ValueError, "invalid framesPerSecond"):
            hi.segment_seconds(segment, {"take-25": {"framesPerSecond": 0}})

    def test_malformed_segment_provenance_is_reported_as_unmatched(self):
        takes = {"take": {"source": {"title": "Harmony4D train/02_grappling.zip, sequence 001_grappling"},
                          "frames": [[], [], []]}}
        malformed = {"clip": "clip", "take": "take", "poses": "test/elsewhere/wrong",
                     "fromFrame": 4, "toFrame": 8}
        audit = hi.audit_provenance(malformed, takes)
        self.assertTrue(audit["take_exists"])
        self.assertFalse(audit["pose_source_matches_take_title"])
        self.assertFalse(audit["frame_range_valid"])


if __name__ == "__main__":
    unittest.main()
