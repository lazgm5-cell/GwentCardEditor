using System.Xml.Linq;
using GwentCardEditor;

internal static class TargetCountTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        const int id = 122301;
        var groups = source.NumberGroups(id).Where(g => g.Display.Role == "targets").ToArray();
        Check(groups.Length == 2 && groups.All(g => g.Members.Count == 2), "Ballista has two independent target selectors");
        var deploy = groups.Single(g => g.Display.Context == "Deploy");
        var order = groups.Single(g => g.Display.Context == "Order");
        var original = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
        Check(original.Count == 12, "all twelve available Ballista localizations covered");
        CardEdit Edit(AbilityNumberGroup group, int count) => source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => count) };
        Check(source.DescriptionBindings(id, deploy.Members.Select(n => n.Id)).Where(b => original.ContainsKey(b.Language)).All(b => b.Linked), "target count has confirmed localized bindings");
        var preview = source.Preview(id, Edit(deploy, 2));
        Check(preview.Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == 12 && !source.HasChanges, "preview contains twelve automatic description edits without mutation");
        Check(source.Apply(id, Edit(deploy, 2)), "deploy count applied");
        foreach (var (language, text) in original)
        {
            var parts = text.Split("\\n"); var changed = source.Text(id, language, "tooltip").Split("\\n");
            Check(parts[0] != changed[0] && parts[1] == changed[1], "only deploy phrase changed " + language);
        }
        Check(source.Text(id, "ru-ru", "tooltip").Contains("каждому из 2 вражеских отрядов"), "Russian deploy plural is explicit per target");
        Check(source.Text(id, "en-us", "tooltip").Contains("each of 2 enemy units"), "English deploy plural is explicit per target");
        Check(source.Text(id, "zh-cn", "tooltip").Contains("对 2 个敌军单位造成 {Damage}"), "Chinese deploy target count updated, damage preserved");
        Check(source.NumberGroups(id).Single(g => g.Display.Context == "Deploy" && g.Display.Role == "targets").Members.All(n => n.Value == 2), "both target bounds changed");
        Check(source.Apply(id, Edit(order, 3)), "order count independently applied");
        Check(source.Text(id, "en-us", "tooltip").Contains("each of 2 enemy units by {Damage}")
            && source.Text(id, "en-us", "tooltip").Contains("each of 3 enemy units by {Damage2}"), "deploy and order descriptions remain separate");
        Check(source.PendingDescriptionWarnings().All(w => w.CardId != id || w.Role != "targets" || w.Kind == "MissingText"), "no false target-description warnings");
        string exported = Path.Combine(scratch, "target-count-export");
        source.Export(exported);
        var reopened = ClientSource.Open(exported, Path.Combine(scratch, "target-count-reopened"));
        Check(reopened.Text(id, "ru-ru", "tooltip") == source.Text(id, "ru-ru", "tooltip"), "description persists through export and reopen");
        var reopenedGroups = reopened.NumberGroups(id).Where(g => g.Display.Role == "targets").ToArray();
        Check(reopenedGroups.Length == 2, "target groups persist through reopen");
        var reopenedEdit = reopened.CurrentEdit(id) with { Numbers = reopenedGroups.SelectMany(g => g.Members).ToDictionary(n => n.Id, _ => 1) };
        reopened.Apply(id, reopenedEdit);
        foreach (var (language, text) in original) Check(reopened.Text(id, language, "tooltip") == text, "reopened counts restore exact singular " + language);
        source.Undo(); source.Undo();
        Check(!source.HasChanges, "undo restores graph and all descriptions");
        source.Apply(id, Edit(deploy, 2)); source.Apply(id, Edit(deploy, 1));
        Check(!source.HasChanges, "two-to-one round trip is byte-exact");
        foreach (int count in new[] { 4, 10, 21, 99 })
        {
            source.Apply(id, Edit(deploy, count));
            string phrase = count == 21 ? "каждому из 21 вражеского отряда" : $"каждому из {count} вражеских отрядов";
            Check(source.Text(id, "ru-ru", "tooltip").Contains(phrase), "plural wording remains valid for " + count);
            source.Apply(id, Edit(deploy, 1));
            Check(!source.HasChanges, "large count round trip " + count);
        }
        var damage = source.Numbers(id).Single(n => n.Editable && n.Name == "Damage");
        source.Apply(id, source.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [damage.Id] = damage.Value + 1 } });
        Check(original.All(p => source.Text(id, p.Key, "tooltip") == p.Value), "damage-only edit leaves target wording unchanged");
        source.Reset();
        bool rejected = false;
        try { source.Apply(id, source.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [deploy.Members[0].Id] = 2 } }); }
        catch (InvalidOperationException e) when (e.Message == "TargetCountTextConflict") { rejected = true; }
        Check(rejected && !source.HasChanges, "inconsistent target bounds blocked atomically");
        var conflict = Edit(deploy, 2);
        conflict = conflict with { Texts = conflict.Texts.ToDictionary(p => p.Key, p => p.Key == "en-us"
            ? (IReadOnlyDictionary<string, string>)p.Value.ToDictionary(f => f.Key, f => f.Key == "tooltip" ? "Custom text." : f.Value) : p.Value) };
        rejected = false;
        try { source.Apply(id, conflict); }
        catch (InvalidOperationException e) when (e.Message == "TargetCountTextConflict") { rejected = true; }
        Check(rejected && !source.HasChanges, "conflicting manual description is not overwritten");
        int supported = 0;
        foreach (var card in source.Cards.Where(c => c.Editable))
        foreach (var group in source.NumberGroups(card.Id).Where(g => g.Display.Role == "targets" && g.Members.Count == 2 && g.Display.Value == 1))
        {
            var bindings = source.DescriptionBindings(card.Id, group.Members.Select(n => n.Id));
            if (!bindings.Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0)) continue;
            var before = source.AvailableLanguages.Where(l => source.HasText(card.Id, l, "tooltip")).ToDictionary(l => l, l => source.Text(card.Id, l, "tooltip"));
            var edit = source.CurrentEdit(card.Id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 2) };
            source.Apply(card.Id, edit);
            Check(before.All(p => source.Text(card.Id, p.Key, "tooltip") != p.Value), $"all localized target phrases synchronized {card.Id}/{group.Display.Context}/{group.Display.Id}");
            source.Undo();
            Check(!source.HasChanges, $"target text audit preserves client {card.Id}/{group.Display.Id}");
            supported++;
        }
        Check(supported >= 2, "verified power target text groups: " + supported);
        return passed;
    }
}
