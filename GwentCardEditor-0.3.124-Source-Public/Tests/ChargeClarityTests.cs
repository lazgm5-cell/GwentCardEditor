using GwentCardEditor;
using System.Xml.Linq;

internal static class ChargeClarityTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed.Add(name); }
        var cases = new[] { (202651, 527, "UnusedOrderCondition"), (202621, 994, "UnusedOrderCondition"),
            (202634, 527, "UnusedOrderCondition"), (202648, 1037, "UnusedOrderCondition"), (202676, 1037, "UnusedOrderCondition"),
            (202445, 200, "ImplicitChargeCondition"), (203282, 94, "ImplicitChargeCondition"), (203091, 1486, "ChargeActivationSignal"),
            (203197, 898, "SelfRevealPresentation"), (203197, 920, "SelfRevealPresentation"), (203197, 943, "SelfRevealPresentation") };
        foreach (var (card, field, reason) in cases)
        {
            var number = source.Numbers(card).Single(n => n.Id == field);
            Check(!number.Editable && number.Reason == reason, "Technical charge or reveal field hidden " + card + "/" + field);
            bool denied = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [field] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { denied = true; }
            Check(denied, "Technical field protected in backend " + card + "/" + field);
        }
        var setting = source.InitialCharges(122214);
        Check(setting is { Editable: true, Value: 3, TextVariableId: 295 }, "Kaedweni Sergeant native initial charges recognized");
        Check(source.Preview(122214, source.CurrentEdit(122214) with { Numbers = new Dictionary<int, int> { [-1] = 4 } }).Count == 2,
            "Sergeant starting charge edit updates template and description variable");
        Check(source.Numbers(203091).Any(n => n.Editable && n.Role == "charge-change"), "Radovid real charge addition still editable");
        int[] ids = cases.Select(c => c.Item1).Append(122214).Distinct().ToArray();
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || ids.Contains((int?)e.Attribute("Id") ?? -1))));
        string folder = Path.Combine(scratch, "charge-clarity"); Directory.CreateDirectory(folder);
        var baselineTemplates = new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1))));
        baselineTemplates.Save(Path.Combine(folder, "Templates.xml"));
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv")).Where(l => ids.Any(id => l.StartsWith(id + "_", StringComparison.Ordinal))));
        EditorProject Load(XDocument doc) { doc.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument doc, int id) => doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        var project = Load(baseline);
        project.Apply(122214, project.CurrentEdit(122214) with { Numbers = new Dictionary<int, int> { [-1] = 4 } });
        string output = Path.Combine(scratch, "sergeant-charges-export"); project.Export(output);
        Check(new EditorProject(output).InitialCharges(122214) is { Editable: true, Value: 4 }, "Sergeant starting charges survive export");
        var exportedGraph = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        Graph(exportedGraph, 122214).Descendants().Single(e => (int?)e.Attribute("Id") == 295).SetAttributeValue("V", 3);
        var exportedTemplates = XDocument.Load(Path.Combine(output, "Templates.xml"));
        exportedTemplates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 122214).SetElementValue("InitalTapCharges", 3);
        Check(XNode.DeepEquals(exportedGraph, baseline) && XNode.DeepEquals(exportedTemplates, baselineTemplates),
            "Sergeant export changes only starting charge and its display alias; Formation untouched");
        project.Undo(); Check(!project.HasChanges && project.InitialCharges(122214).Value == 3, "Sergeant starting charges undo in one step");
        var positive = new XDocument(baseline);
        Graph(positive, 203091).Descendants().Single(e => (int?)e.Attribute("Id") == 1486).SetAttributeValue("V", 1);
        Check(Load(positive).Numbers(203091).Single(n => n.Id == 1486).Editable, "Explicit custom Radovid boost is not hidden as zero signal");
        var changedPool = new XDocument(baseline);
        Graph(changedPool, 203197).Element("Nodes")!.Elements().Single(n => (int?)n.Attribute("Id") == 908).SetAttributeValue("Type", "GetAllCardsNode");
        Check(Load(changedPool).Numbers(203197).Single(n => n.Id == 898).Reason != "SelfRevealPresentation", "Different reveal pool not assumed to be self");
        var changedFormation = new XDocument(baseline);
        changedFormation.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 22)
            .Descendants().Single(e => (int?)e.Attribute("Id") == 79).SetAttributeValue("V", 2);
        Check(!Load(changedFormation).InitialCharges(122214).Editable, "Modified shared Formation blocks charge unlock");
        var changedCall = new XDocument(baseline);
        Graph(changedCall, 122214).Element("Nodes")!.Elements().Single(n => (string?)n.Attribute("Type") == "UseSubAbilityNode").SetElementValue("SubAbilityID", 40);
        Check(!Load(changedCall).InitialCharges(122214).Editable, "Different subability remains blocked for charges");
        Check(!source.HasChanges, "Charge clarity checks do not edit original game data");
        return passed;
    }
}
