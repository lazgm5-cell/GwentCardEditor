import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import media_smoke


class MediaCheckTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="media-check-guards-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.client = self.root / "client"
        self.assets = self.client / "Gwent_Data/StreamingAssets"
        self.assets.mkdir(parents=True)
        (self.client / "Gwent.exe").write_bytes(b"never executed")
        (self.assets / "data_definitions").write_bytes(b"unsupported archive")
        self.before = media_smoke.snapshot(self.client)
        self.report = self.root / "report.json"

    def test_report_inside_client_rejected(self):
        with self.assertRaisesRegex(ValueError, "MediaTestReport"):
            media_smoke.run(self.client, "unused", self.client / "report.json")
        self.assertEqual(self.before, media_smoke.snapshot(self.client))

    def test_existing_report_preserved(self):
        self.report.write_bytes(b"foreign report")
        with self.assertRaisesRegex(ValueError, "MediaTestReport"):
            media_smoke.run(self.client, "unused", self.report)
        self.assertEqual(self.report.read_bytes(), b"foreign report")

    def test_temp_inside_client_rejected(self):
        with patch("media_smoke.tempfile.gettempdir", return_value=str(self.client)):
            with self.assertRaisesRegex(ValueError, "MediaTestReport"):
                media_smoke.run(self.client, "unused", self.report)
        self.assertEqual(self.before, media_smoke.snapshot(self.client))

    def test_low_disk_reports_failure_without_changes(self):
        class Disk:
            free = 0
        with patch("media_smoke.shutil.disk_usage", return_value=Disk()):
            result = media_smoke.run(self.client, "unused", self.report)
        self.assertFalse(result["Passed"])
        self.assertIn("MediaTestSpace", result["Error"])
        self.assertFalse(result["InstalledIntoGame"])
        self.assertEqual(self.before, media_smoke.snapshot(self.client))

    def test_unsupported_source_reports_failure(self):
        result = media_smoke.run(self.client, "unused", self.report)
        self.assertFalse(result["Passed"])
        self.assertEqual(result, json.loads(self.report.read_text(encoding="utf-8")))
        self.assertEqual(self.before, media_smoke.snapshot(self.client))


if __name__ == "__main__":
    unittest.main()
