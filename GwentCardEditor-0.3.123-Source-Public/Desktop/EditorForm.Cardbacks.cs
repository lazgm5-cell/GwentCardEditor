namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private readonly Button cardbackImport = new() { AutoSize = true }, cardbackRestore = new() { AutoSize = true };

    private void AddCardbackControls(FlowLayoutPanel layout)
    {
        layout.AutoScroll = true;
        layout.Controls.Add(Bind(new Label { AutoSize = true, MaximumSize = new Size(500, 0), Margin = new Padding(3, 18, 3, 3) }, "CardbackScope"));
        layout.Controls.Add(Bind(cardbackImport, "CardbackImport"));
        layout.Controls.Add(Bind(cardbackRestore, "CardbackRestore"));
        cardbackImport.Click += async (_, _) =>
        {
            if (!ArtReady() || project?.Client == null || !ArtWorker.CardbacksAvailable) return;
            string client = Path.GetDirectoryName(project.Client.ArchivePath)!;
            await ArtOperation(async () =>
            {
                project.Client.Verify();
                using var catalog = await ArtWorker.Run("cardback-catalog", "--target", client);
                UseWaitCursor = false;
                using var picker = new CardbackPicker(catalog.RootElement);
                if (appearanceFont != null) Theme.Apply(picker, settings, appearanceFont);
                if (picker.ShowDialog(this) != DialogResult.OK || (!picker.ApplyToAll && picker.SelectedId is null)) return;
                var targetBacks = picker.ApplyToAll
                    ? catalog.RootElement.GetProperty("Cardbacks").EnumerateArray().Select(row => row.GetProperty("Id").GetInt32()).ToArray()
                    : [picker.SelectedId!.Value];
                using var chooser = new OpenFileDialog { Title = UiText.Get("ArtChooseImage"), Filter = "PNG / JPEG / WebP|*.png;*.jpg;*.jpeg;*.webp" };
                if (chooser.ShowDialog(this) != DialogResult.OK) return;
                string temporary = Path.Combine(Path.GetTempPath(), "GwentCardbackInput-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    using var normalized = await ArtWorker.Run("normalize", "--image", chooser.FileName, "--output", temporary);
                    using var source = Image.FromFile(temporary);
                    using var bitmap = new Bitmap(source);
                    string name = picker.ApplyToAll ? UiText.Get("CardbackAll") : picker.SelectedName;
                    using var dialog = new ArtImportDialog(bitmap, client, targetBacks[0], name, cardback: true, cardbackTargets: targetBacks);
                    dialog.ShowDialog(this);
                }
                finally { File.Delete(temporary); }
            }, "Cardback");
        };
        cardbackRestore.Click += async (_, _) =>
        {
            if (!ArtReady() || project?.Client == null) return;
            string client = Path.GetDirectoryName(project.Client.ArchivePath)!;
            using var chooser = PackageStorage.RestoreDialog(this, client, UiText.Get("CardbackRestore"));
            if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, UiText.Get("ArtRestoreConfirm") + Environment.NewLine + client, UiText.Get("Cardback"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await ArtOperation(async () =>
            {
                string package = Path.GetDirectoryName(chooser.FileName)!;
                using var verified = await ArtWorker.Run("verify", "--package", package);
                if (verified.RootElement.GetProperty("Scope").GetString() != "static-cardback-image") throw new InvalidOperationException("VisualWrongPackage");
                using var result = await ArtWorker.Run("restore", "--package", package, "--client", client);
                MessageBox.Show(this, UiText.Get("ArtRestored"), UiText.Get("Cardback"));
            }, "Cardback");
        };
    }
}
