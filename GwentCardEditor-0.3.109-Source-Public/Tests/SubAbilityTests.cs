using System.Xml.Linq;
using GwentCardEditor;

internal static class SubAbilityTests
{
    public static IReadOnlyList<string> Run()
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        XElement Mapping(int local, int target, string type, bool input, string? value = null)
        {
            var data = new XElement("Data", new XAttribute("Type", type), new XAttribute("Id", local));
            if (value != null) data.SetAttributeValue("V", value);
            if (type == "CardDefinitionVar") data.Add(new XElement("V", new XAttribute("Type", "CardDefinition"), new XAttribute("TemplateId", 11), new XAttribute("IsPremium", "False")));
            if (type == "CardPositionVar") data.Add(new XElement("V", new XAttribute("Type", "CardPosition"), new XAttribute("PlayerId", 0), new XAttribute("Location", 0), new XAttribute("Index", 0)));
            return new XElement("e" + local, data, new XElement("IdOfDataInAbility", target), new XElement("IsInput", input));
        }
        XElement Graph(int mode, bool literal = false)
        {
            var flows = mode == 0 ? new[] { (143, true), (136, false) } : mode == 1 ? new[] { (8, true), (11, false) } : new[] { (8, true), (11, false), (59, true) };
            var variables = mode == 0 ? new[] { (80, "IntVar", true), (139, "CardListVar", true) }
                : mode == 1 ? new[] { (18, "PlayerIdVar", true), (19, "ELocationVar", true), (40, "CardDefinitionVar", true), (42, "IntVar", true), (50, "CardListVar", false) }
                : new[] { (18, "PlayerIdVar", true), (19, "ELocationVar", true), (40, "CardDefinitionVar", true), (42, "IntVar", true), (50, "CardListVar", false), (63, "CardPositionVar", true) };
            var node = new XElement("e1", new XAttribute("Type", "UseSubAbilityNode"), new XAttribute("Id", 20),
                new XElement("SubAbilityID", mode == 0 ? 17 : 30),
                new XElement("ConnectionMappings", flows.Select((f, i) => Mapping(30 + i, f.Item1, "FlowConnector", f.Item2))),
                new XElement("VarMappings", variables.Select((v, i) => Mapping(40 + i, v.Item1, v.Item2, v.Item3, v.Item2 == "IntVar" ? "2" : null))));
            var numeric = node.Element("VarMappings")!.Elements().Select(m => m.Element("Data")!).Single(d => (string?)d.Attribute("Type") == "IntVar");
            var nodes = new XElement("Nodes", XElement.Parse("<e0 Type='PlayedTrigger' Id='10'><FlowOut Type='FlowConnector' Id='11'/></e0>"), node);
            var links = new XElement("Connections", XElement.Parse("<e0 Type='Connection' Id='300' SrcId='11' DstId='30'/>"));
            if (!literal)
            {
                nodes.Add(XElement.Parse("<e2 Type='GetVarNode' Id='80' VarId='200'><Out Type='IntVar' Id='81' V='0'/></e2>"));
                links.Add(new XElement("e1", new XAttribute("Type", "Connection"), new XAttribute("Id", 301), new XAttribute("SrcId", 81), new XAttribute("DstId", (int)numeric.Attribute("Id")!)));
            }
            return new XElement("Ability", new XAttribute("Type", "CardAbility"), new XAttribute("Id", 11), new XAttribute("NId", 1000),
                new XElement("TemporaryVariables", literal ? null : XElement.Parse("<e0 Type='IntVar' Id='200' Name='Param' V='2'/>")), new XElement("PersistentVariables"), nodes, links);
        }
        XElement Node(XElement graph) => graph.Element("Nodes")!.Elements().Single(n => (int?)n.Attribute("Id") == 20);
        bool Editable(XElement graph, int id = 200) => AbilityNumbers.Inspect(graph, false, "{Param}").Any(n => n.Id == id && n.Editable);
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var graph = Graph(mode);
            string role = mode == 0 ? "intimidate-boost" : "copy-count";
            int min = mode == 0 ? 0 : 1, max = mode == 0 ? 99 : 9;
            Check(AbilityNumbers.Inspect(graph, false, "{Param}").Single(n => n.Id == 200) is { Editable: true } n && n.Role == role, "standard subability role inferred without relying on variable name: " + mode);
            var literal = Graph(mode, true);
            int literalId = (int)Node(literal).Element("VarMappings")!.Elements().Select(m => m.Element("Data")!).Single(e => (string?)e.Attribute("Type") == "IntVar").Attribute("Id")!;
            Check(Editable(literal, literalId), "literal standard subability constant recognized: " + mode);
            foreach (int value in new[] { min, max })
            {
                var edited = AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int,int> { [200] = value }, false, "{Param}");
                edited.Descendants().Single(e => (int?)e.Attribute("Id") == 200).SetAttributeValue("V", 2);
                Check(XNode.DeepEquals(graph, edited), "subability edit preserves target, location, position and outputs: " + mode);
            }
            foreach (int value in new[] { min - 1, max + 1 })
            {
                try { AbilityNumbers.Prepare(new XElement("Template"), graph, new Dictionary<int,int> { [200] = value }, false, "{Param}"); }
                catch (InvalidOperationException) { passed.Add("invalid subability numeric range rejected"); continue; }
                throw new Exception("Invalid subability numeric range accepted");
            }
            foreach (var mutate in new Action<XElement>[] {
                g => Node(g).Element("VarMappings")!.Elements().First().Element("IdOfDataInAbility")!.Value = "999",
                g => { var input = Node(g).Element("VarMappings")!.Elements().Last().Element("IsInput")!; input.Value = bool.Parse(input.Value) ? "False" : "True"; },
                g => Node(g).Element("VarMappings")!.Elements().Last().Element("Data")!.SetAttributeValue("ExternalIn", "True"),
                g => Node(g).Element("ConnectionMappings")!.Elements().First().Element("IsInput")!.Value = "False",
                g => Node(g).Element("VarMappings")!.Elements().First().Remove(),
                g => g.Element("Connections")!.Elements().First().Remove() })
            {
                var bad = new XElement(graph); mutate(bad);
                Check(!Editable(bad), "unsupported standard subability contract blocked: " + mode);
            }
            Check(AbilityNumbers.Inspect(graph, true, "").All(n => !n.Editable), "shared standard subability graph blocked: " + mode);
            var mutable = new XElement(graph);
            mutable.Element("Nodes")!.Add(XElement.Parse("<e9 Type='SetVarNode' Id='90' VarId='200'/>"));
            Check(!Editable(mutable), "runtime subability parameter blocked: " + mode);
            var external = new XElement(graph); external.Descendants().Single(e => (int?)e.Attribute("Id") == 200).SetAttributeValue("ExternalOut", "True");
            Check(!Editable(external), "external subability parameter blocked: " + mode);
            var mixed = new XElement(graph);
            mixed.Element("Nodes")!.Add(XElement.Parse("<e9 Type='ChangeArmorNode' Id='90'><Armor Type='IntVar' Id='91' V='0'/></e9>"));
            mixed.Element("Connections")!.Add(XElement.Parse("<e9 Type='Connection' Id='390' SrcId='81' DstId='91'/>"));
            Check(!Editable(mixed), "mixed-purpose subability parameter blocked: " + mode);
        }
        var misleading = Graph(2);
        var badNumber = Node(misleading).Element("VarMappings")!.Elements().Single(m => (string?)m.Element("IdOfDataInAbility") == "42");
        badNumber.Element("Data")!.SetAttributeValue("Name", "Damage");
        badNumber.Element("IdOfDataInAbility")!.Value = "999";
        Check(!Editable(misleading), "known variable label cannot unlock an unknown copy-count contract");
        var remapped = Graph(2);
        foreach (var attribute in remapped.Descendants().Attributes().Where(a => a.Name.LocalName is "Id" or "SrcId" or "DstId" or "VarId"))
            attribute.Value = (int.Parse(attribute.Value) + 2000).ToString();
        Check(Editable(remapped, 2200), "copy-count contract ignores local numeric IDs");
        return passed;
    }

    public static IReadOnlyList<string> Examples(EditorProject project)
    {
        var passed = new List<string>();
        foreach (var (card, id, role) in new[] { (112208, 265, "copy-count"), (112208, 281, "copy-count"), (122310, 419, "copy-count"), (202312, 85, "intimidate-boost") })
        {
            if (project.Numbers(card).SingleOrDefault(n => n.Id == id) is not { Editable: true } n || n.Value != 1 || n.Role != role)
                throw new Exception("Original subability example failed: " + card);
            passed.Add("original subability example " + card + "/" + id + " = 1 (" + role + ")");
        }
        return passed;
    }
}
