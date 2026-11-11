using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class StatusEconomyTests
{
    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        const int id = 202386;
        string folder = Path.Combine(scratch, "status-economy-source"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id))))
            .Save(Path.Combine(folder, "Templates.xml"));
        var original = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var ability = new XElement(original.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id));
        var definition = new XElement(original.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 16));
        var baseline = new XDocument(new XElement("Abilities", ability, definition));
        string path = Path.Combine(folder, "Abilities.xml"); baseline.Save(path);
        File.WriteAllLines(Path.Combine(folder, "en-us.csv"), File.ReadLines(Path.Combine(source.DirectoryPath, "en-us.csv"))
            .Where(line => line.StartsWith(id + "_", StringComparison.Ordinal)), new UTF8Encoding(false));
        var project = new EditorProject(folder);
        Check(project.InitialStatuses(id).Any(s => s.Editable), "verified profit definition and fee permit independent statuses");
        project.Apply(id, project.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true } });
        string output = Path.Combine(scratch, "status-economy-export"); project.Export(output);
        Check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Abilities.xml"))), "profit and fee definition preserved byte-for-byte");
        Check(new EditorProject(output).InitialStatuses(id).Single(s => s.Key == "shield").Value, "profit status export round-trip");
        project.Undo(); Check(!project.HasChanges, "profit status undo exact");
        var number = project.Numbers(id).First(n => n.Editable && n.Role == "profit");
        project.Apply(id, project.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true },
            Numbers = new Dictionary<int, int> { [number.Id] = number.Value == 9 ? 8 : number.Value + 1 } });
        Check(project.InitialStatuses(id).Single(s => s.Key == "shield").Value && project.Numbers(id).Single(n => n.Id == number.Id).Value != number.Value,
            "profit number and status apply together");
        project.Undo(); Check(!project.HasChanges, "profit number and status undo atomically");
        XElement Caller(XDocument doc) => doc.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility");
        XElement Sub(XDocument doc) => doc.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility");
        XElement Node(XDocument doc, string type) => Caller(doc).Element("Nodes")!.Elements().First(e => (string?)e.Attribute("Type") == type);
        XElement FeeVariable(XDocument doc)
        {
            int variableId = (int)Node(doc, "PaidForTrigger").Element("Costs")!.Elements().Single().Attribute("V")!;
            return Caller(doc).Elements().Where(e => e.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
                .Elements().Single(e => (int?)e.Attribute("Id") == variableId);
        }
        var mutations = new List<(string Name, Action<XDocument> Mutate)>
        {
            ("modified profit implementation", d => Sub(d).Descendants("Value").First().SetAttributeValue("V", 1)),
            ("missing definition", d => Sub(d).Remove()),
            ("duplicate definition", d => d.Root!.Add(new XElement(Sub(d)))),
            ("wrong namespace", d => Sub(d).SetAttributeValue("Type", "InjectableAbility")),
            ("unknown subability", d => Node(d, "UseSubAbilityNode").Element("SubAbilityID")!.Value = "999"),
            ("different definition id", d => Sub(d).SetAttributeValue("AbilityID", 17)),
            ("forced income", d => Node(d, "IncomeTrigger").Element("Forced")!.SetAttributeValue("V", "True")),
            ("income priority", d => Node(d, "IncomeTrigger").Element("Priority")!.SetAttributeValue("V", 1)),
            ("income extra field", d => Node(d, "IncomeTrigger").Add(new XElement("Extra", new XAttribute("Id", 9000), new XAttribute("Type", "IntVar"), new XAttribute("V", 0)))),
            ("unknown mapping", d => Node(d, "UseSubAbilityNode").Element("VarMappings")!.Elements().First().Element("IdOfDataInAbility")!.Value = "35"),
            ("extra caller field", d => Node(d, "UseSubAbilityNode").Add(new XElement("Extra", 1))),
            ("fee madness", d => Node(d, "PaidForTrigger").Element("Madness")!.SetAttributeValue("V", "True")),
            ("fee priority", d => Node(d, "PaidForTrigger").Element("Priority")!.SetAttributeValue("V", 1)),
            ("fee duplicate cost", d => { var costs = Node(d, "PaidForTrigger").Element("Costs")!; var copy = new XElement(costs.Elements().Single()); copy.SetAttributeValue("Id", 9000); costs.Add(copy); }),
            ("fee missing output", d => Node(d, "PaidForTrigger").Element("SwitchOut")!.Remove()),
            ("zero fee", d => FeeVariable(d).SetAttributeValue("V", 0)),
            ("out of range fee", d => FeeVariable(d).SetAttributeValue("V", 10)),
            ("fee container metadata", d => Node(d, "PaidForTrigger").Element("Costs")!.SetAttributeValue("Type", "Unknown")),
            ("fee extra field", d => Node(d, "PaidForTrigger").Add(new XElement("Extra", new XAttribute("Id", 9000), new XAttribute("Type", "IntVar"), new XAttribute("V", 0)))),
            ("invalid pay active", d => Node(d, "ChangeOwnerPayActiveNode").Element("Active")!.SetAttributeValue("V", "invalid"))
        };
        foreach (var (name, mutate) in mutations)
        {
            var changed = new XDocument(baseline); mutate(changed); changed.Save(path); var blocked = new EditorProject(folder);
            Check(blocked.InitialStatuses(id).All(s => !s.Editable), "unsafe economy status contract blocked: " + name);
            bool rejected = false;
            try { blocked.Apply(id, blocked.CurrentEdit(id) with { Power = blocked.Card(id).Power + 1,
                Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "unsafe economy status contract atomic: " + name);
        }
        foreach (var (type, fieldName) in new[] { ("IncomeTrigger", "Forced"), ("IncomeTrigger", "Priority"),
            ("PaidForTrigger", "Madness"), ("PaidForTrigger", "Priority"), ("PaidForTrigger", "Spent") })
        {
            var changed = new XDocument(baseline); var graph = Caller(changed); var field = Node(changed, type).Element(fieldName)!;
            var outputField = new XElement("Out", field.Attributes()); outputField.SetAttributeValue("Id", 9002);
            var variable = new XElement("extra", field.Attributes()); variable.SetAttributeValue("Id", 9001);
            graph.Element("TemporaryVariables")!.Add(variable);
            graph.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Type", "GetVarNode"), new XAttribute("Id", 9000),
                new XAttribute("VarId", 9001), outputField));
            graph.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9003),
                new XAttribute("SrcId", 9002), new XAttribute("DstId", (int)field.Attribute("Id")!)));
            Check(GraphValidator.Validate(graph).ErrorCount == 0, $"wired economy fixture valid {type}/{fieldName}");
            changed.Save(path); var blocked = new EditorProject(folder);
            Check(blocked.InitialStatuses(id).All(s => !s.Editable), $"wired economy guard rejects {type}/{fieldName}");
            bool rejected = false;
            try { blocked.Apply(id, blocked.CurrentEdit(id) with { Power = blocked.Card(id).Power + 1,
                Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, $"wired economy rejection atomic {type}/{fieldName}");
        }
        baseline.Save(path, SaveOptions.DisableFormatting);
        Check(new EditorProject(folder).InitialStatuses(id).Any(s => s.Editable), "profit fingerprint ignores formatting whitespace");
        return passed;
    }
}
