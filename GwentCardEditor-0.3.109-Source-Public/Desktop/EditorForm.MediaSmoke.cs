using System.Text.Json;

namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public async Task SmokeMediaOnly(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index; sections.SelectedIndex = 4;
            foreach (var size in new[] { new Size(1280, 900), new Size(1100, 760) })
            {
                Size = size; Application.DoEvents();
                if (!mediaCheck.Enabled) throw new Exception("Media check disabled");
                using var capture = new Bitmap(Width, Height);
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"media-{UiText.Language}-{size.Width}.png"));
            }
        }
        using var result = await RunMediaCheck(Path.Combine(output, "media-report.json"));
        if (!result.RootElement.GetProperty("Passed").GetBoolean() || !result.RootElement.GetProperty("SourceUnchanged").GetBoolean()) throw new Exception("Media check failed; see media-report.json");
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), JsonSerializer.Serialize(new { Passed=true, Languages=3, Sizes=2, Scope="media-compatibility", SourceUnchanged=true, InstalledIntoGame=false }));
    }
}
