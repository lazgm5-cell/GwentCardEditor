using GwentCardEditor;
using System.Xml.Linq;

internal static class CatalogVisibilityTests
{
    public static IReadOnlyList<string> Run(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var visible = EditorProject.Factions.SelectMany(f => CardCatalog.Query(project, f, "en-us", group: CardGroup.All)).Select(c => c.Id).ToHashSet();
        foreach (var card in project.Cards.Where(c => !c.IsTutorial && (EditorProject.Factions.Contains(c.Faction) || EditorProject.Factions.Contains(c.SecondaryFaction))))
            Check(visible.Contains(card.Id) == !CardCatalog.IsHidden(card), "visibility policy " + card.Id);
        foreach (int id in new[] { 202290, 202395, 202394, 203190, 202570, 202571 })
        {
            Check(!visible.Contains(id) && !project.Card(id).Editable, "explicit hidden card retained " + id);
            Check(EditorProject.Factions.SelectMany(f => CardCatalog.Query(project, f, "en-us", group: CardGroup.All, showHidden: true)).Any(c => c.Id == id), "hidden card available with explicit hidden filter " + id);
        }
        foreach (var card in project.Cards.Where(c => c.DebugName.Contains("Data only", StringComparison.OrdinalIgnoreCase)))
            Check(!visible.Contains(card.Id) && !card.Editable, "data-only remains hidden " + card.Id);
        foreach (var leader in project.Cards.Where(c => c.Type == 1 && c.Availability == 0 && !c.IsTutorial))
            Check(CardCatalog.Query(project, leader.Faction, "en-us", group: CardGroup.Leaders).Any(c => c.Id == leader.Id) && leader.Editable,
                "availability-zero leader restored " + leader.Id);
        foreach (var token in project.Cards.Where(c => c.IsToken && !c.IsTutorial && !CardCatalog.IsHidden(c)))
            Check(CardCatalog.Query(project, token.Faction, "en-us", group: CardGroup.Tokens).Any(c => c.Id == token.Id), "token visible without hidden toggle " + token.Id);
        var stratagems = project.Cards.Where(c => c.Type == 16 && !c.IsTutorial && !CardCatalog.IsHidden(c)).ToArray();
        Check(stratagems.Length == 13 && stratagems.All(c => c.Editable && visible.Contains(c.Id)), "all 13 original stratagems editable and visible");
        var tactical = project.Numbers(202140).First(n => n.Editable && n.Role == "boost");
        Check(project.Preview(202140, project.CurrentEdit(202140) with { Numbers = new Dictionary<int, int> { [tactical.Id] = tactical.Value + 1 } }).Count > 0,
            "Tactical Advantage verified ability number can be changed");
        bool locked = false;
        try { project.Preview(202140, project.CurrentEdit(202140) with { Power = 1 }); }
        catch (InvalidOperationException e) when (e.Message == "Range:Power") { locked = true; }
        Check(locked, "stratagem unit power stays locked");
        var templates = XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml"));
        string folder = Path.Combine(scratch, "stratagem-catalog"); Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => (int?)e.Attribute("Id") == 202140))).Save(Path.Combine(folder, "Templates.xml"));
        new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || (int?)e.Attribute("Id") == 202140))).Save(Path.Combine(folder, "Abilities.xml"));
        foreach (string locale in project.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(project.DirectoryPath, locale + ".csv")).Where(l => l.StartsWith("202140_", StringComparison.Ordinal)));
        var copy = new EditorProject(folder);
        copy.Apply(202140, copy.CurrentEdit(202140) with { Numbers = new Dictionary<int, int> { [tactical.Id] = tactical.Value + 1 } });
        string output = Path.Combine(scratch, "stratagem-export"); copy.Export(output);
        var reopened = new EditorProject(output);
        Check(reopened.Numbers(202140).Single(n => n.Id == tactical.Id).Value == tactical.Value + 1, "stratagem edit survives export");
        Check(reopened.Card(202140).Availability == 0 && reopened.Card(202140).Type == 16, "editor visibility never changes game availability or card type");
        copy.Undo(); Check(!copy.HasChanges && !project.HasChanges, "stratagem changes undo and source stays untouched");
        return passed;
    }
}
