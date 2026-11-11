using System.Xml.Linq;
using GwentCardEditor;

internal static class StatusMoneyTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        XElement Graph(bool money = false, bool literal = false) => XElement.Parse($$"""
        <Ability Type="CardAbility" Id="11" NId="1000"><TemporaryVariables>
        {{(literal ? "" : "<e0 Type=\"IntVar\" Id=\"200\" Name=\"Param\" V=\"3\"/>")}}
        </TemporaryVariables><PersistentVariables/><Nodes>
        <e0 Type="PlayedTrigger" Id="10"><FlowOut Type="FlowConnector" Id="11"/></e0>
        <e1 Type="{{(money ? "ChangeOwnerMoneyNode" : "BalanceCardStatusTimerNode")}}" Id="20">
        <FlowIn Type="FlowConnector" Id="21"/><FlowOut Type="FlowConnector" Id="22"/>
        <{{(money ? "Value" : "DeltaTime")}} Type="IntVar" Id="23" V="3"/>
        {{(money ? "<Operation Type=\"EnumVar`1\" GArg=\"EMoneyOperation\" Id=\"24\" V=\"0\"/><IsTribute Type=\"BoolVar\" Id=\"25\" V=\"False\"/>" : "<Acceptor Type=\"CardTokensVar\" Id=\"24\" V=\"16\"/><Donor Type=\"CardTokensVar\" Id=\"25\" V=\"32\"/><ElementType Type=\"EnumVar`1\" GArg=\"EElementType\" Id=\"26\" V=\"16384\"/><AttackMethod Type=\"EnumVar`1\" GArg=\"EAttackMethod\" Id=\"27\" V=\"1\"/><EffectType Type=\"EnumVar`1\" GArg=\"EEffectType\" Id=\"28\" V=\"0\"/>")}}
        </e1>
        {{(literal ? "" : "<e2 Type=\"GetVarNode\" Id=\"40\" VarId=\"200\"><Out Type=\"IntVar\" Id=\"41\" V=\"0\"/></e2>")}}
        </Nodes><Connections><e0 Type="Connection" Id="300" SrcId="11" DstId="21"/>
        {{(literal ? "" : "<e1 Type=\"Connection\" Id=\"301\" SrcId=\"41\" DstId=\"23\"/>")}}
        </Connections></Ability>
        """);
        XElement Node(XElement g) => g.Element("Nodes")!.Elements().Single(n => (int?)n.Attribute("Id") == 20);
        bool Editable(XElement g, int id) => AbilityNumbers.Inspect(g, false, "{Param}").Any(n => n.Id == id && n.Editable);
        void Reject(XElement g, int id, int value)
        {
            try { AbilityNumbers.Prepare(new XElement("Template"), g, new Dictionary<int,int> { [id] = value }, false, "{Param}"); }
            catch (InvalidOperationException) { passed.Add("status/money rejected unsafe edit"); return; }
            throw new Exception("Unsafe status/money edit accepted");
        }
        foreach (bool money in new[] { false, true })
        {
            var g = Graph(money);
            Check(Editable(g, 200), "semantic role inferred without relying on variable name: " + money);
            Check(!Editable(g, 23), "connected semantic field remains computed: " + money);
            Check(Editable(Graph(money, true), 23), "literal semantic field recognized: " + money);
            Reject(g, 23, 4); Reject(g, 200, -1); Reject(g, 200, money ? 10 : 100);
            var next = AbilityNumbers.Prepare(new XElement("Template"), g, new Dictionary<int,int> { [200] = 4 }, false, "{Param}");
            next.Descendants().Single(e => (int?)e.Attribute("Id") == 200).SetAttributeValue("V", 3);
            Check(XNode.DeepEquals(g, next), "semantic edit changes only requested V: " + money);
            Check(AbilityNumbers.Inspect(g, true, "").All(n => !n.Editable), "shared semantic graph blocked: " + money);
            var mutable = new XElement(g);
            mutable.Element("Nodes")!.Add(new XElement("e3", new XAttribute("Type", "SetVarNode"), new XAttribute("Id", 50), new XAttribute("VarId", 200)));
            Check(!Editable(mutable, 200), "runtime semantic value blocked: " + money);
            foreach (string attribute in new[] { "ExternalIn", "ExternalOut" })
            {
                var external = new XElement(g);
                external.Descendants().Single(e => (int?)e.Attribute("Id") == 200).SetAttributeValue(attribute, "True");
                Check(!Editable(external, 200), "external semantic variable blocked " + attribute + money);
            }
            foreach (string field in money ? new[] { "Operation", "IsTribute" } : new[] { "Acceptor", "Donor", "ElementType", "AttackMethod", "EffectType" })
            {
                var invalid = new XElement(g);
                Node(invalid).Element(field)!.SetAttributeValue("V", money && field == "IsTribute" ? "True" : "999");
                Check(!Editable(invalid, 200), "unknown or unsafe flag blocked " + field + money);
                Reject(invalid, 200, 4);
                var dynamic = new XElement(g);
                Node(dynamic).Element(field)!.SetAttributeValue("ExternalIn", "True");
                Check(!Editable(dynamic, 200), "dynamic semantic flag blocked " + field + money);
                var connected = new XElement(g);
                int destination = (int)Node(connected).Element(field)!.Attribute("Id")!;
                connected.Element("Connections")!.Add(new XElement("e2", new XAttribute("Type", "Connection"), new XAttribute("Id", 302), new XAttribute("SrcId", destination), new XAttribute("DstId", destination)));
                Check(!Editable(connected, 200), "connected semantic flag blocked " + field + money);
            }
            foreach (int boundary in new[] { 0, money ? 9 : 99 })
            {
                var edited = AbilityNumbers.Prepare(new XElement("Template"), g, new Dictionary<int,int> { [200] = boundary }, false, "{Param}");
                Check((int?)edited.Descendants().Single(e => (int?)e.Attribute("Id") == 200).Attribute("V") == boundary, "semantic boundary accepted: " + money + ":" + boundary);
            }
        }
        var vitality = Graph();
        Node(vitality).Element("Acceptor")!.SetAttributeValue("V", 32);
        Node(vitality).Element("Donor")!.SetAttributeValue("V", 16);
        Check(AbilityNumbers.Inspect(vitality, false, "").Single(n => n.Id == 200).Role == "vitality-duration", "status masks determine vitality despite misleading effect element");
        var activation = Graph(); Node(activation).Element("Acceptor")!.SetAttributeValue("V", 0);
        Check(!Editable(activation, 200), "status activation is not duration addition");
        var legacyMoney = Graph(true); Node(legacyMoney).Element("IsTribute")!.Remove();
        Check(Editable(legacyMoney, 200), "legacy coin gain without tribute flag recognized");
        foreach (int operation in new[] { 1, 2 })
        {
            var payment = Graph(true); Node(payment).Element("Operation")!.SetAttributeValue("V", operation);
            Check(!Editable(payment, 200), "coin spend or set operation remains locked: " + operation);
        }
        var mixed = Graph();
        var other = new XElement(Node(vitality));
        foreach (var a in other.DescendantsAndSelf().Attributes("Id")) a.Value = (int.Parse(a.Value) + 100).ToString();
        mixed.Element("Nodes")!.Add(other);
        mixed.Element("Connections")!.Add(new XElement("e2", new XAttribute("Type", "Connection"), new XAttribute("Id", 302), new XAttribute("SrcId", 41), new XAttribute("DstId", 123)));
        Check(!Editable(mixed, 200), "one variable for bleeding and vitality remains ambiguous");
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project, string scratch, bool profit = false, bool payment = false, bool threshold = false, bool subAbility = false)
    {
        var passed = new List<string>();
        string[] roles = subAbility ? ["copy-count", "intimidate-boost"] : threshold ? ["adrenaline-threshold", "berserk-threshold", "bloodthirst"] : payment ? ["tribute-cost", "fee-cost"] : profit ? ["profit", "base-profit"] : ["bleeding-duration", "vitality-duration", "coin-gain"];
        var selected = project.Cards.Where(c => c.Editable).SelectMany(c => project.Numbers(c.Id)
            .Where(n => n.Editable && roles.Contains(n.Role))
            .Select(n => (Card: c.Id, Number: n))).ToArray();
        foreach (string role in roles)
        {
            if (!selected.Any(n => n.Number.Role == role)) throw new Exception("No real examples: " + role);
            passed.Add("real semantic coverage " + role + ": " + selected.Count(n => n.Number.Role == role));
        }
        foreach (var card in selected.GroupBy(n => n.Card))
            project.Apply(card.Key, project.CurrentEdit(card.Key) with { Numbers = card.ToDictionary(n => n.Number.Id, n => n.Number.Value < n.Number.Maximum ? n.Number.Value + 1 : n.Number.Value - 1) });
        string output = Path.Combine(scratch, subAbility ? "real-subability" : threshold ? "real-threshold" : payment ? "real-payment" : profit ? "real-profit" : "real-status-money");
        project.Export(output);
        var original = XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml"));
        var reloaded = ClientSource.Open(output, Path.Combine(scratch, subAbility ? "subability-reload" : threshold ? "threshold-reload" : payment ? "payment-reload" : profit ? "profit-reload" : "status-reload"));
        var actual = XDocument.Load(Path.Combine(reloaded.DirectoryPath, "Abilities.xml"));
        foreach (var item in selected)
        {
            var value = reloaded.Numbers(item.Card).Single(n => n.Id == item.Number.Id);
            if (!value.Editable || value.Value == item.Number.Value) throw new Exception("Semantic round-trip failed");
            actual.Root!.Elements().Single(g => (string?)g.Attribute("Type") == "CardAbility" && (int?)g.Attribute("Id") == item.Card)
                .Descendants().Single(e => (int?)e.Attribute("Id") == item.Number.Id).SetAttributeValue("V", item.Number.Value);
            if (item.Card == 202702 && item.Number.Id == 2)
            {
                var linked = actual.Root!.Elements().Single(g => (int?)g.Attribute("Id") == 202674)
                    .Descendants().Single(e => (int?)e.Attribute("Id") == 1133);
                int expected = item.Number.Value < item.Number.Maximum ? item.Number.Value + 1 : item.Number.Value - 1;
                if ((int?)linked.Attribute("V") != expected) throw new Exception("Choice vitality not synchronized with parent");
                linked.SetAttributeValue("V", item.Number.Value);
            }
        }
        if (!XNode.DeepEquals(original, actual)) throw new Exception("Unrequested semantic graph changes");
        string comparison=reloaded.DirectoryPath;
        if(subAbility)
        {
            foreach(var item in selected.Where(s=>s.Number.LabelRole?.StartsWith("keyword-boost:",StringComparison.Ordinal)==true))
            {
                foreach(string lang in project.AvailableLanguages.Where(l=>project.HasText(item.Card,l,"tooltip")))
                    if(reloaded.Text(item.Card,lang,"tooltip")!=project.Text(item.Card,lang,"tooltip"))throw new Exception("Native keyword description not synchronized");
                reloaded.Apply(item.Card,reloaded.CurrentEdit(item.Card) with {Numbers=new Dictionary<int,int>{{item.Number.Id,item.Number.Value}}});
            }
            if(reloaded.HasChanges)
            {
                string restored=Path.Combine(scratch,"subability-restored-descriptions");reloaded.Export(restored);
                comparison=ClientSource.Open(restored,Path.Combine(scratch,"subability-restored-cache")).DirectoryPath;
            }
        }
        foreach (var file in Directory.GetFiles(project.DirectoryPath).Where(f => Path.GetExtension(f) == ".csv" || Path.GetFileName(f) == "Templates.xml"))
            if (!File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(comparison, Path.GetFileName(file))))) throw new Exception("Semantic edit changed " + file);
        passed.Add("all real status/money edits round-trip and only requested graph values change");
        passed.Add(subAbility?"native keyword descriptions synchronized; reset preserves every localization byte":"all real status/money edits preserve template and every localization byte");
        for (int i = 0; i < selected.Select(n => n.Card).Distinct().Count(); i++) project.Undo();
        if (project.HasChanges) throw new Exception("Semantic undo failed");
        passed.Add("all real status/money edits undo exactly");
        return passed;
    }
}
