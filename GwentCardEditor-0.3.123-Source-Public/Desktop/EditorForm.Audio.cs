namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private readonly Button audioImport = new() { AutoSize = true }, audioRestore = new() { AutoSize = true };
    private readonly Button customAudioImport = new() { AutoSize = true }, customAudioRestore = new() { AutoSize = true };

    private TabPage BuildAudioPage()
    {
        var page = Bind(new TabPage(), "Audio");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(Bind(new Label { AutoSize = true, MaximumSize = new Size(500, 0) }, "AudioScope"));
        layout.Controls.Add(Bind(audioImport, "AudioChoose")); layout.Controls.Add(Bind(audioRestore, "AudioNativeRestore"));
        layout.Controls.Add(Bind(customAudioImport, "CustomAudioChoose")); layout.Controls.Add(Bind(customAudioRestore, "CustomAudioRestore")); page.Controls.Add(layout);
        customAudioImport.Click += async (_, _) =>
        {
            if (!ArtReady() || !ArtWorker.CustomAudioAvailable || project?.Client == null || selectedId is not int id) return;
            using var chooser = new OpenFileDialog { Filter = "WAV / MP3|*.wav;*.mp3|WAV|*.wav|MP3|*.mp3", Title = UiText.Get("CustomAudioChoose") };
            if (chooser.ShowDialog(this) != DialogResult.OK) return;
            string assets = Path.GetDirectoryName(project.Client.ArchivePath)!;
            string temporary = Path.Combine(Path.GetTempPath(), "GwentCustomWav-" + Guid.NewGuid().ToString("N") + ".wav");
            await ArtOperation(async () =>
            {
                try
                {
                    project.Client.Verify();
                    using var audio = await ArtWorker.Run("audio-info", "--target", assets, "--target-card", id.ToString());
                    bool mp3 = Path.GetExtension(chooser.FileName).Equals(".mp3", StringComparison.OrdinalIgnoreCase);
                    using var info = await ArtWorker.Run(mp3 ? "mp3-probe" : "wav-probe", "--source", chooser.FileName);
                    double seconds = info.RootElement.GetProperty("Seconds").GetDouble();
                    var arguments = new List<string> { mp3 ? "mp3-normalize" : "wav-normalize", "--source", chooser.FileName, "--output", temporary };
                    if (seconds > 120)
                    {
                        UseWaitCursor = false;
                        using var range = new AudioRangeDialog(seconds);
                        if (appearanceFont != null) Theme.Apply(range, settings, appearanceFont);
                        if (range.ShowDialog(this) != DialogResult.OK) return;
                        if (!mp3) arguments[0] = "wav-extract";
                        arguments.AddRange(["--start", range.StartSeconds, "--end", range.EndSeconds]);
                        UseWaitCursor = true;
                    }
                    using var normalized = await ArtWorker.Run(arguments.ToArray());
                    using var status = await ArtWorker.Run("wav-status", "--client", assets);
                    UseWaitCursor = false;
                    using var dialog = new CustomAudioDialog(assets, id, project.Name(id, UiText.Language), temporary, normalized.RootElement.GetProperty("Seconds").GetDouble(), status.RootElement.GetProperty("Loader").GetBoolean());
                    if (appearanceFont != null) Theme.Apply(dialog, settings, appearanceFont);
                    dialog.ShowDialog(this);
                }
                finally { File.Delete(temporary); File.Delete(temporary + ".mp3-tmp"); }
            }, "Audio");
        };
        customAudioRestore.Click += async (_, _) =>
        {
            if (!ArtReady() || !ArtWorker.CustomAudioAvailable || project?.Client == null) return;
            string assets = Path.GetDirectoryName(project.Client.ArchivePath)!;
            using var chooser = PackageStorage.RestoreDialog(this, assets, UiText.Get("CustomAudioRestore"));
            if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, UiText.Get("CustomAudioRestoreConfirm") + Environment.NewLine + assets, UiText.Get("Audio"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await ArtOperation(async () =>
            {
                using var result = await ArtWorker.Run("wav-restore", "--package", Path.GetDirectoryName(chooser.FileName)!, "--client", assets, "--runtime", ArtWorker.CustomAudioRuntime);
                MessageBox.Show(this, UiText.Get("CustomAudioRestored"), UiText.Get("Audio"));
            }, "Audio");
        };
        audioImport.Click += async (_, _) =>
        {
            if (!ArtReady() || project?.Client == null || selectedId is not int id) return;
            string source = project.Client.ArchivePath, assets = Path.GetDirectoryName(source)!;
            bool changed = false;
            await ArtOperation(async () =>
            {
                project.Client.Verify();
                using var catalog = await ArtWorker.Run("audio-catalog", "--target", assets);
                using var supported = await ArtWorker.Run("audio-info", "--target", assets, "--target-card", id.ToString());
                var cards = catalog.RootElement.GetProperty("Cards").EnumerateArray().Select(c => c.GetInt32()).ToHashSet();
                UseWaitCursor = false;
                using var dialog = new AudioReferenceDialog(project, assets, id, cards);
                if (appearanceFont != null) Theme.Apply(dialog, settings, appearanceFont);
                dialog.ShowDialog(this); changed = dialog.ClientChanged;
            }, "Audio");
            if (changed) LoadProject(source);
        };
        audioRestore.Click += async (_, _) =>
        {
            if (!ArtReady() || project?.Client == null) return;
            string source = project.Client.ArchivePath, assets = Path.GetDirectoryName(source)!;
            using var chooser = PackageStorage.RestoreDialog(this, assets, UiText.Get("AudioNativeRestore"));
            if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, UiText.Get("AudioRestoreConfirm") + Environment.NewLine + assets, UiText.Get("Audio"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            bool changed = false;
            await ArtOperation(async () =>
            {
                string package = Path.GetDirectoryName(chooser.FileName)!;
                using var verified = await ArtWorker.Run("verify", "--package", package);
                if (verified.RootElement.GetProperty("Scope").GetString() != "native-card-audio-reference") throw new InvalidOperationException("AudioWrongPackage");
                using var result = await ArtWorker.Run("restore", "--package", package, "--client", assets);
                changed = true; MessageBox.Show(this, UiText.Get("AudioRestored"), UiText.Get("Audio"));
            }, "Audio");
            if (changed) LoadProject(source);
        };
        return page;
    }
}
