import argparse
import copy
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zlib

import art_swap


class ArtSwapTests(unittest.TestCase):
    package = None

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="art-swap-tests-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.sa = self.root / "Gwent_Data/StreamingAssets"
        self.sa.mkdir(parents=True)
        self.manifest = art_swap.verify(self.package)
        for entry in self.manifest["Files"]:
            dest = self.sa / entry["Path"]
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.package / "backup" / entry["Path"], dest)
        shutil.copyfile(Path(self.manifest["TargetClient"]) / "data_definitions", self.sa / "data_definitions")
        (self.sa / "unrelated.bin").write_bytes(b"preserve me")
        self.process = patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'"notepad.exe"', b""))
        self.process.start()
        self.addCleanup(self.process.stop)

    def snapshot(self):
        return {str(p.relative_to(self.sa)): art_swap.digest(p.read_bytes()) for p in self.sa.rglob("*") if p.is_file()}

    def copied_package(self):
        target = self.root / "package"
        shutil.copytree(self.package, target)
        return target

    def test_install_restore_exact(self):
        before = self.snapshot()
        self.assertTrue(art_swap.transfer(self.package, self.root)["Completed"])
        for entry in self.manifest["Files"]:
            self.assertEqual(art_swap.digest((self.sa / entry["Path"]).read_bytes()), entry["After"])
        self.assertEqual((self.sa / "unrelated.bin").read_bytes(), b"preserve me")
        self.assertTrue(art_swap.transfer(self.package, self.root, restore=True)["Restored"])
        self.assertEqual(before, self.snapshot())

    def test_existing_changes_not_overwritten(self):
        path = self.sa / "bundledassets.json"
        path.write_bytes(path.read_bytes() + b" ")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "Client changed"):
            art_swap.transfer(self.package, self.root)
        self.assertEqual(before, self.snapshot())

    def test_repeat_restore_is_noop(self):
        art_swap.transfer(self.package, self.root)
        art_swap.transfer(self.package, self.root, restore=True)
        before = self.snapshot()
        with patch("art_swap.os.replace", side_effect=AssertionError("No writes allowed")), patch("art_swap.shutil.copyfile", side_effect=AssertionError("No writes allowed")):
            result = art_swap.transfer(self.package, self.root, restore=True)
        self.assertTrue(result["AlreadyRestored"])
        self.assertEqual(result["Files"], 0)
        self.assertEqual(before, self.snapshot())

    def test_already_restored_after_data_change_is_noop(self):
        (self.sa / "data_definitions").write_bytes(b"later card changes")
        before = self.snapshot()
        result = art_swap.transfer(self.package, self.root, restore=True)
        self.assertTrue(result["AlreadyRestored"])
        self.assertEqual(before, self.snapshot())

    def test_partial_restore_is_not_treated_as_noop(self):
        art_swap.transfer(self.package, self.root)
        record = next(r for r in self.manifest["Files"] if r["Before"] != r["After"])
        shutil.copyfile(self.package / "backup" / record["Path"], self.sa / record["Path"])
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "Client changed"):
            art_swap.transfer(self.package, self.root, restore=True)
        self.assertEqual(before, self.snapshot())

    def test_restore_conflict_not_overwritten(self):
        art_swap.transfer(self.package, self.root)
        path = self.sa / self.manifest["Files"][0]["Path"]
        path.write_bytes(path.read_bytes() + b"unrelated change")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "Client changed"):
            art_swap.transfer(self.package, self.root, restore=True)
        self.assertEqual(before, self.snapshot())

    def test_changed_card_archive_blocks_install(self):
        (self.sa / "data_definitions").write_bytes(b"changed")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "card data differs"):
            art_swap.transfer(self.package, self.root)
        self.assertEqual(before, self.snapshot())

    def test_corrupt_package_blocks_install(self):
        package = self.copied_package()
        path = package / "payload" / self.manifest["Files"][0]["Path"]
        path.write_bytes(b"corrupt")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "checksum"):
            art_swap.transfer(package, self.root)
        self.assertEqual(before, self.snapshot())

    def test_duplicate_or_escaping_paths_rejected(self):
        package = self.copied_package()
        for bad in ("../escape", self.manifest["Files"][1]["Path"]):
            manifest = copy.deepcopy(self.manifest)
            manifest["Files"][0]["Path"] = bad
            art_swap.write_json(package / "manifest.json", manifest)
            with self.assertRaisesRegex(ValueError, "Unexpected package"):
                art_swap.verify(package)
        with self.assertRaisesRegex(ValueError, "escapes"):
            art_swap.safe_path(self.root, "../outside")

    def test_running_game_blocks_install(self):
        before = self.snapshot()
        with patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'"Gwent.exe","123"', b"")):
            with self.assertRaisesRegex(ValueError, "Close Gwent"):
                art_swap.transfer(self.package, self.root)
        self.assertEqual(before, self.snapshot())

    def test_localized_process_output_is_not_decoded(self):
        before = self.snapshot()
        with patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'\x88\xad\xe4\xae: no matching process\r\n', b"")) as check:
            art_swap.transfer(self.package, self.root)
            self.assertNotIn("text", check.call_args.kwargs)
            art_swap.transfer(self.package, self.root, restore=True)
        self.assertEqual(before, self.snapshot())

    def test_empty_process_output_blocks_install(self):
        before = self.snapshot()
        with patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b"", b"")):
            with self.assertRaisesRegex(ValueError, "ArtProcessCheckFailed"):
                art_swap.transfer(self.package, self.root)
        self.assertEqual(before, self.snapshot())

    def test_mid_install_failure_rolls_back(self):
        before = self.snapshot()
        replace = art_swap.os.replace
        calls = 0
        def fail_second(source, target):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise OSError("simulated failure")
            return replace(source, target)
        with patch("art_swap.os.replace", side_effect=fail_second):
            with self.assertRaisesRegex(OSError, "simulated failure"):
                art_swap.transfer(self.package, self.root)
        self.assertEqual(before, self.snapshot())

    def test_payload_matches_catalog_and_pixels(self):
        import UnityPy
        catalog = json.loads((self.package / "payload/bundledassets.json").read_text(encoding="utf-8"))
        name = str(int(self.manifest["TargetArtId"]) * 10000)
        for quality in self.manifest["Checks"]:
            path = "bundledassets/cardassets/textures/standard/" + quality["Quality"]
            if quality["Quality"] != "low":
                path += "/" + name
            raw = (self.package / "payload" / path).read_bytes()
            entry = next(e for e in catalog["Infos"] if e["Path"] == path)
            self.assertEqual(entry["Size"], len(raw))
            self.assertEqual(entry["FileCrc"], zlib.crc32(raw) & 0xffffffff)
            self.assertEqual(entry["CRC"], zlib.crc32(art_swap.bundle_body(raw)) & 0xffffffff)
            _, image = art_swap.texture(UnityPy.load(raw), name)
            self.assertEqual(art_swap.digest(image.image.convert("RGBA").tobytes()), quality["Pixels"])

    def test_normalize_formats_orientation_and_metadata(self):
        from PIL import Image
        for fmt, extension in (("PNG", "png"), ("JPEG", "jpg"), ("WEBP", "webp")):
            path = self.root / ("input." + extension)
            image = Image.new("RGB", (40, 20), "red")
            exif = Image.Exif(); exif[274] = 6
            image.save(path, format=fmt, exif=exif)
            output = self.root / (extension + ".png")
            result = art_swap.normalize_image(path, output)
            self.assertEqual((result["Width"], result["Height"]), (20, 40))
            with Image.open(output) as checked:
                self.assertFalse(checked.getexif())
        path = self.root / "unsupported.gif"
        Image.new("RGB", (40,40)).save(path)
        with self.assertRaisesRegex(ValueError, "ArtImageUnsupported"):
            art_swap.load_image(path)

    def test_arbitrary_image_build_and_padding(self):
        import UnityPy
        from PIL import Image
        image_path = self.root / "custom.png"
        Image.new("RGBA", (128,178), (255,0,0,255)).save(image_path)
        output = self.root / "custom-package"
        before = self.snapshot()
        manifest = art_swap.build(None, None, self.root, self.manifest["TargetCard"], output, image_path)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(manifest["SourceImageHash"], art_swap.digest(image_path.read_bytes()))
        self.assertEqual(len(manifest["Checks"]), 4)
        raw = (output / "payload/bundledassets/cardassets/textures/standard/low").read_bytes()
        _, value = art_swap.texture(UnityPy.load(raw), str(int(self.manifest["TargetArtId"]) * 10000))
        pixels = value.image.convert("RGBA")
        self.assertEqual(pixels.getpixel((64,177)), (255,0,0,255))
        self.assertEqual(pixels.getpixel((64,178)), (0,0,0,255))
        art_swap.transfer(output, self.root)
        art_swap.transfer(output, self.root, restore=True)
        self.assertEqual(self.snapshot(), before)

    def test_image_rejections(self):
        from PIL import Image
        path = self.root / "small.png"
        Image.new("RGB", (8,8)).save(path)
        with self.assertRaisesRegex(ValueError, "ArtImageTooLarge"):
            art_swap.load_image(path)
        path = self.root / "animated.webp"
        Image.new("RGB", (32,32), "red").save(path, save_all=True, append_images=[Image.new("RGB", (32,32), "blue")])
        with self.assertRaisesRegex(ValueError, "ArtImageUnsupported"):
            art_swap.load_image(path)
        path = self.root / "square.png"
        Image.new("RGB", (64,64)).save(path)
        with self.assertRaisesRegex(ValueError, "ArtCropRatio"):
            art_swap.build(None, None, self.root, self.manifest["TargetCard"], self.root / "bad-package", path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--dependencies", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    sys.path.insert(0, str(args.dependencies))
    ArtSwapTests.package = args.package.resolve()
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(ArtSwapTests))
    art_swap.write_json(args.report, dict(Passed=result.wasSuccessful(), Tests=result.testsRun,
                                         Failures=len(result.failures), Errors=len(result.errors)))
    sys.exit(0 if result.wasSuccessful() else 1)
