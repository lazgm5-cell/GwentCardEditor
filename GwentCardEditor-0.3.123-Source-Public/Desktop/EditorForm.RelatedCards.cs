namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private readonly Button relatedCards = new() { AutoSize = true, Enabled = false };

    private void OpenRelatedCards()
    {
        bool hadDraft = draftDirty;
        if (!ResolveDraft() || project == null || !selectedId.HasValue) return;
        if (hadDraft) ShowCard(selectedId.Value);
        using var dialog = new RelatedCardsDialog(project, selectedId.Value, settings, appearanceFont);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCardId.HasValue)
            NavigateToCard(dialog.SelectedCardId.Value);
    }

    private void NavigateToCard(int id)
    {
        if (project == null || project.Card(id).IsTutorial || !ResolveDraft()) return;
        var card = project.Card(id);
        rendering = true;
        currentGroup = card.Type == 1 ? (int)CardGroup.Leaders : CardCatalog.IsCatalogToken(card) ? (int)CardGroup.Tokens : (int)CardGroup.Cards;
        currentFactionIndex = Array.IndexOf(EditorProject.Factions, card.Faction);
        if (currentFactionIndex < 0) currentFactionIndex = Array.IndexOf(EditorProject.Factions, card.SecondaryFaction);
        if (currentFactionIndex < 0) { rendering = false; return; }
        currentHidden = card.Availability <= 0 && !CardCatalog.IsCatalogToken(card);
        currentSearch = id.ToString();
        group.SelectedIndex = currentGroup; factions.SelectedIndex = currentFactionIndex;
        hidden.Checked = currentHidden; search.Text = currentSearch; searchDelay.Stop();
        rendering = false; RefreshList(id);
    }

    private void SmokeRelatedCards(string output)
    {
        const int drone = 132304;
        NavigateToCard(drone);
        var parent = project!.RelatedCards(drone).First(r => r.ReferencedBy && project.Card(r.CardId).Editable);
        if (!relatedCards.Enabled) throw new InvalidOperationException("Related-card button unavailable.");
        foreach (int languageIndex in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = languageIndex;
            using var dialog = new RelatedCardsDialog(project, drone, settings, appearanceFont);
            dialog.Show(this); Application.DoEvents();
            dialog.Search.Text = "missing-card-000000";
            if (dialog.Cards.Rows.Count != 0 || dialog.OpenCard.Enabled) throw new InvalidOperationException("Related-card empty search failed.");
            dialog.Search.Text = parent.CardId.ToString();
            if (dialog.Cards.Rows.Count != 1 || !dialog.OpenCard.Enabled) throw new InvalidOperationException("Related-card ID search failed.");
            dialog.Search.Text = ""; Application.DoEvents();
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(Path.Combine(output, "related-cards-" + UiText.Language + ".png"));
            var row = dialog.Cards.Rows.Cast<DataGridViewRow>().First(r => (int)r.Tag! == parent.CardId);
            dialog.Cards.CurrentCell = row.Cells[0]; dialog.OpenCard.PerformClick();
            if (dialog.SelectedCardId != parent.CardId) throw new InvalidOperationException("Related-card selection failed.");
        }
        language.SelectedIndex = 0;
        NavigateToCard(parent.CardId);
        if (selectedId != parent.CardId || project.HasChanges) throw new InvalidOperationException("Related-card navigation failed.");
        NavigateToCard(drone);
        if (selectedId != drone || currentGroup != (int)CardGroup.Tokens) throw new InvalidOperationException("Related-card token return failed.");
        search.Text = ""; searchDelay.Stop(); group.SelectedIndex = 0; factions.SelectedIndex = 0; FilterChanged();
    }

    public void SmokeRelatedOnly(string output)
    {
        Directory.CreateDirectory(output);
        SmokeRelatedCards(output);
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"RelatedCardsSearchAndNavigation\":true,\"Scope\":\"related-only\"}");
    }
}
