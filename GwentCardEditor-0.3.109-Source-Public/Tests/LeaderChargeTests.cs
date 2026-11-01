using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;
using GwentCardEditor;

internal static class LeaderChargeTests
{
    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var supported = source.Cards.Where(c => c.Type == 1 && source.InitialCharges(c.Id).Editable).ToArray();
        Check(supported.Length >= 12, "verified leader initial charge count " + supported.Length);
        foreach (int id in new[] { 200055, 200162, 200165, 201589, 202303 })
            Check(!source.InitialCharges(id).Editable, "complex or unlinked leader charges stay locked " + id);
        const int card = 122105;
        string folder = Path.Combine(scratch, "leader-charge-source"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var template = new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card))));
        string templatePath = Path.Combine(folder, "Templates.xml"); template.Save(templatePath);
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", new XElement(abilities.Root!.Elements().Single(e =>
            (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == card))));
        string path = Path.Combine(folder, "Abilities.xml"); baseline.Save(path);
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv"))
                .Where(line => line.StartsWith(card + "_", StringComparison.Ordinal)), new UTF8Encoding(false));
        var project = new EditorProject(folder); var initial = project.InitialCharges(card);
        Check(initial.Editable && initial.TextVariableId.HasValue, "native leader charge alias verified");
        foreach (int value in new[] { 1, 9 })
        {
            project.Apply(card, project.CurrentEdit(card) with { Provision = project.Card(card).Provision + 1,
                Numbers = new Dictionary<int, int> { [-1] = value } });
            string export = Path.Combine(scratch, "leader-charge-boundary-" + value); project.Export(export);
            var reopened = new EditorProject(export);
            Check(reopened.InitialCharges(card) is { Editable: true } actual && actual.Value == value, "leader charges boundary export " + value);
            var graph = XDocument.Load(Path.Combine(export, "Abilities.xml"));
            graph.Descendants().Single(e => (int?)e.Attribute("Id") == initial.TextVariableId).SetAttributeValue("V", initial.Value);
            Check(XNode.DeepEquals(graph, baseline), "leader charges preserve every other graph value " + value);
            var actualTemplate = XDocument.Load(Path.Combine(export, "Templates.xml"));
            actualTemplate.Root!.Elements().Single().SetElementValue("InitalTapCharges", initial.Value);
            actualTemplate.Root!.Elements().Single().SetElementValue("Provision", source.Card(card).Provision);
            Check(XNode.DeepEquals(actualTemplate, template), "leader charges preserve unrelated template fields " + value);
            foreach (string locale in source.AvailableLanguages)
                Check(File.ReadAllBytes(Path.Combine(folder, locale + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(export, locale + ".csv"))), "leader charge locale preserved " + value + "/" + locale);
            project.Undo(); Check(!project.HasChanges, "leader charge and bonus undo together " + value);
        }
        XElement Graph(XDocument d) => d.Root!.Elements().Single();
        XElement Alias(XDocument d) => d.Descendants().Single(e => (int?)e.Attribute("Id") == initial.TextVariableId);
        var mutations = new List<(string Name, Action<XDocument> Mutate)>
        {
            ("mismatched alias", d => Alias(d).SetAttributeValue("V", 5)),
            ("unrecognized alias", d => Alias(d).SetAttributeValue("Name", "Count")),
            ("generic alias", d => Alias(d).SetAttributeValue("GArg", "ECardToken")),
            ("nested alias", d => Alias(d).Add(new XElement("Nested", 0))),
            ("external alias", d => Alias(d).SetAttributeValue("ExternalOut", "True")),
            ("resettable alias", d => Alias(d).SetAttributeValue("ResetInInactive", "True")),
            ("short metadata reference", d => Graph(d).SetAttributeValue("TestVarId", initial.TextVariableId)),
            ("long metadata reference", d => Graph(d).SetAttributeValue("TestVariableId", initial.TextVariableId)),
            ("comparison reference", d => Graph(d).Add(new XElement("CompareVarID", initial.TextVariableId))),
            ("integer reference", d => Graph(d).Element("TemporaryVariables")!.Add(new XElement("extra", new XAttribute("Id", 9000), new XAttribute("Type", "IntReferenceVar"), new XAttribute("V", initial.TextVariableId!)))),
            ("boolean reference", d => Graph(d).Element("TemporaryVariables")!.Add(new XElement("extra", new XAttribute("Id", 9000), new XAttribute("Type", "BoolReferenceVar"), new XAttribute("V", initial.TextVariableId!)))),
            ("extra alias", d => { var copy = new XElement(Alias(d)); copy.SetAttributeValue("Id", 9000); Graph(d).Element("TemporaryVariables")!.Add(copy); }),
            ("persistent alias", d => { var alias = Alias(d); alias.Remove(); Graph(d).Element("PersistentVariables")!.Add(alias); })
        };
        foreach (string type in new[] { "ChangeTapCardChargesNode", "GetBlackboardVarNode", "UseSubAbilityNode", "ChangeCardTimerNode", "PlayCardsNode" })
        {
            var native = new XElement(abilities.Descendants("Nodes").Elements().First(e => (string?)e.Attribute("Type") == type));
            int id = 9100;
            foreach (var attribute in native.DescendantsAndSelf().Attributes("Id")) attribute.Value = (id++).ToString();
            mutations.Add(("opaque or charge-changing node " + type, d => Graph(d).Element("Nodes")!.Add(new XElement(native))));
        }
        foreach (var (name, mutate) in mutations)
        {
            var changed = new XDocument(baseline); mutate(changed); changed.Save(path);
            var blocked = new EditorProject(folder);
            Check(!blocked.InitialCharges(card).Editable, "leader charge contract blocks " + name);
            bool rejected = false;
            try { blocked.Apply(card, blocked.CurrentEdit(card) with { Provision = 17, Numbers = new Dictionary<int, int> { [-1] = 7 } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "leader charge atomic refusal " + name);
        }
        baseline.Save(path);
        foreach (var (field, value) in new[] { ("VisibleTapCharges", "False"), ("TapActive", "False"), ("InitialTimer", "0"), ("InitalTapCharges", "0"), ("InitalTapCharges", "10") })
        {
            var changed = new XDocument(template); changed.Root!.Elements().Single().SetElementValue(field, value); changed.Save(templatePath);
            Check(!new EditorProject(folder).InitialCharges(card).Editable, "leader charge template refuses " + field + "/" + value);
        }
        template.Save(templatePath);
        string chinese = Path.Combine(folder, "zh-cn.csv");
        File.WriteAllText(chinese, File.ReadAllText(chinese).Replace("{Charges}", "{Charges-1}", StringComparison.Ordinal));
        Check(!new EditorProject(folder).InitialCharges(card).Editable, "leader charge aliases checked in all loaded languages");
        return passed;
    }
}
