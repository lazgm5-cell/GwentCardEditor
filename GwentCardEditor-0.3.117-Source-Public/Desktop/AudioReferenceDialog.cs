namespace GwentCardEditor;

internal sealed class AudioReferenceDialog : Form
{
    private sealed record Choice(int Id, string Name) { public override string ToString() => Name; }
    private readonly ListBox donors = new() { Dock = DockStyle.Fill };
    private readonly TextBox search = new() { Dock = DockStyle.Fill };
    private readonly Button export = new() { AutoSize = true }, install = new() { AutoSize = true, Enabled = false };
    private readonly Label status = new() { AutoSize = true };
    private readonly Choice[] choices;
    private readonly string client;
    private readonly int target;
    private string? package;
    private bool busy, installed;
    public bool ClientChanged { get; private set; }

    public AudioReferenceDialog(EditorProject project, string client, int target, IReadOnlySet<int> supported)
    {
        this.client = client; this.target = target;
        choices = project.Cards.Where(c => c.Editable && c.Type != 1 && supported.Contains(c.Id) && c.Id != target)
            .Select(c => new Choice(c.Id, project.Name(c.Id, UiText.Language)))
            .OrderBy(c => c.Name.TrimStart('"', '\'', '«', '“'), StringComparer.CurrentCultureIgnoreCase).ToArray();
        Text = UiText.Get("Audio") + ": " + project.Name(target, UiText.Language);
        Size = new Size(690, 590); MinimumSize = new Size(560, 470); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        var note = new Label { Text = UiText.Get("AudioNativeScope"), AutoSize = true };
        layout.Controls.Add(note, 0, 0);
        layout.Controls.Add(new Label { Text = UiText.Get("AudioDonor"), AutoSize = true, Margin = new Padding(0, 12, 0, 4) }, 0, 1);
        layout.Controls.Add(search, 0, 2); layout.Controls.Add(donors, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        export.Text = UiText.Get("ArtExport"); install.Text = UiText.Get("ArtInstall");
        var close = new Button { Text = UiText.Get("ArtClose"), AutoSize = true, DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([export, install, close]); layout.Controls.Add(actions, 0, 4); layout.Controls.Add(status, 0, 5); Controls.Add(layout); CancelButton = close;
        layout.SizeChanged += (_, _) => note.MaximumSize = status.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 30), 0);
        search.TextChanged += (_, _) => Filter(); donors.SelectedIndexChanged += (_, _) => { package = null; installed = false; status.Text = ""; UpdateActions(); };
        export.Click += async (_, _) => await Export();
        install.Click += async (_, _) =>
        {
            if (package == null || MessageBox.Show(this, UiText.Get("AudioInstallConfirm") + Environment.NewLine + client, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await Operation(async () => { using var result = await ArtWorker.Run("install", "--package", package, "--client", client); installed = ClientChanged = true; status.Text = UiText.Get("AudioInstalled"); });
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        Filter();
    }
    internal void CheckControlsForTest()
    {
        if (donors.Items.Count == 0 || export.Enabled || install.Enabled) throw new Exception("Audio donor state invalid");
        donors.SelectedIndex = 0;
        if (!export.Enabled) throw new Exception("Audio donor cannot be exported");
        search.Text = "no-card-" + Guid.NewGuid().ToString("N");
        if (donors.Items.Count != 0 || export.Enabled) throw new Exception("Audio search failed");
        search.Text = ""; donors.SelectedIndex = 0;
    }
    private void Filter()
    {
        donors.BeginUpdate(); donors.Items.Clear();
        donors.Items.AddRange(choices.Where(c => c.Name.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).Cast<object>().ToArray());
        donors.EndUpdate(); package = null; installed = false; status.Text = ""; UpdateActions();
    }
    private void UpdateActions()
    {
        donors.Enabled = search.Enabled = !busy;
        export.Enabled = !busy && donors.SelectedItem is Choice;
        install.Enabled = !busy && package != null && !installed;
    }
    private async Task Operation(Func<Task> action)
    {
        busy = true; UseWaitCursor = true; UpdateActions();
        try { await action(); }
        catch (Exception e) { status.Text = UiText.Error(e); MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; UseWaitCursor = false; UpdateActions(); }
    }
    private async Task Export()
    {
        if (donors.SelectedItem is not Choice donor) return;
        using var chooser = PackageStorage.SaveDialog(this, client, UiText.Get("BackupAudio") + "-" + target, Text, 60);
        if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
        if (Directory.Exists(chooser.FileName) || File.Exists(chooser.FileName)) { MessageBox.Show(this, UiText.Get("ArtNewFolder")); return; }
        await Operation(async () =>
        {
            status.Text = UiText.Get("AudioWorking");
            using var result = await ArtWorker.Run("audio-build", "--target", client, "--target-card", target.ToString(), "--donor-card", donor.Id.ToString(), "--output", chooser.FileName);
            package = chooser.FileName; PackageStorage.Describe(package, client, UiText.Get("BackupAudio"), Text + " -> " + donor.Name + " (" + donor.Id + ")"); installed = false; status.Text = UiText.Get("ArtExported") + " " + package;
        });
    }
}
