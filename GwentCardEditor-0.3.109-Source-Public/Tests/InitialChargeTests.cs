using System.Xml.Linq;
using GwentCardEditor;

internal static class InitialChargeTests
{
    public static IReadOnlyList<string> Run(string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        void Reject(Action action, string reason)
        {
            try { action(); } catch (InvalidOperationException e) when (e.Message == reason) { passed.Add("initial charges reject " + reason); return; }
            throw new Exception("Expected " + reason);
        }
        string folder = Path.Combine(scratch, "initial-charges");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(Path.Combine(scratch, "numbers-source")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        string templatePath = Path.Combine(folder, "Templates.xml"), abilityPath = Path.Combine(folder, "Abilities.xml");
        var templates = XDocument.Load(templatePath);
        var template = templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11);
        foreach (var (field, value) in new[] { ("TapActive", "True"), ("VisibleTapCharges", "True"), ("InitialTimer", "-1"), ("InitalTapCharges", "4") })
            template.SetElementValue(field, value);
        templates.Save(templatePath);
        var abilities = XDocument.Load(abilityPath);
        var graph = abilities.Root!.Elements().Single();
        graph.Element("Nodes")!.Elements().First().SetAttributeValue("Type", "TappedTrigger");
        graph.Element("TemporaryVariables")!.Add(new XElement("e9", new XAttribute("Type", "IntVar"), new XAttribute("Id", 900),
            new XAttribute("Name", "Charges"), new XAttribute("V", 4), new XAttribute("ExternalIn", "False"), new XAttribute("ExternalOut", "False")));
        abilities.Save(abilityPath);
        foreach (string locale in EditorProject.Languages)
        {
            string path = Path.Combine(folder, locale + ".csv");
            File.WriteAllLines(path, File.ReadAllLines(path).Select(line => line.StartsWith("11_tooltip;", StringComparison.Ordinal)
                ? line + " <keyword=charge>Charges</keyword>: {Charges}" : line));
        }
        string originalGraph = File.ReadAllText(abilityPath), originalTemplate = File.ReadAllText(templatePath);
        var project = new EditorProject(folder);
        Check(project.InitialCharges(11) is { Editable: true, Value: 4, TextVariableId: 900 }, "initial charges presentation alias verified");
        Check(project.Numbers(11).Single(v => v.Id == -1) is { Editable: true, Minimum: 1, Maximum: 9, TextLinked: true }, "one editable initial charge parameter");
        Reject(() => project.Apply(11, project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [-1] = 0 } }), "AbilityRange");
        Reject(() => project.Apply(11, project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [-1] = 10 } }), "AbilityRange");
        Reject(() => project.Apply(11, project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [900] = 5 } }), project.Numbers(11).Single(v => v.Id == 900).Reason);
        Reject(() => project.Apply(11, project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [-1] = 5, [134] = 9 } }), "ComputedValue");
        Check(!project.HasChanges, "invalid combined charge edit is atomic");
        Check(!project.Apply(11, project.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [-1] = 4 } }), "initial charge no-op remains exact");
        project.Apply(11, project.CurrentEdit(11) with { Power = 6, Numbers = new Dictionary<int, int> { [-1] = 5, [200] = 5 } });
        Check(project.InitialCharges(11) is { Editable: true, Value: 5 } && project.Card(11).Power == 6
            && project.Numbers(11).Single(v => v.Id == 200).Value == 5, "initial charges combine with basic and graph edits");
        string output = Path.Combine(scratch, "initial-charge-export");
        project.Export(output);
        var reloaded = new EditorProject(output);
        Check(reloaded.InitialCharges(11) is { Editable: true, Value: 5 }, "initial charge template and presentation stay synchronized after export");
        project.Undo(); Check(!project.HasChanges && project.InitialCharges(11).Value == 4, "initial charge transaction undo exact");
        void BlockGraph(Action<XElement> mutate, string reason)
        {
            var doc = XDocument.Parse(originalGraph); mutate(doc.Root!.Elements().Single()); doc.Save(abilityPath);
            var blocked = new EditorProject(folder);
            Check(!blocked.InitialCharges(11).Editable, "initial charge contract blocks " + reason);
            Reject(() => blocked.Apply(11, blocked.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [-1] = 5 } }), blocked.InitialCharges(11).Reason);
            blocked.Apply(11, blocked.CurrentEdit(11) with { Numbers = new Dictionary<int, int> { [200] = 5 } });
            Check(blocked.Numbers(11).Single(v => v.Id == 200).Value == 5, "blocked charges leave unrelated parameters editable: " + reason);
        }
        BlockGraph(g => g.Element("TemporaryVariables")!.Elements().Single(e => (int?)e.Attribute("Id") == 900).SetAttributeValue("V", 3), "text mismatch");
        BlockGraph(g => g.SetAttributeValue("ProfitVarID", 900), "metadata reference");
        BlockGraph(g => g.Element("TemporaryVariables")!.Elements().Single(e => (int?)e.Attribute("Id") == 900).SetAttributeValue("ExternalOut", "True"), "external alias");
        BlockGraph(g => g.Element("TemporaryVariables")!.Add(new XElement("e10", new XAttribute("Type", "IntVar"), new XAttribute("Id", 901), new XAttribute("Name", "Charges2"), new XAttribute("V", 1))), "multiple charge meanings");
        BlockGraph(g => g.Element("Nodes")!.Elements().Last().SetAttributeValue("Type", "ChangeTapCardChargesNode"), "runtime charge changes");
        File.WriteAllText(abilityPath, originalGraph);
        foreach (var (field, value) in new[] { ("TapActive", "False"), ("VisibleTapCharges", "False"), ("InitialTimer", "0"), ("InitalTapCharges", "0"), ("Type", "2") })
        {
            var doc = XDocument.Parse(originalTemplate);
            doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 11).SetElementValue(field, value); doc.Save(templatePath);
            Check(!new EditorProject(folder).InitialCharges(11).Editable, "initial charges block incompatible " + field);
        }
        File.WriteAllText(templatePath, originalTemplate);
        string chinese = Path.Combine(folder, "zh-cn.csv");
        File.WriteAllText(chinese, File.ReadAllText(chinese).Replace("{Charges}", "{Charges-1}", StringComparison.Ordinal));
        Check(new EditorProject(folder).InitialCharges(11).Reason == "ChargeTextMismatch", "charge aliases verified across every loaded locale");
        return passed;
    }

    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var cards = project.Cards.Where(c => project.InitialCharges(c.Id).Editable)
            .Select(c => new { c.Id, Setting = project.InitialCharges(c.Id) }).ToArray();
        Check(cards.Any(c => c.Id == 122212), "Nenneke has verified initial charges");
        foreach (int card in new[] { 152311, 162306, 200041, 202150, 202155, 202256 })
            Check(project.InitialCharges(card) is { Editable: true, RefillValues.Count: 2 }, "independent starting charges verified " + card);
        passed.Add("verified initial charge cards: " + string.Join(", ", cards.Select(c => c.Id)));
        Check(!project.InitialCharges(152210).Editable && !project.InitialCharges(112113).Editable,
            "recharging and cooldown orders remain blocked for initial charges");
        foreach (var card in cards) project.Apply(card.Id, project.CurrentEdit(card.Id) with {
            Numbers = new Dictionary<int, int> { [-1] = card.Setting.Value < 9 ? card.Setting.Value + 1 : card.Setting.Value - 1 } });
        Check(project.Changes().Count == cards.Length * 2, "initial charge changes touch exactly template and presentation alias");
        string output = Path.Combine(scratch, "real-initial-charge-export"); project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-initial-charge-cache"));
        Check(cards.All(c => result.InitialCharges(c.Id).Editable && result.InitialCharges(c.Id).Value != c.Setting.Value), "all verified initial charge edits packed round-trip");
        var templates = XDocument.Load(Path.Combine(result.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(result.DirectoryPath, "Abilities.xml"));
        foreach (var card in cards)
        {
            templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card.Id).Element("InitalTapCharges")!.Value = card.Setting.Value.ToString();
            abilities.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card.Id && (string?)e.Attribute("Type") == "CardAbility")
                .Descendants().Single(e => (int?)e.Attribute("Id") == card.Setting.TextVariableId).SetAttributeValue("V", card.Setting.Value);
        }
        Check(XNode.DeepEquals(templates, XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml")))
            && XNode.DeepEquals(abilities, XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml"))), "initial charge export changes no other template or graph fields");
        foreach (string language in project.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, language + ".csv"))), "initial charge edits preserve locale bytes " + language);
        foreach (var card in cards) project.Undo();
        Check(!project.HasChanges, "all initial charge edits undo exactly");
        foreach (var card in cards.Where(c => c.Setting.RefillValues != null))
        {
            foreach (int refill in card.Setting.RefillValues!)
            {
                Check(!project.Numbers(card.Id).Single(v => v.Id == refill).Editable, "refill is not exposed as initial charges " + card.Id + ":" + refill);
                bool rejected = false;
                try { project.Apply(card.Id, project.CurrentEdit(card.Id) with { Numbers = new Dictionary<int, int> { [refill] = 2, [-1] = 2 } }); }
                catch (InvalidOperationException e) when (e.Message == "InitialValueLocked") { rejected = true; }
                Check(rejected && !project.HasChanges, "combined initial/refill edit rejects atomically " + card.Id + ":" + refill);
            }
        }
        string abilityPath = Path.Combine(result.DirectoryPath, "Abilities.xml");
        string original = File.ReadAllText(abilityPath);
        void Block(Action<XElement> mutate, string reason, int card = 152311)
        {
            var doc = XDocument.Parse(original);
            var graph = doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card && (string?)e.Attribute("Type") == "CardAbility");
            mutate(graph); doc.Save(abilityPath);
            var blocked = new EditorProject(result.DirectoryPath);
            var setting = blocked.InitialCharges(card);
            Check(!setting.Editable, "replenishing initial charges refuse " + reason);
            bool rejected = false;
            try { blocked.Apply(card, blocked.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [-1] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == setting.Reason) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "replenishing rejection atomic " + reason);
        }
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 454).SetAttributeValue("V", 2), "reset instead of additive refill");
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 465).SetAttributeValue("V", 2), "refill description mismatch");
        Block(g => g.SetAttributeValue("TestVarId", 464), "initial alias runtime reference");
        Block(g => g.SetAttributeValue("TestVarId", 465), "refill alias runtime reference");
        Block(g => g.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Id", 90000), new XAttribute("Type", "UseSubAbilityNode"))), "opaque subability");
        Block(g => g.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == 451).Remove(), "unknown refill target");
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 411).SetAttributeValue("Type", "PlayedTrigger"), "deployment can override starting state");
        Block(g => g.SetAttributeValue("TestVarId", 292), "linked refill reader reused", 202256);
        File.WriteAllText(abilityPath, original);
        return passed;
    }
}
