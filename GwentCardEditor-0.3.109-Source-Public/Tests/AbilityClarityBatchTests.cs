using GwentCardEditor;
using System.Xml.Linq;

internal static class AbilityClarityBatchTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        AbilityNumber Number(EditorProject project, int card, int field) => project.Numbers(card).Single(n => n.Id == field);
        Check(Number(source, 202398, 136) is { Editable: false, Reason: "ImmunityArmorBookkeeping" }, "Olgierd armor bookkeeping hidden");
        Check(Number(source, 202194, 305) is { Editable: false, Reason: "OrderActivationSignal" }, "Syanna zero boost activation signal hidden");
        foreach (var (card, field, reason) in new[] { (202398, 136, "ImmunityArmorBookkeeping"), (202194, 305, "OrderActivationSignal"), (202054, 24, "SelectionColorAnchor") })
        {
            bool rejected = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [field] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { rejected = true; }
            Check(rejected, "Service parameter protected in backend " + card);
        }
        Check(Number(source, 202487, 193) is { LabelRole: "reveal-owner-count", HelpKey: "RevealAfterSwap" }, "Merchant own reveal distinguished from swapping");
        Check(Number(source, 202487, 210) is { LabelRole: "reveal-opponent-count", HelpKey: "RevealAfterSwap" }, "Merchant opponent reveal distinguished from swapping");
        var draw = source.NumberGroups(202782).Single(g => g.Display.LabelRole == "draw-selected-count");
        var shuffle = source.NumberGroups(202782).Single(g => g.Display.LabelRole == "shuffle-into-deck-count");
        Check(draw.Members.Select(n => n.Id).ToHashSet().SetEquals([86, 18, 19]), "Mutation draw and selection stay synchronized");
        Check(shuffle.Members.Select(n => n.Id).ToHashSet().SetEquals([150, 151]), "Mutation hand shuffle has separate count");
        Check(source.NumberGroups(202782).Count(g => g.Display.Role is "choices" or "draw-count") == 2, "Mutation has exactly two card-choice settings");
        Check(Number(source, 202054, 24).Reason == "SelectionColorAnchor" && Number(source, 202054, 25).Reason == "SelectionColorAnchor", "Soup first selection is a fixed color anchor");
        var extras = source.NumberGroups(202054).Single(g => g.Display.LabelRole == "additional-destruction-count");
        Check(extras.Members.Select(n => n.Id).ToHashSet().SetEquals([125, 126]) && extras.Display.Value == 1, "Soup exposes additional same-color units only");

        foreach (var (card, field, reason) in new[] { (202402, 259, "ImplicitPowerCondition"), (202399, 363, "ImplicitPowerCondition"),
            (203119, 1704, "ImplicitPowerCondition"), (132102, 524, "PowerExtremumSelection") })
        {
            Check(Number(source, card, field) is { Editable: false } && Number(source, card, field).Reason == reason
                && !source.NumberGroups(card).Any(g => g.Members.Any(n => n.Id == field)), "Technical selection absent from simple editor " + card);
            bool rejected = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [field] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { rejected = true; }
            Check(rejected, "Technical selection rejected by backend " + card);
        }
        Check(Number(source, 112214, 11) is { Editable: true, Role: "power-limit", Value: 4 }, "Cyprian explicit power threshold 4 retained");
        Check(Number(source, 203212, 50) is { Editable: true, Role: "power-limit", Value: 3 }, "Poisoner literal threshold 3 retained without placeholder");
        int[] ids = [202398, 202194, 202487, 202782, 202054, 112214];
        string folder = Path.Combine(scratch, "clarity-batch"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var ranked = abilities.Root!.Elements().Where(e => e.Descendants().Any(n => (string?)n.Attribute("Type") == "FilterPowerLevelNode"
            && (string?)n.Element("PowerFilter")?.Attribute("V") is "1" or "3"))
            .Where(e => source.Cards.Any(c => c.Editable && c.Id == (int?)e.Attribute("Id"))).ToArray();
        var lockedRanks = ranked.SelectMany(g => source.Numbers((int)g.Attribute("Id")!)
            .Where(n => n.Role == "power-level" && n.Reason == "PowerExtremumSelection").Select(n => (Graph: g, Number: n))).ToArray();
        Check(lockedRanks.Length > 0 && lockedRanks.All(p => !p.Number.Editable), "Strongest and weakest level controls hidden: " + lockedRanks.Length);
        foreach (var entry in lockedRanks.Take(2))
        {
            bool denied = false;
            try { source.Preview((int)entry.Graph.Attribute("Id")!, source.CurrentEdit((int)entry.Graph.Attribute("Id")!) with { Numbers = new Dictionary<int, int> { [entry.Number.Id] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == "PowerExtremumSelection") { denied = true; }
            Check(denied, "Extremum selector protected in backend " + entry.Graph.Attribute("Id")!.Value);
        }
        foreach (string mode in new[] { "0", "2" })
        {
            var graph = new XElement(lockedRanks[0].Graph);
            foreach (var field in graph.Descendants().Where(n => n.Name.LocalName == "PowerFilter")) field.SetAttributeValue("V", mode);
            var count = AbilityNumbers.Inspect(graph, false, "").First(n => n.Role == "power-level");
            Check(!count.Editable && count.Reason == "PowerExtremumSelection", "Implicit single strongest/weakest selection hidden " + mode);
            graph.Descendants().Single(e => (int?)e.Attribute("Id") == count.Id).SetAttributeValue("V", 2);
            Check(AbilityNumbers.Inspect(graph, false, "").Any(n => n.Role == "power-level" && n.Editable && n.Value == 2), "Explicit multi-card count retained " + mode);
        }
        var numericThresholds = source.Cards.Where(c => c.Editable).SelectMany(c => source.Numbers(c.Id))
            .Where(n => n.Role == "power-limit" && n.Value > 0 && n.Editable).ToArray();
        Check(numericThresholds.Length > 0, "Explicit numeric power thresholds remain editable: " + numericThresholds.Length);
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || ids.Contains((int?)e.Attribute("Id") ?? -1))));
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder, "Templates.xml"));
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv")).Where(l => ids.Any(id => l.StartsWith(id + "_", StringComparison.Ordinal))));
        EditorProject Load(XDocument doc) { doc.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument doc, int id) => doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        var project = Load(baseline);
        project.Apply(112214, project.CurrentEdit(112214) with { Numbers = new Dictionary<int, int> { [11] = 5 } });
        string thresholdOutput = Path.Combine(scratch, "explicit-threshold-export"); project.Export(thresholdOutput);
        Check(Number(new EditorProject(thresholdOutput), 112214, 11) is { Editable: true, Value: 5 }, "Explicit threshold 4 to 5 survives export");
        project.Undo(); Check(!project.HasChanges, "Explicit threshold undo clean");
        project.Apply(202487, project.CurrentEdit(202487) with { Numbers = new Dictionary<int, int> { [193] = 2 } });
        project.Apply(202782, project.CurrentEdit(202782) with { Numbers = draw.Members.ToDictionary(n => n.Id, _ => 2) });
        project.Apply(202054, project.CurrentEdit(202054) with { Numbers = extras.Members.ToDictionary(n => n.Id, _ => 2) });
        string output = Path.Combine(scratch, "clarity-batch-export"); project.Export(output);
        var reopened = new EditorProject(output);
        Check(Number(reopened, 202487, 193).Value == 2 && Number(reopened, 202487, 210).Value == 1, "Reveal counters export independently");
        Check(reopened.NumberGroups(202782).Single(g => g.Display.LabelRole == "draw-selected-count").Members.All(n => n.Value == 2)
            && reopened.NumberGroups(202782).Single(g => g.Display.LabelRole == "shuffle-into-deck-count").Display.Value == 1, "Mutation draw export does not change hand shuffle");
        var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        foreach (var (card, field, original) in new[] { (202487, 193, 1), (202782, 86, 1), (202782, 18, 1), (202782, 19, 1), (202054, 125, 1), (202054, 126, 1) })
            Graph(changed, card).Descendants().Single(e => (int?)e.Attribute("Id") == field).SetAttributeValue("V", original);
        Check(XNode.DeepEquals(changed, baseline), "Only requested numeric attributes change; swap, tokens and destruction anchors preserved");
        project.Undo(); project.Undo(); project.Undo(); Check(!project.HasChanges, "Batch edits undo exactly");
        var positive = new XDocument(baseline);
        Graph(positive, 202194).Descendants().Single(e => (int?)e.Attribute("Id") == 305).SetAttributeValue("V", 1);
        Check(Number(Load(positive), 202194, 305).Editable, "Explicit nonzero custom boost is not treated as a zero signal");
        var notTop = new XDocument(baseline);
        Graph(notTop, 202487).Descendants().Single(e => (int?)e.Attribute("Id") == 22).SetAttributeValue("V", 1);
        Check(Number(Load(notTop), 202487, 193).HelpKey != "RevealAfterSwap", "Changed swap source index does not inherit top-card explanation");
        var noColor = new XDocument(baseline); var soup = Graph(noColor, 202054);
        soup.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == 96).Remove();
        Check(Number(Load(noColor), 202054, 24).Reason != "SelectionColorAnchor", "Missing color dependency is not inferred from card identity");
        Check(!source.HasChanges, "Five-card audit never edits real source");
        return passed;
    }
}
