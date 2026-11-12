namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeDiscardText(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        foreach (int id in new[] { 152107, 152308, 202512 })
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var group = project!.NumberGroups(id).Single(g => g.Display.LabelRole == "discard-selected-count" && g.Display.TextLinked);
            string labels = string.Join("\n", numberTable.Controls.OfType<Label>().Select(l => l.Text));
            if (!labels.Contains(UiText.Get("Ability.discard-selected-count"))) throw new Exception("Localized discard label missing");
            var others = project.Numbers(id).Where(n => group.Members.All(m => m.Id != n.Id)).ToDictionary(n => n.Id, n => n.Value);
            numberInputs[group.Display.Id].Value = 2;
            var draft = Draft();
            if (project.Preview(id, draft).All(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal))) throw new Exception("Discard description preview missing");
            project.Apply(id, draft); draftDirty = false; ShowCard(id); Application.DoEvents();
            if (!project.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => others[n.Id] == n.Value)) throw new Exception("Other values changed by discard count");
            string expected = UiText.Language switch { "ru-ru" => "сбросьте</keyword> 2 карты", "en-us" => "Discard</keyword> 2 cards", _ => "丢弃</keyword> 2 张牌" };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected)) throw new Exception("Localized discard count missing");
            sections.SelectedIndex = 1; textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language); Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"discard-{id}-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Discard UI smoke left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"Cards\":3,\"LabelsLocalized\":true,\"OtherValuesPreserved\":true,\"ClientModified\":false}");
    }
}
