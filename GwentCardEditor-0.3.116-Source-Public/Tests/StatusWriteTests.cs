using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class StatusWriteTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "status-write-source"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source"))) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string graphPath = Path.Combine(folder, "Abilities.xml"), templatePath = Path.Combine(folder, "Templates.xml");
        var templates = XDocument.Load(templatePath);
        templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue("Tokens", 0); templates.Save(templatePath);
        string originalGraph = File.ReadAllText(graphPath);
        var prototype = XElement.Parse("<e Type='ChangeTokensNode'><FlowIn Type='FlowConnector'/><FlowOut Type='FlowConnector'/><Attacker Type='EntityVar'/><Targets Type='CardListVar'/><ElementType Type='EnumVar`1' GArg='EElementType' V='1'/><AttackMethod Type='EnumVar`1' GArg='EAttackMethod' V='0'/><EffectType Type='EnumVar`1' GArg='EEffectType' V='0'/><Operation Type='EnumVar`1' GArg='ETokenOp' V='0'/><Tokens Type='CardTokensVar' V='2'/></e>");
        int next = 9000; foreach (var e in prototype.DescendantsAndSelf()) e.SetAttributeValue("Id", next++);
        XElement Write(XElement node, string? dynamicField = null)
        {
            var doc = XDocument.Parse(originalGraph); var graph = doc.Root!.Elements().Single();
            graph.Element("Nodes")!.Add(new XElement(node));
            if (dynamicField != null)
            {
                var field = node.Element(dynamicField)!;
                var output = new XElement("Out", field.Attributes()); output.SetAttributeValue("Id", 9502);
                var variable = new XElement("extra", field.Attributes()); variable.SetAttributeValue("Id", 9501);
                graph.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Type", "GetVarNode"), new XAttribute("Id", 9500), new XAttribute("VarId", 9501), output));
                graph.Element("TemporaryVariables")!.Add(variable);
                graph.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9503),
                    new XAttribute("SrcId", 9502), new XAttribute("DstId", (int)field.Attribute("Id")!)));
            }
            doc.Save(graphPath); return graph;
        }
        foreach (int operation in new[] { 0, 1 })
        foreach (int mask in new[] { 1, 2, 256, 512, 1024, 8192, 4, 64, 8194, 65535 })
        {
            var node = new XElement(prototype); node.Element("Operation")!.SetAttributeValue("V", operation); node.Element("Tokens")!.SetAttributeValue("V", mask);
            var graph = Write(node);
            Check(GraphValidator.Validate(graph).ErrorCount == 0, $"status writer fixture valid {operation}/{mask}");
            var project = new EditorProject(folder); var settings = project.InitialStatuses(11);
            Check(settings.All(s => s.Editable == ((mask & (s.Key is "shield" or "veil" ? 8194 : EditorProject.InitialStatusMasks[s.Key])) == 0)),
                $"status writer dependencies {operation}/{mask}");
            var edits = settings.Where(s => s.Editable).ToDictionary(s => s.Key, _ => true);
            if (edits.Count > 0)
            {
                project.Apply(11, project.CurrentEdit(11) with { Statuses = edits });
                Check(project.InitialStatuses(11).All(s => s.Value == s.Editable), $"independent flags beside writer {operation}/{mask}");
                project.Undo(); Check(!project.HasChanges, $"status writer flags undo {operation}/{mask}");
            }
            var blocked = settings.FirstOrDefault(s => !s.Editable);
            if (blocked != null)
            {
                bool rejected = false;
                try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { [blocked.Key] = true } }); }
                catch (InvalidOperationException e) when (e.Message == "InitialStatusesUnsupported") { rejected = true; }
                Check(rejected && !project.HasChanges, $"written flag blocked atomically {operation}/{mask}");
            }
        }
        var mutations = new List<(string Name, Action<XElement> Mutate)>
        {
            ("zero mask", n => n.Element("Tokens")!.SetAttributeValue("V", 0)),
            ("negative mask", n => n.Element("Tokens")!.SetAttributeValue("V", -1)),
            ("unknown mask bit", n => n.Element("Tokens")!.SetAttributeValue("V", 65536)),
            ("invalid mask", n => n.Element("Tokens")!.SetAttributeValue("V", "invalid")),
            ("missing mask", n => n.Element("Tokens")!.Remove()),
            ("wrong mask type", n => n.Element("Tokens")!.SetAttributeValue("Type", "IntVar")),
            ("missing effect field", n => n.Element("EffectType")!.Remove()),
            ("wrong operation enum", n => n.Element("Operation")!.SetAttributeValue("GArg", "ECardToken")),
            ("external mask", n => n.Element("Tokens")!.SetAttributeValue("ExternalIn", "True")),
            ("extra field", n => n.Add(new XElement("Extra", new XAttribute("Type", "IntVar"), new XAttribute("Id", 9400), new XAttribute("V", 0))))
        };
        foreach (string value in new[] { "2", "3", "4", "-1", "99", "invalid", "2147483648" })
            mutations.Add(("unsupported operation " + value, n => n.Element("Operation")!.SetAttributeValue("V", value)));
        foreach (var (name, mutate) in mutations)
        {
            var node = new XElement(prototype); mutate(node); Write(node); var project = new EditorProject(folder);
            Check(project.InitialStatuses(11).All(s => !s.Editable), "unsafe status writer rejected: " + name);
            bool rejected = false;
            try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["doomed"] = true } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !project.HasChanges, "unsafe status writer atomic: " + name);
        }
        foreach (string field in new[] { "Tokens", "Operation" })
        {
            var graph = Write(prototype, dynamicField: field);
            Check(GraphValidator.Validate(graph).ErrorCount == 0, "dynamic status writer fixture valid: " + field);
            var project = new EditorProject(folder);
            Check(project.InitialStatuses(11).All(s => !s.Editable), "dynamic status writer rejected: " + field);
            bool rejected = false;
            try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["doomed"] = true } }); }
            catch (InvalidOperationException e) when (e.Message == "InitialStatusesUnsupported") { rejected = true; }
            Check(rejected && !project.HasChanges, "dynamic status writer atomic: " + field);
        }
        return passed;
    }
}
