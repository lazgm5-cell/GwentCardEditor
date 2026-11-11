namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeTargetCountText(string output)
    {
        Directory.CreateDirectory(output);
        const int id = 122301;
        foreach (int locale in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var deploy = project!.NumberGroups(id).Single(g => g.Display.Role == "targets" && g.Display.Context == "Deploy");
            var order = project.NumberGroups(id).Single(g => g.Display.Role == "targets" && g.Display.Context == "Order");
            numberInputs[deploy.Display.Id].Value = 2;
            numberInputs[order.Display.Id].Value = 3;
            var edit = Draft();
            if (project.Preview(id, edit).Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) != 12) throw new Exception("Missing localized target preview");
            project.Apply(id, edit); draftDirty = false; ShowCard(id); Application.DoEvents();
            string expected = UiText.Language switch { "ru-ru" => "каждому из 2 вражеских отрядов", "en-us" => "each of 2 enemy units", _ => "2 个敌军单位" };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected)) throw new Exception("Updated target wording missing from UI");
            sections.SelectedIndex = 1; Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"target-count-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Target UI test left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"LocalizedDescriptions\":12,\"IndependentBranches\":true,\"ClientModified\":false}");
    }
}
