using System.Text.Json;
using System.Drawing.Imaging;

namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public async Task SmokeCardbacks(string output)
    {
        if (project?.Client == null || !ArtWorker.CardbacksAvailable) throw new Exception("Cardback runtime missing");
        Directory.CreateDirectory(output);
        string client = Path.GetDirectoryName(project.Client.ArchivePath)!;
        using var catalog = await ArtWorker.Run("cardback-catalog", "--target", client);
        using var bitmap = new Bitmap(1200, 800);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.SeaGreen);
            graphics.FillRectangle(Brushes.Crimson, 0, 0, 400, 800);
            graphics.FillRectangle(Brushes.RoyalBlue, 800, 0, 400, 800);
        }
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index;
            sections.SelectedIndex = 4; Application.DoEvents(); RefreshArtState();
            if (!cardbackImport.Enabled || !cardbackRestore.Enabled) throw new Exception("Cardback actions unavailable");
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"visuals-{UiText.Language}.png"));
            }
            using (var picker = new CardbackPicker(catalog.RootElement))
            {
                if (picker.PreviewCount != 7) throw new Exception("Missing cardback previews");
                picker.Show(this); Application.DoEvents();
                using var capture = new Bitmap(picker.Width, picker.Height);
                picker.DrawToBitmap(capture, new Rectangle(Point.Empty, picker.Size));
                capture.Save(Path.Combine(output, $"picker-{UiText.Language}.png"));
                picker.Close();
            }
            using var dialog = new ArtImportDialog(bitmap, client, 25000400, string.Format(UiText.Get("CardbackDefault"), 1), true);
            dialog.Show(this); Application.DoEvents();
            dialog.SetZoomForTest(200);
            var before = dialog.Canvas.Crop;
            var view = dialog.Canvas.Viewport;
            var from = new Point((int)(view.X + view.Width / 2), (int)(view.Y + view.Height / 2));
            dialog.Canvas.SimulateDrag(from, new Point(from.X + 35, from.Y));
            if (dialog.Canvas.Crop.X >= before.X) throw new Exception("Cardback drag failed");
            foreach (Size size in new[] { new Size(790, 760), new Size(620, 580) })
            {
                dialog.Size = size; Application.DoEvents();
                if (dialog.Canvas.Height < 100 || dialog.Canvas.Viewport.Bottom > dialog.Canvas.Height) throw new Exception("Cardback preview clipped");
                using var capture = new Bitmap(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(capture, new Rectangle(Point.Empty, dialog.Size));
                capture.Save(Path.Combine(output, $"crop-{UiText.Language}-{size.Width}.png"));
            }
            using var rendered = dialog.Canvas.Render();
            if (rendered.Width != 1024 || rendered.Height != 1536) throw new Exception("Wrong cardback crop ratio");
            rendered.Save(Path.Combine(output, "crop.png"), ImageFormat.Png);
            dialog.Close();
        }
        project.Client.Verify();
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new { Passed = true, Scope = "cardback-image", Languages = 3, Previews = 7, GameFilesUnchanged = true }));
    }
}
