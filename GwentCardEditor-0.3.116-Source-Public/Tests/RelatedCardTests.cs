using System.Xml.Linq;
using GwentCardEditor;

internal static class RelatedCardTests
{
    public static IReadOnlyList<string> Run(string source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "related-cards-source"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var templates = XDocument.Load(Path.Combine(folder, "Templates.xml"));
        templates.Root!.Elements().Single(t => (int?)t.Attribute("Id") == 12).Element("AbilityIds")!.Elements().Single().Value = "11";
        templates.Save(Path.Combine(folder, "Templates.xml"));
        XElement Graph(int id, params int[] targets) => new("Ability", new XAttribute("Type", "CardAbility"), new XAttribute("Id", id),
            new XElement("TemporaryVariables", targets.Select((target, position) => new XElement("e" + position,
                new XAttribute("Type", "CardDefinitionVar"), new XAttribute("Id", 100 + position), new XAttribute("Name", "Reference" + position),
                new XElement("V", new XAttribute("Type", "CardDefinition"), new XAttribute("TemplateId", target), new XAttribute("IsPremium", false))))),
            new XElement("PersistentVariables"), new XElement("Nodes"), new XElement("Connections"));
        new XDocument(new XElement("Abilities", Graph(11, 16, 16, 13, 17, 999999, 11), Graph(16, 11), Graph(17, 16), Graph(14)))
            .Save(Path.Combine(folder, "Abilities.xml"));
        var project = new EditorProject(folder);
        var from = project.RelatedCards(11);
        Check(from.Where(r => !r.ReferencedBy).Select(r => r.CardId).SequenceEqual(new[] { 13, 16 }), "direct references exclude duplicates missing IDs self and tutorials");
        Check(from.Any(r => r.CardId == 16 && r.ReferencedBy), "reverse reference direction retained");
        Check(project.RelatedCards(16).Where(r => r.ReferencedBy).Select(r => r.CardId).SequenceEqual(new[] { 11, 12 }), "shared graph resolves to all template owners excluding tutorials");
        Check(project.RelatedCards(12).Where(r => !r.ReferencedBy).Select(r => r.CardId).SequenceEqual(new[] { 11, 13, 16 }), "shared graph outgoing links available read-only");
        Check(project.RelatedCards(13).Count == 2 && !project.Card(13).Editable, "hidden read-only targets retain references");
        Check(project.RelatedCards(17).Count == 0, "tutorial relation source stays hidden");
        Check(project.RelatedCards(14).Count == 0 && !project.HasChanges, "empty relation graph and read-only lookup");
        var ids = project.RelatedCards(11).ToArray();
        project.Apply(11, project.CurrentEdit(11) with { Power = 5 });
        Check(project.RelatedCards(11).SequenceEqual(ids), "relation cache survives basic edits");
        project.Undo(); Check(!project.HasChanges && project.RelatedCards(11).SequenceEqual(ids), "relation cache survives undo without edits");
        project.RelatedCards(11); Check(!project.HasChanges, "repeated reference lookup does not dirty project");
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var relations = project.Cards.Where(c => !c.IsTutorial).ToDictionary(c => c.Id, c => project.RelatedCards(c.Id));
        Check(relations.All(item => item.Value.Distinct().Count() == item.Value.Count
            && item.Value.All(link => link.CardId != item.Key && !project.Card(link.CardId).IsTutorial)), "valid unique references across full catalog");
        Check(relations.All(item => item.Value.All(link => relations[link.CardId].Any(reverse =>
            reverse.CardId == item.Key && reverse.ReferencedBy != link.ReferencedBy))), "bidirectional reference index across full catalog");
        Check(project.RelatedCards(132304).Any(r => r.ReferencedBy), "Drone has navigable referring cards");
        Check(!project.HasChanges, "full relation audit preserves project");
        passed.Add("cards with navigable static references " + relations.Count(r => r.Value.Count > 0));
        return passed;
    }
}
