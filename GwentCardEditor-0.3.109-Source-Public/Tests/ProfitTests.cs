using System.Xml.Linq;
using GwentCardEditor;

internal static class ProfitTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        XElement Graph() => XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="200" Name="Unknown" V="4"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="IncomeTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/><Forced Type="BoolVar" Id="3" V="False"/></e0>
        <e1 Type="UseSubAbilityNode" Id="10"><SubAbilityID>16</SubAbilityID><ConnectionMappings>
        <e0><Data Type="FlowConnector" Id="12"/><IdOfDataInAbility>1</IdOfDataInAbility><IsInput>True</IsInput></e0>
        <e1><Data Type="FlowConnector" Id="13"/><IdOfDataInAbility>14</IdOfDataInAbility><IsInput>False</IsInput></e1></ConnectionMappings>
        <VarMappings><e0><Data Type="IntVar" Id="11" Name="Income" V="0"/><IdOfDataInAbility>9</IdOfDataInAbility><IsInput>True</IsInput></e0>
        <e1><Data Type="BoolVar" Id="14" V="False"/><IdOfDataInAbility>28</IdOfDataInAbility><IsInput>True</IsInput></e1></VarMappings></e1>
        <e2 Type="GetVarNode" Id="300" VarId="200"><Out Type="IntVar" Id="301" V="0"/></e2></Nodes>
        <Connections><e0 Type="Connection" Id="400" SrcId="301" DstId="11"/><e1 Type="Connection" Id="401" SrcId="2" DstId="12"/></Connections></Ability>
        """);
        XElement Node(XElement g) => g.Element("Nodes")!.Elements().Single(n => (int?)n.Attribute("Id") == 10);
        bool Editable(XElement g) => AbilityNumbers.Inspect(g, false, "").Any(n => n.Id == 200 && n.Editable);
        Check(AbilityNumbers.Inspect(Graph(), false, "").Single(n => n.Id == 200) is { Role: "profit", Editable: true, Maximum: 9 }, "profit inferred from validated built-in contract, not variable name");
        foreach (var mutate in new Action<XElement>[] {
            g => Node(g).Element("SubAbilityID")!.Value = "18",
            g => Node(g).Element("VarMappings")!.Elements().First().Element("IdOfDataInAbility")!.Value = "35",
            g => Node(g).Element("VarMappings")!.Elements().First().Element("IsInput")!.Value = "False",
            g => Node(g).Element("VarMappings")!.Elements().Last().Element("Data")!.SetAttributeValue("V", "True"),
            g => Node(g).Element("VarMappings")!.Elements().Last().Element("Data")!.SetAttributeValue("ExternalIn", "True"),
            g => g.Element("Nodes")!.Elements().First().SetAttributeValue("Type", "PlayedTrigger"),
            g => g.Element("Nodes")!.Elements().First().Element("Forced")!.SetAttributeValue("V", "True"),
            g => g.Element("Connections")!.Elements().Last().Remove(),
            g => Node(g).Element("ConnectionMappings")!.Elements().Last().Element("IdOfDataInAbility")!.Value = "1",
            g => g.Element("Nodes")!.Add(new XElement("e3", new XAttribute("Type", "SetVarNode"), new XAttribute("Id", 500), new XAttribute("VarId", 200))),
            g => g.SetAttributeValue("ProfitVarID", 200),
            g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 200).SetAttributeValue("ExternalOut", "True") })
        {
            var graph = Graph(); mutate(graph);
            Check(!Editable(graph), "unsafe profit contract or source blocked");
        }
        var addition = Graph();
        addition.Element("Nodes")!.Add(XElement.Parse("<e3 Type='IntPlusNode' Id='500'><A Type='IntVar' Id='501' V='0'/><B Type='IntVar' Id='502' V='1'/><Result Type='IntVar' Id='503' V='0'/></e3>"));
        addition.Element("Connections")!.Elements().First().SetAttributeValue("DstId", 501);
        addition.Element("Connections")!.Add(XElement.Parse("<e2 Type='Connection' Id='402' SrcId='503' DstId='11'/>"));
        Check(AbilityNumbers.Inspect(addition, false, "").Single(n => n.Id == 200) is { Role: "base-profit", Editable: true }, "additive profit source recognized as base, not final profit");
        var multiple = new XElement(addition);
        multiple.Element("Connections")!.Add(XElement.Parse("<e3 Type='Connection' Id='403' SrcId='503' DstId='502'/>"));
        Check(!Editable(multiple), "profit sum with extra consumer or cycle blocked");
        multiple = new XElement(addition);
        multiple.Element("Connections")!.Add(XElement.Parse("<e3 Type='Connection' Id='403' SrcId='301' DstId='502'/>"));
        Check(!Editable(multiple), "base profit reused in both sum operands blocked");
        foreach (int value in new[] { 0, 9 })
        {
            var edited = AbilityNumbers.Prepare(new XElement("Template"), addition, new Dictionary<int,int> { [200] = value }, false, "");
            edited.Descendants().Single(e => (int?)e.Attribute("Id") == 200).SetAttributeValue("V", 4);
            Check(XNode.DeepEquals(addition, edited), "base-profit edit preserves formula and all other data");
        }
        return passed;
    }

    public static IReadOnlyList<string> Examples(EditorProject project)
    {
        var passed = new List<string>();
        foreach (var (card, id, value, role) in new[] { (202427, 2, 2, "profit"), (202299, 917, 4, "base-profit"), (202386, 107, 2, "profit"), (202310, 1, 8, "profit") })
        {
            if (project.Numbers(card).SingleOrDefault(n => n.Id == id) is not { Editable: true } number || number.Value != value || number.Role != role)
                throw new Exception("Expected profit example failed: " + card);
            passed.Add("original profit example " + card + " = " + value + " (" + role + ")");
        }
        return passed;
    }
}
