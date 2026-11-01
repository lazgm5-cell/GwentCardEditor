using System.Text.Json;

namespace GwentCardEditor;

internal sealed class EditorSettings
{
    public string? LastSource { get; set; }
    public Dictionary<string, bool> BalanceCouncilBySource { get; set; } = new();
    public string? BalanceSnapshotFile { get; set; }
    public string? BalanceReferenceDirectory { get; set; }
    public bool CouncilFor(string source) => !BalanceCouncilBySource.TryGetValue(Path.GetFullPath(source).ToUpperInvariant(), out bool enabled) || enabled;
    public string Language { get; set; } = "en-us";
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 10;
    public int Background { get; set; } = Color.FromArgb(247, 248, 250).ToArgb();
    public int Accent { get; set; } = Color.FromArgb(0, 105, 145).ToArgb();
    public static string Home => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GwentCardEditor");
    public static string SettingsFile => Path.Combine(Home, "settings.json");
    public static string CacheSnapshot(BalanceSnapshot snapshot)
    {
        snapshot.Validate();
        string path = Path.Combine(Home, "Balance", "Snapshots", $"balance-{snapshot.CapturedUtc:yyyyMMdd-HHmmssfffffff}-{snapshot.ContentHash[..12]}.json");
        snapshot.Save(path);
        return path;
    }
    public static EditorSettings Load(string? file = null)
    {
        try
        {
            var result = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(file ?? SettingsFile)) ?? new();
            result.BalanceCouncilBySource ??= new();
            return result;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string? file = null)
    {
        string target = Path.GetFullPath(file ?? SettingsFile);
        string directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public Font CreateFont()
    {
        try { return new Font(FontFamily, Math.Clamp(float.IsFinite(FontSize) ? FontSize : 10, 8, 16)); }
        catch (ArgumentException) { return new Font("Segoe UI", 10); }
    }
}

internal static class Theme
{
    public static void ButtonColors(Button button, EditorSettings settings)
    {
        Color background = Color.FromArgb(settings.Background);
        button.BackColor = button.Enabled ? background : Mix(background, Color.White, .65);
        button.ForeColor = Text(button.BackColor);
    }
    public static double Contrast(Color a,Color b)
    {
        double Luminance(Color c)
        {
            double Linear(byte value){double v=value/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}
            return .2126*Linear(c.R)+.7152*Linear(c.G)+.0722*Linear(c.B);
        }
        double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
    }
    public static Color Text(Color color)
    {
        Color dark=Color.FromArgb(25,28,30);
        Color result=Contrast(color,Color.White)>=Contrast(color,dark)?Color.White:dark;
        return Contrast(color,result)>=4.5?result:Contrast(color,Color.Black)>=Contrast(color,Color.White)?Color.Black:Color.White;
    }
    public static Color DisabledText(Color background)
    {
        Color foreground=Text(background),muted=Mix(background,foreground,.65);
        return Contrast(background,muted)>=4.5?muted:foreground;
    }
    public static Color Mix(Color a, Color b, double amount) => Color.FromArgb((int)(a.R * (1 - amount) + b.R * amount),
        (int)(a.G * (1 - amount) + b.G * amount), (int)(a.B * (1 - amount) + b.B * amount));
    public static void Apply(Control root, EditorSettings settings, Font font)
    {
        Color background = Color.FromArgb(settings.Background), foreground = Text(background);
        Color input = Mix(background, foreground, .06), accent = Color.FromArgb(settings.Accent);
        void Visit(Control control)
        {
            control.Font = font;
            control.BackColor = control is TextBoxBase or ComboBox or NumericUpDown or ListBox ? input : background;
            control.ForeColor = foreground;
            if (control is DataGridView grid)
            {
                grid.BackgroundColor = background;
                grid.GridColor = Mix(background, foreground, .25);
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = input;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
                grid.ColumnHeadersDefaultCellStyle.Font = font;
                grid.DefaultCellStyle.BackColor = background;
                grid.DefaultCellStyle.ForeColor = foreground;
                grid.DefaultCellStyle.Font = font;
                grid.DefaultCellStyle.SelectionBackColor = accent;
                grid.DefaultCellStyle.SelectionForeColor = Text(accent);
                grid.AlternatingRowsDefaultCellStyle.BackColor = input;
                grid.RowTemplate.Height = Math.Max(25, font.Height + 8);
                foreach (DataGridViewRow row in grid.Rows) row.Height = grid.RowTemplate.Height;
            }
            if (control is Button button)
            {
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Mix(background, foreground, .3);
                ButtonColors(button, settings);
            }
            foreach (Control child in control.Controls) Visit(child);
        }
        root.SuspendLayout();
        Visit(root);
        root.ResumeLayout(true);
    }
}

internal sealed class EditorCheckBox : CheckBox
{
    public EditorCheckBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Enabled || Appearance!=Appearance.Normal){base.OnPaint(e);return;}
        Color background=BackColor.A==0?Parent?.BackColor??SystemColors.Control:BackColor;
        Color text=Theme.DisabledText(background);
        using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,ClientRectangle);
        int side=Math.Max(13,(int)Math.Round(13*DeviceDpi/96d));bool rtl=RightToLeft==RightToLeft.Yes;
        var glyph=new Rectangle(rtl?ClientSize.Width-Padding.Right-side:Padding.Left,(ClientSize.Height-side)/2,side-1,side-1);
        using(var fill=new SolidBrush(Theme.Mix(background,Theme.Text(background),.12)))e.Graphics.FillRectangle(fill,glyph);
        using(var border=new Pen(text))e.Graphics.DrawRectangle(border,glyph);
        if(CheckState==CheckState.Checked)
        {
            using var pen=new Pen(text,Math.Max(1.6f,DeviceDpi/60f));
            e.Graphics.DrawLines(pen,[new PointF(glyph.Left+side*.2f,glyph.Top+side*.5f),new PointF(glyph.Left+side*.42f,glyph.Top+side*.72f),new PointF(glyph.Left+side*.8f,glyph.Top+side*.25f)]);
        }
        else if(CheckState==CheckState.Indeterminate)
        {
            using var fill=new SolidBrush(text);e.Graphics.FillRectangle(fill,glyph.Left+3,glyph.Top+side/2-1,Math.Max(1,side-6),3);
        }
        var bounds=rtl?new Rectangle(Padding.Left,0,Math.Max(0,glyph.Left-Padding.Left-4),ClientSize.Height)
            :new Rectangle(glyph.Right+4,0,Math.Max(0,ClientSize.Width-glyph.Right-4-Padding.Right),ClientSize.Height);
        TextRenderer.DrawText(e.Graphics,Text,Font,bounds,text,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix
            |TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis|(rtl?TextFormatFlags.Right:TextFormatFlags.Left));
    }
}

internal sealed class EditorComboBox : ComboBox
{
    public EditorComboBox() { DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 24; }
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        ItemHeight = Math.Max(24, Font.Height + 6);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool highlighted = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        Color back = highlighted ? SystemColors.Highlight : BackColor;
        Color fore = highlighted ? SystemColors.HighlightText : ForeColor;
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, e.Bounds);
        object? item = e.Index >= 0 && e.Index < Items.Count ? Items[e.Index] : SelectedItem;
        TextRenderer.DrawText(e.Graphics, GetItemText(item), Font, e.Bounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
}
