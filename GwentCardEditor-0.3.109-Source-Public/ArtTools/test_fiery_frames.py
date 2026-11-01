import argparse
import copy
import json
from pathlib import Path
import shutil
import tempfile
import subprocess
import sys
import unittest
from unittest.mock import patch
import art_swap
import fiery_frames as fiery


class FieryTests(unittest.TestCase):
    runtime = None
    fixture = None
    legacy = None
    worker = None

    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="fiery-tests-"); self.addCleanup(temp.cleanup)
        self.root = Path(temp.name); self.client = self.root / "client"
        self.assets = self.client / "Gwent_Data/StreamingAssets"; self.assets.mkdir(parents=True)
        for relative in fiery.GLOBAL_FILES:
            path = self.assets / relative; path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.fixture / "backup" / relative, path)
        (self.assets / "data_definitions").write_bytes(b"protected card data")
        for relative in ("Gwent.exe", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/interop/Assembly-CSharp.dll"):
            path = self.client / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"test marker")
        (self.client / "unrelated.dll").write_bytes(b"foreign mod")
        mock = patch("art_swap.check_game_closed"); mock.start(); self.addCleanup(mock.stop)
        self.package = self.root / "package"

    def snapshot(self):
        return {p.relative_to(self.client).as_posix(): art_swap.digest(p.read_bytes()) for p in self.client.rglob("*") if p.is_file()}

    def build(self):
        return fiery.build(self.client, self.package, self.runtime)

    def transfer(self, package=None, restore=False):
        return fiery.transfer(package or self.package, self.client, restore, self.runtime)

    def test_fresh_install_restore_and_repeat(self):
        before = self.snapshot(); result = self.build()
        self.assertEqual(len(result["Files"]), 15)
        self.transfer()
        config = json.loads((self.client / fiery.PREFIX / "config.json").read_text())
        self.assertEqual(config, fiery.CONFIG)
        self.transfer(restore=True)
        self.assertEqual(self.snapshot(), before)
        self.assertTrue(self.transfer(restore=True)["AlreadyRestored"])

    def test_legacy_migration_restores_original_experiment(self):
        shutil.copytree(self.legacy, self.client / fiery.PREFIX)
        (self.client / fiery.PREFIX / "frame-0.png").write_bytes(b"diagnostic")
        before = self.snapshot(); self.build(); self.transfer()
        self.assertTrue(json.loads((self.client / fiery.PREFIX / "config.json").read_text())["AllGold"])
        self.transfer(restore=True)
        self.assertEqual(self.snapshot(), before)

    def test_loader_required_only_for_install(self):
        (self.client / "BepInEx/core/BepInEx.Unity.IL2CPP.dll").unlink()
        before = self.snapshot(); self.build()
        with self.assertRaisesRegex(ValueError, "FieryLoader"): self.transfer()
        self.assertEqual(self.snapshot(), before)
        self.assertTrue(self.transfer(restore=True)["AlreadyRestored"])

    def test_foreign_runtime_refused(self):
        path = self.client / fiery.PREFIX / fiery.DLL; path.parent.mkdir(parents=True); path.write_bytes(b"foreign DLL")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "FieryConflict"): self.build()
        self.assertEqual(self.snapshot(), before)

    def test_modified_config_blocks_restore(self):
        self.build(); self.transfer()
        (self.client / fiery.PREFIX / "config.json").write_bytes(b"foreign edit")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "FieryConflict"): self.transfer(restore=True)
        self.assertEqual(self.snapshot(), before)

    def test_configuration_validation(self):
        for value in ({"Enabled":True,"TargetCards":[True]}, {"Version":True,"Enabled":True,"AllGold":True,"TargetCards":[]},
                      {"Enabled":True,"AllGold":True,"TargetCards":[1]}, {"Enabled":True,"TargetCards":[]},
                      {"Enabled":1,"TargetCards":[1]}, {"Enabled":True,"TargetCards":[1],"Other":1}):
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, "FieryConflict"):
                fiery.configuration(json.dumps(value).encode())
        self.assertEqual(fiery.configuration(json.dumps(fiery.CONFIG).encode()), fiery.CONFIG)

    def test_package_tampered_runtime_refused(self):
        self.build()
        path = self.package / "payload" / fiery.PREFIX / fiery.DLL; path.write_bytes(b"tampered")
        manifest = json.loads((self.package / "manifest.json").read_text())
        next(r for r in manifest["Files"] if r["Path"] == fiery.PREFIX + fiery.DLL)["After"] = art_swap.digest(path.read_bytes())
        art_swap.write_json(self.package / "manifest.json", manifest)
        with self.assertRaisesRegex(ValueError, "FieryConflict"): fiery.verify(self.package, self.runtime)

    def test_unsafe_and_duplicate_paths_refused(self):
        self.build(); manifest = json.loads((self.package / "manifest.json").read_text())
        for path in ("../outside", manifest["Files"][1]["Path"]):
            modified = copy.deepcopy(manifest); modified["Files"][0]["Path"] = path
            art_swap.write_json(self.package / "manifest.json", modified)
            with self.assertRaisesRegex(ValueError, "FieryConflict"): fiery.verify(self.package, self.runtime)

    def test_partial_commit_rolled_back(self):
        self.build(); before = self.snapshot(); replace = fiery.os.replace; count = 0
        def fail(source, target):
            nonlocal count
            count += 1
            if count == 3: raise OSError("simulated failure")
            return replace(source, target)
        with patch("fiery_frames.os.replace", side_effect=fail):
            with self.assertRaisesRegex(OSError, "simulated"): self.transfer()
        self.assertEqual(self.snapshot(), before)
        self.assertFalse(list(self.client.rglob("*.fiery-*")))

    def test_remove_then_restore_composite(self):
        before = self.snapshot(); self.build(); self.transfer()
        global_after = {p: (self.assets / p).read_bytes() for p in fiery.GLOBAL_FILES}
        removal = self.root / "removal"
        fiery.build(self.client, removal, self.runtime, remove=True)
        self.transfer(removal)
        self.assertTrue(all((self.assets / p).read_bytes() == raw for p, raw in global_after.items()))
        self.transfer(restore=True)
        self.assertEqual(self.snapshot(), before)

    def test_removal_package_reenables_runtime(self):
        self.build(); self.transfer(); before = self.snapshot()
        removal = self.root / "removal"; fiery.build(self.client, removal, self.runtime, remove=True)
        self.transfer(removal)
        self.transfer(removal, restore=True)
        self.assertEqual(self.snapshot(), before)

    def test_plugin_absent_does_not_allow_unrelated_global_edit(self):
        self.build(); self.transfer()
        removal = self.root / "removal"; fiery.build(self.client, removal, self.runtime, remove=True); self.transfer(removal)
        path = self.assets / fiery.GLOBAL_FILES[0]; path.write_bytes(path.read_bytes() + b"foreign")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "FieryConflict"): self.transfer(restore=True)
        self.assertEqual(self.snapshot(), before)

    def test_card_data_change_blocks_visual_restore_but_not_removal(self):
        self.build(); self.transfer(); (self.assets / "data_definitions").write_bytes(b"later data")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "card data differs"): self.transfer(restore=True)
        self.assertEqual(self.snapshot(), before)
        removal = self.root / "removal"; fiery.build(self.client, removal, self.runtime, remove=True); self.transfer(removal)
        self.assertFalse((self.client / fiery.PREFIX / fiery.DLL).exists())

    def test_output_inside_client_refused(self):
        with self.assertRaisesRegex(ValueError, "FieryOutput"):
            fiery.build(self.client, self.client / "package", self.runtime)

    def test_published_isolated_build_and_verify(self):
        for arguments in (("fiery-build", "--client", str(self.client), "--output", str(self.package)), ("verify", "--package", str(self.package))):
            result = subprocess.run([sys.executable, "-I", "-X", "utf8", str(self.worker), *arguments], capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr.decode("utf-8", errors="replace"))
            self.assertEqual(json.loads(result.stdout)["Scope"], "global-card-visuals-animated")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", required=True, type=Path)
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--legacy", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--worker", required=True, type=Path)
    args = parser.parse_args()
    FieryTests.runtime, FieryTests.fixture, FieryTests.legacy = args.runtime.resolve(), args.fixture.resolve(), args.legacy.resolve()
    FieryTests.worker = args.worker.resolve()
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(FieryTests))
    art_swap.write_json(args.report, dict(Passed=result.wasSuccessful(), Tests=result.testsRun, Failures=len(result.failures), Errors=len(result.errors), InstalledIntoGame=False))
    raise SystemExit(not result.wasSuccessful())
