using System.Text;
using System.Xml.Linq;
using GwentCardEditor;

internal static class AdditiveEffectTests
{
    private static XElement Graph(bool boost = false) => XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables>
        <e0 Type="IntVar" Id="100" Name="Damage" V="4"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="GetVarNode" Id="10" VarId="100"><Out Type="IntVar" Id="11" V="0"/></e1>
        <e2 Type="IntPlusNode" Id="20"><A Type="IntVar" Id="21" V="0"/><B Type="IntVar" Id="22" V="1"/><Result Type="IntVar" Id="23" V="0"/></e2>
        <e3 Type="ChangePowerNode" Id="30"><FlowIn Type="FlowConnector" Id="31"/><FlowOut Type="FlowConnector" Id="32"/>
        <Operation Type="EnumVar`1" GArg="ECardPowerOp" Id="33" V="1"/>
        <TargetedPower Type="EnumVar`1" GArg="EPowerType" Id="34" V="0"/><Power Type="IntVar" Id="35" V="0"/>
        <AttackMethod Type="EnumVar`1" GArg="EAttackMethod" Id="36" V="0"/>
        <EffectType Type="EnumVar`1" GArg="EEffectType" Id="37" V="0"/></e3></Nodes>
        <Connections><e0 Type="Connection" Id="200" SrcId="11" DstId="21"/>
        <e1 Type="Connection" Id="201" SrcId="23" DstId="35"/><e2 Type="Connection" Id="202" SrcId="2" DstId="31"/></Connections></Ability>
        """).WithOperation(boost);

    private static XElement WithOperation(this XElement graph, bool boost)
    {
        if (boost) graph.Descendants("Operation").Single().SetAttributeValue("V", "0");
        return graph;
    }

    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        AbilityNumber Number(XElement graph) => AbilityNumbers.Inspect(graph, false, "{Damage}").Single(n => n.Id == 100);
        foreach (bool boost in new[] { false, true })
        {
            var graph = Graph(boost);
            Check(Number(graph) is { Editable: true, Minimum: 0, Maximum: 9999, Context: "Deploy" } n
                && n.Role == (boost ? "base-boost" : "base-damage"), "additive effect classified by terminal, not alias " + boost);
            Check(!AbilityNumbers.Inspect(graph, false, "").Single(n => n.Id == 35).Editable, "computed sum endpoint stays hidden " + boost);
            foreach (int value in new[] { 0, 5, 9999 })
            {
                var edited = AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int, int> { [100] = value }, false, "{Damage}");
                edited.Descendants().Single(e => (int?)e.Attribute("Id") == 100).SetAttributeValue("V", 4);
                Check(XNode.DeepEquals(graph, edited), "base effect preserves formula and bonus " + boost + "/" + value);
            }
            foreach (int value in new[] { -1, 10000 })
            {
                bool rejected = false;
                try { AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int, int> { [100] = value }, false, "{Damage}"); }
                catch (InvalidOperationException e) when (e.Message == "AbilityRange") { rejected = true; }
                Check(rejected, "base effect bounds " + boost + "/" + value);
            }
            Check(!AbilityNumbers.Inspect(graph, true, "{Damage}").Single(n => n.Id == 100).Editable, "shared additive graph stays locked " + boost);
        }
        foreach (var (name, mutate) in new (string, Action<XElement>)[]
        {
            ("multiple sum consumers", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='203' SrcId='23' DstId='22'/>"))),
            ("two uses of base", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='203' SrcId='11' DstId='22'/>"))),
            ("external sum port", g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 21).SetAttributeValue("ExternalIn", "True")),
            ("external source", g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 100).SetAttributeValue("ExternalOut", "True")),
            ("runtime base", g => g.Element("Nodes")!.Add(XElement.Parse("<extra Type='SetVarNode' Id='40' VarId='100'><FlowIn Type='FlowConnector' Id='41'/><In Type='IntVar' Id='42' V='2'/></extra>"))),
            ("unknown arithmetic", g => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 20).SetAttributeValue("Type", "IntMinusNode")),
            ("unknown power operation", g => g.Descendants("Operation").Single().SetAttributeValue("V", "99")),
            ("mixed direct and additive", g => g.Element("Connections")!.Add(XElement.Parse("<extra Type='Connection' Id='203' SrcId='11' DstId='35'/>")))
        })
        {
            var graph = Graph(); mutate(graph);
            Check(!Number(graph).Editable, "unsafe additive effect rejected: " + name);
        }
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var supported = source.Cards.Where(c => c.Editable && c.Type != 1).SelectMany(c => source.Numbers(c.Id)
            .Where(n => n.Editable && n.Role is "base-damage" or "base-boost").Select(n => (Card: c.Id, Number: n))).ToArray();
        Check(supported.Length == 12 && supported.Select(n => n.Card).Distinct().Count() == 12, "12 original additive-effect settings");
        string folder = Path.Combine(scratch, "additive-effects"); Directory.CreateDirectory(folder);
        int[] ids = supported.Select(n => n.Card).ToArray();
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
        foreach (var (id, number) in supported)
        {
            Check(project.Numbers(id).Any(n => n.Id == number.Id && n.Editable && n.Role == number.Role), "additive project recognized " + id);
            project.Apply(id, project.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [number.Id] = number.Value + 1 } });
        }
        string output = Path.Combine(scratch, "additive-effects-export"); project.Export(output);
        var reopened = new EditorProject(output);
        var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        foreach (var (id, number) in supported)
        {
            Check(reopened.Numbers(id).Any(n => n.Id == number.Id && n.Editable && n.Role == number.Role && n.Value == number.Value + 1), "additive export reopens " + id);
            changed.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id).Descendants().Single(e => (int?)e.Attribute("Id") == number.Id).SetAttributeValue("V", number.Value);
        }
        Check(XNode.DeepEquals(changed, baseline), "batch additive export preserves all formulas and other graphs");
        foreach (string language in source.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(folder, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, language + ".csv"))), "additive export preserves " + language);
        Check(File.ReadAllBytes(Path.Combine(folder, "Templates.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Templates.xml"))), "additive export preserves templates");
        foreach (var item in supported) project.Undo();
        Check(!project.HasChanges, "batch additive undo restores clean project");
        return passed;
    }
}
