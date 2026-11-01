"""Offline checks for bounded, byte-accurate ZIP range reads."""
import io
import sys
import tempfile
import types
import unittest
from contextlib import redirect_stderr
from pathlib import Path
from unittest.mock import patch


# RangeReader does not use NumPy, but the command-line module imports it at
# module load time. Keep these focused tests runnable in a minimal Python env.
try:
    import numpy  # noqa: F401
except ImportError:
    sys.modules["numpy"] = types.ModuleType("numpy")

import harmony_images


class FakeResponse:
    def __init__(self, status, content_range, body):
        self.status_code = status
        self.headers = {"Content-Range": content_range} if content_range else {}
        self.raw = io.BytesIO(body)
        self.closed = False

    def close(self):
        self.closed = True


class FakeSession:
    def __init__(self, responses):
        self.responses = iter(responses)
        self.calls = []
        self.closed = False

    def get(self, url, **kwargs):
        self.calls.append((url, kwargs))
        return next(self.responses)

    def close(self):
        self.closed = True


class RangeReaderTests(unittest.TestCase):
    def test_rejects_existing_evidence_before_opening_inputs_or_network(self):
        with tempfile.TemporaryDirectory() as folder:
            evidence = Path(folder) / "manifest.json"
            evidence.write_text("original evidence")
            args = ["harmony_images.py", "test/03_grappling2.zip", "043_grappling2", "missing.npz",
                    "--from-index", "176", "--to-index", "204", "--output", folder]
            with patch.object(sys, "argv", args), patch.object(harmony_images, "RangeReader") as reader:
                with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                    harmony_images.main()
                reader.assert_not_called()
            self.assertEqual(evidence.read_text(), "original evidence")

    def reader_with(self, data, *read_responses):
        initial = FakeResponse(206, f"bytes 0-0/{len(data)}", data[:1])
        session = FakeSession([initial, *read_responses])
        with patch.object(harmony_images.requests, "Session", return_value=session):
            reader = harmony_images.RangeReader("https://example.test/archive.zip")
        return reader, session

    def test_rejects_server_returning_entire_archive_with_http_200(self):
        body = b"PK\x03\x04whole archive"
        response = FakeResponse(200, None, body)
        session = FakeSession([response])
        with patch.object(harmony_images.requests, "Session", return_value=session):
            with self.assertRaisesRegex(ValueError, "initial byte range"):
                harmony_images.RangeReader("https://example.test/archive.zip")
        self.assertTrue(response.closed)
        self.assertEqual(response.raw.tell(), 0, "a full response must be rejected without consuming its ZIP body")
        self.assertEqual(session.calls[0][1]["headers"]["Range"], "bytes=0-0")

    def test_rejects_mismatched_content_range_without_returning_bytes(self):
        reader, session = self.reader_with(
            b"abcdefgh",
            FakeResponse(206, "bytes 2-4/8", b"cde"),
        )
        with self.assertRaisesRegex(ValueError, "did not honour byte range"):
            reader.seek(3)
            reader.read(2)
        self.assertEqual(session.calls[-1][1]["headers"]["Range"], "bytes=3-4")
        reader.close()

    def test_rejects_truncated_and_inflated_responses(self):
        for label, response in (
            ("truncated", FakeResponse(206, "bytes 2-4/8", b"cd")),
            ("inflated", FakeResponse(206, "bytes 2-4/8", b"cdeX")),
        ):
            with self.subTest(label=label):
                reader, _ = self.reader_with(b"abcdefgh", response)
                reader.seek(2)
                with self.assertRaisesRegex(ValueError, "Short or oversized"):
                    reader.read(3)
                self.assertEqual(reader.tell(), 2, "failed reads must not advance the logical position")
                reader.close()

    def test_seek_and_read_return_only_the_requested_archive_bytes(self):
        archive = b"0123456789abcdef"
        reader, session = self.reader_with(
            archive,
            FakeResponse(206, "bytes 5-9/16", archive[5:10]),
            FakeResponse(206, "bytes 12-15/16", archive[12:16]),
        )
        self.assertEqual(reader.seek(5), 5)
        self.assertEqual(reader.read(5), archive[5:10])
        self.assertEqual(reader.tell(), 10)
        self.assertEqual(reader.seek(-4, 2), 12)
        self.assertEqual(reader.read(), archive[12:16])
        self.assertEqual(reader.tell(), 16)
        self.assertEqual(
            [call[1]["headers"]["Range"] for call in session.calls],
            ["bytes=0-0", "bytes=5-9", "bytes=12-15"],
        )
        self.assertTrue(all(call[1]["headers"]["Accept-Encoding"] == "identity" for call in session.calls))
        reader.close()
        self.assertTrue(session.closed)


if __name__ == "__main__":
    unittest.main()
