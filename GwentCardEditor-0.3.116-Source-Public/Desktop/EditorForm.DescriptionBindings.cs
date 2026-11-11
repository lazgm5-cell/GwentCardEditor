namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeNumberLabels(string output)
    {
        Directory.CreateDirectory(output);
        var audit = project!.CreateAbilityMeaningAudit();
        if (audit.Rows.Any(r => r.LabelRole is "highest-power-levels" or "lowest-power-levels")
            || !audit.SemanticLocks.Any(n => n.Reason == "PowerExtremumSelection"))
            throw new Exception("Extremum selection controls were not hidden");
        foreach (int locale in Enumerable.Range(0, 3))
        {
            language.SelectedIndex = locale;
            foreach (var row in audit.Rows)
            {
                string helpKey = (row.HelpKey == AbilityMeaningVocabulary.Help(row.Role) ? "MeaningHelp." : "AbilityHelp.") + row.HelpKey;
                if (!UiText.EnglishTemplate.ContainsKey(helpKey) || UiText.Get(helpKey) == helpKey) throw new Exception("Missing meaning translation: " + helpKey);
                foreach (var fact in row.Facts.Where(f => f.Kind is not ("Comparison" or "Template")))
                {
                    string factKey = fact.Kind == "Action" ? (char.IsUpper(fact.Value[0]) ? "Meaning.Action." : "Ability.") + fact.Value : "Meaning." + fact.Kind + "." + fact.Value;
                    if (!UiText.EnglishTemplate.ContainsKey(factKey)) throw new Exception("Unknown meaning fact: " + factKey);
                }
            }
            foreach (int id in new[] { 200049, 202828, 112103, 112109, 112112, 142207, 112208, 122310, 132305, 113309, 132312 })
            {
                group.SelectedIndex = (int)CardGroup.Cards;
                search.Text = ""; searchDelay.Stop();
                factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project!.Card(id).Faction);
                search.Text = id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 2;
                Application.DoEvents();
                string labels = string.Join("\n", numberTable.Controls.OfType<Label>().Select(l => l.Text));
                if (labels.Contains(UiText.Get("Uses") + ":")) throw new Exception("Technical usage count still visible");
                if (id is 200049 or 113309 or 132312 && (labels.Contains(UiText.Get("Ability.highest-power-levels"))
                    || labels.Contains(UiText.Get("Ability.lowest-power-levels"))
                    || project.Numbers(id).Where(n => n.Reason == "PowerExtremumSelection").Any(n => numberInputs.ContainsKey(n.Id))))
                    throw new Exception("Technical strongest-unit selection still visible");
                if (id == 202828 && (numberInputs.ContainsKey(318) || numberInputs.ContainsKey(319)))
                    throw new Exception("Single-row selection still editable");
                if (id == 112103 && (!labels.Contains(">=") || !labels.Contains(UiText.Get("Meaning.Action.Destroy"))))
                    throw new Exception("Geralt threshold and target action missing");
                if (id == 112208 && (!labels.Contains(UiText.Get("Meaning.CopyPlayer.Owner")) || !labels.Contains(UiText.Get("Meaning.CopyPlayer.Opponent"))))
                    throw new Exception("Operator copy sides missing");
                if (id == 122310 && !labels.Contains(UiText.Get("Meaning.CopyDeckPosition.Bottom"))) throw new Exception("Deck-bottom annotation missing");
                using var capture = new Bitmap(Width, Height);
                DrawToBitmap(capture, new Rectangle(Point.Empty, Size));
                capture.Save(Path.Combine(output, $"numbers-{id}-{UiText.Language}.png"));
            }
        }
        if (project!.HasChanges || draftDirty) throw new Exception("Number label smoke changed card data");
        File.WriteAllText(Path.Combine(output, "ui-smoke.json"), "{\"Passed\":true,\"Cards\":11,\"Languages\":3,\"AllMeaningKeysChecked\":true,\"ClientModified\":false}");
    }

    private string DescriptionNote(int id, AbilityNumberGroup group)
    {
        if (project == null) return "";
        var lines = new List<string>();
        if (group.Display.HelpKey is string key) lines.Add(UiText.Get("AbilityHelp." + key));
        else if (AbilityMeaningVocabulary.Help(group.Display.Role) is string help) lines.Add(UiText.Get("MeaningHelp." + help));
        if (group.Display.Role.StartsWith("base-", StringComparison.Ordinal)) lines.Add(UiText.Get("Meaning.Base"));
        if (group.Display.Role.StartsWith("alternate-", StringComparison.Ordinal)) lines.Add(UiText.Get("Meaning.Alternative"));
        foreach (var fact in group.Display.MeaningFacts)
        {
            if (fact.Kind == "Template" && int.TryParse(fact.Value, out int card))
                lines.Add(UiText.Get("Meaning.Template") + " " + (project.Cards.Any(c => c.Id == card) ? project.Name(card, UiText.Language) : UiText.Get("Meaning.UnknownTemplate")));
            else if (fact.Kind == "Comparison") lines.Add(UiText.Get("Meaning.Comparison") + " " + fact.Value + " " + UiText.Get("Meaning.ThisValue"));
            else if (fact.Kind == "Action") lines.Add(UiText.Get("Meaning.Action") + " " + (char.IsUpper(fact.Value[0])
                ? UiText.Get("Meaning.Action." + fact.Value) : AbilityLabel(fact.Value)));
            else lines.Add(UiText.Get("Meaning." + fact.Kind + "." + fact.Value));
        }
        string excerpt = project.DescriptionExcerpt(id, group.Members.Select(n => n.Id), UiText.Language);
        if (excerpt.Length > 0) lines.Add(UiText.Get("Meaning.Excerpt") + " " + excerpt.Replace("[[value]]", "[" + UiText.Get("Meaning.ThisValue") + "]")
            .Replace("[[formula]]", "[" + UiText.Get("Meaning.Formula") + "]"));
        return string.Join(Environment.NewLine, lines.Distinct());
    }

    private static void AssertNoDescriptionDiagnostics(Control root)
    {
        string[] diagnostic = UiText.EnglishTemplate.Keys.Where(k => k.StartsWith("DescriptionBinding.", StringComparison.Ordinal)
            || k is "TextLinked" or "DescriptionWarning" or "DescriptionAbsentWarning").Select(UiText.Get).ToArray();
        void Visit(Control control)
        {
            if (control is Label && diagnostic.Any(text => control.Text.Contains(text, StringComparison.Ordinal)))
                throw new Exception("Technical description diagnostic still visible");
            foreach (Control child in control.Controls) Visit(child);
        }
        Visit(root);
    }

    public void SmokeDescriptionBindings(string output)
    {
        Directory.CreateDirectory(output);
        const int id = 202122;
        group.SelectedIndex = (int)CardGroup.Cards;
        search.Text = ""; searchDelay.Stop();
        factions.SelectedIndex = Array.IndexOf(EditorProject.Factions, project!.Card(id).Faction);
        search.Text = id.ToString(); searchDelay.Stop(); FilterChanged(); sections.SelectedIndex = 2;
        var number = project.Numbers(id).Single(n => n.Editable && n.Role == "alternate-damage");
        var draft = project.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [number.Id] = number.Value + 1 } };
        var changes = project.Preview(id, draft);
        var warnings = project.DescriptionWarnings(id, draft);
        if (!warnings.Any(w => w.Language == "en-us" && w.Kind == "Unlinked") || project.HasChanges)
            throw new InvalidOperationException("Description warning diagnostics failed.");
        for (int index = 0; index < 3; index++)
        {
            language.SelectedIndex = index; Application.DoEvents();
            AssertNoDescriptionDiagnostics(numberTable);
            using var dialog = CreateChangeConfirmation(changes, warnings);
            dialog.Show(this); Application.DoEvents();
            AssertNoDescriptionDiagnostics(dialog);
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
            bitmap.Save(Path.Combine(output, "description-confirm-" + UiText.Language + ".png"));
            dialog.Close();
        }
        language.SelectedIndex = 0;
        if (project.HasChanges || project.PendingDescriptionWarnings().Count != 0)
            throw new InvalidOperationException("Description confirmation must not modify the project.");
        File.WriteAllText(Path.Combine(output, "description-ui.json"), "{\"Passed\":true,\"Languages\":3,\"DiagnosticsHidden\":true,\"BackendDiagnosticsPreserved\":true,\"ClientModified\":false}");
    }
}
