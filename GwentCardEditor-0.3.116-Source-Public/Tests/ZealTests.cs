using System.Xml.Linq;
using GwentCardEditor;

internal static class ZealTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string source = Path.Combine(scratch, "numbers-source");
        string folder = Path.Combine(scratch, "zeal-source");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string templatesPath = Path.Combine(folder, "Templates.xml");
        var templates = XDocument.Load(templatesPath);
        var card = templates.Root!.Elements().Single(e => (string?)e.Attribute("Id") == "11");
        card.SetElementValue("TapActive", "True");
        card.SetElementValue("SummoningSickness", "True");
        card.SetElementValue("InitalTapCharges", "1");
        templates.Save(templatesPath);
        string graphPath = Path.Combine(folder, "Abilities.xml");
        var abilities = XDocument.Load(graphPath);
        var graph = abilities.Root!.Elements().Single();
        graph.Element("Nodes")!.Elements().First().SetAttributeValue("Type", "TappedTrigger");
        abilities.Save(graphPath);
        var project = new EditorProject(folder);
        Check(project.Zeal(11) is { Editable: true, Value: false }, "initial zeal available for verified existing order");
        var before = project.CurrentEdit(11);
        project.Apply(11, before with { Zeal = true, Power = before.Power + 1 });
        Check(project.Zeal(11).Value && project.Changes().Count == 2, "zeal and power form one transaction");
        string output = Path.Combine(scratch, "zeal-export");
        project.Export(output);
        var reloaded = new EditorProject(output);
        Check(reloaded.Zeal(11).Value && reloaded.Card(11).Power == before.Power + 1, "zeal flat export round-trip");
        Check(File.ReadAllBytes(graphPath).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Abilities.xml"))), "zeal leaves ability bytes unchanged");
        project.Undo();
        Check(!project.HasChanges && !project.Zeal(11).Value, "zeal atomic undo exact");
        string originalGraph = File.ReadAllText(graphPath);
        string originalTemplates = File.ReadAllText(templatesPath);
        void Block(Action<XElement> mutate, string reason)
        {
            var altered = XDocument.Parse(originalGraph);
            mutate(altered.Root!.Elements().Single());
            altered.Save(graphPath);
            var blocked = new EditorProject(folder);
            Check(!blocked.Zeal(11).Editable && blocked.Zeal(11).Reason == reason, "zeal blocks " + reason);
            try { blocked.Apply(11, blocked.CurrentEdit(11) with { Zeal = true }); throw new Exception("Unsafe zeal accepted"); }
            catch (InvalidOperationException e) when (e.Message == reason) { passed.Add("zeal rejects " + reason); }
            blocked.Apply(11, blocked.CurrentEdit(11) with { Power = before.Power + 1 });
            Check(blocked.Card(11).Power == before.Power + 1, "blocked zeal does not block unrelated power edit");
        }
        Block(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "UnknownNode"), "UnsupportedZealGraph");
        Block(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "UseSubAbilityNode"), "UnsupportedZealGraph");
        Block(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "ChangeTapCardSummoningSicknessNode"), "GraphControlsZeal");
        Block(g => g.Element("TemporaryVariables")!.Elements().First().SetAttributeValue("ExternalIn", "True"), "UnsupportedZealGraph");
        File.WriteAllText(graphPath, originalGraph);
        foreach (var (field, value) in new[] { ("TapActive", "False"), ("InitalTapCharges", "0"), ("SummoningSickness", "invalid"), ("Type", "2") })
        {
            var altered = XDocument.Parse(originalTemplates);
            altered.Root!.Elements().Single(e => (string?)e.Attribute("Id") == "11").SetElementValue(field, value);
            altered.Save(templatesPath);
            Check(!new EditorProject(folder).Zeal(11).Editable, "zeal blocks incompatible " + field);
        }
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        Check(project.GameTerm("intimidate", "ru-ru") == "Устрашение", "official Russian Intimidate term");
        Check(project.GameTerm("intimidate", "zh-cn") == "恐吓", "official Chinese Intimidate term");
        Check(project.GameTerm("adrenaline", "zh-cn") == "亢奋", "official Chinese Adrenaline term");
        foreach (string language in project.AvailableLanguages)
            Check(project.GameTerm("zeal", language) is { Length: > 0 } term && !term.Contains('<') && !term.EndsWith(':'), "official Zeal title parsed " + language);
        Check(project.GameTerm("missing-keyword", "ru-ru") == null, "unknown game term is not invented");
        int[] ids = project.Cards.Where(c => project.Zeal(c.Id).Editable).Select(c => c.Id).ToArray();
        Check(ids.Contains(122301) && ids.Contains(122212), "Ballista and Nenneke support initial zeal toggle");
        var before = ids.ToDictionary(id => id, id => project.Zeal(id).Value);
        foreach (int id in ids) project.Apply(id, project.CurrentEdit(id) with { Zeal = !before[id] });
        Check(project.Changes().Count == ids.Length && project.Changes().All(c => c.Field.EndsWith("/SummoningSickness")), "only initial sickness changes on all compatible cards: " + ids.Length);
        string output = Path.Combine(scratch, "real-zeal-export");
        project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-zeal-cache"));
        Check(ids.All(id => result.Zeal(id).Value != before[id]), "all initial zeal toggles packed export round-trip");
        var oldXml = XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml"));
        var newXml = XDocument.Load(Path.Combine(result.DirectoryPath, "Templates.xml"));
        foreach (var element in newXml.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))
            element.Element("SummoningSickness")!.Value = (!before[(int)element.Attribute("Id")!]).ToString();
        Check(XNode.DeepEquals(oldXml, newXml), "all other template fields preserved during zeal export");
        foreach (string name in EditorProject.Languages.Select(l => l + ".csv").Append("Abilities.xml"))
            if (File.Exists(Path.Combine(project.DirectoryPath, name)))
                Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, name))), "zeal preserves bytes " + name);
        foreach (int id in ids) project.Undo();
        Check(!project.HasChanges, "all initial zeal changes undo exactly");
        return passed;
    }
}
