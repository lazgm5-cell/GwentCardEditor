namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private void SmokeLeaders(string output)
    {
        var oldSnapshot = balanceSnapshot;
        balanceSnapshot = null; RebuildBalanceProjection();
        group.SelectedIndex = (int)CardGroup.Leaders;
        balanceCouncil.Checked = true;
        foreach (var leader in project!.Cards.Where(c => c.Type == 1 && c.Editable).ToArray())
        {
            search.Text = ""; searchDelay.Stop();
            factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, leader.Faction);
            search.Text = leader.Id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 0;
            if (selectedId != leader.Id || grid.Rows.Count != 1 || !fields["Provision"].Enabled
                || new[] { "Power", "Armor", "Tier", "Rarity", "FactionId", "PrimaryCategory" }.Any(key => fields[key].Enabled)
                || categories.Enabled || !balanceNote.Text.Contains(UiText.Get("LeaderLocalNote"), StringComparison.Ordinal)
                || balanceState.Text != UiText.Get("LeaderLocalBalance")
                || ChangeName("Provision") != UiText.Get("LeaderProvision") || project.Preview(leader.Id, Draft()).Count != 0)
                throw new InvalidOperationException("Leader controls or local balance failed: " + leader.Id);
            ((NumericUpDown)fields["Provision"]).Value = leader.Provision + 1;
            if (!CommitDraft(false) || project.Card(leader.Id).Provision != leader.Provision + 1 || project.Changes().Count != 1)
                throw new InvalidOperationException("Leader provision apply failed: " + leader.Id);
            project.Undo(); RefreshList(leader.Id);
            if (project.HasChanges || draftDirty) throw new InvalidOperationException("Leader undo failed.");
        }
        search.Text = "200166"; searchDelay.Stop(); factions.SelectedIndex = 3; FilterChanged();
        var damage = project.Numbers(200166).First(n => n.Editable && n.Role == "damage");
        ((NumericUpDown)fields["Provision"]).Value++;
        numberInputs[damage.Id].Value++;
        numberInputs[-1].Value = 5;
        if (!CommitDraft(false) || project.Numbers(200166).Single(n => n.Id == damage.Id).Value != damage.Value + 1
            || project.InitialCharges(200166) is not { Editable: true, Value: 5 })
            throw new InvalidOperationException("Leader combined numeric edit failed.");
        project.Undo(); RefreshList(200166);
        foreach (int index in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = index; Application.DoEvents();
            sections.SelectedIndex = 0;
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(output, "leader-" + UiText.Language + ".png"));
        }
        language.SelectedIndex = 0;
        if (project.HasChanges) throw new InvalidOperationException("Leader combined undo failed.");
        balanceCouncil.Checked = false; balanceSnapshot = oldSnapshot; RebuildBalanceProjection();
        search.Text = ""; searchDelay.Stop(); group.SelectedIndex = 0; factions.SelectedIndex = 0; FilterChanged();
    }
}
