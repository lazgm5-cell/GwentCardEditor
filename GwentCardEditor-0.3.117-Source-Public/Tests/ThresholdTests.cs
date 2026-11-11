using System.Xml.Linq;
using GwentCardEditor;

internal static class ThresholdTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        XElement Adrenaline() => XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="200" Name="Unknown" V="5"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="IsOwnerKeywordActiveAndEmitMessageBranchNode" Id="10"><FlowIn Type="FlowConnector" Id="11"/><FlowOutTrue Type="FlowConnector" Id="12"/><FlowOutFalse Type="FlowConnector" Id="13"/>
        <Keyword Type="EnumVar`1" GArg="ECardKeyword" Id="14" V="1"/><EmitKeywordMessageIfTrue Type="BoolVar" Id="15" V="True"/><EmitKeywordMessageIfFalse Type="BoolVar" Id="16" V="False"/></e1>
        <e2 Type="ChangeArmorNode" Id="20"><FlowIn Type="FlowConnector" Id="21"/><FlowOut Type="FlowConnector" Id="22"/><Armor Type="IntVar" Id="23" V="1"/></e2></Nodes>
        <Keywords><e0 Type="AbilityKeyword" Id="50"><Keyword>1</Keyword><Conditions>
        <e0 Type="BlackboardIntVarCondition"><Negate>False</Negate><BlackboardType>2</BlackboardType><VarID>11</VarID><Compare>4</Compare><CompareVarID>200</CompareVarID></e0>
        <e1 Type="OwnerPositionCondition"><Negate>False</Negate><ValidLocations>8</ValidLocations></e1></Conditions></e0>
        <e1 Type="AbilityKeyword" Id="51"><Keyword>1</Keyword><Conditions>
        <e0 Type="BlackboardIntVarCondition"><Negate>False</Negate><BlackboardType>2</BlackboardType><VarID>11</VarID><Compare>2</Compare><CompareVarID>200</CompareVarID></e0>
        <e1 Type="OwnerPositionCondition"><Negate>False</Negate><ValidLocations>256</ValidLocations></e1></Conditions></e1></Keywords>
        <Connections><e0 Type="Connection" Id="300" SrcId="2" DstId="11"/><e1 Type="Connection" Id="301" SrcId="12" DstId="21"/></Connections></Ability>
        """);
        XElement Berserk() => XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables><e0 Type="IntVar" Id="200" Name="Unknown" V="3"/></TemporaryVariables><PersistentVariables/>
        <Nodes><e0 Type="PlayedTrigger" Id="1"><FlowOut Type="FlowConnector" Id="2"/></e0>
        <e1 Type="UseSubAbilityNode" Id="10"><SubAbilityID>9</SubAbilityID><ConnectionMappings>
        <e0><Data Type="FlowConnector" Id="12"/><IdOfDataInAbility>1</IdOfDataInAbility><IsInput>True</IsInput></e0>
        <e1><Data Type="FlowConnector" Id="13"/><IdOfDataInAbility>4</IdOfDataInAbility><IsInput>False</IsInput></e1>
        <e2><Data Type="FlowConnector" Id="14"/><IdOfDataInAbility>45</IdOfDataInAbility><IsInput>False</IsInput></e2></ConnectionMappings>
        <VarMappings><e0><Data Type="IntVar" Id="11" Name="Power" V="0"/><IdOfDataInAbility>48</IdOfDataInAbility><IsInput>True</IsInput></e0>
        <e1><Data Type="BoolVar" Id="15" V="True"/><IdOfDataInAbility>62</IdOfDataInAbility><IsInput>True</IsInput></e1></VarMappings></e1>
        <e2 Type="GetVarNode" Id="40" VarId="200"><Out Type="IntVar" Id="41" V="0"/></e2>
        <e3 Type="ChangeArmorNode" Id="20"><FlowIn Type="FlowConnector" Id="21"/><FlowOut Type="FlowConnector" Id="22"/><Armor Type="IntVar" Id="23" V="1"/></e3></Nodes>
        <Connections><e0 Type="Connection" Id="300" SrcId="2" DstId="12"/><e1 Type="Connection" Id="301" SrcId="41" DstId="11"/>
        <e2 Type="Connection" Id="302" SrcId="13" DstId="21"/></Connections></Ability>
        """);
        XElement Variable(XElement g) => g.Descendants().Single(e => (int?)e.Attribute("Id") == 200);
        XElement FirstCondition(XElement g) => g.Element("Keywords")!.Elements().First().Element("Conditions")!.Elements().First();
        XElement Node(XElement g) => g.Element("Nodes")!.Elements().Single(e => (int?)e.Attribute("Id") == 10);
        bool Editable(XElement g, string text = "{Unknown-1}") => AbilityNumbers.Inspect(g, false, text).Any(n => n.Id == 200 && n.Editable);
        var adren = AbilityNumbers.Inspect(Adrenaline(), false, "{Unknown-1}").Single(n => n.Id == 200);
        Check(adren is { Editable: true, Role: "adrenaline-threshold", Value: 5, DisplayValue: 4, DisplayOffset: -1, TextLinked: true }, "adrenaline uses verified hand-size references and displays card value");
        Check(adren.ToStoredValue(0) == 1 && adren.ToDisplayValue(11) == 10, "adrenaline display boundaries map to stored thresholds");
        foreach (string unsupported in new[] { "{Unknown}", "{Unknown-2}", "{Unknown-1} {Unknown}", "literal text" })
            Check(!Editable(Adrenaline(), unsupported), "ambiguous or missing adrenaline display formula hidden: " + unsupported);
        foreach (var mutate in new Action<XElement>[] {
            g => FirstCondition(g).Element("VarID")!.Value = "7",
            g => FirstCondition(g).Element("BlackboardType")!.Value = "3",
            g => FirstCondition(g).Element("Compare")!.Value = "0",
            g => FirstCondition(g).Element("Negate")!.Value = "True",
            g => FirstCondition(g).Element("CompareVarID")!.Value = "999",
            g => FirstCondition(g).SetAttributeValue("Type", "UnknownCondition"),
            g => g.Element("Keywords")!.Elements().First().Element("Conditions")!.Elements().Last().Element("ValidLocations")!.Value = "999",
            g => Node(g).Element("Keyword")!.SetAttributeValue("ExternalIn", "True"),
            g => Node(g).SetAttributeValue("Type", "UnknownKeywordReader"),
            g => g.Element("Keywords")!.Elements().Last().Remove() })
        {
            var bad = Adrenaline(); mutate(bad); Check(!Editable(bad), "unverified adrenaline condition or reader blocked");
        }
        var reversed = Adrenaline();
        var merged = Berserk();
        merged.Element("Nodes")!.Add(XElement.Parse("<e9 Type='PlayedTrigger' Id='90'><FlowOut Type='FlowConnector' Id='91'/></e9>"));
        merged.Element("Connections")!.Add(XElement.Parse("<e9 Type='Connection' Id='390' SrcId='91' DstId='12'/>"));
        Check(Editable(merged, "{Unknown}"), "berserk threshold shared by multiple valid flow triggers recognized");
        FirstCondition(reversed).Element("Compare")!.Value = "3";
        reversed.Element("Keywords")!.Elements().Last().Element("Conditions")!.Elements().First().Element("Compare")!.Value = "5";
        Check(Editable(reversed), "verified complementary adrenaline branch comparisons recognized");
        foreach (var mutate in new Action<XElement>[] {
            g => Node(g).Element("SubAbilityID")!.Value = "18",
            g => Node(g).Element("VarMappings")!.Elements().First().Element("IdOfDataInAbility")!.Value = "35",
            g => Node(g).Element("VarMappings")!.Elements().Last().Element("Data")!.SetAttributeValue("ExternalIn", "True"),
            g => Node(g).Element("VarMappings")!.Elements().Last().Element("IsInput")!.Value = "False",
            g => Node(g).Element("ConnectionMappings")!.Elements().Last().Element("IdOfDataInAbility")!.Value = "4",
            g => g.Element("Connections")!.Elements().First().Remove() })
        {
            var bad = Berserk(); mutate(bad); Check(!Editable(bad, "{Unknown}"), "unverified berserk contract blocked");
        }
        foreach (var (graph, text, role, low, high) in new[] { (Adrenaline(), "{Unknown-1}", "adrenaline-threshold", 1, 11), (Berserk(), "{Unknown}", "berserk-threshold", 1, 99) })
        {
            Check(AbilityNumbers.Inspect(graph, false, text).Single(n => n.Id == 200) is { Editable: true } n && n.Role == role, "threshold inferred independently of variable name: " + role);
            foreach (int value in new[] { low, high })
            {
                var edited = AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int,int> { [200] = value }, false, text);
                Variable(edited).SetAttributeValue("V", (string)Variable(graph).Attribute("V")!);
                Check(XNode.DeepEquals(graph, edited), "threshold edit preserves every comparison and effect: " + role);
            }
            foreach (int value in new[] { low - 1, high + 1 })
            {
                try { AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int,int> { [200] = value }, false, text); }
                catch (InvalidOperationException) { passed.Add("threshold range violation rejected: " + role); continue; }
                throw new Exception("Invalid threshold accepted");
            }
            Check(AbilityNumbers.Inspect(graph, true, text).All(n => !n.Editable), "shared threshold graph blocked: " + role);
            var mutable = new XElement(graph);
            mutable.Element("Nodes")!.Add(XElement.Parse("<e9 Type='SetVarNode' Id='90' VarId='200'/>"));
            Check(!Editable(mutable, text), "runtime threshold variable remains locked: " + role);
            var mixed = new XElement(graph);
            mixed.Element("Nodes")!.Add(XElement.Parse("<e9 Type='GetVarNode' Id='90' VarId='200'><Out Type='IntVar' Id='91' V='0'/></e9>"));
            mixed.Element("Connections")!.Add(XElement.Parse("<e9 Type='Connection' Id='390' SrcId='91' DstId='23'/>"));
            Check(!Editable(mixed, text), "threshold also used as effect magnitude remains locked: " + role);
            var external = new XElement(graph); Variable(external).SetAttributeValue("ExternalIn", "True");
            Check(!Editable(external, text), "external threshold variable remains locked: " + role);
        }
        return passed;
    }

    public static IReadOnlyList<string> Examples(EditorProject project)
    {
        var passed = new List<string>();
        foreach (var (card, id, stored, displayed, role) in new[] { (112202, 197, 5, 4, "adrenaline-threshold"), (200046, 363, 3, 3, "berserk-threshold"), (152301, 574, 2, 2, "bloodthirst") })
        {
            if (project.Numbers(card).SingleOrDefault(n => n.Id == id) is not { Editable: true } n || n.Value != stored || n.DisplayValue != displayed || n.Role != role)
                throw new Exception("Original threshold example failed: " + card);
            passed.Add("original threshold example " + card + ": stored=" + stored + ", displayed=" + displayed);
        }
        return passed;
    }
}
