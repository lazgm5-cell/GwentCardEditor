import math
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import wave
import custom_audio


class EditTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="audio-edit-test-")
        self.addCleanup(self.temp.cleanup)
        self.source = Path(self.temp.name) / "source.wav"
        self.output = Path(self.temp.name) / "edited.wav"
        with wave.open(str(self.source), "wb") as writer:
            writer.setparams((2, 2, 48000, 4800, "NONE", "not compressed"))
            writer.writeframes(struct.pack("<hh", 12000, -6000) * 4800)
        self.original = self.source.read_bytes()

    def run_edit(self, start=0, end=.1, incoming=0, outgoing=0):
        result = custom_audio.edit(self.source, self.output, start, end, incoming, outgoing)
        self.assertEqual(self.source.read_bytes(), self.original)
        with wave.open(str(self.output), "rb") as reader:
            frames = reader.readframes(reader.getnframes())
            self.assertEqual(reader.getnchannels(), 2)
            self.assertEqual(reader.getframerate(), 48000)
        return result, list(struct.iter_unpack("<hh", frames))

    def test_identity(self):
        _, samples = self.run_edit()
        self.assertEqual(samples, [(12000, -6000)] * 4800)

    def test_trim(self):
        result, samples = self.run_edit(.02, .08)
        self.assertEqual(len(samples), 2880)
        self.assertEqual(result["Seconds"], .06)

    def test_fades(self):
        _, samples = self.run_edit(.02, .08, .01, .02)
        self.assertEqual(samples[0], (0, 0))
        self.assertEqual(samples[-1], (0, 0))
        self.assertEqual(samples[479], (12000, -6000))
        self.assertEqual(samples[1000], (12000, -6000))
        self.assertTrue(0 < samples[200][0] < 12000)
        self.assertTrue(-6000 < samples[200][1] < 0)

    def test_touching_fades(self):
        _, samples = self.run_edit(0, .1, .05, .05)
        self.assertEqual(samples[2399], (12000, -6000))
        self.assertEqual(samples[2400], (12000, -6000))

    def test_invalid(self):
        for values in ((-.1, .1, 0, 0), (.1, .1, 0, 0), (0, .2, 0, 0),
                       (0, .1, -.1, 0), (0, .1, .06, .06),
                       (0, .1, math.nan, 0), (0, math.inf, 0, 0),
                       (False, .1, 0, 0), (0, .000001, 0, 0)):
            with self.subTest(values=values):
                with self.assertRaisesRegex(ValueError, "CustomAudioRange"):
                    custom_audio.edit(self.source, self.output, *values)
                self.assertFalse(self.output.exists())

    def test_no_overwrite(self):
        self.output.write_bytes(b"owned")
        with self.assertRaisesRegex(ValueError, "OutputExists"):
            self.run_edit()
        self.assertEqual(self.output.read_bytes(), b"owned")
        with self.assertRaisesRegex(ValueError, "OutputExists"):
            custom_audio.edit(self.source, self.source, 0, .1)
        self.assertEqual(self.source.read_bytes(), self.original)

    def test_mono_44100(self):
        with wave.open(str(self.source), "wb") as writer:
            writer.setparams((1, 2, 44100, 4410, "NONE", "not compressed"))
            writer.writeframes(struct.pack("<h", -32768) * 4410)
        custom_audio.edit(self.source, self.output, 0, .1, .05, .05)
        with wave.open(str(self.output), "rb") as reader:
            samples = list(struct.iter_unpack("<hh", reader.readframes(4410)))
            self.assertEqual(reader.getnframes(), 4410)
        self.assertEqual(samples[0], (0, 0))
        self.assertEqual(samples[2204], (-32768, -32768))

    def long_wav(self):
        with wave.open(str(self.source), "wb") as writer:
            writer.setparams((1, 2, 44100, 0, "NONE", "not compressed"))
            writer.writeframes(b"\0\0" * (120 * 44100))
            writer.writeframes(struct.pack("<h", 12345) * 44100)
        self.original = self.source.read_bytes()

    def test_long_wav_probe_and_extract(self):
        self.long_wav()
        self.assertEqual(custom_audio.probe(self.source)["Seconds"], 121)
        result = custom_audio.extract(self.source, self.output, 120, 121)
        self.assertEqual(result["Seconds"], 1)
        with wave.open(str(self.output), "rb") as reader:
            samples = list(struct.iter_unpack("<hh", reader.readframes(44100)))
        self.assertEqual(samples, [(12345, 12345)] * 44100)
        self.assertEqual(self.original, self.source.read_bytes())

    def test_extract_crosses_sample_boundary(self):
        self.long_wav()
        custom_audio.extract(self.source, self.output, 119.5, 120.5)
        with wave.open(str(self.output), "rb") as reader:
            samples = list(struct.iter_unpack("<hh", reader.readframes(44100)))
        self.assertEqual(samples[:22050], [(0, 0)] * 22050)
        self.assertEqual(samples[22050:], [(12345, 12345)] * 22050)

    def test_extract_invalid(self):
        self.long_wav()
        for bounds in ((0, 121), (120, 122), (-1, 1), (1, 1), (0, math.nan), (0, math.inf), (False, 1)):
            with self.subTest(bounds=bounds), self.assertRaisesRegex(ValueError, "CustomAudioRange"):
                custom_audio.extract(self.source, self.output, *bounds)
            self.assertFalse(self.output.exists())

    def test_probe_truncated(self):
        self.source.write_bytes(self.original[:-2])
        with self.assertRaisesRegex(ValueError, "CustomAudioFormat"):
            custom_audio.probe(self.source)

    def test_extract_preserves_existing_output(self):
        self.output.write_bytes(b"foreign")
        with self.assertRaisesRegex(ValueError, "OutputExists"):
            custom_audio.extract(self.source, self.output, 0, .1)
        with self.assertRaisesRegex(ValueError, "OutputExists"):
            custom_audio.extract(self.source, self.source, 0, .1)
        self.assertEqual(self.output.read_bytes(), b"foreign")
        self.assertEqual(self.source.read_bytes(), self.original)

    def test_extract_cli(self):
        self.long_wav()
        result = subprocess.run([sys.executable, "-I", "-X", "utf8", str(Path(__file__).with_name("art_swap.py")),
                                 "wav-extract", "--source", str(self.source), "--output", str(self.output),
                                 "--start", "120", "--end", "121"], capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(self.output.exists())


if __name__ == "__main__":
    unittest.main()
