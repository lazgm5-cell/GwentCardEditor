using System.Text;
using System.Xml.Linq;
using GwentCardEditor;

internal static class AlternativeEffectTests
{
    private static XElement Graph(bool boost = false)
    {
        var graph = XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables>
        <e0 Type="IntVar" Id="100" Name="Damage" V="1"/><e1 Type="IntVar" Id="101" Name="Damage2" V="3"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="GetVarNode" Id="10" VarId="101"><Out Type="IntVar" Id="11" V="0"/></e1>
        <e2 Type="SetVarNode" Id="20" VarId="100"><In Type="IntVar" Id="21" V="0"/><Out Type="IntVar" Id="22" V="0"/><FlowIn Type="FlowConnector" Id="23"/><FlowOut Type="FlowConnector" Id="24"/></e2>
        <e3 Type="GetVarNode" Id="30" VarId="100"><Out Type="IntVar" Id="31" V="0"/></e3>
        <e4 Type="ChangePowerNode" Id="40"><FlowIn Type="FlowConnector" Id="41"/><FlowOut Type="FlowConnector" Id="42"/>
        <Operation Type="EnumVar`1" GArg="ECardPowerOp" Id="43" V="1"/><TargetedPower Type="EnumVar`1" GArg="EPowerType" Id="44" V="0"/>
        <Power Type="IntVar" Id="45" V="0"/><AttackMethod Type="EnumVar`1" GArg="EAttackMethod" Id="46" V="0"/><EffectType Type="EnumVar`1" GArg="EEffectType" Id="47" V="0"/></e4></Nodes>
        <Connections><e0 Type="Connection" Id="200" SrcId="11" DstId="21"/><e1 Type="Connection" Id="201" SrcId="31" DstId="45"/>
        <e2 Type="Connection" Id="202" SrcId="2" DstId="23"/><e3 Type="Connection" Id="203" SrcId="24" DstId="41"/></Connections></Ability>
        """);
        if (boost) graph.Descendants("Operation").Single().SetAttributeValue("V", "0");
        return graph;
    }

    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        AbilityNumber Number(XElement graph) => AbilityNumbers.Inspect(graph, false, "{Damage2}").Single(n => n.Id == 101);
        foreach (bool boost in new[] { false, true })
        {
            var graph = Graph(boost);
            Check(Number(graph) is { Editable: true, Context: "Deploy", Minimum: 0, Maximum: 9999 } n
                && n.Role == (boost ? "alternate-boost" : "alternate-damage"), "alternative value follows terminal semantics " + boost);
            Check(!AbilityNumbers.Inspect(graph, false, "").Single(n => n.Id == 100).Editable, "runtime target state stays locked " + boost);
            foreach (int value in new[] { 0, 4, 9999 })
            {
                var changed = AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int, int> { [101] = value }, false, "{Damage2}");
                changed.Descendants().Single(e => (int?)e.Attribute("Id") == 101).SetAttributeValue("V", 3);
                Check(XNode.DeepEquals(changed, graph), "alternate edit preserves setter flow and initial state " + boost + "/" + value);
            }
            foreach (int value in new[] { -1, 10000 })
            {
                bool rejected = false;
                try { AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int, int> { [101] = value }, false, "{Damage2}"); }
                catch (InvalidOperationException e) when (e.Message == "AbilityRange") { rejected = true; }
                Check(rejected, "alternate bounds " + boost + "/" + value);
            }
            Check(!AbilityNumbers.Inspect(graph, true, "").Single(n => n.Id == 101).Editable, "shared alternate graph locked " + boost);
        }
        foreach (var (name, mutate) in new (string, Action<XElement>)[]
        {
            ("armor is not boost", g => g.Descendants("TargetedPower").Single().SetAttributeValue("V", 3)),
            ("base power is not boost", g => g.Descendants("TargetedPower").Single().SetAttributeValue("V", 1)),
            ("dynamic operation", g => g.Descendants("Operation").Single().SetAttributeValue("ExternalIn", true)),
            ("dynamic power kind", g => g.Descendants("TargetedPower").Single().SetAttributeValue("ExternalIn", true)),
            ("persistent target", g => { var value = g.Descendants().Single(e => (int?)e.Attribute("Id") == 100); value.Remove(); g.Element("PersistentVariables")!.Add(value); }),
            ("external target", g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 100).SetAttributeValue("ExternalOut", true)),
            ("resettable target", g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 100).SetAttributeValue("ResetInInactive", true)),
            ("comparison reference", g => g.Add(new XElement("CompareVarID", 100))),
            ("metadata reference", g => g.SetAttributeValue("OtherVariableId", 100)),
            ("integer reference", g => g.Element("TemporaryVariables")!.Add(XElement.Parse("<extra Type='IntReferenceVar' Id='102' V='100'/>"))),
            ("extra target reader use", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='204' SrcId='31' DstId='21'/>"))),
            ("setter output used", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='204' SrcId='22' DstId='45'/>"))),
            ("second writer", g => g.Element("Nodes")!.Add(XElement.Parse("<extra Type='SetVarNode' Id='50' VarId='100'><In Type='IntVar' Id='51' V='2'/></extra>"))),
            ("unknown setter field", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 20).Add(new XElement("Unknown", 1))),
            ("missing flow", g => g.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("Id") == 202).Remove()),
            ("external setter input", g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 21).SetAttributeValue("ExternalIn", true)),
            ("external getter output", g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 31).SetAttributeValue("ExternalOut", true)),
            ("runtime alternate source", g => g.Element("Nodes")!.Add(XElement.Parse("<extra Type='SetVarNode' Id='50' VarId='101'><In Type='IntVar' Id='51' V='2'/></extra>")))
        })
        {
            var graph = Graph(); mutate(graph);
            Check(!Number(graph).Editable, "unsafe alternate rejected: " + name);
        }
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var settings = source.Cards.Where(c => c.Editable && c.Type != 1).SelectMany(c => source.Numbers(c.Id)
            .Where(n => n.Editable && n.Role is "alternate-damage" or "alternate-boost").Select(n => (Id: c.Id, Number: n))).ToArray();
        Check(settings.Length == 15 && settings.Select(s => s.Id).Distinct().Count() == 15, "15 original alternative settings on 15 cards");
        Check(!source.Numbers(202416).Any(n => n.Id == 262 && n.Editable), "alternative workshop armor not mistaken for boost");
        string folder = Path.Combine(scratch, "alternative-effects"); Directory.CreateDirectory(folder);
        int[] ids = settings.Select(s => s.Id).ToArray();
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e)))).Save(Path.Combine(folder, "Templates.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e =>
            (string?)e.Attribute("Type") == "CardAbility" && ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e))));
        baseline.Save(Path.Combine(folder, "Abilities.xml"));
        foreach (string language in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, language + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, language + ".csv"))
                .Where(line => ids.Any(id => line.StartsWith(id + "_", StringComparison.Ordinal))), new UTF8Encoding(false));
        var project = new EditorProject(folder);
        foreach (var (id, number) in settings)
            project.Apply(id, project.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [number.Id] = number.Value + 1 } });
        string output = Path.Combine(scratch, "alternative-effects-export"); project.Export(output);
        var reopened = new EditorProject(output);
        var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        foreach (var (id, number) in settings)
        {
            Check(reopened.Numbers(id).Any(n => n.Id == number.Id && n.Editable && n.Role == number.Role && n.Value == number.Value + 1), "alternative export reopens " + id);
            changed.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id).Descendants().Single(e => (int?)e.Attribute("Id") == number.Id).SetAttributeValue("V", number.Value);
        }
        Check(XNode.DeepEquals(changed, baseline), "alternative batch preserves conditions runtime state and other graphs");
        foreach (string language in source.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(folder, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, language + ".csv"))), "alternative CSV preserved " + language);
        Check(File.ReadAllBytes(Path.Combine(folder, "Templates.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Templates.xml"))), "alternative templates preserved");
        foreach (var item in settings) project.Undo();
        Check(!project.HasChanges, "alternative batch undo clean");
        return passed;
    }
}
