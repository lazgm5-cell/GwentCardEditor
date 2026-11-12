import io
import unittest
import zipfile
import audio_swap


class DefinitionsTests(unittest.TestCase):
    def parse(self, content):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("CardAudio.xml", content)
        with zipfile.ZipFile(stream) as archive:
            return audio_swap.definitions(archive)

    def test_standard_wrapper(self):
        self.assertEqual(set(self.parse('<CardAudioCollection><CardAudio Type="CardAudio"><CardAudio id="1" /></CardAudio></CardAudioCollection>')), {"1"})

    def test_multiple_definitions_preserved(self):
        rows = self.parse('<CardAudioCollection><CardAudio Type="CardAudio"><CardAudio id="1047" name="Crach" /><CardAudio id="92438" name="Portal" /></CardAudio></CardAudioCollection>')
        self.assertEqual(set(rows), {"1047", "92438"})
        self.assertEqual(rows["92438"].get("name"), "Portal")

    def test_ambiguous_and_unknown_structures_rejected(self):
        cases = [
            '<CardAudio Type="CardAudio"><CardAudio id="1" /><CardAudio id="1" /></CardAudio>',
            '<CardAudio Type="CardAudio"><CardAudio id="1" /></CardAudio><CardAudio Type="CardAudio"><CardAudio id="1" /></CardAudio>',
            '<CardAudio Type="CardAudio"><Unknown id="1" /></CardAudio>',
            '<CardAudio Type="CardAudio"><CardAudio /></CardAudio>',
            '<CardAudio Type="CardAudio" />',
            '<CardAudio Type="Other"><CardAudio id="1" /></CardAudio>',
        ]
        for content in cases:
            with self.subTest(content=content), self.assertRaisesRegex(ValueError, "AudioUnsupported"):
                self.parse('<CardAudioCollection>' + content + '</CardAudioCollection>')


if __name__ == "__main__":
    unittest.main()
