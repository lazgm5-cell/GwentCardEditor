namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeSelectedDrawText(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        foreach (int id in new[] { 112205, 202498, 202187, 202782 })
        {
            language.SelectedIndex = locale; NavigateToCard(id); sections.SelectedIndex = 2; Application.DoEvents();
            var group = project!.NumberGroups(id).Single(g => g.Display.LabelRole == "draw-selected-count" && g.Display.TextLinked);
            if (group.Members.Count != 3) throw new Exception("Selection and draw not grouped");
            var others = project.Numbers(id).Where(n => group.Members.All(m => m.Id != n.Id)).ToDictionary(n => n.Id, n => n.Value);
            numberInputs[group.Display.Id].Value = 2;
            var draft = Draft();
            if (project.Preview(id, draft).All(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal))) throw new Exception("Selected draw description preview missing");
            project.Apply(id, draft); draftDirty = false; ShowCard(id); Application.DoEvents();
            if (!project.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => others[n.Id] == n.Value)) throw new Exception("Other values changed by selected draw");
            string expected = (UiText.Language, id) switch
            {
                ("ru-ru", 112205) => "возьмите 2 карты с категорией «Дракон»",
                ("ru-ru", 202187) => "возьмите 2 не нейтральные карты",
                ("ru-ru", 202782) => "возьмите 2 карты с <keyword=adrenaline>адреналином</keyword>",
                ("ru-ru", _) => "возьмите 2 карты из вашей колоды по выбору",
                ("en-us", 112205) => "Draw 2 Dragons",
                ("en-us", 202187) => "Draw 2 non-Neutral cards of your choice",
                ("en-us", 202782) => "Draw 2 cards with <keyword=adrenaline>Adrenaline</keyword>",
                ("en-us", _) => "Draw 2 cards of your choice",
                (_, 112205) => "抽 2 张“巨龙”牌",
                (_, 202187) => "选择并抽 2 张非中立牌",
                (_, 202782) => "选择抽 2 张带有<keyword=adrenaline>亢奋</keyword>",
                _ => "从牌组添加 2 张牌至手牌"
            };
            if (!textFields[(UiText.Language, "tooltip")].Text.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Localized selected draw count missing");
            sections.SelectedIndex = 1; textTabs.SelectedIndex = Array.IndexOf(EditorProject.Languages, UiText.Language); Application.DoEvents();
            using (var capture = new Bitmap(Width, Height))
            {
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"selected-draw-{id}-{UiText.Language}.png"));
            }
            project.Undo(); draftDirty = false; ShowCard(id);
            if (project.HasChanges) throw new Exception("Selected draw UI smoke left changes");
        }
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"Cards\":4,\"SelectionAndDrawSynchronized\":true,\"OtherValuesPreserved\":true,\"ClientModified\":false}");
    }
}
