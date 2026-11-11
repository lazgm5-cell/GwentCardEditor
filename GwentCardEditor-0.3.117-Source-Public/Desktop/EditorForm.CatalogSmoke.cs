namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeCatalogVisibility(string output)
    {
        Directory.CreateDirectory(output);
        foreach (int locale in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = locale;
            hidden.Checked = false;
            foreach (int id in new[] { 202140, 202491, 203172 })
            {
                var card = project!.Card(id);
                group.SelectedIndex = card.Type == 1 ? (int)CardGroup.Leaders : (int)CardGroup.Cards;
                search.Text = ""; searchDelay.Stop();
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, card.Faction);
                search.Text = id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 0;
                balanceCouncil.Checked = true; Application.DoEvents();
                if (selectedId != id || !card.Editable) throw new Exception("Restored card cannot be selected: " + id);
                if (card.Type == 16)
                {
                    if (fields["Power"].Enabled || fields["Armor"].Enabled || fields["Provision"].Enabled || !fields["Tier"].Enabled)
                        throw new Exception("Stratagem basic field eligibility incorrect");
                    if (!identity.Text.Contains(UiText.Get("Stratagem"))) throw new Exception("Stratagem type not translated");
                    sections.SelectedIndex = 2;
                    if (id == 202140)
                    {
                        var number = project.Numbers(id).First(n => n.Editable && n.Role == "boost");
                        if (!numberInputs.TryGetValue(number.Id, out var input)) throw new Exception("Stratagem boost input missing");
                        rendering = true; input.Value = number.DisplayValue + 1; rendering = false;
                        if (project.Preview(id, Draft()).Count == 0) throw new Exception("Stratagem draft loses ability changes");
                        rendering = true; input.Value = number.DisplayValue; rendering = false; draftDirty = false;
                    }
                }
                using var capture = new Bitmap(Width, Height);
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"catalog-{id}-{UiText.Language}.png"));
            }
            group.SelectedIndex = (int)CardGroup.All;
            search.Text = "202571"; searchDelay.Stop(); FilterChanged();
            if (grid.Rows.Count != 0) throw new Exception("202571 is no longer hidden");
        }
        if (project!.HasChanges || draftDirty) throw new Exception("Catalog UI smoke modified card data");
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Languages\":3,\"BalanceCouncilEnabled\":true,\"ClientModified\":false}");
    }
}
