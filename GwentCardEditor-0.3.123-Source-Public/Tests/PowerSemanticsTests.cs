using System.Text;
using System.Xml.Linq;
using GwentCardEditor;

internal static class PowerSemanticsTests
{
    private static XElement Graph(int target, int operation, bool named)
    {
        var graph = XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="100" Name="Damage" V="3"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="GetVarNode" Id="10" VarId="100"><Out Type="IntVar" Id="11" V="0"/></e1>
        <e2 Type="ChangePowerNode" Id="20"><FlowIn Type="FlowConnector" Id="21"/><FlowOut Type="FlowConnector" Id="22"/>
        <Operation Type="EnumVar`1" GArg="ECardPowerOp" Id="23" V="0"/><TargetedPower Type="EnumVar`1" GArg="EPowerType" Id="24" V="0"/>
        <Power Type="IntVar" Id="25" V="3"/></e2></Nodes>
        <Connections><e0 Type="Connection" Id="200" SrcId="2" DstId="21"/></Connections></Ability>
        """);
        graph.Descendants("TargetedPower").Single().SetAttributeValue("V", target);
        graph.Descendants("Operation").Single().SetAttributeValue("V", operation);
        if (named) graph.Element("Connections")!.Add(XElement.Parse("<e1 Type='Connection' Id='201' SrcId='11' DstId='25'/>"));
        return graph;
    }

    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        foreach (var (target, operation, role) in new[]
        {
            (0, 0, "boost"), (0, 1, "damage"), (0, 2, "set-power"), (0, 4, "heal"),
            (1, 0, "strengthen"), (1, 1, "base-power-loss"), (1, 2, "set-base-power"),
            (3, 0, "armor-gain"), (3, 1, "armor-loss"), (3, 2, "set-armor")
        })
        foreach (bool named in new[] { false, true })
        {
            int id = named ? 100 : 25;
            var graph = Graph(target, operation, named);
            var number = AbilityNumbers.Inspect(graph, false, "{Damage}").Single(n => n.Id == id);
            Check(number is { Editable: true, Minimum: 0, Context: "Deploy" } && number.Role == role, "verified power kind and operation " + role + "/" + named);
            foreach (int value in new[] { 0, 4 })
            {
                var changed = AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int, int> { [id] = value }, false, "{Damage}");
                changed.Descendants().Single(e => (int?)e.Attribute("Id") == id).SetAttributeValue("V", 3);
                Check(XNode.DeepEquals(graph, changed), "power semantic edit preserves target and operation " + role + "/" + named + "/" + value);
            }
        }
        foreach (var (name, mutate) in new (string, Action<XElement>)[]
        {
            ("missing target", g => g.Descendants("TargetedPower").Single().Remove()),
            ("unknown target", g => g.Descendants("TargetedPower").Single().SetAttributeValue("V", 2)),
            ("unknown operation", g => g.Descendants("Operation").Single().SetAttributeValue("V", 99)),
            ("wrong target enum", g => g.Descendants("TargetedPower").Single().SetAttributeValue("GArg", "Unknown")),
            ("missing operation type", g => g.Descendants("Operation").Single().Attribute("GArg")!.Remove()),
            ("external target", g => g.Descendants("TargetedPower").Single().SetAttributeValue("ExternalIn", true)),
            ("external operation", g => g.Descendants("Operation").Single().SetAttributeValue("ExternalOut", true)),
            ("wired operation", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='202' SrcId='11' DstId='23'/>"))),
            ("wired target", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='202' SrcId='11' DstId='24'/>")))
        })
        foreach (bool named in new[] { false, true })
        {
            var graph = Graph(0, 0, named); mutate(graph); int id = named ? 100 : 25;
            Check(!AbilityNumbers.Inspect(graph, false, "").Any(n => n.Id == id && n.Editable), "ambiguous power selector rejected " + name + "/" + named);
        }
        foreach (int target in new[] { 1, 3 })
            Check(!AbilityNumbers.Inspect(Graph(target, 4, true), false, "").Single(n => n.Id == 100).Editable, "unsupported restore target locked " + target);
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string[] roles = ["armor-gain", "armor-loss", "set-armor", "strengthen", "base-power-loss", "set-base-power"];
        var settings = source.Cards.Where(c => c.Editable).SelectMany(c => source.Numbers(c.Id)
            .Where(n => n.Editable && roles.Contains(n.Role)).Select(n => (Id: c.Id, Number: n))).ToArray();
        Check(settings.Length > 20, "verified armor and base power settings " + settings.Length);
        Check(source.Numbers(202416).Any(n => n.Id == 197 && !n.Editable && n.Role == "armor-gain"), "workshop runtime armor correctly distinguished from boost and stays locked");
        Check(source.Cards.Where(c => c.Type == 1 && c.Availability > 0).Sum(c => source.Numbers(c.Id).Count(n => n.Id > 0 && (n.Editable || n.Reason is "SingleRowSelection" or "SingleSelectionOnly" or "SurvivalCheck"
            or "OrderActivationSignal" or "ImmunityArmorBookkeeping" or "SelectionColorAnchor" or "PowerExtremumSelection" or "ImplicitPowerCondition"
            or "SingleStrengthSource" or "UnusedOrderCondition" or "ImplicitChargeCondition" or "SelfRevealPresentation" or "ChargeActivationSignal" or "ArmorConversionReset"))) == 119,
            "leader numeric baseline preserved except explicitly locked selection controls");
        string folder = Path.Combine(scratch, "power-semantics"); Directory.CreateDirectory(folder);
        int[] ids = settings.Select(s => s.Id).Distinct().ToArray();
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e)))).Save(Path.Combine(folder, "Templates.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e =>
            (string?)e.Attribute("Type") == "SubAbility" || (string?)e.Attribute("Type") == "CardAbility" && ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e))));
        baseline.Save(Path.Combine(folder, "Abilities.xml"));
        foreach (string language in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, language + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, language + ".csv"))
                .Where(line => ids.Any(id => line.StartsWith(id + "_", StringComparison.Ordinal))), new UTF8Encoding(false));
        var project = new EditorProject(folder);
        foreach (var group in settings.GroupBy(s => s.Id))
            project.Apply(group.Key, project.CurrentEdit(group.Key) with { Numbers = group.ToDictionary(s => s.Number.Id, s => s.Number.Value + 1) });
        string output = Path.Combine(scratch, "power-semantics-export"); project.Export(output);
        var reopened = new EditorProject(output); var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        foreach (var (id, number) in settings)
        {
            Check(reopened.Numbers(id).Any(n => n.Id == number.Id && n.Role == number.Role && n.Value == number.Value + 1 && n.Editable), "power semantics export reopens " + id + "/" + number.Id);
            changed.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id).Descendants()
                .Single(e => (int?)e.Attribute("Id") == number.Id).SetAttributeValue("V", number.Value);
        }
        Check(XNode.DeepEquals(changed, baseline), "power semantics export preserves target operations and every other graph value");
        Check(File.ReadAllBytes(Path.Combine(folder, "Templates.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Templates.xml"))), "power semantics templates preserved");
        foreach (string language in source.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(folder, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, language + ".csv"))), "power semantics CSV preserved " + language);
        foreach (int id in ids) project.Undo();
        Check(!project.HasChanges, "power semantics batch undo clean");
        return passed;
    }
}
