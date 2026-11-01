namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeTargetPowerText(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        foreach (int id in new[] { 122302, 122214, 113310 })
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var group = project!.NumberGroups(id).Single(g => g.Display.Role == "targets" && g.Members.Count == 2);
            numberInputs[group.Display.Id].Value = 2;
            var edit = Draft();
            if (project.Preview(id, edit).All(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal))) throw new Exception("Target text preview missing");
            project.Apply(id, edit); draftDirty = false; ShowCard(id); Application.DoEvents();
            string expected = UiText.Language switch
            {
                "ru-ru" => id == 122302 ? "урона каждому из 2 отрядов" : id == 122214 ? "каждый из 2 дружественных отрядов" : "каждый из 2 отрядов",
                "en-us" => id == 122214 ? "each of 2 allied units" : "each of 2 units",
                _ => id == 122214 ? "2 个友军单位" : "2 个单位"
            };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected)) throw new Exception("Power target text missing from UI");
            sections.SelectedIndex = 1;
            textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language);
            Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"power-target-{id}-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Power target UI test left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"Cards\":3,\"ClientModified\":false}");
    }
}
