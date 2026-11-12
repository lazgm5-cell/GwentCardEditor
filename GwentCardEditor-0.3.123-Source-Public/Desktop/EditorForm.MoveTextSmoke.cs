namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeMoveText(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        foreach (int id in new[] { 122403, 202215, 202498, 112205, 122107, 202187, 202326, 202782 })
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var group = project!.NumberGroups(id).Single(g => g.Display.Role == "choices" && g.Display.TextLinked
                && project.DescriptionBindings(id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0));
            var others = project.Numbers(id).Where(n => group.Members.All(m => m.Id != n.Id)).ToDictionary(n => n.Id, n => n.Value);
            numberInputs[group.Display.Id].Value = 2;
            var draft = Draft();
            if (project.Preview(id, draft).All(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal))) throw new Exception("Move description preview missing");
            project.Apply(id, draft); draftDirty = false; ShowCard(id); Application.DoEvents();
            if (!project.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => others[n.Id] == n.Value)) throw new Exception("Other values changed by move count");
            string expected = UiText.Language switch { "ru-ru" => "2 карты из вашей руки", "en-us" => "2 cards from your hand", _ => "2 张手牌" };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected)) throw new Exception("Localized move count missing");
            sections.SelectedIndex = 1; textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language); Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"move-{id}-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Move UI smoke left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"Cards\":8,\"OtherValuesPreserved\":true,\"ClientModified\":false}");
    }
}
