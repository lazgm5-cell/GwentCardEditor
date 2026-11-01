using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class StatusReadTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "status-read-source"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source"))) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string graphPath = Path.Combine(folder, "Abilities.xml"), templatePath = Path.Combine(folder, "Templates.xml");
        var templates = XDocument.Load(templatePath);
        templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue("Tokens", 0);
        templates.Save(templatePath);
        string originalGraph = File.ReadAllText(graphPath);
        const string filterXml = "<e Type='FilterCardTokensNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><Tokens Type='CardTokensVar' V='2'/></e>";
        const string triggerXml = "<e Type='AfterChangedCardStatusTrigger'><Priority Type='EnumVar`1' GArg='ETriggerPriority' V='0'/><FlowOut Type='FlowConnector'/><ActionOnTrigger Type='EnumVar`1' GArg='EActionOnTrigger' V='0'/><OwnerLocation Type='ELocationVar' V='3'/><IsAmbushing Type='BoolVar' V='False'/><TargetCard Type='EnumVar`1' GArg='EPlayerSide' V='1'/><TriggerPlayer Type='EnumVar`1' GArg='ETriggerPlayer' V='3'/><OperationValue Type='EnumVar`1' GArg='EStatusOperationTrigger' V='1'/><StatusMask Type='EnumVar`1' GArg='ECardToken' V='2'/><PlayerMask Type='PlayerIdVar' V='3'/><CardStatusAttack Type='CardStatusAttackVar'/></e>";
        XElement Write(XElement node, string? dynamicField = null)
        {
            var doc = XDocument.Parse(originalGraph); var graph = doc.Root!.Elements().Single();
            graph.Element("Nodes")!.Add(new XElement(node));
            if (dynamicField != null)
            {
                var mask = node.Element(dynamicField)!;
                var output = new XElement("Out", mask.Attributes()); output.SetAttributeValue("Id", 9502);
                var variable = new XElement("extra", mask.Attributes()); variable.SetAttributeValue("Id", 9501);
                graph.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Type", "GetVarNode"), new XAttribute("Id", 9500), new XAttribute("VarId", 9501),
                    output));
                graph.Element("TemporaryVariables")!.Add(variable);
                graph.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9503),
                    new XAttribute("SrcId", 9502), new XAttribute("DstId", (int)mask.Attribute("Id")!)));
            }
            doc.Save(graphPath);
            return graph;
        }
        foreach (string xml in new[] { filterXml, triggerXml })
        {
            var prototype = XElement.Parse(xml); int next = 9000;
            foreach (var e in prototype.DescendantsAndSelf()) e.SetAttributeValue("Id", next++);
            string type = (string)prototype.Attribute("Type")!;
            string maskName = type == "FilterCardTokensNode" ? "Tokens" : "StatusMask";
            foreach (int mask in new[] { 1, 2, 256, 512, 1024, 8192, 16, 3, 9987, 65535 })
            {
                var node = new XElement(prototype); node.Element(maskName)!.SetAttributeValue("V", mask); Write(node);
                var project = new EditorProject(folder); var settings = project.InitialStatuses(11);
                Check(settings.All(s => s.Editable == ((mask & (s.Key is "shield" or "veil" ? 8194 : EditorProject.InitialStatusMasks[s.Key])) == 0)),
                    $"static status read dependencies {type}/{mask}");
                var edits = settings.Where(s => s.Editable).ToDictionary(s => s.Key, _ => true);
                if (edits.Count > 0)
                {
                    project.Apply(11, project.CurrentEdit(11) with { Statuses = edits });
                    Check(project.InitialStatuses(11).All(s => s.Value == s.Editable), $"independent flags apply {type}/{mask}");
                    project.Undo(); Check(!project.HasChanges, $"independent flags undo {type}/{mask}");
                }
                var blocked = settings.FirstOrDefault(s => !s.Editable);
                if (blocked != null)
                {
                    bool rejected = false;
                    try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { [blocked.Key] = true } }); }
                    catch (InvalidOperationException e) when (e.Message == "InitialStatusesUnsupported") { rejected = true; }
                    Check(rejected && !project.HasChanges, $"dependent flag blocked atomically {type}/{mask}");
                }
            }
            var mutations = new List<(string Name, Action<XElement> Mutate)>
            {
                ("zero mask", n => n.Element(maskName)!.SetAttributeValue("V", 0)),
                ("negative mask", n => n.Element(maskName)!.SetAttributeValue("V", -1)),
                ("unknown bit", n => n.Element(maskName)!.SetAttributeValue("V", 65536)),
                ("invalid mask", n => n.Element(maskName)!.SetAttributeValue("V", "invalid")),
                ("missing mask", n => n.Element(maskName)!.Remove()),
                ("wrong mask type", n => n.Element(maskName)!.SetAttributeValue("Type", "IntVar")),
                ("external mask", n => n.Element(maskName)!.SetAttributeValue("ExternalIn", "True")),
                ("extra field", n => n.Add(new XElement("Extra", new XAttribute("Type", "IntVar"), new XAttribute("Id", 9400), new XAttribute("V", 0))))
            };
            if (type == "FilterCardTokensNode") mutations.Add(("invalid negate", n => n.Element("Negate")!.SetAttributeValue("V", "invalid")));
            else
                foreach (string field in new[] { "Priority", "ActionOnTrigger", "OwnerLocation", "IsAmbushing", "TargetCard", "TriggerPlayer", "OperationValue", "PlayerMask" })
                    mutations.Add(("changed " + field, n => n.Element(field)!.SetAttributeValue("V", "99")));
            foreach (var (name, mutate) in mutations)
            {
                var node = new XElement(prototype); mutate(node); Write(node);
                var project = new EditorProject(folder);
                Check(project.InitialStatuses(11).All(s => !s.Editable), $"unsafe status read rejected {type}: {name}");
                bool rejected = false;
                try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["doomed"] = true } }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !project.HasChanges, $"unsafe status read atomic {type}: {name}");
            }
            string[] dynamicFields = type == "FilterCardTokensNode" ? [maskName]
                : [maskName, "Priority", "ActionOnTrigger", "OwnerLocation", "IsAmbushing", "TargetCard", "TriggerPlayer", "OperationValue", "PlayerMask"];
            foreach (string field in dynamicFields)
            {
                var graph = Write(prototype, dynamicField: field);
                Check(GraphValidator.Validate(graph).ErrorCount == 0, "dynamic status fixture is structurally valid " + type + ": " + field);
                var project = new EditorProject(folder);
                Check(project.InitialStatuses(11).All(s => !s.Editable), "dynamic status input rejected " + type + ": " + field);
                bool rejected = false;
                try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["doomed"] = true } }); }
                catch (InvalidOperationException e) when (e.Message == "InitialStatusesUnsupported") { rejected = true; }
                Check(rejected && !project.HasChanges, "dynamic status input atomic " + type + ": " + field);
            }
            if (type == "FilterCardTokensNode")
            {
                var negated = new XElement(prototype); negated.Element("Negate")!.SetAttributeValue("V", "True"); Write(negated);
                Check(new EditorProject(folder).InitialStatuses(11).All(s => s.Editable == (s.Key is not ("shield" or "veil"))), "negation preserves static status dependency");
            }
        }
        return passed;
    }
}
