namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeDrawText(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        foreach (int id in new[] { 112110, 201776, 152308 })
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var group = project!.NumberGroups(id).Single(g => g.Display.Role == "draw-count" && g.Display.TextLinked);
            var others = project.Numbers(id).Where(n => n.Id != group.Display.Id).ToDictionary(n => n.Id, n => n.Value);
            numberInputs[group.Display.Id].Value = 2;
            var draft = Draft();
            if (project.Preview(id, draft).All(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal))) throw new Exception("Draw description preview missing");
            project.Apply(id, draft); draftDirty = false; ShowCard(id); Application.DoEvents();
            if (!project.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => others[n.Id] == n.Value)) throw new Exception("Other draw card values changed");
            string expected = UiText.Language switch { "ru-ru" => "возьмите 2 карты", "en-us" => "Draw 2 cards", _ => "抽 2 张牌" };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected)) throw new Exception("Localized draw count missing");
            sections.SelectedIndex = 1; textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language); Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"draw-{id}-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Draw UI smoke left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"Cards\":3,\"OtherValuesPreserved\":true,\"ClientModified\":false}");
    }
}
