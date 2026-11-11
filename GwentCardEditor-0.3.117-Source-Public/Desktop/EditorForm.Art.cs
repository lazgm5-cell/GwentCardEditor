namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private readonly Button artImport = new() { AutoSize = true }, artRestore = new() { AutoSize = true };
    private readonly Label artNote = new() { AutoSize = true, Dock = DockStyle.Fill };
    private bool artBusy;
    private readonly Button openBackups = new() { AutoSize = true };
    private readonly Button globalVisuals = new() { AutoSize = true }, starterDecks = new() { AutoSize = true }, mediaCheck = new() { AutoSize = true };
    private readonly Label starterNote = new() { AutoSize = true, MaximumSize = new Size(500, 0) };

    private TabPage BuildVisualPage()
    {
        var page = Bind(new TabPage(), "GlobalVisuals");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(Bind(new Label { AutoSize = true, MaximumSize = new Size(500, 0) }, "VisualScope"));
        layout.Controls.Add(Bind(globalVisuals, "VisualConfigure"));
        layout.Controls.Add(Bind(starterNote, "VisualStarterScope"));
        layout.Controls.Add(starterDecks);
        AddCardbackControls(layout);
        layout.Controls.Add(Bind(new Label { AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(3, 18, 3, 3) }, "MediaCheckScope"));
        layout.Controls.Add(Bind(mediaCheck, "MediaCheck"));
        layout.Controls.Add(Bind(openBackups, "BackupOpen")); page.Controls.Add(layout);
        openBackups.Click += (_, _) => Guard(() =>
        {
            if (project == null) return;
            string source = project.Client == null ? project.DirectoryPath : Path.GetDirectoryName(project.Client.ArchivePath)!;
            string? folder = PackageStorage.Prepare(this, source, false);
            if (folder != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        });
        mediaCheck.Click += async (_, _) =>
        {
            if (!ArtReady() || !ArtWorker.MediaCheckAvailable || project?.Client == null) return;
            if (MessageBox.Show(this, UiText.Get("MediaCheckConfirm"), UiText.Get("MediaCheck"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            await ArtOperation(async () =>
            {
                string report = Path.Combine(EditorSettings.Home, "Reports", "media-check-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
                using var result = await RunMediaCheck(report);
                bool passed = result.RootElement.GetProperty("Passed").GetBoolean();
                MessageBox.Show(this, UiText.Get(passed ? "MediaCheckPassed" : "MediaCheckFailed") + Environment.NewLine + report,
                    UiText.Get("MediaCheck"), MessageBoxButtons.OK, passed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }, "GlobalVisuals");
        };
        globalVisuals.Click += (_, _) =>
        {
            if (!ArtReady() || project?.Client == null) return;
            try
            {
                project.Client.Verify();
                using var dialog = new GlobalVisualDialog(Path.GetDirectoryName(project.Client.ArchivePath)!);
                if (appearanceFont != null) Theme.Apply(dialog, settings, appearanceFont);
                dialog.ShowDialog(this);
            }
            catch (Exception e) { MessageBox.Show(this, UiText.Error(e), UiText.Get("GlobalVisuals")); }
        };
        starterDecks.Click += (_, _) =>
        {
            if (!ArtReady() || project?.Client == null) return;
            string assets = Path.GetDirectoryName(project.Client.ArchivePath)!;
            bool hide = !DeckVisibilityInstaller.Installed(assets);
            if (MessageBox.Show(this, UiText.Get(hide ? "VisualStarterConfirm" : "VisualStarterRestoreConfirm") + Environment.NewLine + assets,
                UiText.Get("GlobalVisuals"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try { DeckVisibilityInstaller.SetHidden(assets, hide); MessageBox.Show(this, UiText.Get("VisualStarterDone")); }
            catch (Exception e) { MessageBox.Show(this, UiText.Error(e), UiText.Get("GlobalVisuals")); }
            RefreshArtState();
        };
        return page;
    }

    private TabPage BuildArtPage()
    {
        var page = Bind(new TabPage(), "Art");
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(14), ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(Bind(new Label { AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(0, 0, 0, 12) }, "ArtScope"));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.AddRange([Bind(artImport, "ArtChooseImage"), Bind(artRestore, "ArtRestore")]);
        layout.Controls.Add(actions); layout.Controls.Add(artNote); page.Controls.Add(layout);
        artImport.Click += async (_, _) =>
        {
            if (!ArtReady() || project?.Client == null || selectedId is not int id) return;
            using var chooser = new OpenFileDialog { Title = UiText.Get("ArtChooseImage"), Filter = "PNG / JPEG / WebP|*.png;*.jpg;*.jpeg;*.webp" };
            if (chooser.ShowDialog(this) != DialogResult.OK) return;
            string temporary = Path.Combine(Path.GetTempPath(), "GwentArtInput-" + Guid.NewGuid().ToString("N") + ".png");
            await ArtOperation(async () =>
            {
                project.Client.Verify();
                try
                {
                    using var result = await ArtWorker.Run("normalize", "--image", chooser.FileName, "--output", temporary);
                    using var file = Image.FromFile(temporary);
                    using var bitmap = new Bitmap(file);
                    UseWaitCursor = false;
                    using var dialog = new ArtImportDialog(bitmap, Path.GetDirectoryName(project.Client.ArchivePath)!, id, project.Name(id, UiText.Language));
                    dialog.ShowDialog(this);
                }
                finally { File.Delete(temporary); }
            });
        };
        artRestore.Click += async (_, _) =>
        {
            if (project?.Client == null || !ArtReady()) return;
            string client = Path.GetDirectoryName(project.Client.ArchivePath)!;
            using var chooser = PackageStorage.RestoreDialog(this, client, UiText.Get("ArtRestore"));
            if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, UiText.Get("ArtRestoreConfirm") + Environment.NewLine + client, UiText.Get("Art"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await ArtOperation(async () =>
            {
                using var verified = await ArtWorker.Run("verify", "--package", Path.GetDirectoryName(chooser.FileName)!);
                if (verified.RootElement.GetProperty("Scope").GetString() != "standard-art-only") throw new InvalidOperationException("VisualWrongPackage");
                using var result = await ArtWorker.Run("restore", "--package", Path.GetDirectoryName(chooser.FileName)!, "--client", client);
                MessageBox.Show(this, UiText.Get("ArtRestored"), UiText.Get("Art"));
            });
        };
        FormClosing += (_, e) => { if (artBusy) e.Cancel = true; };
        return page;
    }
    private bool ArtReady()
    {
        if (project?.Client == null) return false;
        if (draftDirty || project.HasChanges) { MessageBox.Show(this, UiText.Get("ArtCleanSource")); return false; }
        if (!ArtWorker.Available) { MessageBox.Show(this, UiText.Get("ArtRuntimeMissing")); return false; }
        return true;
    }
    private async Task ArtOperation(Func<Task> operation, string section = "Art")
    {
        artBusy = true; UseWaitCursor = true; Controls[0].Enabled = false; RefreshArtState();
        try { await operation(); }
        catch (Exception e) { MessageBox.Show(this, UiText.Error(e), UiText.Get(section), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { artBusy = false; UseWaitCursor = false; Controls[0].Enabled = true; RefreshArtState(); }
    }
    private async Task<System.Text.Json.JsonDocument> RunMediaCheck(string report)
    {
        if (project?.Client == null) throw new InvalidOperationException("ArtNeedsClient");
        project.Client.Verify();
        return await ArtWorker.Run("media-check", "--client", Path.GetDirectoryName(project.Client.ArchivePath)!, "--runtime", ArtWorker.CustomAudioRuntime, "--report", report);
    }
    private void RefreshArtState()
    {
        bool client = project?.Client != null;
        openBackups.Enabled = project != null && !artBusy;
        artImport.Enabled = client && selectedId.HasValue && project!.Card(selectedId.Value).Editable && !artBusy;
        artRestore.Enabled = client && !artBusy;
        globalVisuals.Enabled = client && !artBusy;
        cardbackImport.Enabled = cardbackRestore.Enabled = client && !artBusy && ArtWorker.CardbacksAvailable;
        mediaCheck.Enabled = client && !artBusy && ArtWorker.MediaCheckAvailable;
        audioImport.Enabled = client && selectedId.HasValue && project!.Card(selectedId.Value).Editable && project.Card(selectedId.Value).Type != 1 && !artBusy;
        audioRestore.Enabled = client && !artBusy;
        customAudioImport.Enabled = audioImport.Enabled && ArtWorker.CustomAudioAvailable;
        customAudioRestore.Enabled = client && !artBusy && ArtWorker.CustomAudioAvailable;
        string? assets = client ? Path.GetDirectoryName(project!.Client!.ArchivePath) : null;
        starterDecks.Enabled = assets != null && DeckVisibilityInstaller.Supported(assets) && !artBusy;
        starterDecks.Text = UiText.Get(assets != null && DeckVisibilityInstaller.Installed(assets) ? "VisualStarterShow" : "VisualStarterHide");
        artNote.MaximumSize = new Size(Math.Max(240, split.Panel2.ClientSize.Width - 50), 0);
        artNote.Text = UiText.Get(!client ? "ArtNeedsClient" : !ArtWorker.Available ? "ArtRuntimeMissing"
            : draftDirty || project!.HasChanges ? "ArtCleanSource" : artBusy ? "ArtWorking" : "ArtSeparatePackage");
    }
}
