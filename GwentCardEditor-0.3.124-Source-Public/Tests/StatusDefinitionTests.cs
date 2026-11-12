using System.Text;
using System.Xml.Linq;
using GwentCardEditor;

internal static class StatusDefinitionTests
{
    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        const int id = 200050;
        string folder = Path.Combine(scratch, "status-definition-source"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id))))
            .Save(Path.Combine(folder, "Templates.xml"));
        var original = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", new XElement(original.Root!.Elements()
            .Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id))));
        string path = Path.Combine(folder, "Abilities.xml"); baseline.Save(path);
        File.WriteAllLines(Path.Combine(folder, "en-us.csv"), File.ReadLines(Path.Combine(source.DirectoryPath, "en-us.csv"))
            .Where(line => line.StartsWith(id + "_", StringComparison.Ordinal)), new UTF8Encoding(false));
        var project = new EditorProject(folder);
        Check(project.InitialStatuses(id).Any(s => s.Editable), "verified identity filter permits independent statuses");
        project.Apply(id, project.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true } });
        string output = Path.Combine(scratch, "status-definition-export"); project.Export(output);
        Check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Abilities.xml"))), "definition getter graph preserved byte-for-byte");
        Check(new EditorProject(output).InitialStatuses(id).Single(s => s.Key == "shield").Value, "definition status export round-trip");
        project.Undo(); Check(!project.HasChanges, "definition status undo exact");
        var number = project.Numbers(id).First(n => n.Editable);
        project.Apply(id, project.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true },
            Numbers = new Dictionary<int, int> { [number.Id] = number.Value < number.Maximum ? number.Value + 1 : number.Value - 1 } });
        Check(project.InitialStatuses(id).Single(s => s.Key == "shield").Value && project.Numbers(id).Single(n => n.Id == number.Id).Value != number.Value,
            "identity filter number and status apply together");
        project.Undo(); Check(!project.HasChanges, "identity filter number and status undo atomically");
        XElement Graph(XDocument d) => d.Root!.Elements().Single();
        XElement Node(XDocument d) => Graph(d).Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "GetCardDefinitionNode");
        XElement Filter(XDocument d) => Graph(d).Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "FilterDefinitionNode");
        void RemoveLink(XDocument d, string endpoint, string field) => Graph(d).Element("Connections")!.Elements()
            .Where(e => (int?)e.Attribute(endpoint) == (int?)Node(d).Element(field)!.Attribute("Id")).Remove();
        void Reverse(XDocument d, bool input) => Graph(d).Element("Connections")!.Add(new XElement("extra",
            new XAttribute("Type", "Connection"), new XAttribute("Id", 9000),
            new XAttribute("SrcId", (int)Node(d).Element(input ? "DS" : "DV")!.Attribute("Id")!),
            new XAttribute("DstId", (int)Node(d).Element("DV")!.Attribute("Id")!)));
        var mutations = new List<(string Name, Action<XDocument> Mutate)>
        {
            ("unknown getter", d => Node(d).SetAttributeValue("Type", "UnknownDefinitionNode")),
            ("extra field", d => Node(d).Add(new XElement("Extra", 0))),
            ("missing input", d => Node(d).Element("DS")!.Remove()),
            ("duplicate output", d => Node(d).Add(new XElement(Node(d).Element("DV")!))),
            ("wrong input type", d => Node(d).Element("DS")!.SetAttributeValue("Type", "IntVar")),
            ("nested input", d => Node(d).Element("DS")!.Add(new XElement("Extra", 0))),
            ("generic input", d => Node(d).Element("DS")!.SetAttributeValue("GArg", "ECardToken")),
            ("wrong output type", d => Node(d).Element("DV")!.SetAttributeValue("Type", "CardVar")),
            ("generic output", d => Node(d).Element("DV")!.SetAttributeValue("GArg", "ECardToken")),
            ("missing default", d => Node(d).Element("DV")!.Element("V")!.Remove()),
            ("unknown default type", d => Node(d).Element("DV")!.Element("V")!.SetAttributeValue("Type", "Card")),
            ("nonempty template default", d => Node(d).Element("DV")!.Element("V")!.SetAttributeValue("TemplateId", 200050)),
            ("premium default", d => Node(d).Element("DV")!.Element("V")!.SetAttributeValue("IsPremium", "True")),
            ("unknown default attribute", d => Node(d).Element("DV")!.Element("V")!.SetAttributeValue("Tokens", 2)),
            ("nested default", d => Node(d).Element("DV")!.Element("V")!.Add(new XElement("Extra", 0))),
            ("text default", d => Node(d).Element("DV")!.Element("V")!.Value = "unknown"),
            ("external input", d => Node(d).Element("DS")!.SetAttributeValue("ExternalIn", "True")),
            ("external output", d => Node(d).Element("DV")!.SetAttributeValue("ExternalOut", "True")),
            ("disconnected input", d => RemoveLink(d, "DstId", "DS")),
            ("disconnected output", d => RemoveLink(d, "SrcId", "DV")),
            ("reverse input", d => Reverse(d, true)),
            ("reverse output", d => Reverse(d, false)),
            ("unknown consumer", d => Filter(d).SetAttributeValue("Type", "UnknownFilterNode")),
            ("consumer extra field", d => Filter(d).Add(new XElement("Extra", 0))),
            ("consumer wrong type", d => Filter(d).Element("Definition")!.SetAttributeValue("Type", "CardVar")),
            ("consumer unknown default", d => Filter(d).Element("Definition")!.Element("V")!.SetAttributeValue("Tokens", 2)),
            ("consumer malformed negate", d => Filter(d).Element("Negate")!.SetAttributeValue("V", "unknown"))
        };
        foreach (var (name, mutate) in mutations)
        {
            var changed = new XDocument(baseline); mutate(changed); changed.Save(path); var blocked = new EditorProject(folder);
            Check(blocked.InitialStatuses(id).All(s => !s.Editable), "unsafe definition status contract blocked: " + name);
            bool rejected = false;
            try { blocked.Apply(id, blocked.CurrentEdit(id) with { Power = blocked.Card(id).Power + 1,
                Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "unsafe definition status contract atomic: " + name);
        }
        return passed;
    }
}
