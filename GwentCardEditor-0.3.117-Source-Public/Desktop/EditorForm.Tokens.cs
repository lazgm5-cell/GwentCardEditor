namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private void SmokeTokens(string output)
    {
        var snapshot = balanceSnapshot; var reference = balanceReference;
        balanceSnapshot = null; balanceReference = null; RebuildBalanceProjection();
        group.SelectedIndex = (int)CardGroup.Tokens;
        balanceCouncil.Checked = true;
        var tokens = project!.Cards.Where(c => c.IsToken && c.Editable && !c.IsTutorial).ToArray();
        if (tokens.Length == 0) throw new InvalidOperationException("Missing token fixtures.");
        foreach (var token in tokens)
        {
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, token.Faction);
            search.Text = token.Id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 0;
            if (selectedId != token.Id || !fields["Provision"].Enabled || fields["Power"].Enabled != (token.Type == 4)
                || BalanceCardView(token).Provision != token.Provision || balanceState.Text != UiText.Get("TokenLocalBalance")
                || !balanceNote.Text.Contains(UiText.Get("TokenLocalNote"), StringComparison.Ordinal)
                || project.Preview(token.Id, Draft()).Count != 0)
                throw new InvalidOperationException("Token local controls failed: " + token.Id);
            string field = token.Type == 4 ? "Power" : "Provision";
            int old = field == "Power" ? token.Power : token.Provision;
            ((NumericUpDown)fields[field]).Value = old + 1;
            if (!CommitDraft(false) || project.Changes().Count != 1
                || (field == "Power" ? project.Card(token.Id).Power : project.Card(token.Id).Provision) != old + 1)
                throw new InvalidOperationException("Token local apply failed: " + token.Id);
            project.Undo(); RefreshList(token.Id);
            if (project.HasChanges || draftDirty) throw new InvalidOperationException("Token local undo failed.");
        }
        var sample = tokens.First(c => c.Type == 4);
        search.Text = sample.Id.ToString(); searchDelay.Stop();
        factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, sample.Faction); FilterChanged();
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index; Application.DoEvents();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "token-local-" + UiText.Language + ".png"));
        }
        language.SelectedIndex = 0;
        balanceCouncil.Checked = false; balanceSnapshot = snapshot; balanceReference = reference; RebuildBalanceProjection();
        search.Text = ""; searchDelay.Stop(); group.SelectedIndex = 0; factions.SelectedIndex = 0; FilterChanged();
    }
}
