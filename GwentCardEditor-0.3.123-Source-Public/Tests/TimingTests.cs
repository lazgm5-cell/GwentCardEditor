using System.Xml.Linq;
using GwentCardEditor;

internal static class TimingTests
{
    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var settings = project.Cards.SelectMany(c => project.Timing(c.Id).Select(s => (Card: c.Id, Setting: s))).ToArray();
        Check(settings.Any(c => c.Card == 112110 && c.Setting.Id == -2), "Ciri Dash initial timer verified");
        Check(settings.Any(c => c.Card == 112113 && c.Setting.Id == -3), "Yennefer Conjurer cooldown verified");
        Check(settings.Any(c => c.Card == 142210 && c.Setting is { Id: -3, GraphValues.Count: 0 }), "Malena linked cooldown verified without rewriting fallback");
        Check(settings.Any(c => c.Card == 112107 && c.Setting.Id == -2), "Villentretenmerth timer independent of verified Lineage");
        Check(settings.Any(c => c.Card == 201659 && c.Setting is { Id: -4, GraphValues.Count: 0 }), "Winch reduction controls both branches through one source");
        passed.Add("timing coverage: " + string.Join(", ", settings.Select(c => c.Card + ":" + c.Setting.Role)));
        Check(project.Timing(203000).Count == 0 && project.Timing(202504).Count == 0
            && project.Timing(122302).Count == 0, "event counters scenarios and resupply cooldowns not guessed");
        foreach (var (card, setting) in settings)
        {
            Check(!project.Apply(card, project.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [setting.Id] = setting.Value } })
                && !project.HasChanges, "timing no-op exact " + card);
            Check(project.Numbers(card).Count(n => n.Editable && n.Role == setting.Role) == 1, "one timing input " + card);
            Check(!project.Numbers(card).Any(n => n.Editable && n.Role is "timer-change" or "row-timer-change" or "timer-limit"), "service timer controls hidden " + card);
            if (setting.Id == -3) Check(!project.Numbers(card).Any(n => n.Editable && n.Role == "charge-change"), "cooldown refill not exposed separately " + card);
            foreach (int invalid in new[] { 0, 100 })
            {
                bool rejected = false;
                try { project.Apply(card, project.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [setting.Id] = invalid } }); }
                catch (InvalidOperationException e) when (e.Message == "AbilityRange") { rejected = true; }
                Check(rejected && !project.HasChanges, "timing bounds atomic " + card + ":" + invalid);
            }
        }
        foreach (var (card, setting) in settings)
            project.Apply(card, project.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [setting.Id] = setting.Value + 1 } });
        Check(project.Changes().Count == settings.Sum(c => c.Setting.Id == -2 ? 2 : c.Setting.GraphValues.Count + 1), "timing updates only effective duration and required presentation values");
        string output = Path.Combine(scratch, "real-timing-export"); project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-timing-cache"));
        Check(settings.All(c => result.Timing(c.Card).Single().Value == c.Setting.Value + 1), "all timing settings packed round-trip");
        var templates = XDocument.Load(Path.Combine(result.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(result.DirectoryPath, "Abilities.xml"));
        foreach (var (card, setting) in settings)
        {
            if (setting.Id == -2) templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card).Element("InitialTimer")!.Value = setting.Value.ToString();
            var graph = abilities.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card && (string?)e.Attribute("Type") == "CardAbility");
            foreach (int variable in setting.GraphValues.Append(setting.TextVariableId))
                graph.Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V", setting.Value);
        }
        Check(XNode.DeepEquals(templates, XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml")))
            && XNode.DeepEquals(abilities, XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml"))), "timing changes no tick speed charge refill or unrelated data");
        foreach (string language in project.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, language + ".csv"))), "timing preserves localization " + language);
        foreach (var _ in settings) project.Undo();
        Check(!project.HasChanges, "all timing changes undo exactly");

        string abilityPath = Path.Combine(result.DirectoryPath, "Abilities.xml");
        string original = File.ReadAllText(abilityPath);
        void Block(Action<XElement> mutate, string reason, int card = 112113, int editId = -3)
        {
            var doc = XDocument.Parse(original);
            var graph = doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == card && (string?)e.Attribute("Type") == "CardAbility");
            mutate(graph); doc.Save(abilityPath);
            var blocked = new EditorProject(result.DirectoryPath);
            Check(blocked.Timing(card).Count == 0, "cooldown refuses " + reason);
            bool rejected = false;
            try { blocked.Apply(card, blocked.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [editId] = 4 } }); }
            catch (InvalidOperationException e) when (e.Message == "InitialValueLocked") { rejected = true; }
            Check(rejected && !blocked.HasChanges, "invalid cooldown edit atomic " + reason);
        }
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 205).SetAttributeValue("V", 2), "nonstandard tick");
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 217).SetAttributeValue("V", 9), "mismatched alias");
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 180).SetAttributeValue("V", 2), "nonstandard refill");
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 195).SetAttributeValue("ExternalIn", "True"), "external data");
        Block(g => g.SetAttributeValue("TestVarId", 217), "runtime alias reference");
        Block(g => g.SetAttributeValue("TestVarId", 195), "shared reset constant");
        Block(g => g.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == 167).Remove(), "unknown turn owner");
        Block(g => g.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Id", 90000), new XAttribute("Type", "GetCardTimerNode"))), "timer readers");
        Block(g => g.SetAttributeValue("TestVarId", 217), "linked duration runtime writer", 142210);
        Block(g => g.SetAttributeValue("TestVarId", 267), "linked reader output reused", 142210);
        Block(g => g.SetAttributeValue("TestVarId", 195), "linked reset input reused", 142210);
        Block(g => g.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 90001),
            new XAttribute("SrcId", 267), new XAttribute("DstId", 205))), "linked duration feeds tick speed", 142210);
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 208).SetAttributeValue("V", 1), "reduction changed to addition", 201659, -4);
        Block(g => g.SetAttributeValue("TestVarId", 150), "reduction runtime reference", 201659, -4);
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 193).SetAttributeValue("VarId", 149), "reduction source replaced with boost", 201659, -4);
        Block(g => g.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == 243).Remove(), "missing Fee branch filter", 201659, -4);
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 229).SetAttributeValue("V", "True"), "negated timer filter", 201659, -4);
        Block(g => g.Descendants().Single(e => (int?)e.Attribute("Id") == 154).SetAttributeValue("V", 2), "unverified multi-target reduction", 201659, -4);
        Block(g => g.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 90001),
            new XAttribute("SrcId", 194), new XAttribute("DstId", 172))), "reduction also controls boost", 201659, -4);
        void BlockLineage(Action<XElement, XElement> mutate, string reason)
        {
            var doc = XDocument.Parse(original);
            var card = doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 112107 && (string?)e.Attribute("Type") == "CardAbility");
            var lineage = doc.Root!.Elements().Single(e => (int?)e.Attribute("AbilityID") == 53 && (string?)e.Attribute("Type") == "SubAbility");
            mutate(card, lineage); doc.Save(abilityPath);
            var blocked = new EditorProject(result.DirectoryPath);
            Check(blocked.Timing(112107).Count == 0, "Lineage guard rejects " + reason);
            bool rejected = false;
            try { blocked.Apply(112107, blocked.CurrentEdit(112107) with { Numbers = new Dictionary<int, int> { [-2] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == "InitialValueLocked") { rejected = true; }
            Check(rejected && !blocked.HasChanges, "Lineage rejection atomic " + reason);
        }
        BlockLineage((_, sub) => sub.Descendants().Single(e => (int?)e.Attribute("Id") == 251).SetAttributeValue("Type", "ChangeCardTimerNode"), "modified implementation");
        BlockLineage((card, _) => card.Descendants().Single(e => (int?)e.Attribute("Id") == 373).Element("SubAbilityID")!.Value = "54", "unknown subability");
        BlockLineage((card, _) => card.Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == 374).SetAttributeValue("SrcId", 359), "Lineage called from timer tick");
        BlockLineage((card, _) => card.Descendants("ConnectionMappings").Single().Elements().Single().Element("IsInput")!.Value = "False", "outgoing mapping");
        File.WriteAllText(abilityPath, original);
        return passed;
    }
}
