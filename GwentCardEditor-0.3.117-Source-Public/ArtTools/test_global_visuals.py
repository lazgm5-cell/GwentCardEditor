import argparse
import json
from pathlib import Path
import unittest
import shutil
import tempfile
from unittest.mock import patch
from types import SimpleNamespace
from PIL import Image
import UnityPy
import art_swap
import global_visuals
from test_art_swap import ArtSwapTests


class GlobalVisualTests(unittest.TestCase):
    package = None

    def test_correct_bronze_texture_source(self):
        env = object()
        image = Image.new("RGBA", (1024, 2048), (120, 80, 40, 255))
        with patch("global_visuals.UnityPy.load", return_value=env), patch("global_visuals.texture", return_value=(None, SimpleNamespace(image=image))) as texture:
            result = global_visuals.rogue_frame_image(b"source", "bronze")
        texture.assert_called_once_with(env, "Standard_CardBorder")
        self.assertEqual(result.tobytes(), image.tobytes())
        manifest = json.loads((global_visuals.FRAME_ASSETS / "manifest.json").read_text())
        self.assertTrue(manifest["Frames"]["bronze"]["Source"].endswith("/20820480"))
        self.assertEqual(manifest["Frames"]["bronze"]["Pixels"], "3334d00657258453d01170f548ab18bc091166f1b120a9de64cd680e824ff949")

    def test_fiery_static_composition_order_and_layout(self):
        left = Image.new("RGBA", (1024, 2048), (200, 0, 0, 200))
        right = Image.new("RGBA", (1024, 2048), (0, 0, 200, 100))
        def texture(_, name):
            return None, SimpleNamespace(image=left if name == "left" else right)
        with patch("global_visuals.UnityPy.load", return_value=object()), patch("global_visuals.texture", side_effect=texture):
            result = global_visuals.rogue_frame_image(b"source", "2087")
            self.assertEqual(result.tobytes(), Image.alpha_composite(left, right).tobytes())
            self.assertNotEqual(result.tobytes(), Image.alpha_composite(right, left).tobytes())
            right = Image.new("RGBA", (64, 64))
            with self.assertRaisesRegex(ValueError, "ArtTextureLayout"):
                global_visuals.rogue_frame_image(b"source", "2087")

    def test_bundled_variants_match_pixels(self):
        manifest = json.loads((global_visuals.FRAME_ASSETS / "manifest.json").read_text())
        for key in global_visuals.BORDERS:
            images, hashes = global_visuals.bundled_frames(key)
            self.assertEqual(set(images), {"bronze", key})
            self.assertEqual(set(hashes), {"embedded/bronze.png", "embedded/" + key + ".png"})
            for name, image in images.items():
                self.assertEqual(image.size, (1024, 2048))
                self.assertEqual(art_swap.digest(image.tobytes()), manifest["Frames"][name]["Pixels"])
                self.assertEqual(image.getchannel("A").getextrema(), (0, 255))

    def test_bundled_corruption_and_missing_rejected(self):
        with tempfile.TemporaryDirectory(prefix="frame-assets-test-") as temporary:
            root = Path(temporary) / "frames"
            shutil.copytree(global_visuals.FRAME_ASSETS, root)
            (root / "2083.png").write_bytes(b"broken")
            with self.assertRaisesRegex(ValueError, "VisualFrameAssets"):
                global_visuals.bundled_frames("2083", root)
            (root / "manifest.json").unlink()
            with self.assertRaisesRegex(ValueError, "VisualFrameAssets"):
                global_visuals.bundled_frames("2086", root)

    def test_bundled_unknown_variant_rejected(self):
        with self.assertRaisesRegex(ValueError, "VisualFrameAssets"):
            global_visuals.bundled_frames("../2083")

    def test_only_selected_asset_required(self):
        with tempfile.TemporaryDirectory(prefix="selected-frame-") as temporary:
            root = Path(temporary)
            for name in ("manifest.json", "2086.png"):
                shutil.copyfile(global_visuals.FRAME_ASSETS / name, root / name)
            images, hashes = global_visuals.bundled_frames("2086", root, keys=["2086"])
            self.assertEqual(set(images), {"2086"})
            self.assertEqual(set(hashes), {"embedded/2086.png"})

    def test_independent_frame_matrix(self):
        with tempfile.TemporaryDirectory(prefix="independent-frames-") as temporary:
            root = Path(temporary); client = root / "client"; client.mkdir()
            (client / "data_definitions").write_bytes(b"protected data")
            for relative in (global_visuals.GLOBAL, "bundledassets.json"):
                output = client / relative; output.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(self.package / "backup" / relative, output)
            original = UnityPy.load((client / global_visuals.GLOBAL).read_bytes())
            old_pixels = {name: art_swap.texture(original, name)[1].image.convert("RGBA").tobytes() for name in ("CardFrame_Bronze", "CardFrame_Gold")}
            for bronze in ("keep", "none", "rogue"):
                for gold in ("keep", "none", "rogue"):
                    with self.subTest(bronze=bronze, gold=gold):
                        output = root / (bronze + "-" + gold)
                        if bronze == gold == "keep":
                            with self.assertRaisesRegex(ValueError, "No visual changes"):
                                global_visuals.build(client, output, bronze_frames=bronze, gold_frames=gold)
                            continue
                        assets = global_visuals.FRAME_ASSETS if "rogue" in (bronze, gold) else root / "absent-library"
                        with patch("global_visuals.FRAME_ASSETS", assets):
                            result = global_visuals.build(client, output, bronze_frames=bronze, gold_frames=gold)
                        changed = UnityPy.load((output / "payload" / global_visuals.GLOBAL).read_bytes())
                        expected = {name for name, mode in (("CardFrame_Bronze", bronze), ("CardFrame_Gold", gold)) if mode != "keep"}
                        self.assertEqual({check["Texture"] for check in result["Checks"]}, expected)
                        self.assertEqual(result["Settings"]["BronzeFrames"], bronze)
                        self.assertEqual(result["Settings"]["GoldFrames"], gold)
                        for name, mode in (("CardFrame_Bronze", bronze), ("CardFrame_Gold", gold)):
                            image = art_swap.texture(changed, name)[1].image.convert("RGBA")
                            if mode == "keep": self.assertEqual(image.tobytes(), old_pixels[name])
                            elif mode == "none": self.assertEqual(image.getchannel("A").getextrema(), (0, 0))
                            else:
                                images, _ = global_visuals.bundled_frames("2083")
                                self.assertEqual(image.tobytes(), images["bronze" if name.endswith("Bronze") else "2083"].tobytes())
                        art_swap.verify(output)

    def test_invalid_independent_mode(self):
        with self.assertRaisesRegex(ValueError, "Unknown frame option"):
            global_visuals.build("unused", "unused", bronze_frames="unknown")

    def test_build_all_bundled_variants_without_rogue_mage(self):
        with tempfile.TemporaryDirectory(prefix="embedded-frames-build-") as temporary:
            root = Path(temporary); client = root / "client"; client.mkdir()
            (client / "data_definitions").write_bytes(b"protected card data")
            for relative in (global_visuals.GLOBAL, "bundledassets.json"):
                output = client / relative; output.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(self.package / "backup" / relative, output)
            before = {str(p.relative_to(client)): art_swap.digest(p.read_bytes()) for p in client.rglob("*") if p.is_file()}
            for border in global_visuals.BORDERS:
                output = root / border
                result = global_visuals.build(client, output, frames="rogue", border=border)
                self.assertEqual(len(result["Files"]), 2)
                self.assertEqual(set(result["SourceHashes"]), {"embedded/bronze.png", "embedded/" + border + ".png"})
                art_swap.verify(output)
            self.assertEqual(before, {str(p.relative_to(client)): art_swap.digest(p.read_bytes()) for p in client.rglob("*") if p.is_file()})

    def test_rarity_cells_leave_gameplay_icons_intact(self):
        image = Image.new("RGBA", (1024, 1024), (10, 20, 30, 255))
        regions = global_visuals.overlay_regions(hide_rarity=True)
        result = global_visuals.clear_regions(image, regions["CardElement_Atlas"])
        self.assertEqual(result.getpixel((600, 600)), (0, 0, 0, 0))
        for point in ((50, 50), (380, 380), (128, 620), (511, 600), (768, 600)):
            self.assertEqual(result.getpixel(point), image.getpixel(point))

    def test_bread_and_background_are_independent(self):
        image = Image.new("RGBA", (1024, 1024), (10, 20, 30, 255))
        for provision, bread in ((True, False), (False, True), (True, True)):
            result = global_visuals.clear_regions(image, global_visuals.overlay_regions(
                hide_provision=provision, hide_bread=bread)["CardProvision_Atlas"])
            self.assertEqual(result.getpixel((50, 50))[3], 0 if provision else 255)
            self.assertEqual(result.getpixel((780, 150))[3], 0 if bread else 255)
            self.assertEqual(result.getpixel((1000, 1000)), image.getpixel((1000, 1000)))

    def test_saved_pixels_catalog_and_object_preservation(self):
        manifest = art_swap.verify(self.package)
        self.assertEqual(manifest["Scope"], "global-card-visuals")
        original = UnityPy.load(str(self.package / "backup" / global_visuals.GLOBAL))
        raw = (self.package / "payload" / global_visuals.GLOBAL).read_bytes()
        changed = UnityPy.load(raw)
        before, after = art_swap.object_hashes(original), art_swap.object_hashes(changed)
        keys = set()
        for check in manifest["Checks"]:
            obj, value = art_swap.texture(changed, check["Texture"])
            keys.add((obj.assets_file.name, obj.path_id))
            self.assertEqual(art_swap.digest(value.image.convert("RGBA").tobytes()), check["Pixels"])
        self.assertEqual(before.keys(), after.keys())
        self.assertTrue(all(before[k] == after[k] for k in before.keys() - keys))
        self.assertEqual(len(before) - len(keys), manifest["PreservedObjects"])
        catalog = json.loads((self.package / "payload/bundledassets.json").read_text(encoding="utf-8"))
        old = json.loads((self.package / "backup/bundledassets.json").read_text(encoding="utf-8-sig"))
        import zlib
        entry = next(e for e in catalog["Infos"] if e["Path"] == global_visuals.GLOBAL)
        self.assertEqual(entry["CRC"], zlib.crc32(art_swap.bundle_body(raw)) & 0xffffffff)
        self.assertEqual(entry["FileCrc"], zlib.crc32(raw) & 0xffffffff)
        self.assertEqual(entry["Size"], len(raw))
        self.assertEqual([e for e in catalog["Infos"] if e["Path"] != global_visuals.GLOBAL],
                         [e for e in old["Infos"] if e["Path"] != global_visuals.GLOBAL])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", required=True, type=Path)
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    ArtSwapTests.package = GlobalVisualTests.package = args.package.resolve()
    suite = unittest.TestSuite()
    for name in unittest.defaultTestLoader.getTestCaseNames(ArtSwapTests):
        if name not in ("test_payload_matches_catalog_and_pixels", "test_normalize_formats_orientation_and_metadata",
                        "test_arbitrary_image_build_and_padding", "test_image_rejections"):
            suite.addTest(ArtSwapTests(name))
    suite.addTests(unittest.defaultTestLoader.loadTestsFromTestCase(GlobalVisualTests))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if args.report:
        art_swap.write_json(args.report, dict(Passed=result.wasSuccessful(), Tests=result.testsRun,
                                             Failures=len(result.failures), Errors=len(result.errors)))
    raise SystemExit(not result.wasSuccessful())
