namespace GwentCardEditor;

internal sealed class AppearanceDialog : Form
{
    private readonly List<Font> previewFonts = new();
    private readonly ToolTip backupTip = new();
    public EditorSettings Result { get; }
    public AppearanceDialog(EditorSettings settings)
    {
        Result = new EditorSettings { LastSource = settings.LastSource, Language = settings.Language, FontFamily = settings.FontFamily,
            FontSize = settings.FontSize, Background = settings.Background, Accent = settings.Accent,
            BalanceSnapshotFile = settings.BalanceSnapshotFile, BalanceReferenceDirectory = settings.BalanceReferenceDirectory,
            BackupRoot = settings.BackupRoot,
            BalanceCouncilBySource = new(settings.BalanceCouncilBySource) };
        Text = UiText.Get("Appearance");
        Size = new Size(650, 380);
        MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        var presets = new EditorComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        presets.Items.AddRange([UiText.Get("Light"), UiText.Get("Dark"), UiText.Get("Custom")]);
        presets.SelectedIndex = 2;
        var background = new Button { AutoSize = true, Dock = DockStyle.Fill };
        var accent = new Button { AutoSize = true, Dock = DockStyle.Fill };
        var font = new Button { AutoSize = true, Dock = DockStyle.Fill };
        var backupPath = new Button { AutoSize = false, Width = 300, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
        var resetBackup = new Button { Text = UiText.Get("BackupFolderDefault"), AutoSize = true };
        void RefreshBackupPath()
        {
            backupPath.Text = Result.BackupRoot ?? Path.Combine(PackageStorage.ProgramRoot, "Backups");
            backupTip.SetToolTip(backupPath, backupPath.Text);
        }
        backupPath.Click += (_, _) =>
        {
            using var chooser = new FolderBrowserDialog { Description = UiText.Get("BackupChooseFolder"), UseDescriptionForTitle = true,
                InitialDirectory = Result.BackupRoot is { Length: > 0 } selected ? selected : PackageStorage.ProgramRoot };
            if (chooser.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                string chosenPath = Path.GetFullPath(chooser.SelectedPath);
                Directory.CreateDirectory(chosenPath);
                using var probe = new FileStream(Path.Combine(chosenPath, ".write-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                Result.BackupRoot = chosenPath;
                RefreshBackupPath();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { MessageBox.Show(this, UiText.Get("BackupLocationError"), UiText.Get("BackupChooseFolder"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        resetBackup.Click += (_, _) => { Result.BackupRoot = null; RefreshBackupPath(); };
        var backupControls = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        backupControls.Controls.AddRange([backupPath, resetBackup]);
        void RefreshPreview()
        {
            Font previewFont = Result.CreateFont();
            previewFonts.Add(previewFont);
            Theme.Apply(table, Result, previewFont);
            background.BackColor = Color.FromArgb(Result.Background);
            background.ForeColor = Theme.Text(background.BackColor);
            background.Text = $"#{background.BackColor.R:X2}{background.BackColor.G:X2}{background.BackColor.B:X2}";
            accent.BackColor = Color.FromArgb(Result.Accent);
            accent.ForeColor = Theme.Text(accent.BackColor);
            accent.Text = $"#{accent.BackColor.R:X2}{accent.BackColor.G:X2}{accent.BackColor.B:X2}";
            font.Text = $"{Result.FontFamily}, {Result.FontSize:0.#} pt";
        }
        void ColorPicker(bool isBackground)
        {
            using var picker = new ColorDialog { FullOpen = true, Color = Color.FromArgb(isBackground ? Result.Background : Result.Accent) };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            if (isBackground) Result.Background = picker.Color.ToArgb(); else Result.Accent = picker.Color.ToArgb();
            presets.SelectedIndex = 2;
            RefreshPreview();
        }
        background.Click += (_, _) => ColorPicker(true);
        accent.Click += (_, _) => ColorPicker(false);
        font.Click += (_, _) =>
        {
            using var picker = new FontDialog { Font = Result.CreateFont(), MinSize = 8, MaxSize = 16, FontMustExist = true,
                ShowEffects = false, AllowVerticalFonts = false };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            Result.FontFamily = picker.Font.FontFamily.Name;
            Result.FontSize = picker.Font.SizeInPoints;
            RefreshPreview();
        };
        presets.SelectedIndexChanged += (_, _) =>
        {
            if (presets.SelectedIndex == 0) { Result.Background = Color.FromArgb(247, 248, 250).ToArgb(); Result.Accent = Color.FromArgb(0, 105, 145).ToArgb(); }
            if (presets.SelectedIndex == 1) { Result.Background = Color.FromArgb(32, 34, 36).ToArgb(); Result.Accent = Color.FromArgb(55, 180, 160).ToArgb(); }
            RefreshPreview();
        };
        foreach (var (key, input) in new (string, Control)[] { ("Theme", presets), ("BackgroundColor", background), ("AccentColor", accent), ("Font", font) })
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = UiText.Get(key), AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            table.Controls.Add(input, 1, row);
        }
        int backupRow = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = UiText.Get("BackupFolderSetting"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, backupRow);
        table.Controls.Add(backupControls, 1, backupRow);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = UiText.Get("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        actions.Controls.AddRange([ok, cancel]);
        int last = table.RowCount++;
        table.Controls.Add(actions, 0, last);
        table.SetColumnSpan(actions, 2);
        Controls.Add(table);
        RefreshBackupPath();
        AcceptButton = ok;
        CancelButton = cancel;
        RefreshPreview();
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { backupTip.Dispose(); foreach (Font font in previewFonts) font.Dispose(); }
    }
}
