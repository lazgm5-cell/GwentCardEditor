using System.Xml.Linq;
using GwentCardEditor;

internal static class PaymentTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        XElement Fee() => XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="200" Name="Unknown" V="2"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PaidForTrigger" Id="10"><Priority Type="EnumVar`1" GArg="ETriggerPriority" Id="11" V="0"/>
        <Costs><e0 Type="IntReferenceVar" Id="12" V="200"/></Costs><SwitchOut><e0 Type="FlowConnector" Id="13"/></SwitchOut>
        <Spent Type="IntVar" Id="14" V="0"/><Madness Type="BoolVar" Id="15" V="False"/></e0>
        <e1 Type="ChangeArmorNode" Id="20"><FlowIn Type="FlowConnector" Id="21"/><FlowOut Type="FlowConnector" Id="22"/><Armor Type="IntVar" Id="23" V="1"/></e1></Nodes>
        <Connections><e0 Type="Connection" Id="300" SrcId="13" DstId="21"/></Connections></Ability>
        """);
        XElement Tribute() => XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="200" Name="Unknown" V="2"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="UseSubAbilityNode" Id="10"><SubAbilityID>18</SubAbilityID><ConnectionMappings>
        <e0><Data Type="FlowConnector" Id="12"/><IdOfDataInAbility>1</IdOfDataInAbility><IsInput>True</IsInput></e0>
        <e1><Data Type="FlowConnector" Id="13"/><IdOfDataInAbility>4</IdOfDataInAbility><IsInput>False</IsInput></e1>
        <e2><Data Type="FlowConnector" Id="14"/><IdOfDataInAbility>6</IdOfDataInAbility><IsInput>False</IsInput></e2></ConnectionMappings>
        <VarMappings><e0><Data Type="IntVar" Id="11" Name="Cost" V="0"/><IdOfDataInAbility>35</IdOfDataInAbility><IsInput>True</IsInput></e0></VarMappings></e1>
        <e2 Type="GetVarNode" Id="40" VarId="200"><Out Type="IntVar" Id="41" V="0"/></e2>
        <e3 Type="ChangeArmorNode" Id="20"><FlowIn Type="FlowConnector" Id="21"/><FlowOut Type="FlowConnector" Id="22"/><Armor Type="IntVar" Id="23" V="1"/></e3></Nodes>
        <Connections><e0 Type="Connection" Id="300" SrcId="2" DstId="12"/><e1 Type="Connection" Id="301" SrcId="41" DstId="11"/>
        <e2 Type="Connection" Id="302" SrcId="13" DstId="21"/></Connections></Ability>
        """);
        XElement PaymentNode(XElement g) => g.Element("Nodes")!.Elements().Single(n => (int?)n.Attribute("Id") == 10);
        XElement Variable(XElement g) => g.Descendants().Single(e => (int?)e.Attribute("Id") == 200);
        bool Editable(XElement g) => AbilityNumbers.Inspect(g, false, "{Unknown}").Any(n => n.Id == 200 && n.Editable);
        void Reject(XElement g, int id, int value)
        {
            try { AbilityNumbers.Prepare(new XElement("Template"), g, new Dictionary<int,int> { [id] = value }, false, "{Unknown}"); }
            catch (InvalidOperationException) { passed.Add("unsafe payment edit rejected"); return; }
            throw new Exception("Unsafe payment edit accepted");
        }
        foreach (var (graph, role) in new[] { (Fee(), "fee-cost"), (Tribute(), "tribute-cost") })
        {
            Check(AbilityNumbers.Inspect(graph, false, "{Unknown}").Single(n => n.Id == 200) is { Editable: true, Minimum: 1, Maximum: 9, TextLinked: true } n && n.Role == role, "payment price recognized without relying on variable name: " + role);
            foreach (int value in new[] { 1, 9 })
            {
                var changed = AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int,int> { [200] = value }, false, "{Unknown}");
                Variable(changed).SetAttributeValue("V", 2);
                Check(XNode.DeepEquals(graph, changed), "only actual price changes, not reference, formula, or effect: " + role);
            }
            Reject(graph, 200, 0); Reject(graph, 200, -1); Reject(graph, 200, 10);
            Check(AbilityNumbers.Inspect(graph, true, "").All(n => !n.Editable), "shared payment graph locked: " + role);
            foreach (int invalid in new[] { 0, 10 })
            {
                var bad = new XElement(graph); Variable(bad).SetAttributeValue("V", invalid);
                Check(!Editable(bad), "out-of-range existing payment value hidden, not silently clamped");
            }
            foreach (string flag in new[] { "ExternalIn", "ExternalOut" })
            {
                var bad = new XElement(graph); Variable(bad).SetAttributeValue(flag, "True");
                Check(!Editable(bad), "external payment price locked: " + flag + role);
            }
            var mutable = new XElement(graph);
            mutable.Element("Nodes")!.Add(new XElement("e9", new XAttribute("Type", "SetVarNode"), new XAttribute("Id", 50), new XAttribute("VarId", 200)));
            Check(!Editable(mutable), "runtime-mutated payment cost locked: " + role);
            var metadata = new XElement(graph); metadata.SetAttributeValue("TributeVarID", 200);
            Check(!Editable(metadata), "additional payment metadata use stays locked: " + role);
            var mixed = new XElement(graph);
            mixed.Element("Nodes")!.Add(XElement.Parse("<e9 Type='GetVarNode' Id='60' VarId='200'><Out Type='IntVar' Id='61' V='0'/></e9>"));
            mixed.Element("Connections")!.Add(XElement.Parse("<e9 Type='Connection' Id='310' SrcId='61' DstId='23'/>"));
            Check(!Editable(mixed), "one variable used as price and effect amount locked: " + role);
            var unknownRef = new XElement(graph);
            unknownRef.Element("Nodes")!.Add(XElement.Parse("<e9 Type='UnknownNode' Id='70'><Ref Type='IntReferenceVar' Id='71' V='200'/></e9>"));
            Check(!Editable(unknownRef), "unrecognized integer-reference use blocks payment editing: " + role);
        }
        Reject(Fee(), 12, 3); Reject(Fee(), 14, 3); Reject(Tribute(), 11, 3);
        foreach (var mutate in new Action<XElement>[] {
            g => PaymentNode(g).Element("Costs")!.Elements().First().SetAttributeValue("ExternalIn", "True"),
            g => PaymentNode(g).Element("Costs")!.Elements().First().SetAttributeValue("V", 999),
            g => PaymentNode(g).Element("Costs")!.Add(XElement.Parse("<e1 Type='IntReferenceVar' Id='80' V='200'/>")),
            g => PaymentNode(g).Element("SwitchOut")!.Add(XElement.Parse("<e1 Type='FlowConnector' Id='80'/>")),
            g => PaymentNode(g).Element("Madness")!.SetAttributeValue("V", "True"),
            g => PaymentNode(g).Element("Madness")!.SetAttributeValue("ExternalIn", "True"),
            g => PaymentNode(g).Element("Priority")!.SetAttributeValue("V", 999),
            g => g.Element("Connections")!.RemoveNodes() })
        {
            var bad = Fee(); mutate(bad); Check(!Editable(bad), "unsupported or dynamic fee contract locked");
        }
        foreach (var mutate in new Action<XElement>[] {
            g => PaymentNode(g).Element("SubAbilityID")!.Value = "16",
            g => PaymentNode(g).Element("VarMappings")!.Elements().First().Element("IdOfDataInAbility")!.Value = "9",
            g => PaymentNode(g).Element("VarMappings")!.Elements().First().Element("IsInput")!.Value = "False",
            g => PaymentNode(g).Element("ConnectionMappings")!.Elements().Last().Element("IdOfDataInAbility")!.Value = "4",
            g => PaymentNode(g).Element("ConnectionMappings")!.Elements().Last().Element("IsInput")!.Value = "True",
            g => PaymentNode(g).Element("ConnectionMappings")!.Elements().First().Remove(),
            g => g.Element("Connections")!.Elements().First().Remove() })
        {
            var bad = Tribute(); mutate(bad); Check(!Editable(bad), "unsupported tribute contract locked");
        }
        var remapped = Fee();
        var misleading = Tribute();
        PaymentNode(misleading).Element("VarMappings")!.Elements().First().Element("Data")!.SetAttributeValue("Name", "Damage");
        PaymentNode(misleading).Element("VarMappings")!.Elements().First().Element("IdOfDataInAbility")!.Value = "99";
        Check(!Editable(misleading), "misleading known field name cannot unlock an invalid tribute contract");
        foreach (var element in remapped.Descendants())
        {
            foreach (var attr in element.Attributes().Where(a => a.Name.LocalName is "Id" or "SrcId" or "DstId")) attr.Value = (int.Parse(attr.Value) + 2000).ToString();
            if ((string?)element.Attribute("Type") == "IntReferenceVar") element.SetAttributeValue("V", 2200);
        }
        Check(AbilityNumbers.Inspect(remapped, false, "").Any(n => n.Id == 2200 && n.Editable && n.Role == "fee-cost"), "fee recognition follows remapped variable IDs");
        return passed;
    }

    public static IReadOnlyList<string> Examples(EditorProject project)
    {
        var passed = new List<string>();
        foreach (var (card, id, value, role) in new[] { (202427, 1, 9, "tribute-cost"), (202386, 5, 2, "fee-cost"), (202296, 131, 3, "fee-cost") })
        {
            if (project.Numbers(card).SingleOrDefault(n => n.Id == id) is not { Editable: true } n || n.Value != value || n.Role != role)
                throw new Exception("Original payment example failed: " + card);
            passed.Add("original payment example " + card + " = " + value + " (" + role + ")");
        }
        foreach (int card in new[] { 202333, 203120, 203238 })
        {
            if (project.Numbers(card).Any(n => n.Editable && n.Role == "fee-cost")) throw new Exception("Dynamic fee unexpectedly editable: " + card);
            passed.Add("dynamic fee remains locked: " + card);
        }
        return passed;
    }
}
