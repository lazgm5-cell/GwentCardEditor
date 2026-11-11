using System.Drawing.Imaging;
using System.Text.Json;

namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public async Task SmokeAudioOnly(string output)
    {
        if (project?.Client == null || !ArtWorker.Available) throw new Exception("Audio smoke needs a client and worker");
        Directory.CreateDirectory(output);
        string assets = Path.GetDirectoryName(project.Client.ArchivePath)!;
        using var catalog = await ArtWorker.Run("audio-catalog", "--target", assets);
        var supported = catalog.RootElement.GetProperty("Cards").EnumerateArray().Select(c => c.GetInt32()).ToHashSet();
        string temporary = Path.Combine(Path.GetTempPath(), "GwentAudioUi-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (int index in Enumerable.Range(0, 3))
            {
                language.SelectedIndex = index;
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project.Card(202103).Faction);
                search.Text = "202103"; searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 5;
                if (!audioImport.Enabled || !audioRestore.Enabled) throw new Exception("Audio controls disabled");
                using var dialog = new AudioReferenceDialog(project, assets, 202103, supported);
                dialog.CheckControlsForTest(); dialog.Show(this);
                foreach (var size in new[] { new Size(690, 590), new Size(560, 470) })
                {
                    dialog.Size = size; Application.DoEvents();
                    using var capture = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                    capture.Save(Path.Combine(output, $"audio-{UiText.Language}-{size.Width}.png"));
                }
                dialog.Close();
            }
            using var result = await ArtWorker.Run("audio-build", "--target", assets, "--target-card", "202103", "--donor-card", "112103", "--output", temporary);
            if (result.RootElement.GetProperty("Scope").GetString() != "native-card-audio-reference") throw new Exception("Wrong audio package type");
            using var verified = await ArtWorker.Run("verify", "--package", temporary);
            project.Client.Verify();
            if (project.HasChanges || draftDirty) throw new Exception("Audio smoke changed card data");
            File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new { Passed=true, Scope="native-audio", Languages=3, Sizes=2, ExportVerified=true, InstalledIntoGame=false }));
        }
        finally
        {
            if (Path.GetFullPath(temporary).StartsWith(Path.Combine(Path.GetTempPath(), "GwentAudioUi-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    public void SmokeVisualsOnly(string output)
    {
        if (project?.Client == null) throw new InvalidOperationException("Visual smoke needs a client");
        Directory.CreateDirectory(output);
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index; sections.SelectedIndex = 4; Application.DoEvents();
            if (!globalVisuals.Enabled) throw new Exception("Global appearance incorrectly requires an editable card");
            if (!openBackups.Enabled || openBackups.Text != UiText.Get("BackupOpen")) throw new Exception("Backup shortcut is unavailable or untranslated");
            using (var mainCapture = new Bitmap(Width, Height))
            {
                DrawToBitmap(mainCapture, new Rectangle(Point.Empty, Size));
                mainCapture.Save(Path.Combine(output, $"backup-page-{UiText.Language}.png"));
            }
            using var dialog = new GlobalVisualDialog(Path.GetDirectoryName(project.Client.ArchivePath)!);
            dialog.CheckControlsForTest();
            dialog.Show(this);
            foreach (var size in new[] { new Size(720, 590), new Size(620, 520) })
            {
                dialog.Size = size; Application.DoEvents();
                using var capture = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                capture.Save(Path.Combine(output, $"visuals-{UiText.Language}-{size.Width}.png"));
            }
                dialog.SelectFieryForTest();
                foreach (var size in new[] { new Size(720, 590), new Size(620, 520) })
                {
                    dialog.Size = size; Application.DoEvents();
                    using var capture = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                    capture.Save(Path.Combine(output, $"fiery-{UiText.Language}-{size.Width}.png"));
                }
            dialog.SelectAnimatedForTest();
            foreach (var size in new[] { new Size(720, 590), new Size(620, 520) })
            {
                dialog.Size = size; Application.DoEvents();
                using var capture = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                capture.Save(Path.Combine(output, $"animated-{UiText.Language}-{size.Width}.png"));
            }
            dialog.Close();
        }
        project.Client.Verify();
        if (project.HasChanges || draftDirty) throw new Exception("Visual smoke changed card data");
        bool installerChecked = false;
        var games = System.Diagnostics.Process.GetProcessesByName("Gwent");
        bool gameRunning = games.Length > 0;
        foreach (var game in games) game.Dispose();
        if (!gameRunning)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "GwentVisualInstaller-" + Guid.NewGuid().ToString("N"));
            try
            {
                string assets = Path.Combine(temporary, "Gwent_Data", "StreamingAssets"); Directory.CreateDirectory(assets);
                if (DeckVisibilityInstaller.Supported(assets) || DeckVisibilityInstaller.Supported(Path.GetPathRoot(temporary)!)) throw new Exception("Unsupported plugin target accepted");
                Directory.CreateDirectory(Path.Combine(temporary, "BepInEx", "core")); Directory.CreateDirectory(Path.Combine(temporary, "BepInEx", "interop"));
                foreach (string relative in new[] { "Gwent.exe", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/interop/Assembly-CSharp.dll" }) File.WriteAllText(Path.Combine(temporary, relative), "test marker");
                using (var animated = new GlobalVisualDialog(assets)) animated.SelectAnimatedForTest();
                DeckVisibilityInstaller.SetHidden(assets, true);
                if (!DeckVisibilityInstaller.Installed(assets)) throw new Exception("Plugin install failed");
                string installed = Path.Combine(temporary, "BepInEx", "plugins", "GwentCardEditor.DeckVisibility", "GwentCardEditor.DeckVisibility.dll");
                byte[] original = File.ReadAllBytes(installed); File.WriteAllText(installed, "foreign change");
                bool refused = false;
                try { DeckVisibilityInstaller.SetHidden(assets, false); }
                catch (InvalidOperationException e) when (e.Message == "ArtConflict") { refused = true; }
                if (!refused || File.ReadAllText(installed) != "foreign change") throw new Exception("Plugin installer overwrote foreign changes");
                File.WriteAllBytes(installed, original); DeckVisibilityInstaller.SetHidden(assets, false);
                if (DeckVisibilityInstaller.Installed(assets)) throw new Exception("Plugin removal failed");
                installerChecked = true;
            }
            finally
            {
                if (Path.GetFullPath(temporary).StartsWith(Path.Combine(Path.GetTempPath(), "GwentVisualInstaller-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new { Passed=true, Scope="global-visuals", InstalledIntoGame=false, PluginInstallerChecked=installerChecked }));
    }

    public async Task SmokeArtOnly(string output)
    {
        if (project?.Client == null || !ArtWorker.Available) throw new InvalidOperationException("Art smoke needs client and runtime");
        Directory.CreateDirectory(output);
        string temporary = Path.Combine(Path.GetTempPath(), "GwentArtUi-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string input = Path.Combine(temporary, "input photograph.jpg"), normalized = Path.Combine(temporary, "normalized.png");
            using (var sample = new Bitmap(1200,800))
            {
                using var graphics = Graphics.FromImage(sample);
                graphics.Clear(Color.LightGray);
                graphics.FillRectangle(Brushes.Firebrick, 0, 0, 400, 800);
                graphics.FillRectangle(Brushes.ForestGreen, 400, 0, 400, 800);
                graphics.FillRectangle(Brushes.DodgerBlue, 800, 0, 400, 800);
                graphics.DrawEllipse(Pens.White, 300, 100, 600, 600);
                sample.Save(input, ImageFormat.Jpeg);
            }
            using (var result = await ArtWorker.Run("normalize", "--image", input, "--output", normalized))
                if (result.RootElement.GetProperty("Width").GetInt32() != 1200) throw new Exception("Portable image normalization failed");
            using var file = Image.FromFile(normalized);
            using var bitmap = new Bitmap(file);
            string crop = Path.Combine(temporary, "crop.png");
            foreach (int index in Enumerable.Range(0,3))
            {
                language.SelectedIndex = index;
                search.Text = "202103"; searchDelay.Stop();
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project.Card(202103).Faction);
                search.Text = "202103"; searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 3;
                using var dialog = new ArtImportDialog(bitmap, Path.GetDirectoryName(project.Client.ArchivePath)!, 202103, project.Name(202103, UiText.Language));
                dialog.Show(this); Application.DoEvents();
                dialog.SetZoomForTest(200);
                var before = dialog.Canvas.Crop;
                var view = dialog.Canvas.Viewport;
                var from = new Point((int)(view.X + view.Width / 2), (int)(view.Y + view.Height / 2));
                dialog.Canvas.SimulateDrag(from, new Point(from.X + 40, from.Y));
                if (dialog.Canvas.Crop.X >= before.X) throw new Exception("Crop dragging failed");
                foreach (Size size in new[] { new Size(790,760), new Size(620,580) })
                {
                    dialog.Size = size; Application.DoEvents();
                    if (dialog.Canvas.Height < 100 || dialog.Canvas.Viewport.Bottom > dialog.Canvas.Height)
                        throw new Exception("Art preview is clipped");
                    using var capture = new Bitmap(dialog.Width, dialog.Height);
                    dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                    capture.Save(Path.Combine(output, $"art-{UiText.Language}-{size.Width}.png"));
                }
                using (var rendered = dialog.Canvas.Render()) rendered.Save(crop, ImageFormat.Png);
                dialog.Close();
            }
            string package = Path.Combine(temporary, "package");
            using (var result = await ArtWorker.Run("image-build", "--image", crop, "--target", Path.GetDirectoryName(project.Client.ArchivePath)!, "--target-card", "202103", "--output", package))
                if (result.RootElement.GetProperty("Checks").GetArrayLength() != 4) throw new Exception("Four quality export failed");
            using (var result = await ArtWorker.Run("verify", "--package", package))
                if (result.RootElement.GetProperty("TargetCard").GetInt32() != 202103) throw new Exception("Art target changed");
            project.Client.Verify();
            if (project.HasChanges || draftDirty) throw new Exception("Art smoke changed card data");
            File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new
            { Passed=true, Scope="art-only", PortableRuntime=true, Languages=new[]{"ru-ru","en-us","zh-cn"},
                Drag=true, Zoom=true, FourQualities=true, GameFilesUnchanged=true }));
        }
        finally
        {
            if (Path.GetFullPath(temporary).StartsWith(Path.Combine(Path.GetTempPath(), "GwentArtUi-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(temporary, true);
        }
    }
}
