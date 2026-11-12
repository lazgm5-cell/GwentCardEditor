using System.Xml.Linq;
using GwentCardEditor;

internal static class NumberTests
{
    public static IReadOnlyList<string> Run(string source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (InvalidOperationException e) when (e.Message == reason) { passed.Add("numbers reject " + reason); return; }
            throw new Exception("Expected " + reason);
        }
        string directory = Path.Combine(scratch, "numbers-source");
        Directory.CreateDirectory(directory);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        var graph = XElement.Parse("""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables>
          <e0 Type="IntVar" Id="200" Name="Damage" ExternalIn="False" ExternalOut="False" V="3" />
          <e1 Type="IntVar" Id="201" Name="Value" ExternalIn="False" ExternalOut="False" V="7" />
        </TemporaryVariables><PersistentVariables/><Nodes>
          <e0 Type="PlayedTrigger" Id="100"><FlowOut Type="FlowConnector" Id="101"/></e0>
          <e1 Type="RequestCardTargetsNode" Id="110"><FlowIn Type="FlowConnector" Id="111"/><FlowOutSuccess Type="FlowConnector" Id="112"/>
            <MinTargets Type="IntVar" Id="113" V="1"/><MaxTargets Type="IntVar" Id="114" V="1"/>
            <SelectedTargets Type="CardListVar" Id="115"/><ValidTargets Type="CardListVar" Id="116"/></e1>
          <e2 Type="GetCardsNode" Id="120"><Out Type="CardListVar" Id="121"/></e2>
          <e3 Type="ChangePowerNode" Id="130"><FlowIn Type="FlowConnector" Id="131"/><FlowOut Type="FlowConnector" Id="132"/>
            <Targets Type="CardListVar" Id="133"/><Power Type="IntVar" Id="134" V="0"/><Operation Type="EnumVar`1" GArg="ECardPowerOp" Id="135" V="1"/>
            <TargetedPower Type="EnumVar`1" GArg="EPowerType" Id="136" V="0"/></e3>
          <e4 Type="GetVarNode" Id="140" VarId="200"><Out Type="IntVar" Id="141" V="0"/></e4>
          <e5 Type="ChangeArmorNode" Id="150"><FlowIn Type="FlowConnector" Id="151"/><FlowOut Type="FlowConnector" Id="152"/><Armor Type="IntVar" Id="153" V="1"/></e5>
        </Nodes><Connections>
          <e0 Type="Connection" Id="300" SrcId="101" DstId="111"/>
          <e1 Type="Connection" Id="301" SrcId="121" DstId="116"/>
          <e2 Type="Connection" Id="302" SrcId="112" DstId="131"/>
          <e3 Type="Connection" Id="303" SrcId="115" DstId="133"/>
          <e4 Type="Connection" Id="304" SrcId="141" DstId="134"/>
          <e5 Type="Connection" Id="305" SrcId="132" DstId="151"/>
        </Connections></Ability>
        """);
        string abilityFile = Path.Combine(directory, "Abilities.xml");
        new XDocument(new XElement("Abilities", graph)).Save(abilityFile);
        var project = new EditorProject(directory);
        var numbers = project.Numbers(11);
        Check(numbers.Single(n => n.Id == 200) is { Editable: true, Role: "damage", Context: "Deploy", TextLinked: true }, "named damage verified through GetVarNode");
        Check(numbers.Single(n => n.Id == 134) is { Editable: false, Reason: "ComputedValue" }, "computed damage field blocked");
        Check(numbers.Single(n => n.Id == 201) is { Editable: false, Reason: "AmbiguousValue" }, "generic value blocked");
        Check(numbers.Single(n => n.Id == 153) is { Editable: true, Role: "armor-change" }, "direct node constant verified");
        CardEdit Edit(params (int Id, int Value)[] values) => project.CurrentEdit(11) with { Numbers = values.ToDictionary(x => x.Id, x => x.Value) };
        Reject(() => project.Apply(11, Edit((134, 8))), "ComputedValue");
        Reject(() => project.Apply(11, Edit((201, 8))), "AmbiguousValue");
        Reject(() => project.Apply(11, Edit((999, 8))), "AmbiguousValue");
        Reject(() => project.Apply(11, Edit((200, -1))), "AbilityRange");
        Reject(() => project.Apply(11, Edit((113, 3), (114, 2))), "InvalidTargetLimits");
        Check(!project.HasChanges && !project.CanUndo, "invalid graph edits do not alter transaction state");
        project.Apply(11, Edit((200, 5), (113, 2), (114, 2)) with { Power = 7 });
        Check(project.Card(11).Power == 7 && project.Numbers(11).Single(n => n.Id == 200).Value == 5, "atomic basic and numeric apply");
        project.Undo();
        Check(!project.HasChanges && project.Numbers(11).Single(n => n.Id == 200).Value == 3, "numeric undo restores exact graph");
        project.Apply(11, Edit((200, 5)));
        string output = Path.Combine(scratch, "numbers-export");
        project.Export(output);
        var loaded = new EditorProject(output);
        Check(loaded.Numbers(11).Single(n => n.Id == 200).Value == 5, "numeric export round-trip");
        Check(File.ReadAllBytes(Path.Combine(directory, "Templates.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Templates.xml"))), "ability-only edit preserves template bytes");
        Check(File.ReadAllText(Path.Combine(directory, "ru-ru.csv")) == File.ReadAllText(Path.Combine(output, "ru-ru.csv")), "ability edit preserves localized placeholders");
        var original = XDocument.Load(abilityFile);
        var actual = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        actual.Descendants().Single(x => (string?)x.Attribute("Type") == "IntVar" && (int?)x.Attribute("Id") == 200).SetAttributeValue("V", 3);
        Check(XNode.DeepEquals(original, actual), "only requested numeric attribute changes");
        project.Reset(); project.Undo();
        Check(project.Numbers(11).Single(n => n.Id == 200).Value == 5, "undo reset preserves numeric edit");
        XElement ambiguous = new(graph);
        ambiguous.Element("Connections")!.Add(new XElement("e6", new XAttribute("Type", "Connection"), new XAttribute("Id", 306), new XAttribute("SrcId", 141), new XAttribute("DstId", 153)));
        Check(AbilityNumbers.Inspect(ambiguous, false, "{Damage}").Single(n => n.Id == 200).Reason == "AmbiguousValue", "mixed-purpose shared number blocked");
        XElement mutable = new(graph);
        mutable.Element("Nodes")!.Add(new XElement("e6", new XAttribute("Type", "SetVarNode"), new XAttribute("Id", 160), new XAttribute("VarId", 200)));
        Check(AbilityNumbers.Inspect(mutable, false, "{Damage}").Single(n => n.Id == 200).Reason == "RuntimeVariable", "runtime reassignment detected");
        Check(AbilityNumbers.Inspect(graph, true, "{Damage}").All(n => !n.Editable), "shared ability cannot be edited");
        XElement metadataUse = new(graph);
        metadataUse.SetAttributeValue("ProfitVarID", 200);
        Check(AbilityNumbers.Inspect(metadataUse, false, "{Damage}").Single(n => n.Id == 200).Reason == "AmbiguousValue", "metadata reference prevents hidden shared-purpose edit");
        return passed;
    }
}
