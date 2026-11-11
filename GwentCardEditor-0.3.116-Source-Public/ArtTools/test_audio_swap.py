import argparse
import copy
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile
import art_swap
import audio_swap


class AudioTests(unittest.TestCase):
    package = None

    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="card-audio-tests-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.sa = self.root / "Gwent_Data/StreamingAssets"
        self.sa.mkdir(parents=True)
        self.manifest = art_swap.verify(self.package)
        shutil.copyfile(self.package / "backup/data_definitions", self.sa / "data_definitions")
        for relative in self.manifest["ProtectedAudioMetadata"]:
            dest = self.sa / relative
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(Path(self.manifest["TargetClient"]) / relative, dest)
        process = patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'"notepad.exe"', b""))
        process.start(); self.addCleanup(process.stop)

    def test_install_restore_exact(self):
        before = (self.sa / "data_definitions").read_bytes()
        art_swap.transfer(self.package, self.root)
        self.assertEqual(art_swap.digest((self.sa / "data_definitions").read_bytes()), self.manifest["Files"][0]["After"])
        art_swap.transfer(self.package, self.root, restore=True)
        self.assertEqual(before, (self.sa / "data_definitions").read_bytes())

    def test_only_audio_reference_and_templates_entry_change(self):
        with zipfile.ZipFile(self.package / "backup/data_definitions") as old, zipfile.ZipFile(self.package / "payload/data_definitions") as new:
            self.assertEqual(old.namelist(), new.namelist())
            for name in old.namelist():
                if name != "Templates.xml":
                    self.assertEqual(old.read(name), new.read(name), name)
            before = ET.fromstring(old.read("Templates.xml"))
            expected = copy.deepcopy(before)
            audio_swap.card_template(expected, self.manifest["TargetCard"]).set("AudioId", self.manifest["AfterAudio"]["AudioId"])
            self.assertEqual(ET.tostring(expected), ET.tostring(ET.fromstring(new.read("Templates.xml"))))
            changed = [c.get("Id") for c, n in zip(before, ET.fromstring(new.read("Templates.xml"))) if ET.tostring(c) != ET.tostring(n)]
            self.assertEqual(changed, [str(self.manifest["TargetCard"])])

    def test_changed_data_blocks_install(self):
        path = self.sa / "data_definitions"
        path.write_bytes(path.read_bytes() + b"later edit")
        before = path.read_bytes()
        with self.assertRaisesRegex(ValueError, "Client changed"):
            art_swap.transfer(self.package, self.root)
        self.assertEqual(before, path.read_bytes())

    def test_changed_audio_metadata_blocks_install(self):
        path = self.sa / "audio.json"
        path.write_bytes(path.read_bytes() + b" ")
        before = (self.sa / "data_definitions").read_bytes()
        with self.assertRaisesRegex(ValueError, "audio metadata"):
            art_swap.transfer(self.package, self.root)
        self.assertEqual(before, (self.sa / "data_definitions").read_bytes())

    def test_changed_data_blocks_restore(self):
        art_swap.transfer(self.package, self.root)
        path = self.sa / "data_definitions"
        path.write_bytes(path.read_bytes() + b"later edit")
        before = path.read_bytes()
        with self.assertRaisesRegex(ValueError, "Client changed"):
            art_swap.transfer(self.package, self.root, restore=True)
        self.assertEqual(before, path.read_bytes())

    def test_same_audio_noop_blocked(self):
        with self.assertRaisesRegex(ValueError, "AudioSameSource"):
            audio_swap.build(self.root, self.manifest["TargetCard"], self.manifest["TargetCard"], self.root / "noop")
        self.assertFalse((self.root / "noop").exists())

    def test_unsafe_package_paths_rejected(self):
        package = self.root / "modified"
        shutil.copytree(self.package, package)
        for part in ("Files", "ProtectedAudioMetadata"):
            manifest = copy.deepcopy(self.manifest)
            if part == "Files": manifest["Files"][0]["Path"] = "../data_definitions"
            else: manifest["ProtectedAudioMetadata"] = {"../other.json": "0"}
            art_swap.write_json(package / "manifest.json", manifest)
            with self.assertRaisesRegex(ValueError, "Unexpected"):
                art_swap.verify(package)

    def test_running_game_blocks_install(self):
        with patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'"Gwent.exe"', b"")):
            with self.assertRaisesRegex(ValueError, "Close Gwent"):
                art_swap.transfer(self.package, self.root)

    def test_catalog_and_descriptions(self):
        catalog = audio_swap.catalog(self.root)
        self.assertIn(self.manifest["DonorCard"], catalog["Cards"])
        self.assertEqual(audio_swap.describe(self.root, self.manifest["TargetCard"]), self.manifest["BeforeAudio"])
        with self.assertRaisesRegex(ValueError, "AudioUnsupported"):
            audio_swap.describe(self.root, 99999999)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    AudioTests.package = args.package.resolve()
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(AudioTests))
    art_swap.write_json(args.report, dict(Passed=result.wasSuccessful(), Tests=result.testsRun, Failures=len(result.failures), Errors=len(result.errors)))
    raise SystemExit(not result.wasSuccessful())
