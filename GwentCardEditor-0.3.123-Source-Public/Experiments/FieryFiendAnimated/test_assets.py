import hashlib
import json
import math
from pathlib import Path
import re
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parent
SCENE = json.loads((ROOT / "assets/scene.json").read_text(encoding="utf-8"))


class FieryAssetsTests(unittest.TestCase):
    def test_scene_checksum_pinned_in_plugin(self):
        expected = hashlib.sha256((ROOT / "assets/scene.json").read_bytes()).hexdigest().upper()
        self.assertIn('SceneSha256 = "' + expected + '"', (ROOT / "Plugin/FieryFiendPlugin.cs").read_text())

    def test_six_components_present(self):
        self.assertEqual({n["Name"] for n in SCENE["Nodes"]}, {"Fire_AB", "Fire_Additive", "Eyes", "Glow", "FieryFiend_Left", "FieryFiend_Right"})
        self.assertEqual(len(SCENE["Materials"]), 6)
        self.assertEqual(len(SCENE["Textures"]), 10)
        self.assertEqual(SCENE["Shader"], "ShaderLibrary/Generic/GwentStandard")

    def test_texture_hashes_and_dimensions(self):
        for key, entry in SCENE["Textures"].items():
            with self.subTest(key=key):
                self.assertRegex(entry["File"], r"^textures/-?[0-9]+\.png$")
                path = ROOT / "assets" / entry["File"]
                self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), entry["Sha256"])
                with Image.open(path) as image:
                    self.assertEqual(image.size, (entry["Width"], entry["Height"]))
                    self.assertEqual(image.mode, "RGBA")

    def test_meshes_complete(self):
        for mesh in SCENE["Meshes"]:
            count = len(mesh["Vertices"])
            self.assertTrue(0 < count <= 10000)
            self.assertEqual(len(mesh["Uv"]), count)
            self.assertEqual(len(mesh["Normals"]), count)
            self.assertEqual(len(mesh["Colors"]), count)
            self.assertEqual(len(mesh["Triangles"]) % 3, 0)
            self.assertTrue(all(0 <= i < count for i in mesh["Triangles"]))
            self.assertTrue(all(math.isfinite(v) for field in ("Vertices", "Uv", "Normals", "Colors") for row in mesh[field] for v in row))

    def test_fire_and_gradient_clamp(self):
        for key in ("-43137523323442226", "-4210385997605371657"):
            sampling = SCENE["Textures"][key]["Sampling"]
            self.assertEqual([sampling[k] for k in ("WrapU", "WrapV", "WrapW")], [1, 1, 1])

    def test_noise_repeat_and_filter(self):
        for key in ("5654814437984703768", "-1436412703761287052", "-1633395239367107374"):
            sampling = SCENE["Textures"][key]["Sampling"]
            self.assertEqual([sampling[k] for k in ("WrapU", "WrapV", "WrapW")], [0, 0, 0])
        for entry in SCENE["Textures"].values():
            self.assertEqual(entry["Sampling"]["Filter"], 1)
            self.assertEqual(entry["Sampling"]["Aniso"], 1)
            self.assertEqual(entry["Sampling"]["MipBias"], 0)

    def test_scene_references(self):
        for node in SCENE["Nodes"]:
            self.assertIn(node["Mesh"], range(len(SCENE["Meshes"])))
            self.assertIn(node["Material"], range(len(SCENE["Materials"])))
        for material in SCENE["Materials"]:
            self.assertEqual(material["Shader"], SCENE["Shader"])
            for entry in material["Textures"]:
                self.assertIn(entry["Texture"], SCENE["Textures"])

    def test_animation_parameters_present(self):
        for material in SCENE["Materials"]:
            self.assertIn("_UVANIM_ON", material["Keywords"])
            speeds = material["Colors"]["_SecondTexUVAnim"]
            self.assertNotEqual(abs(speeds["r"]) + abs(speeds["g"]), 0)

    def test_package_complete_and_no_old_bundles(self):
        package = ROOT / "Package/BepInEx/plugins/GwentCardEditor.FieryFiend.Experimental"
        manifest = json.loads((ROOT / "package-files.json").read_text(encoding="utf-8-sig"))
        self.assertEqual(len(manifest["Files"]), 13)
        paths = {r["Path"] for r in manifest["Files"]}
        self.assertEqual(paths, {p.relative_to(package).as_posix() for p in package.rglob("*") if p.is_file()})
        for record in manifest["Files"]:
            path = package / record["Path"]
            self.assertIn(path.suffix, (".dll", ".png", ".json"))
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest().upper(), record["Sha256"])

    def test_target_limited(self):
        config = json.loads((ROOT / "config.json").read_text())
        self.assertEqual(config, dict(Enabled=True, TargetCards=[202103]))


if __name__ == "__main__":
    unittest.main()
