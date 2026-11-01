using System.Xml.Linq;
using GwentCardEditor;

internal static class InitialPatienceTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed.Add(name); }
        var expected = new[] { (122108, 235, 0), (202917, 235, 4), (202918, 151, 1),
            (202920, 15, 0), (202992, 138, 0), (203052, 216, 0) };
        foreach (var (card, variable, value) in expected)
        {
            Check(source.InitialPatience(card) == new InitialPatienceSetting(variable, value), "Initial Patience recognized " + card);
            var number = source.Numbers(card).Single(n => n.Id == -6);
            Check(number is { Editable: true, Role: "initial-patience", Minimum: 0, Maximum: 99 } && number.Value == value,
                "One bounded initial Patience setting " + card);
            Check(!source.Numbers(card).Any(n => n.Id == variable && n.Editable), "Runtime Patience is not separately unlocked " + card);
            Check(source.DescriptionBindings(card, [-6]).Any(b => b.Linked || b.Kind == "CaseMismatch"), "Patience bound to effect description " + card);
            Check(source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [-6] = value + 2 } }).Count == 1,
                "Patience edit changes one initializer only " + card);
        }
        Check(source.InitialPatience(202990) == null && !source.Numbers(202990).Any(n => n.Id == -6), "Leticia multi-writer Patience remains blocked");
        Check(source.InitialPatience(112103) == null, "Non-Patience card has no initial Patience setting");
        foreach (var (card, value, reason) in new[] { (202920, -1, "AbilityRange"), (202920, 100, "AbilityRange"),
            (112103, 1, "InitialValueLocked"), (202990, 2, "InitialValueLocked") })
        {
            bool denied = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [-6] = value } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { denied = true; }
            Check(denied, "Unsupported initial Patience rejected " + card + "/" + value);
        }
        int[] ids = expected.Select(e => e.Item1).Append(202990).ToArray();
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || ids.Contains((int?)e.Attribute("Id") ?? -1))));
        string folder = Path.Combine(scratch, "initial-patience"); Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder, "Templates.xml"));
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv")).Where(l => ids.Any(id => l.StartsWith(id + "_", StringComparison.Ordinal))));
        EditorProject Load(XDocument doc) { doc.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument doc, int id) => doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        foreach (int target in new[] { 0, 3, 99 })
        {
            var project = Load(baseline);
            foreach (var (card, _, _) in expected)
                project.Apply(card, project.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [-6] = target } });
            string output = Path.Combine(scratch, "patience-export-" + target); project.Export(output);
            var reopened = new EditorProject(output);
            var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
            foreach (var (card, variable, original) in expected)
            {
                Check(reopened.InitialPatience(card) is { } setting && setting.Value == target,
                    "Patience initializer survives export " + card + "/" + target);
                Graph(changed, card).Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V", original);
            }
            Check(XNode.DeepEquals(changed, baseline), "Patience export preserves growth, Order, blackboards, subabilities and every other graph value " + target);
            Check(File.ReadAllBytes(Path.Combine(folder, "Templates.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Templates.xml"))), "Patience export does not edit templates " + target);
            Check(source.AvailableLanguages.All(locale => File.ReadAllBytes(Path.Combine(folder, locale + ".csv"))
                .SequenceEqual(File.ReadAllBytes(Path.Combine(output, locale + ".csv")))), "Patience export preserves all localization files " + target);
            while (project.HasChanges) project.Undo();
            Check(expected.All(e => project.InitialPatience(e.Item1)?.Value == e.Item3), "Patience undo restores initial values " + target);
        }
        var extraWriter = new XDocument(baseline);
        var copy = new XElement(Graph(extraWriter, 202920).Element("Nodes")!.Elements().Single(n => (string?)n.Attribute("Type") == "SetVarNode" && (int?)n.Attribute("VarId") == 15));
        foreach (var field in copy.DescendantsAndSelf().Where(e => e.Attribute("Id") != null)) field.SetAttributeValue("Id", (int)field.Attribute("Id")! + 10000);
        Graph(extraWriter, 202920).Element("Nodes")!.Add(copy);
        Check(Load(extraWriter).InitialPatience(202920) == null, "Additional Patience writer blocks initialization edit");
        var differentIncrement = new XDocument(baseline);
        Graph(differentIncrement, 202920).Element("Nodes")!.Elements().Single(n => (string?)n.Attribute("Type") == "GetBlackboardVarNode" && (int?)n.Attribute("VarId") == 14).SetAttributeValue("VarId", 21);
        Check(Load(differentIncrement).InitialPatience(202920) == null, "Unrecognized Patience increment blocks edit");
        var differentSub = new XDocument(baseline);
        differentSub.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 40)
            .Descendants().Single(e => e.Name.LocalName == "Message").SetAttributeValue("V", "other");
        Check(Load(differentSub).InitialPatience(202920) == null, "Modified shared Patience implementation blocks edit");
        Check(!source.HasChanges, "Patience tests leave original game unchanged");
        return passed;
    }
}
