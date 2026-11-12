import argparse
import copy
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import wave
import zipfile
import art_swap
import custom_audio as audio


class CustomAudioTests(unittest.TestCase):
    runtime = None

    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="custom-audio-tests-")
        self.addCleanup(temporary.cleanup)
        self.temp = Path(temporary.name); self.client = self.temp / "client"
        self.assets = self.client / "Gwent_Data/StreamingAssets"; self.assets.mkdir(parents=True)
        (self.client / "Gwent.exe").write_bytes(b"test marker")
        for file in ("BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/interop/Assembly-CSharp.dll"):
            path = self.client / file; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"test marker")
        with zipfile.ZipFile(self.assets / "data_definitions", "w") as archive:
            archive.writestr("Templates.xml", '<Templates>' + ''.join(f'<Template Type="CardTemplate" Id="{n}" AudioId="{n}" Availability="3" DebugName="Card{n}" />' for n in (1, 2)) + '</Templates>')
            archive.writestr("CardAudio.xml", '<CardAudioCollection>' + ''.join(f'<CardAudio Type="CardAudio"><CardAudio id="{n}"><Voiceovers><Voiceover key="test" /></Voiceovers><Triggers /></CardAudio></CardAudio>' for n in (1, 2)) + '</CardAudioCollection>')
        self.source = self.temp / "input.wav"; self.wav(self.source)
        process = patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'"notepad.exe"', b""))
        process.start(); self.addCleanup(process.stop)

    @staticmethod
    def wav(path, channels=2, rate=48000, width=2):
        with wave.open(str(path), "wb") as writer:
            writer.setparams((channels, width, rate, 64, "NONE", "not compressed"))
            writer.writeframes((b"\x01\x02" if width == 2 else b"\0" * width) * channels * 64)

    def build(self, name="package", card=1, **kwargs):
        output = self.temp / name
        audio.build(self.client, card, self.source, output, self.runtime, **kwargs)
        return output

    def snapshot(self):
        return {str(p.relative_to(self.client)): p.read_bytes() for p in self.client.rglob("*") if p.is_file()}

    def test_stereo_and_mono_normalization(self):
        for channels in (1, 2):
            source = self.temp / f"input-{channels}.wav"; output = self.temp / f"output-{channels}.wav"
            self.wav(source, channels, 44100)
            result = audio.normalize(source, output)
            self.assertEqual(result["Channels"], 2)
            with wave.open(str(output)) as reader:
                self.assertEqual(reader.readframes(64), b"\x01\x02" * 128)

    def test_format_rejections(self):
        for n, values in enumerate(((2, 48000, 3), (3, 48000, 2), (2, 22050, 2))):
            path = self.temp / f"bad-{n}.wav"; self.wav(path, *values)
            with self.assertRaisesRegex(ValueError, "CustomAudioFormat"): audio.normalize(path, self.temp / f"out-{n}.wav")
        bad = self.temp / "bad.wav"; bad.write_bytes(b"not a WAV")
        with self.assertRaisesRegex(ValueError, "CustomAudioFormat"): audio.normalize(bad, self.temp / "out.wav")

    def test_truncated_input_rejected(self):
        self.source.write_bytes(self.source.read_bytes()[:-2])
        with self.assertRaisesRegex(ValueError, "CustomAudioFormat"): audio.normalize(self.source, self.temp / "out.wav")

    def test_oversized_duration_rejected(self):
        data = bytearray(self.source.read_bytes()); struct.pack_into("<I", data, 40, 48000 * 4 * 121); self.source.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "CustomAudioFormat"): audio.normalize(self.source, self.temp / "out.wav")

    def test_normalization_never_overwrites(self):
        before = self.source.read_bytes()
        with self.assertRaises(ValueError): audio.normalize(self.source, self.source)
        self.assertEqual(before, self.source.read_bytes())

    def test_install_restore_no_game_resources_changed(self):
        before = self.snapshot(); package = self.build()
        audio.transfer(package, self.client, self.runtime)
        for path, data in before.items(): self.assertEqual((self.client / path).read_bytes(), data)
        audio.transfer(package, self.client, self.runtime, restore=True)
        self.assertEqual(before, self.snapshot())

    def test_multiple_cards_and_reverse_restore(self):
        first = self.build("one", 1); audio.transfer(first, self.client, self.runtime)
        second = self.build("two", 2, gain=.5, loop=True); audio.transfer(second, self.client, self.runtime)
        config = audio.configuration((self.client / (audio.PREFIX + "config.json")).read_bytes())
        self.assertEqual(set(config["Cards"]), {"1", "2"})
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): audio.transfer(first, self.client, self.runtime, True)
        self.assertEqual(before, self.snapshot())
        audio.transfer(second, self.client, self.runtime, True); audio.transfer(first, self.client, self.runtime, True)

    def test_replace_same_card_restore_exact(self):
        first = self.build("one"); audio.transfer(first, self.client, self.runtime); before = self.snapshot()
        second = self.build("two", gain=.25, loop=True); audio.transfer(second, self.client, self.runtime)
        audio.transfer(second, self.client, self.runtime, True); self.assertEqual(before, self.snapshot())

    def test_foreign_runtime_is_not_overwritten(self):
        path = self.client / (audio.PREFIX + audio.RUNTIME[0]); path.parent.mkdir(parents=True); path.write_bytes(b"foreign plugin")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): self.build()
        self.assertEqual(before, self.snapshot())

    def test_no_loader_no_install_but_restore_allowed(self):
        package = self.build(); loader = self.client / "BepInEx/core/BepInEx.Unity.IL2CPP.dll"; data = loader.read_bytes(); loader.unlink()
        with self.assertRaisesRegex(ValueError, "CustomAudioLoader"): audio.transfer(package, self.client, self.runtime)
        loader.write_bytes(data); audio.transfer(package, self.client, self.runtime); loader.unlink()
        audio.transfer(package, self.client, self.runtime, True)

    def test_foreign_edit_blocks_restore(self):
        package = self.build(); audio.transfer(package, self.client, self.runtime)
        path = self.client / (audio.PREFIX + "config.json"); path.write_bytes(path.read_bytes() + b" "); before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): audio.transfer(package, self.client, self.runtime, True)
        self.assertEqual(before, self.snapshot())

    def test_running_game_blocks_install(self):
        package = self.build(); before = self.snapshot()
        with patch("art_swap.subprocess.run", return_value=subprocess.CompletedProcess([], 0, b'"Gwent.exe"', b"")):
            with self.assertRaisesRegex(ValueError, "Close Gwent"): audio.transfer(package, self.client, self.runtime)
        self.assertEqual(before, self.snapshot())

    def test_partial_failure_rolls_back(self):
        package = self.build(); before = self.snapshot(); replace = audio.os.replace; count = 0
        def failing(source, destination):
            nonlocal count
            count += 1
            if count == 3: raise OSError("simulated failure")
            return replace(source, destination)
        with patch("custom_audio.os.replace", side_effect=failing):
            with self.assertRaises(OSError): audio.transfer(package, self.client, self.runtime)
        self.assertEqual(before, self.snapshot())

    def test_paths_and_runtime_payload_checked(self):
        package = self.build(); original = json.loads((package / "manifest.json").read_text())
        for kind in ("path", "runtime"):
            value = copy.deepcopy(original)
            if kind == "path": value["Files"][0]["Path"] = "../outside.dll"
            else:
                path = package / "payload" / value["Files"][0]["Path"]; path.write_bytes(b"foreign plugin")
                value["Files"][0]["After"] = art_swap.digest(path.read_bytes())
            art_swap.write_json(package / "manifest.json", value)
            with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): audio.verify(package, self.runtime)

    def test_gains_and_configs_fail_closed(self):
        for gain in (-1, 1.1, float("nan"), float("inf")):
            with self.assertRaisesRegex(ValueError, "CustomAudioFormat"): self.build(gain=gain)
        with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): audio.configuration(b'{"Version":2,"Cards":{}}')
        for raw in (b"", b"null", b"[]", b'{"Version":true,"Cards":{}}'):
            with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): audio.configuration(raw)

    def test_data_changes_block_install_not_cleanup(self):
        package = self.build(); path = self.assets / "data_definitions"; old = path.read_bytes(); path.write_bytes(old + b" ")
        with self.assertRaisesRegex(ValueError, "CustomAudioConflict"): audio.transfer(package, self.client, self.runtime)
        path.write_bytes(old); audio.transfer(package, self.client, self.runtime); path.write_bytes(old + b" ")
        audio.transfer(package, self.client, self.runtime, True); self.assertEqual(path.read_bytes(), old + b" ")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(); parser.add_argument("--runtime", required=True, type=Path); parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args(); CustomAudioTests.runtime = args.runtime.resolve()
    result = unittest.TextTestRunner(verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(CustomAudioTests))
    art_swap.write_json(args.report, dict(Passed=result.wasSuccessful(), Tests=result.testsRun, Errors=len(result.errors), Failures=len(result.failures)))
    raise SystemExit(not result.wasSuccessful())
