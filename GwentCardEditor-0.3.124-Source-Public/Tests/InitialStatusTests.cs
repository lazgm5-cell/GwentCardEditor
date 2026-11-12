using System.Xml.Linq;
using GwentCardEditor;

internal static class InitialStatusTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        string folder = Path.Combine(scratch, "initial-status-source"); Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source"))) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string templatePath = Path.Combine(folder, "Templates.xml"), graphPath = Path.Combine(folder, "Abilities.xml");
        File.AppendAllText(Path.Combine(folder, "ru-ru.csv"), "keyword_shield;<#600101>Щит:</color>\r\nkeyword_veil;<#600101>Покров:</color>\r\n", new System.Text.UTF8Encoding(false));
        File.AppendAllText(Path.Combine(folder, "en-us.csv"), "keyword_shield;<#600101>Shield:</color>\r\nkeyword_veil;<#600101>Veil:</color>\r\n", new System.Text.UTF8Encoding(false));
        File.AppendAllText(Path.Combine(folder, "zh-cn.csv"), "keyword_shield;<#600101>护盾：</color>\r\nkeyword_veil;<#600101>遮蔽：</color>\r\n", new System.Text.UTF8Encoding(false));
        var templates = XDocument.Load(templatePath);
        const int preserved = 1048576 + 65536;
        templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue("Tokens", preserved + 8192);
        templates.Save(templatePath);
        string originalTemplate = File.ReadAllText(templatePath), originalGraph = File.ReadAllText(graphPath);
        var project = new EditorProject(folder);
        Check(project.InitialStatuses(11).All(s => s.Editable) && project.InitialStatuses(11).Single(s => s.Key == "veil").Value, "verified status flags read from template");
        Check(!project.Apply(11, project.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["shield"] = false, ["veil"] = true } }), "status no-op exact");
        bool unknownRejected = false;
        try { project.Apply(11, project.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["poison"] = true } }); }
        catch (InvalidOperationException e) when (e.Message == "UnsupportedFields") { unknownRejected = true; }
        Check(unknownRejected && !project.HasChanges, "unknown status cannot bypass verified flag list");
        project.Apply(11, project.CurrentEdit(11) with { Power = 6, Numbers = new Dictionary<int, int> { [200] = 5 },
            Statuses = new Dictionary<string, bool> { ["shield"] = true, ["veil"] = false } });
        Check(project.Card(11).Power == 6 && project.Numbers(11).Single(n => n.Id == 200).Value == 5
            && project.InitialStatuses(11).Single(s => s.Key == "shield").Value && !project.InitialStatuses(11).Single(s => s.Key == "veil").Value,
            "statuses basic settings and graph numbers apply atomically");
        string originalTooltip = "<keyword=deploy>Deploy</keyword>: damage {Damage}.\\nNext";
        Check(project.Text(11, "ru-ru", "tooltip").StartsWith("Начальные статусы: <keyword=shield>Щит</keyword>\\n", StringComparison.Ordinal)
            && project.Text(11, "ru-ru", "tooltip").EndsWith(originalTooltip, StringComparison.Ordinal), "localized starting Shield is added before original description");
        Check(project.Text(11, "en-us", "tooltip").StartsWith("Starting statuses: <keyword=shield>Shield</keyword>\\n", StringComparison.Ordinal)
            && project.Text(11, "zh-cn", "tooltip").Contains("<keyword=shield>护盾</keyword>", StringComparison.Ordinal), "status keywords use each game's localized term");
        Check(!project.Text(11, "ru-ru", "tooltip").Contains('\n') && !project.Text(11, "ru-ru", "tooltip").Contains('\r'), "status description stays on one physical line");
        string output = Path.Combine(scratch, "initial-status-export"); project.Export(output);
        var exported = XDocument.Load(Path.Combine(output, "Templates.xml"));
        Check((int)exported.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).Element("Tokens")! == preserved + 2,
            "unknown and unrelated status bits preserved");
        Check(new EditorProject(output).InitialStatuses(11).Single(s => s.Key == "shield").Value, "status export round-trip");
        var exportedProject = new EditorProject(output);
        Check(exportedProject.Text(11, "ru-ru", "tooltip") == project.Text(11, "ru-ru", "tooltip")
            && exportedProject.Text(11, "en-us", "tooltip") == project.Text(11, "en-us", "tooltip"), "localized status text export round-trip");
        project.Apply(11, project.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["shield"] = false, ["veil"] = true } });
        Check(project.Text(11, "ru-ru", "tooltip").StartsWith("Начальные статусы: <keyword=veil>Покров</keyword>\\n", StringComparison.Ordinal)
            && !project.Text(11, "ru-ru", "tooltip").Contains("<keyword=shield>", StringComparison.Ordinal), "status changes replace the generated localized list");
        project.Apply(11, project.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["veil"] = false } });
        Check(project.Text(11, "ru-ru", "tooltip") == originalTooltip
            && project.Text(11, "en-us", "tooltip") == originalTooltip, "removing final status restores the unmodified description");
        project.Undo(); project.Undo(); project.Undo(); Check(!project.HasChanges, "combined status and localized text undo exact");
        void BlockGraph(Action<XElement> mutate, string reason, bool shieldAndVeilOnly = false)
        {
            var doc = XDocument.Parse(originalGraph); mutate(doc.Root!.Elements().Single()); doc.Save(graphPath);
            var blocked = new EditorProject(folder);
            Check(blocked.InitialStatuses(11).Where(s => !shieldAndVeilOnly || s.Key is "shield" or "veil").All(s => !s.Editable), "initial statuses block " + reason);
            bool rejected = false;
            try { blocked.Apply(11, blocked.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
            catch (InvalidOperationException e) when (e.Message == blocked.InitialStatuses(11)[0].Reason) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "blocked status application atomic " + reason);
        }
        XElement BlackboardRead(string varType = "IntVar", int boardType = 2, int variableId = 5,
            int boardFieldId = 9101, int outputId = 9102, string outputValue = "0", bool extraField = false)
        {
            var node = new XElement("node", new XAttribute("Type", "GetBlackboardVarNode"),
                new XAttribute("Id", 9100), new XAttribute("VarId", variableId),
                new XElement("BlackboardType", new XAttribute("Type", "BlackboardVar"),
                    new XAttribute("Id", boardFieldId), new XAttribute("ExternalOut", "False"),
                    new XAttribute("ExternalIn", "False"), new XAttribute("ResetInInactive", "False"),
                    new XAttribute("V", boardType), new XElement("VarType", varType)),
                new XElement("Out", new XAttribute("Type", varType), new XAttribute("Id", outputId),
                    new XAttribute("ExternalOut", "False"), new XAttribute("ExternalIn", "False"),
                    new XAttribute("ResetInInactive", "False"), new XAttribute("V", outputValue)));
            if (extraField) node.Add(new XElement("Unexpected", new XAttribute("Id", 9103)));
            return node;
        }
        var blackboardPositive = XDocument.Parse(originalGraph);
        blackboardPositive.Root!.Elements().Single().Element("Nodes")!.Add(BlackboardRead());
        blackboardPositive.Save(graphPath);
        Check(new EditorProject(folder).InitialStatuses(11).All(s => s.Editable),
            "integer blackboard read contract accepted");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead("BoolVar")), "blackboard BoolVar read");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead("CardListVar")), "blackboard list read");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead(boardType: 99)), "unknown blackboard enum");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead(variableId: 0)), "invalid blackboard variable ID");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead(boardFieldId: 0)), "invalid blackboard field ID");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead(outputId: 0)), "invalid blackboard output ID");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead(outputValue: "invalid")), "invalid blackboard output value");
        BlockGraph(g => g.Element("Nodes")!.Add(BlackboardRead(extraField: true)), "unexpected blackboard node field");
        BlockGraph(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "ChangeTokensNode"), "status writer");
        BlockGraph(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "FilterCardTokensNode"), "status reader");
        BlockGraph(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "TransformCardsNode"), "transformation");
        BlockGraph(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "UseSubAbilityNode"), "opaque subability");
        BlockGraph(g => g.SetAttributeValue("ExternalOut", "True"), "external graph");
        BlockGraph(g => g.Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "GetCardsNode").Add(
            new XElement("Ignore", new XAttribute("Type", "CardTokensVar"), new XAttribute("Id", 9000), new XAttribute("V", 2))), "status mask in card query", true);
        foreach (var (key, flag) in new[] { ("resilient", 1), ("immune", 256), ("defender", 1024), ("doomed", 512) })
        {
            var dependency = XDocument.Parse(originalGraph);
            dependency.Root!.Elements().Single().Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "GetCardsNode").Add(
                new XElement("Ignore", new XAttribute("Type", "CardTokensVar"), new XAttribute("Id", 9000), new XAttribute("V", flag)));
            dependency.Save(graphPath);
            var dependent = new EditorProject(folder);
            Check(!dependent.InitialStatuses(11).Single(s => s.Key == key).Editable
                && dependent.InitialStatuses(11).Single(s => s.Key == "shield").Editable, "status dependency independently blocked " + key);
            bool rejected = false;
            try { dependent.Apply(11, dependent.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { [key] = !dependent.InitialStatuses(11).Single(s => s.Key == key).Value } }); }
            catch (InvalidOperationException e) when (e.Message == "InitialStatusesUnsupported") { rejected = true; }
            Check(rejected && !dependent.HasChanges, "status dependency rejection atomic " + key);
        }
        BlockGraph(g => g.Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "GetCardsNode").Add(
            new XElement("Ignore", new XAttribute("Type", "IntVar"), new XAttribute("Id", 9000), new XAttribute("V", 0))), "unexpected query mask type");
        BlockGraph(g =>
        {
            g.Element("Nodes")!.Elements().Single(e => (string?)e.Attribute("Type") == "GetCardsNode").Add(
                new XElement("Ignore", new XAttribute("Type", "CardTokensVar"), new XAttribute("Id", 9000), new XAttribute("V", 0)));
            g.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9001), new XAttribute("SrcId", 141), new XAttribute("DstId", 9000)));
        }, "dynamic query mask");
        BlockGraph(g => g.Element("Connections")!.Remove(), "incomplete graph");
        File.WriteAllText(graphPath, originalGraph);
        foreach (int id in new[] { 12, 14, 16 })
        {
            var blocked = new EditorProject(folder);
            Check(blocked.InitialStatuses(id).All(s => !s.Editable), "statuses exclude specials leaders and tokens " + id);
            bool rejected = false;
            try { blocked.Apply(id, blocked.CurrentEdit(id) with { Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "unsupported status cannot be applied " + id);
        }
        foreach (string? mask in new[] { "-1", "2147483648", "invalid", null })
        {
            var doc = XDocument.Parse(originalTemplate); var card = doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11);
            if (mask == null) card.Element("Tokens")!.Remove(); else card.SetElementValue("Tokens", mask);
            doc.Save(templatePath);
            Check(new EditorProject(folder).InitialStatuses(11).All(s => !s.Editable), "invalid status mask locked " + (mask ?? "missing"));
        }
        File.WriteAllText(templatePath, originalTemplate);
        var shared = XDocument.Parse(originalTemplate);
        shared.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 12).Element("AbilityIds")!.Elements().Single().Value = "11";
        shared.Save(templatePath);
        Check(new EditorProject(folder).InitialStatuses(11).All(s => !s.Editable && s.Reason == "SharedAbility"), "shared status graph locked");
        File.WriteAllText(templatePath, originalTemplate);
        var vanilla = XDocument.Parse(originalGraph); var graph = vanilla.Root!.Elements().Single();
        graph.Element("Nodes")!.Remove(); graph.Element("Connections")!.Remove(); graph.Element("TemporaryVariables")!.RemoveNodes();
        vanilla.Save(graphPath);
        Check(new EditorProject(folder).InitialStatuses(11).All(s => s.Editable), "native empty vanilla ability accepted");
        File.WriteAllText(graphPath, originalGraph);
        var artifact = XDocument.Parse(originalTemplate);
        artifact.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue("Type", 8);
        artifact.Save(templatePath);
        var artifactProject = new EditorProject(folder);
        Check(artifactProject.InitialStatuses(11).Single(s => s.Key == "resilient").Editable
            && artifactProject.InitialStatuses(11).Where(s => s.Key != "resilient").All(s => !s.Editable), "artifact supports only resilience");
        artifactProject.Apply(11, artifactProject.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["resilient"] = true } });
        Check(artifactProject.InitialStatuses(11).Single(s => s.Key == "resilient").Value, "artifact resilience enabled");
        string artifactOutput = Path.Combine(scratch, "artifact-status-export"); artifactProject.Export(artifactOutput);
        Check(new EditorProject(artifactOutput).InitialStatuses(11).Single(s => s.Key == "resilient").Value, "artifact resilience export round-trip");
        artifactProject.Undo(); Check(!artifactProject.HasChanges, "artifact resilience undo exact");
        bool artifactRejected = false;
        try { artifactProject.Apply(11, artifactProject.CurrentEdit(11) with { Provision = artifactProject.Card(11).Provision + 1, Statuses = new Dictionary<string, bool> { ["immune"] = true } }); }
        catch (InvalidOperationException e) when (e.Message == "StatusNeedsUnit") { artifactRejected = true; }
        Check(artifactRejected && !artifactProject.HasChanges, "artifact immunity remains blocked atomically");
        foreach (string key in new[] { "defender", "doomed" })
        {
            bool rejected = false;
            try { artifactProject.Apply(11, artifactProject.CurrentEdit(11) with { Statuses = new Dictionary<string, bool>
                { ["resilient"] = true, [key] = !artifactProject.InitialStatuses(11).Single(s => s.Key == key).Value } }); }
            catch (InvalidOperationException e) when (e.Message == "StatusNeedsUnit") { rejected = true; }
            Check(rejected && !artifactProject.HasChanges, "artifact rejects additional unit status atomically " + key);
        }
        File.WriteAllText(templatePath, originalTemplate);
        var expanded = new EditorProject(folder);
        expanded.Apply(11, expanded.CurrentEdit(11) with { Statuses = new Dictionary<string, bool> { ["resilient"] = true, ["immune"] = true } });
        Check(expanded.InitialStatuses(11).Where(s => s.Key is "resilient" or "immune").All(s => s.Value), "unit resilience and immunity independently enabled");
        expanded.Undo(); Check(!expanded.HasChanges, "expanded unit flags undo exact");
        foreach (bool defender in new[] { false, true })
        foreach (bool doomed in new[] { false, true })
        {
            bool changed = expanded.Apply(11, expanded.CurrentEdit(11) with { Statuses = new Dictionary<string, bool>
                { ["defender"] = defender, ["doomed"] = doomed } });
            Check(changed == (defender || doomed), $"defender/doomed no-op detection {defender}/{doomed}");
            string statusOutput = Path.Combine(scratch, $"defender-doomed-{defender}-{doomed}");
            if (changed) expanded.Export(statusOutput); else statusOutput = folder;
            var xml = XDocument.Load(Path.Combine(statusOutput, "Templates.xml"));
            int expected = ((preserved + 8192) & ~1536) | (defender ? 1024 : 0) | (doomed ? 512 : 0);
            Check((int)xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).Element("Tokens")! == expected,
                $"defender/doomed combination preserves unrelated flags {defender}/{doomed}");
            var loaded = new EditorProject(statusOutput);
            Check(!loaded.Card(11).IsToken && loaded.InitialStatuses(11).Single(s => s.Key == "defender").Value == defender
                && loaded.InitialStatuses(11).Single(s => s.Key == "doomed").Value == doomed,
                $"defender/doomed round-trip without token category {defender}/{doomed}");
            if (expanded.HasChanges) expanded.Undo();
            Check(!expanded.HasChanges, $"defender/doomed undo exact {defender}/{doomed}");
        }
        foreach (string xml in new[]
        {
            "<e Type='FilterCardTypeNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><Type Type='EnumVar`1' GArg='ECardType' V='4'/></e>",
            "<e Type='FilterCardPowerNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><PowerType Type='EnumVar`1' GArg='EPowerType' V='0'/><Op Type='EnumVar`1' GArg='ECompareOp' V='5'/><Power Type='IntVar' V='0'/></e>",
            "<e Type='FilterCardPlayerIdNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><PlayerId Type='PlayerIdVar' V='3'/></e>",
            "<e Type='GetOwnerLocationNode'><Out Type='ELocationVar' V='0'/></e>",
            "<e Type='GetCardTemplateProvisionNode'><DS Type='CardVar'/><DV Type='IntVar' V='0'/></e>",
            "<e Type='IntMultiplyNode'><Value Type='IntVar' V='0'/><Factor Type='IntVar' V='100'/><Rounding Type='EnumVar`1' GArg='ERounding' V='2'/><Result Type='IntVar' V='0'/></e>",
            "<e Type='IntDivideNode'><Value Type='IntVar' V='0'/><Divisor Type='IntVar' V='1'/><Rounding Type='EnumVar`1' GArg='ERounding' V='1'/><Result Type='IntVar' V='0'/></e>",
            "<e Type='BranchNode'><FlowIn Type='FlowConnector'/><FlowOutTrue Type='FlowConnector'/><FlowOutFalse Type='FlowConnector'/><Condition Type='BoolVar' V='False'/></e>",
            "<e Type='CardHasArmorBranchNode'><FlowIn Type='FlowConnector'/><FlowOutTrue Type='FlowConnector'/><FlowOutFalse Type='FlowConnector'/><Card Type='CardVar'/></e>",
            "<e Type='FilterCardProvisionNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><Provision Type='IntVar' V='0'/><Op Type='EnumVar`1' GArg='ECompareOp' V='0'/></e>",
            "<e Type='FilterHasOnPlayedAbilityNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/></e>",
            "<e Type='FilterTapCardVisibleChargesNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><VisibleCharges Type='BoolVar' V='True'/></e>",
            "<e Type='FilterTapCardStateNode'><Negate Type='BoolVar' V='False'/><Out Type='CardFilterVar' V='False'/><TapCardState Type='EnumVar`1' GArg='ETapCardState' V='0'/></e>",
            "<e Type='ChangeOwnerMoneyNode'><FlowIn Type='FlowConnector'/><FlowOut Type='FlowConnector'/><Operation Type='EnumVar`1' GArg='EMoneyOperation' V='0'/><Value Type='IntVar' V='0'/><IsTribute Type='BoolVar' V='False'/></e>",
            "<e Type='ChangeOwnerMoneyNode'><FlowIn Type='FlowConnector'/><FlowOut Type='FlowConnector'/><Operation Type='EnumVar`1' GArg='EMoneyOperation' V='0'/><Value Type='IntVar' V='0'/></e>"
        })
        {
            var node = XElement.Parse(xml); int next = 9000;
            foreach (var element in node.DescendantsAndSelf()) element.SetAttributeValue("Id", next++);
            var doc = XDocument.Parse(originalGraph); doc.Root!.Elements().Single().Element("Nodes")!.Add(node); doc.Save(graphPath);
            Check(new EditorProject(folder).InitialStatuses(11).All(s => s.Editable), "status-neutral selection schema accepted " + (string?)node.Attribute("Type"));
            foreach (string mutation in new[] { "extra field", "missing field", "duplicate field", "wrong type", "wrong enum", "nested field", "external input" })
            {
                var broken = new XElement(node);
                switch (mutation)
                {
                    case "extra field": broken.Add(new XElement("Unknown", new XAttribute("Id", 9100), new XAttribute("Type", "IntVar"), new XAttribute("V", 0))); break;
                    case "missing field": broken.Elements().First().Remove(); break;
                    case "duplicate field": var duplicate = new XElement(broken.Elements().First()); duplicate.SetAttributeValue("Id", 9200); broken.Add(duplicate); break;
                    case "wrong type": broken.Elements().First().SetAttributeValue("Type", "CardTokensVar"); break;
                    case "wrong enum": broken.Elements().First().SetAttributeValue("GArg", "ECardToken"); break;
                    case "nested field": broken.Elements().First().Add(new XElement("Nested")); break;
                    case "external input": broken.Elements().First().SetAttributeValue("ExternalIn", "True"); break;
                }
                doc = XDocument.Parse(originalGraph); doc.Root!.Elements().Single().Element("Nodes")!.Add(broken); doc.Save(graphPath);
                var blocked = new EditorProject(folder);
                Check(blocked.InitialStatuses(11).All(s => !s.Editable), "status-neutral schema rejects " + (string?)node.Attribute("Type") + ": " + mutation);
                bool rejected = false;
                try { blocked.Apply(11, blocked.CurrentEdit(11) with { Power = 6, Statuses = new Dictionary<string, bool> { ["defender"] = true } }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !blocked.HasChanges, "status-neutral malformed schema atomic " + (string?)node.Attribute("Type") + ": " + mutation);
            }
        }
        File.WriteAllText(graphPath, originalGraph);
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        int supported = project.Cards.Count(c => c.Type == 4 && project.InitialStatuses(c.Id).Single(s => s.Key == "shield").Editable);
        Check(supported > 72, "initial shield coverage expanded: " + supported + " regular units");
        foreach (string key in new[] { "resilient", "immune", "defender", "doomed" })
            Check(project.Cards.Count(c => c.Type == 4 && project.InitialStatuses(c.Id).Single(s => s.Key == key).Editable) > 20,
                "initial " + key + " coverage: " + project.Cards.Count(c => c.Type == 4 && project.InitialStatuses(c.Id).Single(s => s.Key == key).Editable));
        Check(project.Cards.Where(c => c.IsToken || c.Type is not (4 or 8)).All(c => project.InitialStatuses(c.Id).All(s => !s.Editable)), "initial statuses do not unlock leaders tokens or specials");
        var artifacts = project.Cards.Where(c => c.Type == 8 && project.InitialStatuses(c.Id).Any(s => s.Editable)).ToArray();
        Check(artifacts.Length > 0 && artifacts.All(c => project.InitialStatuses(c.Id).Where(s => s.Key != "resilient").All(s => !s.Editable)), "verified artifact resilience coverage: " + artifacts.Length);
        int[] examples = [200041, 202609, 202418, 122212, 113311, 112211, 112212, 142207, 142317, 202198, 202462,
            122314, 152314, 162307, 201616, 202224, 202335, 202432,
            122211, 200062, 201630, 201661, 202106, 202479, 202602,
            142309, 152317, 202799, 202407, 202044, 201753, 162313, 122204, 202340,
            142101, 142212, 200102, 200103, 202122, 202483,
            202296, 202370, 202379, 202386, 202559, 202849, 202306, 202311, 202426,
            162208, 200050, 202260, 202419, 112405, 200145, 203231, 203233];
        foreach (int id in examples)
        {
            var settings = project.InitialStatuses(id);
            Check(settings.Any(s => s.Editable), "verified original status controls " + id);
            project.Apply(id, project.CurrentEdit(id) with { Statuses = settings.Where(s => s.Editable).ToDictionary(s => s.Key, s => !s.Value) });
        }
        var cavalry = project.InitialStatuses(122314);
        Check(cavalry.Where(s => s.Key is "shield" or "veil").All(s => !s.Editable)
            && cavalry.Where(s => s.Key is not ("shield" or "veil")).All(s => s.Editable), "Kaedweni Cavalry locks dependent flags but permits independent statuses");
        Check(project.InitialStatuses(202106).Where(s => s.Key is "resilient" or "immune").All(s => !s.Editable)
            && project.InitialStatuses(202106).Where(s => s.Key is not ("resilient" or "immune")).All(s => s.Editable), "Gabor locks both written statuses but permits independent flags");
        Check(project.InitialStatuses(202602).Where(s => s.Key is "shield" or "veil" or "doomed").All(s => !s.Editable)
            && project.InitialStatuses(202602).Single(s => s.Key == "resilient").Editable, "Fortune Teller combines dependencies from both status writers");
        Check(project.InitialStatuses(162316).All(s => !s.Editable), "Alchemist card-object state remains excluded");
        Check(project.InitialStatuses(202264).All(s => !s.Editable), "Great Oak position state remains excluded");
        Check(project.Changes().Count(c => c.Field.EndsWith("/Tokens", StringComparison.Ordinal)) == examples.Length
            && project.Changes().Any(c => c.Field.EndsWith("_tooltip", StringComparison.Ordinal)), "real status changes include localized descriptions");
        string output = Path.Combine(scratch, "real-initial-status-export"); project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-initial-status-cache"));
        var templates = XDocument.Load(Path.Combine(result.DirectoryPath, "Templates.xml"));
        var original = XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml"));
        foreach (int id in examples)
        {
            Check(result.InitialStatuses(id).SequenceEqual(project.InitialStatuses(id)), "real initial status packed round-trip " + id);
            var card = templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
            var before = original.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
            int editableMask = project.InitialStatuses(id).Where(s => s.Editable).Aggregate(0, (mask, s) => mask | EditorProject.InitialStatusMasks[s.Key]);
            Check(((int)card.Element("Tokens")! & ~editableMask) == ((int)before.Element("Tokens")! & ~editableMask), "other original statuses preserved " + id);
            card.Element("Tokens")!.Value = before.Element("Tokens")!.Value;
            foreach (string language in result.AvailableLanguages.Where(l => result.HasText(id, l, "tooltip")))
                Check(result.Text(id, language, "tooltip") == project.Text(id, language, "tooltip"), "real localized status description round-trip " + id + "/" + language);
        }
        Check(XNode.DeepEquals(templates, original), "status export preserves every other template field");
        Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, "Abilities.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, "Abilities.xml"))), "status edit preserves ability archive entry exactly");
        for (int i = 0; i < examples.Length; i++) project.Undo();
        Check(!project.HasChanges, "all initial status changes undo exactly");
        return passed;
    }
}
