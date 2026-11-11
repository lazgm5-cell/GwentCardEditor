using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class StatusMathTests
{
    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "status-math-source"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string path = Path.Combine(folder, "Abilities.xml"), templatePath = Path.Combine(folder, "Templates.xml");
        var templates = XDocument.Load(templatePath);
        templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue("Tokens", 0); templates.Save(templatePath);
        var original = XDocument.Load(path);
        var native = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        foreach (string type in new[] { "IntMinNode", "IntMaxNode", "IntAbsNode", "GetCurrentMoneyNode" })
        {
            var baseline = new XDocument(original); var graph = baseline.Root!.Elements().Single();
            var node = new XElement(native.Descendants("Nodes").Elements().First(e => (string?)e.Attribute("Type") == type));
            node.SetAttributeValue("Id", 9000);
            int nextId = 9001;
            foreach (var field in node.Elements()) field.SetAttributeValue("Id", nextId++);
            graph.Element("Nodes")!.Add(node); baseline.Save(path);
            Check(GraphValidator.Validate(graph).ErrorCount == 0, "native numeric status fixture valid: " + type);
            var project = new EditorProject(folder);
            Check(project.InitialStatuses(11).All(s => s.Editable), "pure numeric node permits independent statuses: " + type);
            project.Apply(11, project.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["shield"] = true } });
            string output = Path.Combine(scratch, "status-math-export-" + type); project.Export(output);
            Check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Abilities.xml"))), "pure numeric graph preserved byte-for-byte: " + type);
            Check(new EditorProject(output).InitialStatuses(11).Single(s => s.Key == "shield").Value, "pure numeric status round-trip: " + type);
            project.Undo(); Check(!project.HasChanges, "pure numeric status undo exact: " + type);
            XElement TestNode(XDocument d) => d.Root!.Elements().Single().Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 9000);
            var mutations = new List<(string Name, Action<XDocument> Mutate)>
            {
                ("unknown node", d => TestNode(d).SetAttributeValue("Type", "UnknownMathNode")),
                ("extra field", d => TestNode(d).Add(new XElement("Extra", new XAttribute("Id", 9100), new XAttribute("Type", "IntVar"), new XAttribute("V", 0)))),
                ("missing field", d => TestNode(d).Elements().First().Remove()),
                ("duplicate field", d => { var field = new XElement(TestNode(d).Elements().First()); field.SetAttributeValue("Id", 9100); TestNode(d).Add(field); })
            };
            foreach (string name in node.Elements().Select(e => e.Name.LocalName))
            {
                mutations.Add((name + " wrong type", d => TestNode(d).Element(name)!.SetAttributeValue("Type", "CardTokensVar")));
                mutations.Add((name + " generic argument", d => TestNode(d).Element(name)!.SetAttributeValue("GArg", "ECardToken")));
                mutations.Add((name + " nested data", d => TestNode(d).Element(name)!.Add(new XElement("Extra", 0))));
                mutations.Add((name + " text data", d => TestNode(d).Element(name)!.Value = "unknown"));
                mutations.Add((name + " invalid fallback", d => TestNode(d).Element(name)!.SetAttributeValue("V", "invalid")));
                mutations.Add((name + " external input", d => TestNode(d).Element(name)!.SetAttributeValue("ExternalIn", "True")));
                mutations.Add((name + " external output", d => TestNode(d).Element(name)!.SetAttributeValue("ExternalOut", "True")));
                mutations.Add((name + " missing id", d => TestNode(d).Element(name)!.Attribute("Id")!.Remove()));
            }
            foreach (var (name, mutate) in mutations)
            {
                var changed = new XDocument(baseline); mutate(changed); changed.Save(path); var blocked = new EditorProject(folder);
                Check(blocked.InitialStatuses(11).All(s => !s.Editable), $"unsafe pure numeric status blocked {type}: {name}");
                bool rejected = false;
                try { blocked.Apply(11, blocked.CurrentEdit(11) with { Power = blocked.Card(11).Power + 1,
                    Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !blocked.HasChanges, $"unsafe pure numeric status atomic {type}: {name}");
            }
            string outputName = type is "IntMinNode" or "IntMaxNode" ? "Result" : type == "IntAbsNode" ? "DV" : "Out";
            foreach (string name in node.Elements().Select(e => e.Name.LocalName))
            {
                var changed = new XDocument(baseline); var g = changed.Root!.Elements().Single();
                int fieldId = (int)TestNode(changed).Element(name)!.Attribute("Id")!;
                g.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Type", "GetVarNode"), new XAttribute("Id", 9200),
                    new XAttribute("VarId", 9201), new XElement("Out", new XAttribute("Id", 9202), new XAttribute("Type", "IntVar"), new XAttribute("V", 0))));
                g.Element("TemporaryVariables")!.Add(new XElement("extra", new XAttribute("Id", 9201), new XAttribute("Type", "IntVar"), new XAttribute("V", 0)));
                g.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9203),
                    new XAttribute("SrcId", name == outputName ? 9202 : fieldId), new XAttribute("DstId", name == outputName ? fieldId : 9202)));
                Check(GraphValidator.Validate(g).ErrorCount == 0, $"reverse numeric fixture structurally valid {type}/{name}");
                changed.Save(path);
                Check(new EditorProject(folder).InitialStatuses(11).All(s => !s.Editable), $"reverse numeric direction blocked {type}/{name}");
            }
        }
        Check(source.InitialStatuses(113315).All(s => !s.Editable), "Bekkers Mirror remains a special without initial statuses");
        return passed;
    }
}
