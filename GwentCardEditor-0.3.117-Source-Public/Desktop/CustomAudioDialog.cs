using System.Globalization;
using System.Media;

namespace GwentCardEditor;

internal sealed class CustomAudioDialog : Form
{
    private SoundPlayer player;
    private readonly ToolTip tips = new();
    private readonly TrackBar gain = new() { Minimum = 0, Maximum = 100, Value = 100, TickStyle = TickStyle.None, Width = 300 };
    private readonly CheckBox loop = new() { AutoSize = true };
    private readonly Label gainLabel = new() { AutoSize = true }, status = new() { AutoSize = true };
    private readonly Button play = new() { AutoSize = true }, stop = new() { AutoSize = true },
        export = new() { AutoSize = true }, install = new() { AutoSize = true };
    private readonly string client, source;
    private readonly int cardId;
    private readonly bool loader;
    private readonly NumericUpDown begin = SecondsControl(), end = SecondsControl(), fadeIn = SecondsControl(), fadeOut = SecondsControl();
    private string? prepared;
    private string? package;
    private bool busy, installed;

    public CustomAudioDialog(string client, int cardId, string cardName, string source, double seconds, bool loader)
    {
        this.client = client; this.cardId = cardId; this.source = source; this.loader = loader;
        player = new SoundPlayer();
        end.Maximum = (decimal)seconds; end.Value = (decimal)seconds;
        Text = UiText.Get("CustomAudio") + ": " + cardName; Size = new Size(650, 600); MinimumSize = new Size(560, 570);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int row = 0; row < 7; row++) layout.RowStyles.Add(new RowStyle(row == 4 ? SizeType.Percent : SizeType.AutoSize, row == 4 ? 100 : 0));
        var note = new Label { Text = UiText.Get("CustomAudioScope"), AutoSize = true };
        layout.Controls.Add(note, 0, 0);
        layout.Controls.Add(new Label { Text = UiText.Get("CustomAudioDuration") + " " + seconds.ToString("0.00", CultureInfo.CurrentCulture), AutoSize = true }, 0, 1);
        var playback = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        play.Text = UiText.Get("CustomAudioPlay"); stop.Text = UiText.Get("CustomAudioStop"); playback.Controls.AddRange([play, stop]); layout.Controls.Add(playback, 0, 2);
        var settings = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        settings.Controls.Add(gainLabel); settings.Controls.Add(gain); loop.Text = UiText.Get("CustomAudioLoop"); settings.Controls.Add(loop); layout.Controls.Add(settings, 0, 3);
        var edits = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 4 };
        var fields = new[] { begin, end, fadeIn, fadeOut };
        var labels = new[] { "CustomAudioStart", "CustomAudioEnd", "CustomAudioFadeIn", "CustomAudioFadeOut" };
        for (int index = 0; index < fields.Length; index++)
        {
            edits.Controls.Add(new Label { Text = UiText.Get(labels[index]), AutoSize = true, Anchor = AnchorStyles.Left }, 0, index);
            edits.Controls.Add(fields[index], 1, index);
            fields[index].ValueChanged += (_, _) => { ClearPrepared(); InvalidatePackage(); };
        }
        settings.Controls.Add(edits);
        tips.SetToolTip(gain, UiText.Get("CustomAudioGainHint"));
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        export.Text = UiText.Get("ArtExport"); install.Text = UiText.Get("ArtInstall");
        var close = new Button { AutoSize = true, Text = UiText.Get("ArtClose"), DialogResult = DialogResult.Cancel };
        actions.Controls.AddRange([export, install, close]); layout.Controls.Add(actions, 0, 5); layout.Controls.Add(status, 0, 6);
        Controls.Add(layout); CancelButton = close;
        layout.SizeChanged += (_, _) => note.MaximumSize = status.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 30), 0);
        play.Click += async (_, _) => await Operation(async () => { player.SoundLocation = await Prepare(); player.Load(); if (loop.Checked) player.PlayLooping(); else player.Play(); });
        stop.Click += (_, _) => player.Stop();
        gain.ValueChanged += (_, _) => InvalidatePackage(); loop.CheckedChanged += (_, _) => { player.Stop(); InvalidatePackage(); };
        export.Click += async (_, _) => await Export();
        install.Click += async (_, _) =>
        {
            if (package == null || !loader || MessageBox.Show(this, UiText.Get("CustomAudioInstallConfirm") + Environment.NewLine + client, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await Operation(async () => { using var result = await ArtWorker.Run("wav-install", "--package", package, "--client", client, "--runtime", ArtWorker.CustomAudioRuntime); installed = true; status.Text = UiText.Get("CustomAudioInstalled"); });
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        InvalidatePackage();
    }
    internal void CheckControlsForTest()
    {
        if (!export.Enabled || install.Enabled) throw new Exception("Custom audio initial state invalid");
        gain.Value = 25; loop.Checked = true;
        if (install.Enabled || !export.Enabled) throw new Exception("Custom audio settings did not invalidate package");
        package = "test-package"; UpdateActions();
        if (install.Enabled != loader) throw new Exception("Custom audio loader gate failed");
        InvalidatePackage();
    }
    private void InvalidatePackage()
    {
        package = null; installed = false; gainLabel.Text = UiText.Get("CustomAudioGain") + " " + gain.Value + "%";
        status.Text = loader ? "" : UiText.Get("CustomAudioLoader"); UpdateActions();
    }
    private void UpdateActions()
    {
        bool valid = begin.Value < end.Value && fadeIn.Value + fadeOut.Value <= end.Value - begin.Value;
        gain.Enabled = loop.Enabled = begin.Enabled = end.Enabled = fadeIn.Enabled = fadeOut.Enabled = !busy;
        play.Enabled = export.Enabled = !busy && valid; stop.Enabled = !busy;
        tips.SetToolTip(export, valid ? null : UiText.Get("CustomAudioRange"));
        install.Enabled = !busy && loader && package != null && !installed;
    }
    private static NumericUpDown SecondsControl() => new() { DecimalPlaces = 3, Increment = .1m, Maximum = 120, Width = 110 };
    private void ClearPrepared(bool recreatePlayer = true)
    {
        player.Stop(); player.Dispose();
        if (recreatePlayer) player = new SoundPlayer();
        if (prepared != null) File.Delete(prepared);
        prepared = null;
    }
    internal async Task<string> Prepare()
    {
        if (prepared != null) return prepared;
        string path = Path.Combine(Path.GetTempPath(), "GwentAudioPrepared-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using var result = await ArtWorker.Run("wav-edit", "--source", source, "--output", path,
                "--start", begin.Value.ToString(CultureInfo.InvariantCulture), "--end", end.Value.ToString(CultureInfo.InvariantCulture),
                "--fade-in", fadeIn.Value.ToString(CultureInfo.InvariantCulture), "--fade-out", fadeOut.Value.ToString(CultureInfo.InvariantCulture));
            prepared = path; return path;
        }
        catch { File.Delete(path); throw; }
    }
    internal void SetEditForTest(decimal start, decimal finish, decimal incoming, decimal outgoing)
    {
        begin.Value = start; end.Value = finish; fadeIn.Value = incoming; fadeOut.Value = outgoing;
        if (!export.Enabled) throw new Exception("Audio edit controls invalid");
    }
    internal void LoadPreparedForTest()
    {
        player.SoundLocation = prepared ?? throw new Exception("No prepared clip");
        player.Load();
    }
    private async Task Operation(Func<Task> action)
    {
        player.Stop(); busy = true; UseWaitCursor = true; UpdateActions();
        try { await action(); }
        catch (Exception e) { status.Text = UiText.Error(e); MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; UseWaitCursor = false; UpdateActions(); }
    }
    private async Task Export()
    {
        using var chooser = PackageStorage.SaveDialog(this, client, UiText.Get("BackupCustomAudio") + "-" + cardId, Text, 85);
        if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
        if (File.Exists(chooser.FileName) || Directory.Exists(chooser.FileName)) { MessageBox.Show(this, UiText.Get("ArtNewFolder")); return; }
        await Operation(async () => {
        var args = new List<string> { "wav-build", "--client", client, "--target-card", cardId.ToString(CultureInfo.InvariantCulture), "--source", await Prepare(), "--output", chooser.FileName,
            "--runtime", ArtWorker.CustomAudioRuntime, "--gain", (gain.Value / 100d).ToString(CultureInfo.InvariantCulture) };
        if (loop.Checked) args.Add("--loop");
        using var result = await ArtWorker.Run(args.ToArray()); package = chooser.FileName;
        PackageStorage.Describe(package, client, UiText.Get("BackupCustomAudio"), Text + Environment.NewLine + begin.Value + " - " + end.Value + Environment.NewLine + gainLabel.Text + Environment.NewLine + loop.Text + ": " + loop.Checked);
        installed = false; status.Text = UiText.Get("ArtExported") + " " + package; });
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { ClearPrepared(false); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
