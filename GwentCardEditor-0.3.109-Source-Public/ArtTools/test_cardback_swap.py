"""Read-only real-client builds and isolated installation/rollback checks."""
import copy
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

import art_swap as api
import cardback_swap as backs
from PIL import Image, ImageDraw
import UnityPy


class CardbackTests(unittest.TestCase):
    client = None
    output = None

    @classmethod
    def setUpClass(cls):
        cls.output.mkdir(parents=True, exist_ok=True)
        cls.source = cls.output / "sample.png"
        image = Image.new("RGB", (600, 900), "#236855")
        draw = ImageDraw.Draw(image)
        draw.rectangle((0, 0, 599, 100), fill="#df4a5c")
        draw.rectangle((0, 800, 599, 899), fill="#327bd1")
        draw.line((0, 0, 599, 899), fill="white", width=15)
        image.save(cls.source)
        cls.target = api.streaming_assets(cls.client)
        inputs = set(p for n in backs.IDS for p in backs.paths(n))
        inputs.update(("data_definitions", "bundledassets.json", "bundledassets/dependencies/shared/global"))
        cls.original_hashes = {p: api.digest((cls.target / p).read_bytes()) for p in inputs}
        cls.catalog = backs.catalog(cls.client)
        cls.packages = []
        for number in backs.IDS:
            package = cls.output / str(number)
            if not package.exists():
                backs.build(cls.client, number, cls.source, package)
            cls.packages.append(package)

    def test_catalog_and_whitelist(self):
        self.assertEqual([r["Id"] for r in self.catalog["Cardbacks"]], list(backs.IDS))
        for bad in (0, True, "25000400", 25220402):
            with self.assertRaisesRegex(ValueError, "CardbackUnsupported"):
                backs.paths(bad)

    def test_client_unchanged(self):
        self.assertEqual(self.original_hashes, {p: api.digest((self.target / p).read_bytes()) for p in self.original_hashes})

    def test_pixels_padding_and_unrelated_objects(self):
        for package in self.packages:
            manifest = api.verify(package)
            number = manifest["TargetCardback"]
            self.assertEqual(len(manifest["Files"]), 5)
            for row in manifest["Files"][:-1]:
                before = UnityPy.load(str(package / "backup" / row["Path"]))
                after = UnityPy.load(str(package / "payload" / row["Path"]))
                obj, old = api.texture(before, str(number))
                _, new = api.texture(after, str(number))
                a, b = old.image.convert("RGBA"), new.image.convert("RGBA")
                region = (0, a.width * 3 // 2, a.width, a.height)
                self.assertEqual(a.crop(region).tobytes(), b.crop(region).tobytes())
                self.assertNotEqual(a.tobytes(), b.tobytes())
                self.assertGreater(b.getpixel((b.width // 2, 5))[0], 150)
                self.assertGreater(b.getpixel((b.width // 2, b.width * 3 // 2 - 5))[2], 150)
                hashes, result = api.object_hashes(before), api.object_hashes(after)
                key = (obj.assets_file.name, obj.path_id)
                self.assertEqual(hashes.keys(), result.keys())
                self.assertTrue(all(hashes[k] == result[k] for k in hashes if k != key))

    def test_install_restore_conflict_and_integrity(self):
        package = self.packages[0]
        manifest = api.verify(package)
        with tempfile.TemporaryDirectory(prefix="cardback-transfer-") as tmp:
            root = Path(tmp)
            target = root / "Gwent_Data/StreamingAssets"
            target.mkdir(parents=True)
            for row in manifest["Files"]:
                dest = target / row["Path"]
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(package / "backup" / row["Path"], dest)
            for path in ("data_definitions", "bundledassets/dependencies/shared/global"):
                dest = target / path
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(self.target / path, dest)
            def snapshot():
                return {str(p.relative_to(target)): api.digest(p.read_bytes()) for p in target.rglob("*") if p.is_file()}
            original = snapshot()
            with patch.object(api, "check_game_closed"):
                api.transfer(package, root)
                changed = snapshot()
                self.assertEqual(sum(original[k] != changed[k] for k in original), 5)
                api.transfer(package, root, restore=True)
                self.assertEqual(original, snapshot())
                self.assertTrue(api.transfer(package, root, restore=True)["AlreadyRestored"])
                (target / "bundledassets.json").write_bytes(b"foreign change")
                conflict = snapshot()
                with self.assertRaisesRegex(ValueError, "Client changed"):
                    api.transfer(package, root)
                self.assertEqual(conflict, snapshot())
            copied = root / "tampered"
            shutil.copytree(package, copied)
            altered = copy.deepcopy(manifest)
            altered["TargetCardback"] = 25220402
            api.write_json(copied / "manifest.json", altered)
            with self.assertRaisesRegex(ValueError, "CardbackUnsupported"):
                api.verify(copied)
            altered = copy.deepcopy(manifest)
            altered["Files"][0]["Path"] = "../outside"
            api.write_json(copied / "manifest.json", altered)
            with self.assertRaisesRegex(ValueError, "Unexpected package file paths"):
                api.verify(copied)
            api.write_json(copied / "manifest.json", manifest)
            (copied / "payload" / manifest["Files"][0]["Path"]).write_bytes(b"broken")
            with self.assertRaisesRegex(ValueError, "checksum mismatch"):
                api.verify(copied)

    def test_invalid_crop(self):
        bad = self.output / "wrong-ratio.png"
        Image.new("RGB", (100, 100)).save(bad)
        with self.assertRaisesRegex(ValueError, "CardbackCropRatio"):
            backs.build(self.client, backs.IDS[0], bad, self.output / "rejected")
        self.assertFalse((self.output / "rejected").exists())


if __name__ == "__main__":
    CardbackTests.client = Path(sys.argv[1])
    CardbackTests.output = Path(sys.argv[2])
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(CardbackTests))
    api.write_json(CardbackTests.output / "tests.json", dict(Passed=result.wasSuccessful(), Tests=result.testsRun, Packages=7, ClientUnchanged=True))
    sys.exit(0 if result.wasSuccessful() else 1)
