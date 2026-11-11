using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class StatusVariableTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "status-variable-source"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source"))) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string graphPath = Path.Combine(folder, "Abilities.xml"), templatePath = Path.Combine(folder, "Templates.xml");
        var templates = XDocument.Load(templatePath);
        templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue("Tokens", 0); templates.Save(templatePath);
        string originalGraph = File.ReadAllText(graphPath);
        foreach (string type in new[] { "IntVar", "BoolVar" })
        foreach (string container in new[] { "TemporaryVariables", "PersistentVariables" })
        {
            string value = type == "IntVar" ? "0" : "False";
            var doc = XDocument.Parse(originalGraph); var graph = doc.Root!.Elements().Single();
            graph.Element(container)!.Add(new XElement("extra", new XAttribute("Type", type), new XAttribute("Id", 9000), new XAttribute("V", value)));
            XElement Scalar(string name, int id) => new(name, new XAttribute("Type", type), new XAttribute("Id", id), new XAttribute("V", value));
            var writer = new XElement("extra", new XAttribute("Type", "SetVarNode"), new XAttribute("Id", 9100), new XAttribute("VarId", 9000),
                new XElement("FlowIn", new XAttribute("Type", "FlowConnector"), new XAttribute("Id", 9101)),
                new XElement("FlowOut", new XAttribute("Type", "FlowConnector"), new XAttribute("Id", 9102)), Scalar("In", 9103), Scalar("Out", 9104));
            var reader = new XElement("extraReader", new XAttribute("Type", "GetVarNode"), new XAttribute("Id", 9200), new XAttribute("VarId", 9000), Scalar("Out", 9201));
            graph.Element("Nodes")!.Add(writer, reader); doc.Save(graphPath);
            Check(GraphValidator.Validate(graph).ErrorCount == 0, $"scalar state fixture valid {type}/{container}");
            var project = new EditorProject(folder);
            Check(project.InitialStatuses(11).All(s => s.Editable), $"scalar state permits independent statuses {type}/{container}");
            project.Apply(11, project.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["shield"] = true, ["resilient"] = true } });
            string output = Path.Combine(scratch, $"scalar-state-export-{type}-{container}"); project.Export(output);
            Check(File.ReadAllBytes(graphPath).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Abilities.xml"))), $"scalar state graph preserved byte-for-byte {type}/{container}");
            Check(new EditorProject(output).InitialStatuses(11).Where(s => s.Key is "shield" or "resilient").All(s => s.Value), $"scalar state status export round-trip {type}/{container}");
            project.Undo(); Check(!project.HasChanges, $"scalar state status undo exact {type}/{container}");
            var mutations = new List<(string Name, Action<XElement> Mutate)>
            {
                ("unknown variable", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).SetAttributeValue("VarId", 9999)),
                ("port instead of variable", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).SetAttributeValue("VarId", 9103)),
                ("missing variable", g => g.Element(container)!.Elements().Single(e => (int?)e.Attribute("Id") == 9000).Remove()),
                ("status variable", g => g.Element(container)!.Elements().Single(e => (int?)e.Attribute("Id") == 9000).SetAttributeValue("Type", "CardTokensVar")),
                ("object variable", g => g.Element(container)!.Elements().Single(e => (int?)e.Attribute("Id") == 9000).SetAttributeValue("Type", "CardVar")),
                ("invalid initial value", g => g.Element(container)!.Elements().Single(e => (int?)e.Attribute("Id") == 9000).SetAttributeValue("V", "invalid")),
                ("external variable", g => g.Element(container)!.Elements().Single(e => (int?)e.Attribute("Id") == 9000).SetAttributeValue("ExternalIn", "True")),
                ("metadata reference", g => g.SetAttributeValue("StatusVarID", 9000)),
                ("long metadata reference", g => g.SetAttributeValue("StatusVariableId", 9000)),
                ("comparison reference", g => g.Add(new XElement("CompareVarID", 9000))),
                ("reader type mismatch", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9200).Element("Out")!.SetAttributeValue("Type", "CardTokensVar")),
                ("reader extra field", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9200).Add(Scalar("Extra", 9300))),
                ("writer missing field", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).Element("Out")!.Remove()),
                ("writer extra field", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).Add(Scalar("Extra", 9300))),
                ("writer wrong flow type", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).Element("FlowIn")!.SetAttributeValue("Type", "BoolVar")),
                ("writer enum argument", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).Element("In")!.SetAttributeValue("GArg", "ECardToken")),
                ("writer nested field", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9100).Element("In")!.Add(new XElement("Nested"))),
                ("integer reference", g => g.Element("TemporaryVariables")!.Add(new XElement("reference", new XAttribute("Type", "IntReferenceVar"), new XAttribute("Id", 9300), new XAttribute("V", 9000)))),
                ("boolean reference", g => g.Element("TemporaryVariables")!.Add(new XElement("reference", new XAttribute("Type", "BoolReferenceVar"), new XAttribute("Id", 9300), new XAttribute("V", 9000))))
            };
            foreach (var (name, mutate) in mutations)
            {
                var broken = new XElement(graph); mutate(broken); new XDocument(new XElement("Abilities", broken)).Save(graphPath);
                var blocked = new EditorProject(folder);
                Check(blocked.InitialStatuses(11).All(s => !s.Editable), $"unsafe scalar state rejected {type}/{container}: {name}");
                bool rejected = false;
                try { blocked.Apply(11, blocked.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !blocked.HasChanges, $"unsafe scalar state atomic {type}/{container}: {name}");
            }
        }
        return passed;
    }
}
