namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeTargetStatusText(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        foreach (int id in new[] { 142202, 202227, 202463, 202258 })
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var group = project!.NumberGroups(id).Where(g => g.Display.Role == "targets" && g.Display.TextLinked
                && project.DescriptionBindings(id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0))
                .OrderBy(g => g.Display.Context == "Order" ? 0 : 1).First();
            var others = project.Numbers(id).Where(n => !group.Members.Any(m => m.Id == n.Id)).ToDictionary(n => n.Id, n => n.Value);
            numberInputs[group.Display.Id].Value = 2;
            var draft = Draft();
            if (project.Preview(id, draft).All(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal))) throw new Exception("Status description preview missing");
            project.Apply(id, draft); draftDirty = false; ShowCard(id); Application.DoEvents();
            if (!project.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => others[n.Id] == n.Value)) throw new Exception("Status amount or duration changed");
            bool enemy = id == 202227;
            string expected = UiText.Language switch
            {
                "ru-ru" => enemy ? "каждому из 2 вражеских отрядов" : "каждому из 2 дружественных отрядов",
                "en-us" => enemy ? "each of 2 enemy units" : "each of 2 allied units",
                _ => enemy ? "2 个敌军单位" : "2 个友军单位"
            };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected)) throw new Exception("Localized status recipient count missing");
            sections.SelectedIndex = 1; textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language); Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"status-target-{id}-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Status UI smoke left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"Cards\":4,\"OtherValuesPreserved\":true,\"ClientModified\":false}");
    }
}
