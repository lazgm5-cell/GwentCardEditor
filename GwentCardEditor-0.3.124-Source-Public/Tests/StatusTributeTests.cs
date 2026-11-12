using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class StatusTributeTests
{
    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        const int id = 202426;
        string folder = Path.Combine(scratch, "status-tribute-source"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id))))
            .Save(Path.Combine(folder, "Templates.xml"));
        var original = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities",
            new XElement(original.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id)),
            new XElement(original.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 18))));
        string path = Path.Combine(folder, "Abilities.xml"); baseline.Save(path);
        File.WriteAllLines(Path.Combine(folder, "en-us.csv"), File.ReadLines(Path.Combine(source.DirectoryPath, "en-us.csv"))
            .Where(line => line.StartsWith(id + "_", StringComparison.Ordinal)), new UTF8Encoding(false));
        var project = new EditorProject(folder);
        Check(project.InitialStatuses(id).Any(s => s.Editable), "verified tribute permits independent statuses");
        project.Apply(id, project.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true } });
        string output = Path.Combine(scratch, "status-tribute-export"); project.Export(output);
        Check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Abilities.xml"))), "tribute definition preserved byte-for-byte");
        Check(new EditorProject(output).InitialStatuses(id).Single(s => s.Key == "shield").Value, "tribute status export round-trip");
        project.Undo(); Check(!project.HasChanges, "tribute status undo exact");
        var number = project.Numbers(id).First(n => n.Editable && n.Role == "tribute-cost");
        project.Apply(id, project.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true },
            Numbers = new Dictionary<int, int> { [number.Id] = number.Value == 9 ? 8 : number.Value + 1 } });
        Check(project.InitialStatuses(id).Single(s => s.Key == "shield").Value && project.Numbers(id).Single(n => n.Id == number.Id).Value != number.Value,
            "tribute cost and status apply together");
        project.Undo(); Check(!project.HasChanges, "tribute cost and status undo atomically");
        XElement Caller(XDocument d) => d.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility");
        XElement Sub(XDocument d) => d.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility");
        XElement Node(XDocument d) => Caller(d).Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "UseSubAbilityNode");
        XElement Flow(XDocument d, int mapped) => Node(d).Element("ConnectionMappings")!.Elements()
            .Single(e => (int?)e.Element("IdOfDataInAbility") == mapped);
        XElement Cost(XDocument d) => Node(d).Element("VarMappings")!.Elements().Single();
        void Reverse(XDocument d, bool input)
        {
            var mapping = Flow(d, input ? 1 : 4);
            int field = (int)mapping.Element("Data")!.Attribute("Id")!;
            Caller(d).Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9000),
                new XAttribute("SrcId", input ? field : (int)Flow(d, 6).Element("Data")!.Attribute("Id")!),
                new XAttribute("DstId", input ? (int)Flow(d, 6).Element("Data")!.Attribute("Id")! : field)));
        }
        var mutations = new List<(string Name, Action<XDocument> Mutate)>
        {
            ("modified definition", d => Sub(d).SetAttributeValue("NId", 9999)),
            ("missing definition", d => Sub(d).Remove()),
            ("duplicate definition", d => d.Root!.Add(new XElement(Sub(d)))),
            ("wrong namespace", d => Sub(d).SetAttributeValue("Type", "InjectableAbility")),
            ("wrong definition id", d => Sub(d).SetAttributeValue("AbilityID", 19)),
            ("unknown subability", d => Node(d).Element("SubAbilityID")!.Value = "999"),
            ("missing paid branch", d => Flow(d, 4).Remove()),
            ("missing declined branch", d => Flow(d, 6).Remove()),
            ("duplicate paid branch", d => Node(d).Element("ConnectionMappings")!.Add(new XElement(Flow(d, 4)))),
            ("wrong branch direction", d => Flow(d, 6).Element("IsInput")!.Value = "True"),
            ("wrong cost mapping", d => Cost(d).Element("IdOfDataInAbility")!.Value = "9"),
            ("wrong cost direction", d => Cost(d).Element("IsInput")!.Value = "False"),
            ("wrong cost type", d => Cost(d).Element("Data")!.SetAttributeValue("Type", "BoolVar")),
            ("generic cost", d => Cost(d).Element("Data")!.SetAttributeValue("GArg", "ECardToken")),
            ("invalid cost fallback", d => Cost(d).Element("Data")!.SetAttributeValue("V", "invalid")),
            ("external cost", d => Cost(d).Element("Data")!.SetAttributeValue("ExternalIn", "True")),
            ("extra mapping field", d => Cost(d).Add(new XElement("Extra", 0))),
            ("nested cost data", d => Cost(d).Element("Data")!.Add(new XElement("Extra", 0))),
            ("extra caller field", d => Node(d).Add(new XElement("Extra", 0))),
            ("missing mappings", d => Node(d).Element("VarMappings")!.Remove()),
            ("duplicate mappings", d => Node(d).Add(new XElement(Node(d).Element("VarMappings")!))),
            ("disconnected input", d => { int local = (int)Flow(d, 1).Element("Data")!.Attribute("Id")!;
                Caller(d).Element("Connections")!.Elements().Where(e => (int?)e.Attribute("DstId") == local).Remove(); }),
            ("flow out of input", d => Reverse(d, true)),
            ("flow into output", d => Reverse(d, false))
        };
        foreach (var (name, mutate) in mutations)
        {
            var changed = new XDocument(baseline); mutate(changed); changed.Save(path); var blocked = new EditorProject(folder);
            Check(blocked.InitialStatuses(id).All(s => !s.Editable), "unsafe tribute status contract blocked: " + name);
            bool rejected = false;
            try { blocked.Apply(id, blocked.CurrentEdit(id) with { Power = blocked.Card(id).Power + 1,
                Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "unsafe tribute status contract atomic: " + name);
        }
        baseline.Save(path, SaveOptions.DisableFormatting);
        Check(new EditorProject(folder).InitialStatuses(id).Any(s => s.Editable), "tribute fingerprint ignores formatting whitespace");
        Check(source.InitialStatuses(202842).All(s => !s.Editable), "Salamander indirect profit remains locked");
        string mixedFolder = Path.Combine(scratch, "status-profit-tribute-source"); Directory.CreateDirectory(mixedFolder);
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id))))
            .Save(Path.Combine(mixedFolder, "Templates.xml"));
        File.WriteAllLines(Path.Combine(mixedFolder, "en-us.csv"), File.ReadLines(Path.Combine(source.DirectoryPath, "en-us.csv"))
            .Where(line => line.StartsWith(id + "_", StringComparison.Ordinal)), new UTF8Encoding(false));
        var mixed = new XDocument(baseline);
        mixed.Root!.Add(new XElement(original.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 16)));
        var profitGraph = original.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == 202386);
        var profitNodes = profitGraph.Element("Nodes")!.Elements().Where(e => (string?)e.Attribute("Type") is "IncomeTrigger" or "UseSubAbilityNode")
            .Select(e => new XElement(e)).ToArray();
        foreach (var attribute in profitNodes.SelectMany(e => e.DescendantsAndSelf()).Attributes("Id"))
            attribute.Value = ((int)attribute + 10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Caller(mixed).Element("Nodes")!.Add(profitNodes);
        int triggerOut = (int)profitNodes.Single(e => (string?)e.Attribute("Type") == "IncomeTrigger").Element("FlowOut")!.Attribute("Id")!;
        int flowIn = (int)profitNodes.Single(e => (string?)e.Attribute("Type") == "UseSubAbilityNode").Element("ConnectionMappings")!
            .Elements().Single(e => (string?)e.Element("IsInput") == "True").Element("Data")!.Attribute("Id")!;
        Caller(mixed).Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 20000),
            new XAttribute("SrcId", triggerOut), new XAttribute("DstId", flowIn)));
        Check(GraphValidator.Validate(Caller(mixed)).ErrorCount == 0, "composed economy fixture structurally valid");
        string mixedPath = Path.Combine(mixedFolder, "Abilities.xml"); mixed.Save(mixedPath);
        Check(new EditorProject(mixedFolder).InitialStatuses(id).Any(s => s.Editable), "verified profit and tribute compose");
        foreach (int subId in new[] { 16, 18 })
        {
            var changed = new XDocument(mixed);
            changed.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == subId)
                .SetAttributeValue("NId", 9999);
            changed.Save(mixedPath);
            Check(new EditorProject(mixedFolder).InitialStatuses(id).All(s => !s.Editable), $"mixed economy rejects changed definition {subId}");
        }
        return passed;
    }
}
