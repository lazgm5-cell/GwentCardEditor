using System.Text;
using System.Xml.Linq;
using GwentCardEditor;

internal static class CopyMeaningTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        AbilityNumber Number(EditorProject project, int card, int number) => project.Numbers(card).Single(n => n.Id == number);
        bool Has(AbilityNumber n, string kind, string value) => n.MeaningFacts.Contains(new(kind, value));
        Check(Has(Number(source, 112208, 265), "CopySource", "ChosenCard"), "Operator source is selected card");
        Check(Has(Number(source, 112208, 265), "CopyPlayer", "Owner") && Number(source, 112208, 265).LabelRole == "copy-count-owner", "Operator first copy goes to owner, also in field title");
        Check(Has(Number(source, 112208, 281), "CopyPlayer", "Opponent") && Number(source, 112208, 281).LabelRole == "copy-count-opponent", "Operator second copy goes to opponent, also in field title");
        Check(!Has(Number(source, 112208, 281), "CopyPlayer", "Owner"), "Opponent copy is not labelled friendly");
        var nekker = Number(source, 132305, 198);
        Check(Has(nekker, "Template", "132305") && nekker.LabelRole == "spawn-count-owner" && nekker.HelpKey == "TemplateCopies", "Fixed-template copies labelled as spawning named cards");
        Check(Has(Number(source, 122310, 419), "CopyPlace", "Deck") && Has(Number(source, 122310, 419), "CopyDeckPosition", "Bottom"), "Blue Stripes copy goes to deck bottom");
        Check(Has(Number(source, 123201, 669), "CopyPlace", "PlayStack"), "Reinforcements creation distinguished from direct hand spawn");
        Check(Has(Number(source, 200146, 244), "CopySource", "Self"), "Dimun Smuggler copies its own template");
        Check(Number(source, 112208, 265).HelpKey == "BaseCopies", "Chosen source copy explains base-template semantics");
        Check(!Number(source, 202572, 121).MeaningFacts.Any(f => f.Kind == "CopyPlace"), "Cached location is not guessed from an earlier value");
        Check(Has(Number(source, 202800, 195), "CopyDeckPosition", "Top"), "Coated Weapons deck-top destination is explicit");
        Check(Has(Number(source, 202530, 1113), "CopyPlace", "RandomAvailableRow"), "Noonwraith random available row is explicit");
        Check(Has(Number(source, 200170, 987), "CopyPlace", "ChosenCardZone"), "Mobilization uses selected card's current zone type");

        string folder = Path.Combine(scratch, "copy-meanings"); Directory.CreateDirectory(folder);
        int[] ids = [112208, 122310, 123201, 132305, 200146, 200170];
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var all = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", all.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility"
            || ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e))));
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder, "Templates.xml"));
        foreach (string language in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, language + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, language + ".csv"))
                .Where(line => ids.Any(id => line.StartsWith(id + "_", StringComparison.Ordinal))), new UTF8Encoding(false));
        EditorProject Load(XDocument document) { document.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument doc, int id = 132305) => doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        XElement CopyNode(XElement graph) => graph.Element("Nodes")!.Elements().Single(n => n.Descendants().Any(e => (int?)e.Attribute("Id") == 198));
        XElement Input(XElement node, int port) => node.Element("VarMappings")!.Elements().Single(m => (int?)m.Element("IdOfDataInAbility") == port).Element("Data")!;
        var initial = Load(baseline);
        Check(Number(initial, 132305, 198).MeaningFacts.SequenceEqual(nekker.MeaningFacts), "Extracted fixture preserves copy evidence");
        var cosmetic = new XDocument(baseline);
        cosmetic.Root!.Elements().Single(e => (int?)e.Attribute("AbilityID") == 30).Descendants().Attributes("X").Remove();
        Check(Has(Number(Load(cosmetic), 132305, 198), "Template", "132305"), "Cosmetic layout does not invalidate verified body");
        var modified = new XDocument(baseline);
        modified.Root!.Elements().Single(e => (int?)e.Attribute("AbilityID") == 30).Descendants("SpawnType").Single().SetAttributeValue("V", 0);
        Check(Number(Load(modified), 132305, 198).MeaningFacts.Count == 0, "Changed subability body is not interpreted as stock contract");

        var unusedPosition = new XDocument(baseline);
        Input(CopyNode(Graph(unusedPosition)), 63).Element("V")!.SetAttributeValue("Location", 8);
        Check(Has(Number(Load(unusedPosition), 132305, 198), "CopyPlace", "SelfZone") && !Has(Number(Load(unusedPosition), 132305, 198), "CopyPlace", "Hand"), "Inactive position input ignored");

        var historical = new XDocument(baseline);
        foreach (var position in Graph(historical, 200170).Descendants("PositionType")) position.SetAttributeValue("V", 1);
        Check(!Number(Load(historical), 200170, 987).MeaningFacts.Any(f => f.Kind == "CopyPlace"), "Previous location is not labelled as current location");

        var bothEntries = new XDocument(baseline);
        var bothGraph = Graph(bothEntries); var bothCopy = CopyNode(bothGraph);
        int offset = bothGraph.Descendants().Attributes("Id").Select(a => (int)a).Max() + 200;
        var trigger = new XElement(bothGraph.Element("Nodes")!.Elements().First(n => (string?)n.Attribute("Type") == "PlayedTrigger"));
        foreach (var id in trigger.DescendantsAndSelf().Attributes("Id")) id.Value = ((int)id + offset).ToString();
        bothGraph.Element("Nodes")!.Add(trigger);
        int positionPort = (int)bothCopy.Element("ConnectionMappings")!.Elements().Single(m => (int?)m.Element("IdOfDataInAbility") == 59).Element("Data")!.Attribute("Id")!;
        bothGraph.Element("Connections")!.Add(new XElement("extraEntry", new XAttribute("Type", "Connection"), new XAttribute("Id", offset + 100),
            new XAttribute("SrcId", (int)trigger.Element("FlowOut")!.Attribute("Id")!), new XAttribute("DstId", positionPort)));
        var conditional = Number(Load(bothEntries), 132305, 198);
        Check(conditional.Editable && Has(conditional, "Template", "132305") && !conditional.MeaningFacts.Any(f => f.Kind is "CopyPlace" or "CopyPlayer"), "Two active entries do not produce one assumed placement");

        var mutable = new XDocument(baseline);
        var graph = Graph(mutable); var copyNode = CopyNode(graph); var definition = Input(copyNode, 40);
        int next = graph.Descendants().Attributes("Id").Select(a => (int)a).Max() + 100;
        int variable = next + 6;
        XElement CardDefinition(string name, int id, int template) => new(name, new XAttribute("Type", "CardDefinitionVar"), new XAttribute("Id", id),
            new XAttribute("ExternalIn", "False"), new XAttribute("ExternalOut", "False"),
            new XElement("V", new XAttribute("Type", "CardDefinition"), new XAttribute("TemplateId", template), new XAttribute("IsPremium", "False")));
        var storedDefinition = CardDefinition("sourceDefinition", variable, 132305);
        storedDefinition.SetAttributeValue("Name", "SourceDefinition");
        graph.Element("TemporaryVariables")!.Add(storedDefinition);
        graph.Element("Nodes")!.Add(new XElement("getter", new XAttribute("Type", "GetVarNode"), new XAttribute("Id", next + 7),
            new XAttribute("VarId", variable), CardDefinition("Out", next + 8, 0)));
        graph.Element("Connections")!.Add(new XElement("definitionLink", new XAttribute("Type", "Connection"), new XAttribute("Id", next + 9),
            new XAttribute("SrcId", next + 8), new XAttribute("DstId", (int)definition.Attribute("Id")!)));
        var writer = new XElement("writer", new XAttribute("Type", "SetVarNode"), new XAttribute("Id", next), new XAttribute("VarId", variable),
            new XElement("FlowIn", new XAttribute("Type", "FlowConnector"), new XAttribute("Id", next + 1)),
            new XElement("FlowOut", new XAttribute("Type", "FlowConnector"), new XAttribute("Id", next + 2)),
            CardDefinition("In", next + 3, 122310), CardDefinition("Out", next + 4, 0));
        graph.Element("Nodes")!.Add(writer);
        var entry = copyNode.Element("ConnectionMappings")!.Elements().Single(m => (int?)m.Element("IdOfDataInAbility") == 8).Element("Data")!;
        var flow = graph.Element("Connections")!.Elements().Single(c => (int?)c.Attribute("DstId") == (int?)entry.Attribute("Id"));
        flow.SetAttributeValue("DstId", next + 1);
        graph.Element("Connections")!.Add(new XElement("writerFlow", new XAttribute("Type", "Connection"), new XAttribute("Id", next + 5),
            new XAttribute("SrcId", next + 2), new XAttribute("DstId", (int)entry.Attribute("Id")!)));
        var uncertain = Number(Load(mutable), 132305, 198);
        Check(uncertain.Editable && !uncertain.MeaningFacts.Any(f => f.Kind == "Template"), "Mutable definition does not inherit its initial template annotation");

        var remapped = new XDocument(baseline);
        foreach (var attribute in Graph(remapped).Descendants().Attributes().Where(a => a.Name.LocalName is "Id" or "SrcId" or "DstId" or "VarId"))
            if (int.TryParse(attribute.Value, out int id) && id > 0) attribute.Value = (id + 10000).ToString();
        Check(Has(Number(Load(remapped), 132305, 10198), "Template", "132305"), "Local graph IDs are not hardcoded");
        Check(!source.HasChanges, "Copy audit never edits the input project");
        return passed;
    }
}
