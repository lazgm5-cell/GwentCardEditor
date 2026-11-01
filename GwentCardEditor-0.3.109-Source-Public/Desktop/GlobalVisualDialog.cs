namespace GwentCardEditor;

internal sealed class GlobalVisualDialog : Form
{
    private readonly ComboBox frames = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox border = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly PictureBox bronzePreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(72, 72, 72) },
        goldPreview = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(72, 72, 72) };
    private readonly TableLayoutPanel previews = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
    private readonly CheckBox banners = new() { AutoSize = true }, rarity = new() { AutoSize = true },
        provision = new() { AutoSize = true }, bread = new() { AutoSize = true };
    private readonly Button export = new() { AutoSize = true }, install = new() { AutoSize = true },
        restore = new() { AutoSize = true }, removeAnimation = new() { AutoSize = true };
    private readonly Label animationNote = new() { AutoSize = true, MaximumSize = new Size(280, 0) };
    private readonly Label status = new() { AutoSize = true };
    private readonly FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly string client;
    private bool busy, installed;
    private bool frameAssetsReady;
    private string? package;

    internal void CheckControlsForTest()
    {
        if (export.Enabled || install.Enabled) throw new Exception("Empty appearance can be installed");
        frames.SelectedIndex = 1;
        if (!export.Enabled) throw new Exception("Frame removal cannot be exported");
        frames.SelectedIndex = 2;
        if (!export.Enabled || border.Items.Count != 7 || bronzePreview.Image == null || goldPreview.Image != null) throw new Exception("Independent bronze frame failed");
        frames.SelectedIndex = 0;
        for (int index = 2; index < 6; index++) { border.SelectedIndex = index; if (!export.Enabled || goldPreview.Image == null || bronzePreview.Image != null) throw new Exception("Independent gold frame failed"); }
        border.SelectedIndex = 1;
        if (!export.Enabled || goldPreview.Image != null) throw new Exception("Independent gold removal failed");
        border.SelectedIndex = 0; rarity.Checked = true;
        if (!export.Enabled) throw new Exception("Independent overlay control failed");
        rarity.Checked = false;
        if (export.Enabled) throw new Exception("Empty appearance export allowed");
        frames.SelectedIndex = 2; border.SelectedIndex = 2;
    }

    public GlobalVisualDialog(string client)
    {
        this.client = client;
        Text = UiText.Get("GlobalVisuals"); StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 590); MinimumSize = new Size(620, 520); ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var note = new Label { Text = UiText.Get("VisualScope"), AutoSize = true };
        layout.Controls.Add(note, 0, 0);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroller.Controls.Add(options);
        content.Controls.Add(scroller, 0, 0); content.Controls.Add(previews, 1, 0); layout.Controls.Add(content, 0, 1);
        previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); previews.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        previews.RowStyles.Add(new RowStyle(SizeType.AutoSize)); previews.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previews.Controls.Add(new Label { AutoSize = true, Text = UiText.Get("VisualBronze") }, 0, 0);
        previews.Controls.Add(new Label { AutoSize = true, Text = UiText.Get("VisualGold") }, 1, 0);
        previews.Controls.Add(bronzePreview, 0, 1); previews.Controls.Add(goldPreview, 1, 1);
        options.Controls.Add(new Label { Text = UiText.Get("VisualBronzeFrames"), AutoSize = true });
        frames.Items.AddRange(new object[] { UiText.Get("VisualKeep"), UiText.Get("VisualNoFrames"), "Rogue Mage" });
        frames.SelectedIndex = 0; options.Controls.Add(frames);
        options.Controls.Add(new Label { Text = UiText.Get("VisualGoldFrames"), AutoSize = true });
        border.Items.AddRange(new object[] { UiText.Get("VisualKeep"), UiText.Get("VisualNoFrames"), "Rogue Mage: " + UiText.Get("VisualWhiteDragon"), "Rogue Mage: " + UiText.Get("VisualElite"), "Rogue Mage: Gorspeth", UiText.Get("VisualFieryFiend"), UiText.Get("FieryAnimated") }); border.SelectedIndex = 0;
        options.Controls.Add(border);
        options.Controls.Add(animationNote);
        bronzePreview.Paint += (_, e) => PaintPlaceholder(e, bronzePreview, frames.SelectedIndex);
        goldPreview.Paint += (_, e) => PaintPlaceholder(e, goldPreview, border.SelectedIndex);
        foreach (var (control, key) in new[] { (banners, "VisualBanners"), (rarity, "VisualRarity"), (provision, "VisualProvision"), (bread, "VisualBread") })
        { control.Text = UiText.Get(key); options.Controls.Add(control); control.CheckedChanged += (_, _) => InvalidatePackage(); }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        export.Text = UiText.Get("ArtExport"); install.Text = UiText.Get("ArtInstall"); restore.Text = UiText.Get("ArtRestore");
        var close = new Button { Text = UiText.Get("ArtClose"), AutoSize = true, DialogResult = DialogResult.Cancel };
        removeAnimation.Text = UiText.Get("FieryRemove");
        actions.Controls.AddRange([export, install, restore, removeAnimation, close]); layout.Controls.Add(actions, 0, 2); layout.Controls.Add(status, 0, 3);
        Controls.Add(layout); CancelButton = close;
        layout.SizeChanged += (_, _) => note.MaximumSize = status.MaximumSize = new Size(Math.Max(200, layout.ClientSize.Width - 30), 0);
        frames.SelectedIndexChanged += (_, _) => { InvalidatePackage(); UpdatePreviews(); };
        border.SelectedIndexChanged += (_, _) => { InvalidatePackage(); UpdatePreviews(); };
        export.Click += async (_, _) => await Export();
        install.Click += async (_, _) =>
        {
            if (package == null || !Confirm(border.SelectedIndex == 6 ? "FieryInstallConfirm" : "VisualInstallConfirm")) return;
            await Operation(async () =>
            {
                if (border.SelectedIndex != 6 && border.SelectedIndex != 0 && FieryFrameSupport.Installed(client)) throw new InvalidOperationException("FieryActive");
                using var result = await ArtWorker.Run("install", "--package", package, "--client", client); installed = true; status.Text = UiText.Get("VisualInstalled");
            });
        };
        restore.Click += async (_, _) =>
        {
            using var chooser = PackageStorage.RestoreDialog(this, client, UiText.Get("ArtRestore"));
            if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK || !Confirm("VisualRestoreConfirm")) return;
            await Operation(async () =>
            {
                string selected = Path.GetDirectoryName(chooser.FileName)!;
                using var verified = await ArtWorker.Run("verify", "--package", selected);
                string? scope = verified.RootElement.GetProperty("Scope").GetString();
                if (scope is not ("global-card-visuals" or "global-card-visuals-animated" or "animated-frame-removal")) throw new InvalidOperationException("VisualWrongPackage");
                if (scope == "global-card-visuals" && FieryFrameSupport.Installed(client)) throw new InvalidOperationException("FieryActive");
                using var result = await ArtWorker.Run("restore", "--package", selected, "--client", client);
                InvalidatePackage();
                status.Text = UiText.Get(result.RootElement.TryGetProperty("AlreadyRestored", out var already) && already.GetBoolean() ? "VisualAlreadyRestored" : "VisualRestored");
            });
        };
        removeAnimation.Click += async (_, _) =>
        {
            if (!Confirm("FieryRemoveConfirm")) return;
            await Operation(async () =>
            {
                string? folder = PackageStorage.Prepare(this, client);
                if (folder == null) return;
                string backup = PackageStorage.NewPath(folder, UiText.Get("BackupAnimationRemoval"), null);
                using var result = await ArtWorker.Run("fiery-remove", "--client", client, "--output", backup);
                if (Directory.Exists(backup)) PackageStorage.Describe(backup, client, UiText.Get("BackupAnimationRemoval"));
                InvalidatePackage();
                status.Text = result.RootElement.TryGetProperty("AlreadyRemoved", out var absent) && absent.GetBoolean() ? UiText.Get("FieryAlreadyRemoved") : UiText.Get("FieryRemoved") + " " + backup;
            });
        };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
        UpdatePreviews();
    }

    private bool Confirm(string key) => MessageBox.Show(this, UiText.Get(key) + Environment.NewLine + client, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
    internal void SelectFieryForTest() { frames.SelectedIndex = 2; border.SelectedIndex = 5; }
    internal void SelectAnimatedForTest()
    {
        border.SelectedIndex = 6;
        if (!export.Enabled || goldPreview.Image == null) throw new Exception("Animated frame export unavailable");
        package = "test-package"; UpdateActions();
        if (install.Enabled != FieryFrameSupport.Supported(client)) throw new Exception("Animated frame loader gate failed");
        package = null; UpdateActions();
    }
    private void InvalidatePackage() { package = null; installed = false; status.Text = ""; UpdateActions(); }
    private void UpdateActions()
    {
        options.Enabled = !busy; export.Enabled = !busy && (frames.SelectedIndex != 0 || border.SelectedIndex != 0 || banners.Checked || rarity.Checked || provision.Checked || bread.Checked)
            && frameAssetsReady && (border.SelectedIndex != 6 || FieryFrameSupport.Available);
        bool active = FieryFrameSupport.Installed(client);
        install.Enabled = !busy && package != null && !installed && (border.SelectedIndex == 6 ? FieryFrameSupport.Supported(client) : border.SelectedIndex == 0 || !active);
        restore.Enabled = !busy;
        removeAnimation.Enabled = !busy && active && FieryFrameSupport.Available;
        animationNote.Text = border.SelectedIndex == 6 ? UiText.Get(!FieryFrameSupport.Available ? "FieryRuntime" : FieryFrameSupport.Supported(client) ? "FieryScope" : "FieryLoader") : active ? UiText.Get("FieryActive") : "";
    }
    private void UpdatePreviews()
    {
        previews.Visible = true; frameAssetsReady = true;
        bronzePreview.Image?.Dispose(); bronzePreview.Image = null; goldPreview.Image?.Dispose(); goldPreview.Image = null;
        if (frames.SelectedIndex == 2 || border.SelectedIndex >= 2)
        {
            try
            {
                string root = Path.Combine(AppContext.BaseDirectory, "Assets", "RogueFrames");
                using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "manifest.json")));
                var records = manifest.RootElement.GetProperty("Frames");
                if (manifest.RootElement.GetProperty("Version").GetInt32() != 1) throw new InvalidDataException();
                var selected = new List<(string Key, PictureBox Control)>();
                if (frames.SelectedIndex == 2) selected.Add(("bronze", bronzePreview));
                if (border.SelectedIndex >= 2) selected.Add((border.SelectedIndex >= 5 ? "2087" : new[] { "2083", "2086", "2088" }[border.SelectedIndex - 2], goldPreview));
                foreach (var (key, control) in selected)
                {
                    byte[] raw = File.ReadAllBytes(Path.Combine(root, key + ".png"));
                    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw));
                    if (!hash.Equals(records.GetProperty(key).GetProperty("Sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                    using var stream = new MemoryStream(raw); using var image = Image.FromStream(stream);
                    if (image.Width != 1024 || image.Height != 2048) throw new InvalidDataException();
                    control.Image = new Bitmap(image);
                }
                frameAssetsReady = true;
            }
            catch (Exception e) when (e is IOException or ArgumentException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or System.Runtime.InteropServices.ExternalException)
            { frameAssetsReady = false; status.Text = UiText.Get("VisualFrameAssets"); }
        }
        bronzePreview.Invalidate(); goldPreview.Invalidate();
        UpdateActions();
    }
    private void PaintPlaceholder(PaintEventArgs e, PictureBox control, int mode)
    {
        if (control.Image == null && mode < 2)
            TextRenderer.DrawText(e.Graphics, UiText.Get(mode == 0 ? "VisualKeep" : "VisualNoFrames"), Font, control.ClientRectangle,
                Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
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
        bool animated = border.SelectedIndex == 6;
        string kind = UiText.Get(animated ? "BackupAnimated" : "BackupAppearance");
        string detail = UiText.Get("VisualGold") + ": " + border.Text + Environment.NewLine + UiText.Get("VisualBronze") + ": " + frames.Text + Environment.NewLine
            + string.Join(Environment.NewLine, new[] { banners, rarity, provision, bread }.Select(c => c.Text + ": " + c.Checked));
        string? nameDetail = animated ? null : border.SelectedIndex != 0 ? border.Text : frames.SelectedIndex != 0 ? UiText.Get("VisualBronze") + "-" + frames.Text : string.Join("-", new[] { banners, rarity, provision, bread }.Where(c => c.Checked).Select(c => c.Text));
        using var chooser = PackageStorage.SaveDialog(this, client, kind, nameDetail, animated ? 104 : 70);
        if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
        if (File.Exists(chooser.FileName) || Directory.Exists(chooser.FileName)) { MessageBox.Show(this, UiText.Get("ArtNewFolder")); return; }
        var args = new List<string> { animated ? "fiery-build" : "global-build", animated ? "--client" : "--target", client, "--output", chooser.FileName,
            "--bronze-frames", new[] { "keep", "none", "rogue" }[frames.SelectedIndex] };
        if (!animated)
        {
            args.AddRange(["--gold-frames", border.SelectedIndex >= 2 ? "rogue" : new[] { "keep", "none" }[border.SelectedIndex]]);
            if (border.SelectedIndex >= 2) args.AddRange(["--border", new[] { "2083", "2086", "2088", "2087" }[border.SelectedIndex - 2]]);
        }
        foreach (var (control, name) in new[] { (banners, "banners"), (rarity, "rarity"), (provision, "provision"), (bread, "bread") })
            if (control.Checked) args.Add("--hide-" + name);
        await Operation(async () => { status.Text = UiText.Get("VisualWorking"); using var result = await ArtWorker.Run(args.ToArray()); package = chooser.FileName; PackageStorage.Describe(package, client, kind, detail); installed = false; status.Text = UiText.Get("ArtExported") + " " + package; });
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { bronzePreview.Image?.Dispose(); goldPreview.Image?.Dispose(); }
        base.Dispose(disposing);
    }
}
