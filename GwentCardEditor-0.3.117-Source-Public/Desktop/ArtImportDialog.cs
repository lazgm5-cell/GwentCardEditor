using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.ComponentModel;
using System.Diagnostics;

namespace GwentCardEditor;

internal sealed class ArtCropCanvas : Control
{
    public Bitmap Source { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Zoom { get; set; } = 1;
    private double centerX = .5, centerY = .5;
    private Point? drag;
    private readonly double ratio;
    public ArtCrop Crop => ArtCrop.Calculate(Source.Width, Source.Height, Zoom, centerX, centerY, ratio);
    public RectangleF Viewport
    {
        get
        {
            float height = Math.Max(1, Math.Min(ClientSize.Height - 16, (ClientSize.Width - 16) / (float)ratio));
            float width = height * (float)ratio;
            return new((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
        }
    }
    public ArtCropCanvas(Bitmap source, double ratio = ArtCrop.Ratio)
    {
        Source = source; this.ratio = ratio; Dock = DockStyle.Fill; DoubleBuffered = true; ResizeRedraw = true;
        Cursor = Cursors.SizeAll; BackColor = Color.FromArgb(35, 35, 35);
    }
    public void ResetCrop() { Zoom = 1; centerX = centerY = .5; Invalidate(); }
    internal void SimulateDrag(Point from, Point to)
    {
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
        OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, to.X, to.Y, 0));
        OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
    }
    public Bitmap Render()
    {
        int height = (int)Math.Round(1024 / ratio);
        var bitmap = new Bitmap(1024, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        PaintImage(graphics, new RectangleF(0, 0, 1024, height));
        return bitmap;
    }
    private void PaintImage(Graphics graphics, RectangleF destination)
    {
        var region = Crop;
        graphics.Clear(Color.Black);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var attributes = new ImageAttributes(); attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(Source, Rectangle.Round(destination), (float)region.X, (float)region.Y,
            (float)region.Width, (float)region.Height, GraphicsUnit.Pixel, attributes);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var region = Crop; var view = Viewport;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.FillRectangle(Brushes.Black, view);
        e.Graphics.DrawImage(Source, view, new RectangleF((float)region.X, (float)region.Y, (float)region.Width, (float)region.Height), GraphicsUnit.Pixel);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Viewport.Contains(e.Location)) { drag = e.Location; Capture = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (drag is not Point previous) return;
        var region = Crop; var view = Viewport;
        centerX = (region.X + region.Width / 2 - (e.X - previous.X) * region.Width / view.Width) / Source.Width;
        centerY = (region.Y + region.Height / 2 - (e.Y - previous.Y) * region.Height / view.Height) / Source.Height;
        drag = e.Location; Invalidate();
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); drag = null; Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) drag = null; }
}

internal sealed class ArtImportDialog : Form
{
    private readonly ArtCropCanvas canvas;
    private readonly TrackBar zoom = new() { Minimum = 100, Maximum = 400, Value = 100, TickStyle = TickStyle.None, Dock = DockStyle.Fill };
    private readonly Button export = new() { AutoSize = true }, install = new() { AutoSize = true, Enabled = false }, reset = new() { AutoSize = true };
    private readonly Button restore = new() { AutoSize = true, Enabled = false }, openPackage = new() { AutoSize = true, Enabled = false };
    private readonly Button openLog = new() { AutoSize = true, Enabled = false };
    private readonly Label status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly string client;
    private readonly int cardId;
    private readonly bool cardback;
    private readonly IReadOnlyList<int>? cardbackTargets;
    private bool busy;
    private bool installed;
    private string? package;
    internal ArtCropCanvas Canvas => canvas;
    internal void SetZoomForTest(int value) => zoom.Value = value;

    public ArtImportDialog(Bitmap image, string client, int cardId, string name, bool cardback = false, IReadOnlyList<int>? cardbackTargets = null)
    {
        this.client = client; this.cardId = cardId; this.cardback = cardback; this.cardbackTargets = cardbackTargets;
        Text = UiText.Get(cardback ? "Cardback" : "Art") + ": " + name;
        Size = new Size(790, Math.Min(790, Screen.PrimaryScreen?.WorkingArea.Height - 60 ?? 790));
        MinimumSize = new Size(620, 580); StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
        canvas = new ArtCropCanvas(image, cardback ? 2d / 3 : ArtCrop.Ratio);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var scope = new Label { Text = UiText.Get(cardback ? "CardbackScope" : "ArtScope"), AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(scope, 0, 0);
        layout.SizeChanged += (_, _) => { scope.MaximumSize = status.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - 24), 0); };
        layout.Controls.Add(canvas, 0, 1);
        var zoomRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        zoomRow.Controls.Add(new Label { Text = UiText.Get("ArtZoom"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        zoomRow.Controls.Add(zoom, 1, 0); reset.Text = UiText.Get("ArtResetCrop"); zoomRow.Controls.Add(reset, 2, 0);
        layout.Controls.Add(zoomRow, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        export.Text = UiText.Get("ArtExport"); install.Text = UiText.Get("ArtInstall");
        var close = new Button { Text = UiText.Get("ArtClose"), AutoSize = true, DialogResult = DialogResult.Cancel };
        restore.Text = UiText.Language == "ru-ru" ? "Откатить установку" : "Restore installation";
        openPackage.Text = UiText.Language == "ru-ru" ? "Открыть пакет" : "Open package";
        openLog.Text = UiText.Language == "ru-ru" ? "Журнал ошибки" : "Error log";
        actions.Controls.AddRange([export, install, restore, openPackage, openLog, close]); layout.Controls.Add(actions, 0, 3); layout.Controls.Add(status, 0, 4);
        Controls.Add(layout); CancelButton = close;
        zoom.ValueChanged += (_, _) => { canvas.Zoom = zoom.Value / 100d; canvas.Invalidate(); InvalidatePackage(); };
        canvas.MouseUp += (_, _) => InvalidatePackage();
        reset.Click += (_, _) => { zoom.Value = 100; canvas.ResetCrop(); InvalidatePackage(); };
        export.Click += async (_, _) => await ExportArt();
        install.Click += async (_, _) =>
        {
            if (package == null || MessageBox.Show(this, UiText.Get("ArtInstallConfirm") + Environment.NewLine + client,
                Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            await Operation(async () =>
            {
                using var verified = await ArtWorker.Run("verify", "--package", package);
                using var result = await ArtWorker.Run("install", "--package", package, "--client", client);
                installed = true; restore.Enabled = true; openPackage.Enabled = true;
                status.Text = UiText.Get("ArtInstalled") + Environment.NewLine + (UiText.Language == "ru-ru" ? "Пакет проверен перед записью. Резервная копия и откат находятся в:" : "Package verified before write. Backup and rollback package:") + " " + package;
            });
        };
        restore.Click += async (_, _) =>
        {
            if (package == null || !installed || MessageBox.Show(this, (UiText.Language == "ru-ru" ? "Вернуть файлы из резервной копии?" : "Restore original files from this package?") + Environment.NewLine + client, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            await Operation(async () => { using var result = await ArtWorker.Run("restore", "--package", package, "--client", client); installed = false; restore.Enabled = false; status.Text = UiText.Language == "ru-ru" ? "Исходные файлы восстановлены из резервной копии." : "Original files restored from backup."; });
        };
        openPackage.Click += (_, _) => { if (package != null) Process.Start(new ProcessStartInfo("explorer.exe", package) { UseShellExecute = true }); };
        openLog.Click += (_, _) => { string path = Path.Combine(EditorSettings.Home, "art-worker.log"); if (File.Exists(path)) Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true }); };
        FormClosing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private void InvalidatePackage() { package = null; installed = false; install.Enabled = restore.Enabled = openPackage.Enabled = false; status.Text = ""; }
    private async Task Operation(Func<Task> operation)
    {
        busy = true; UseWaitCursor = true; export.Enabled = install.Enabled = restore.Enabled = reset.Enabled = zoom.Enabled = canvas.Enabled = false;
        try { await operation(); }
        catch (Exception e)
        {
            string log = Path.Combine(EditorSettings.Home, "art-worker.log");
            openLog.Enabled = File.Exists(log);
            status.Text = UiText.Error(e) + (openLog.Enabled ? Environment.NewLine + (UiText.Language == "ru-ru" ? "Подробности записаны в " : "Details logged to ") + log : "");
            MessageBox.Show(this, status.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            busy = false; UseWaitCursor = false;
            export.Enabled = true;
            reset.Enabled = zoom.Enabled = canvas.Enabled = !installed;
            install.Enabled = package != null && !installed;
            restore.Enabled = package != null && installed;
            openPackage.Enabled = package != null;
        }
    }
    private async Task ExportArt()
    {
        string packageKind = UiText.Get(cardback ? "Cardback" : "BackupArt");
        string packageDetail = cardbackTargets is { Count: > 1 } ? UiText.Get("CardbackAll") : Text + " (" + cardId + ")";
        using var chooser = PackageStorage.SaveDialog(this, client, packageKind, cardbackTargets is { Count: > 1 } ? "All-7" : cardId.ToString(System.Globalization.CultureInfo.InvariantCulture), 75);
        if (chooser == null || chooser.ShowDialog(this) != DialogResult.OK) return;
        if (File.Exists(chooser.FileName) || Directory.Exists(chooser.FileName)) { MessageBox.Show(this, UiText.Get("ArtNewFolder")); return; }
        await Operation(async () =>
        {
            status.Text = UiText.Get("ArtWorking");
            string temporary = Path.Combine(Path.GetTempPath(), "GwentArtCrop-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (var bitmap = canvas.Render()) bitmap.Save(temporary, ImageFormat.Png);
                var arguments = new List<string> { cardback ? "cardback-image-build" : "image-build", "--image", temporary, "--target", client,
                    cardback ? "--target-cardback" : "--target-card" };
                if (cardback && cardbackTargets is { Count: > 0 })
                    arguments.AddRange(cardbackTargets.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                else
                    arguments.Add(cardId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                arguments.AddRange(["--output", chooser.FileName]);
                using var result = await ArtWorker.Run(arguments.ToArray());
                package = chooser.FileName; PackageStorage.Describe(package, client, packageKind, packageDetail); installed = false; status.Text = UiText.Get("ArtExported") + " " + package;
            }
            finally { File.Delete(temporary); }
        });
    }
}
