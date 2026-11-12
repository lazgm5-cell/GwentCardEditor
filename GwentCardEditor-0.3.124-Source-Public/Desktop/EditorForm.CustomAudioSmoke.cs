using System.Text;
using System.Text.Json;

namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public async Task SmokeMp3Only(string output)
    {
        if (project?.Client == null || !ArtWorker.CustomAudioAvailable || !ArtWorker.Mp3Available) throw new Exception("MP3 smoke needs full runtime and client");
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "GwentMp3Ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string input = Path.Combine(temporary, "\u0442\u0435\u0441\u0442 audio.MP3"), normalized = Path.Combine(temporary, "decoded.wav");
            byte[] frame = new byte[417]; frame[0] = 0xff; frame[1] = 0xfb; frame[2] = 0x90;
            File.WriteAllBytes(input, Enumerable.Range(0, 5000).SelectMany(_ => frame).ToArray());
            byte[] original = File.ReadAllBytes(input);
            using var metadata = await ArtWorker.Run("mp3-probe", "--source", input);
            if (metadata.RootElement.GetProperty("Seconds").GetDouble() <= 120) throw new Exception("Long MP3 not detected");
            using var audio = await ArtWorker.Run("mp3-normalize", "--source", input, "--output", normalized, "--start", "120", "--end", "121");
            if (audio.RootElement.GetProperty("Seconds").GetDouble() != 1) throw new Exception("Long MP3 range duration mismatch");
            if (audio.RootElement.GetProperty("Channels").GetInt32() != 2 || audio.RootElement.GetProperty("SampleRate").GetInt32() != 48000) throw new Exception("MP3 worker format mismatch");
            string assets = Path.GetDirectoryName(project.Client.ArchivePath)!;
            foreach (int index in Enumerable.Range(0, 3))
            {
                language.SelectedIndex = index;
                using (var range = new AudioRangeDialog(metadata.RootElement.GetProperty("Seconds").GetDouble()))
                {
                    range.CheckControlsForTest(); range.Show(this);
                    foreach (var size in new[] { new Size(500, 300), new Size(460, 280) })
                    {
                        range.Size = size; Application.DoEvents();
                        using var capture = new Bitmap(range.Width, range.Height);
                        range.DrawToBitmap(capture, new Rectangle(Point.Empty, range.Size));
                        capture.Save(Path.Combine(output, $"audio-range-{UiText.Language}-{size.Width}.png"));
                    }
                    range.Close();
                }
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project.Card(202103).Faction);
                search.Text = "202103"; searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 5;
                using var dialog = new CustomAudioDialog(assets, 202103, project.Name(202103, UiText.Language), normalized, audio.RootElement.GetProperty("Seconds").GetDouble(), false);
                dialog.CheckControlsForTest(); dialog.Show(this);
                foreach (var size in new[] { new Size(650, 600), new Size(560, 570) })
                {
                    dialog.Size = size; Application.DoEvents();
                    using var capture = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                    capture.Save(Path.Combine(output, $"mp3-{UiText.Language}-{size.Width}.png"));
                }
                dialog.Close();
            }
            string package = Path.Combine(temporary, "package");
            using var built = await ArtWorker.Run("wav-build", "--client", assets, "--target-card", "202103", "--source", normalized, "--output", package, "--runtime", ArtWorker.CustomAudioRuntime);
            using var verified = await ArtWorker.Run("wav-verify", "--package", package, "--runtime", ArtWorker.CustomAudioRuntime);
            if (!original.SequenceEqual(File.ReadAllBytes(input))) throw new Exception("MP3 source modified");
            project.Client.Verify();
            File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new { Passed=true, Scope="mp3-import", Languages=3, Sizes=2, LongSourceRange=true, SeparateDecoderProcess=true, ExportVerified=true, AudioPlayed=false, InstalledIntoGame=false }));
        }
        finally
        {
            if (Path.GetFullPath(temporary).StartsWith(Path.Combine(Path.GetTempPath(), "GwentMp3Ui-"), StringComparison.OrdinalIgnoreCase)) Directory.Delete(temporary, true);
        }
    }

    public async Task SmokeCustomAudioOnly(string output)
    {
        if (project?.Client == null || !ArtWorker.CustomAudioAvailable) throw new Exception("Custom audio smoke needs full runtime and client");
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "GwentCustomAudioUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string input = Path.Combine(temporary, "test.wav"), normalized = Path.Combine(temporary, "normalized.wav");
            using (var writer = new BinaryWriter(File.Create(input)))
            {
                int bytes = 4410 * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
            }
            using var audio = await ArtWorker.Run("wav-normalize", "--source", input, "--output", normalized);
            if (audio.RootElement.GetProperty("Channels").GetInt32() != 2) throw new Exception("Custom audio normalization failed");
            string assets = Path.GetDirectoryName(project.Client.ArchivePath)!;
            foreach (int index in Enumerable.Range(0, 3))
            {
                language.SelectedIndex = index;
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project.Card(202103).Faction);
                search.Text = "202103"; searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 5;
                if (!customAudioImport.Enabled || !customAudioRestore.Enabled) throw new Exception("Custom audio controls disabled");
                using var dialog = new CustomAudioDialog(assets, 202103, project.Name(202103, UiText.Language), normalized, .1, false);
                dialog.CheckControlsForTest(); dialog.Show(this);
                string previous = await dialog.Prepare();
                dialog.LoadPreparedForTest();
                dialog.SetEditForTest(.01m, .09m, .01m, .02m);
                if (File.Exists(previous)) throw new Exception("Old prepared clip not removed");
                string prepared = await dialog.Prepare();
                if (prepared != await dialog.Prepare()) throw new Exception("Prepared clip not reused");
                if (index == 0)
                {
                    string editedPackage = Path.Combine(temporary, "edited-package");
                    using var edited = await ArtWorker.Run("wav-build", "--client", assets, "--target-card", "202103", "--source", prepared, "--output", editedPackage, "--runtime", ArtWorker.CustomAudioRuntime);
                    using var checkedEdit = await ArtWorker.Run("wav-verify", "--package", editedPackage, "--runtime", ArtWorker.CustomAudioRuntime);
                    string exported = Path.Combine(editedPackage, "payload", "BepInEx", "plugins", "GwentCardEditor.CustomAudio", "clips", "202103.wav");
                    if (!File.ReadAllBytes(prepared).SequenceEqual(File.ReadAllBytes(exported))) throw new Exception("Prepared/export clip mismatch");
                }
                foreach (var size in new[] { new Size(650, 600), new Size(560, 570) })
                {
                    dialog.Size = size; Application.DoEvents();
                    using var capture = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                    capture.Save(Path.Combine(output, $"custom-audio-{UiText.Language}-{size.Width}.png"));
                }
                dialog.Close();
                dialog.Dispose();
                if (File.Exists(prepared)) throw new Exception("Prepared clip leaked");
                using var loaderEnabled = new CustomAudioDialog(assets, 202103, "test", normalized, .1, true);
                loaderEnabled.CheckControlsForTest();
            }
            string package = Path.Combine(temporary, "package");
            using var built = await ArtWorker.Run("wav-build", "--client", assets, "--target-card", "202103", "--source", normalized, "--output", package, "--runtime", ArtWorker.CustomAudioRuntime, "--gain", "0.5", "--loop");
            using var verified = await ArtWorker.Run("wav-verify", "--package", package, "--runtime", ArtWorker.CustomAudioRuntime);
            if (verified.RootElement.GetProperty("Files").GetArrayLength() != 6) throw new Exception("Custom audio package contents invalid");
            project.Client.Verify();
            File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new { Passed=true, Scope="custom-premium-wav", Languages=3, Sizes=2, ExportVerified=true, PreparedExportIdentical=true, TemporaryClipsCleaned=true, AudioPlayed=false, InstalledIntoGame=false }));
        }
        finally
        {
            if (Path.GetFullPath(temporary).StartsWith(Path.Combine(Path.GetTempPath(), "GwentCustomAudioUi-"), StringComparison.OrdinalIgnoreCase)) Directory.Delete(temporary, true);
        }
    }
}
