using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
using GwentCardEditor;

internal static class StatusBatchTests
{
    public static IReadOnlyList<string> Run(string fixture, EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add("status batch: " + name); }
        var native = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        string folder = Path.Combine(scratch, "status-batch-fixture"); Directory.CreateDirectory(folder);
        var template = new XElement(XDocument.Load(Path.Combine(fixture, "Templates.xml")).Root!.Elements().First());
        template.SetElementValue("Tokens", 0);
        new XDocument(new XElement("Templates", template)).Save(Path.Combine(folder, "Templates.xml"));
        string path = Path.Combine(folder, "Abilities.xml");
        foreach (var (type, effect) in new[] { ("DestroyCardsNode", false), ("DestroyCardsNode", true), ("BanishCardsNode", true),
            ("BalanceCardStatusTimerNode", false), ("BalanceCardStatusTimerNode", true) })
        {
            string label = type + "/" + effect;
            var node = new XElement(native.Descendants("Nodes").Elements().First(n => (string?)n.Attribute("Type") == type && n.Elements("EffectType").Any() == effect));
            node.SetAttributeValue("Id", 9000);
            int next = 9001; foreach (var field in node.Elements()) field.SetAttributeValue("Id", next++);
            var graph = new XElement("Ability", new XAttribute("Type", "CardAbility"), new XAttribute("Id", 11), new XAttribute("NId", 10000),
                new XElement("TemporaryVariables"), new XElement("PersistentVariables"),
                new XElement("Nodes", node), new XElement("Connections"));
            var baseline = new XDocument(new XElement("Abilities", graph)); baseline.Save(path);
            var project = new EditorProject(folder);
            Check(project.InitialStatuses(11).All(s => s.Editable), "native status-node layout " + label);
            if (type == "BalanceCardStatusTimerNode")
            {
                foreach (var (acceptor, donor) in new[] { (16,32), (32,16), (0,16), (0,32) })
                {
                    var variant = new XDocument(baseline);
                    var timer = variant.Root!.Element("Ability")!.Element("Nodes")!.Elements().Single();
                    timer.Element("Acceptor")!.SetAttributeValue("V", acceptor);
                    timer.Element("Donor")!.SetAttributeValue("V", donor);
                    variant.Save(path);
                    Check(new EditorProject(folder).InitialStatuses(11).All(s => s.Editable), "known status timer pair " + label + ":" + acceptor + "/" + donor);
                }
                var dynamicDuration = new XDocument(baseline);
                var g = dynamicDuration.Root!.Element("Ability")!;
                g.Element("TemporaryVariables")!.Add(new XElement("extra", new XAttribute("Id", 9903), new XAttribute("Type", "IntVar"), new XAttribute("V", 3)));
                g.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Id", 9900), new XAttribute("Type", "GetVarNode"), new XAttribute("VarId", 9903),
                    new XElement("Out", new XAttribute("Id", 9901), new XAttribute("Type", "IntVar"), new XAttribute("V", 0))));
                g.Element("Connections")!.Add(new XElement("extra", new XAttribute("Id", 9902), new XAttribute("Type", "Connection"),
                    new XAttribute("SrcId", 9901), new XAttribute("DstId", (int)node.Element("DeltaTime")!.Attribute("Id")!)));
                dynamicDuration.Save(path);
                Check(new EditorProject(folder).InitialStatuses(11).All(s => s.Editable), "typed runtime duration independent of initial flags " + label);
            }
            var mutations = new List<(string Name, Action<XElement> Mutate)>
            {
                ("unknown type", n => n.SetAttributeValue("Type", "UnknownStatusNode")),
                ("extra port", n => n.Add(new XElement("Extra", new XAttribute("Id", 9900), new XAttribute("Type", "IntVar"), new XAttribute("V", 0)))),
                ("duplicate port", n => { var f = new XElement(n.Elements().First()); f.SetAttributeValue("Id", 9900); n.Add(f); }),
                ("missing port", n => n.Elements().First().Remove()),
                ("unknown enum", n => n.Element("AttackMethod")!.SetAttributeValue("V", 9999))
            };
            if (type == "BalanceCardStatusTimerNode")
            {
                mutations.Add(("unknown acceptor", n => n.Element("Acceptor")!.SetAttributeValue("V", 256)));
                mutations.Add(("unknown donor", n => n.Element("Donor")!.SetAttributeValue("V", 512)));
                mutations.Add(("empty donor", n => n.Element("Donor")!.SetAttributeValue("V", 0)));
                mutations.Add(("same timer masks", n => n.Element("Acceptor")!.SetAttributeValue("V", (string)n.Element("Donor")!.Attribute("V")!)));
                mutations.Add(("invalid duration", n => n.Element("DeltaTime")!.SetAttributeValue("V", "invalid")));
            }
            foreach (string name in node.Elements().Select(e => e.Name.LocalName))
            {
                mutations.Add((name + " type", n => n.Element(name)!.SetAttributeValue("Type",
                    (string?)n.Element(name)!.Attribute("Type") == "CardTokensVar" ? "IntVar" : "CardTokensVar")));
                mutations.Add((name + " generic", n => n.Element(name)!.SetAttributeValue("GArg", "ECardToken")));
                mutations.Add((name + " nested", n => n.Element(name)!.Add(new XElement("Hidden", 1))));
                mutations.Add((name + " text", n => n.Element(name)!.Value = "unknown"));
                mutations.Add((name + " id", n => n.Element(name)!.Attribute("Id")!.Remove()));
                mutations.Add((name + " external", n => n.Element(name)!.SetAttributeValue("ExternalIn", "True")));
            }
            foreach (var (name, mutate) in mutations)
            {
                var changed = new XDocument(baseline); mutate(changed.Root!.Element("Ability")!.Element("Nodes")!.Elements().Single()); changed.Save(path);
                var blocked = new EditorProject(folder);
                Check(blocked.InitialStatuses(11).All(s => !s.Editable), "malformed status-node rejected " + label + ":" + name);
                bool rejected = false;
                try { blocked.Apply(11, blocked.CurrentEdit(11) with { Power = 5, Statuses = new Dictionary<string, bool> { ["shield"] = true } }); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !blocked.HasChanges, "malformed status-node atomic " + label + ":" + name);
            }
            foreach (string name in node.Elements().Select(e => e.Name.LocalName))
            {
                var changed = new XDocument(baseline);
                var g = changed.Root!.Element("Ability")!;
                int port = (int)node.Element(name)!.Attribute("Id")!;
                bool dynamicEnum = node.Element(name)!.Attribute("GArg") != null || name is "Acceptor" or "Donor";
                g.Element("Nodes")!.Add(new XElement("extra", new XAttribute("Type", "PlayedTrigger"), new XAttribute("Id", 9900),
                    new XElement("FlowOut", new XAttribute("Id", 9901), new XAttribute("Type", "FlowConnector"))));
                g.Element("Connections")!.Add(new XElement("extra", new XAttribute("Type", "Connection"), new XAttribute("Id", 9902),
                    new XAttribute("SrcId", name == "FlowOut" || dynamicEnum ? 9901 : port),
                    new XAttribute("DstId", name == "FlowOut" || dynamicEnum ? port : 9901)));
                changed.Save(path);
                Check(new EditorProject(folder).InitialStatuses(11).All(s => !s.Editable), "dynamic or reversed status-node port blocked " + label + ":" + name);
            }
        }
        int[] ids = [112103,112214,112215,132202,132215,132216,132307,142108,162207,200037,200539,201701,201774,202098,202105,202784,202815,203108,
            152210,152302,200139,202222,202230,202233,202245,202272,202307,202309,202316,202318,202344,202429,202547,202557,202625];
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        var localeHashes = source.AvailableLanguages.ToDictionary(language => language,
            language => Hash(Path.Combine(source.DirectoryPath, language + ".csv")));
        var expected = new Dictionary<int, Dictionary<string, bool>>();
        foreach (int id in ids)
        {
            var settings = source.InitialStatuses(id);
            Check(settings.Any(s => s.Editable), "new native family supported " + id);
            expected[id] = settings.Where(s => s.Editable).ToDictionary(s => s.Key, s => !s.Value);
            source.Apply(id, source.CurrentEdit(id) with { Statuses = expected[id] });
        }
        string output = Path.Combine(scratch, "status-batch-export"); source.Export(output);
        var reopened = ClientSource.Open(output, Path.Combine(scratch, "status-batch-cache"));
        foreach (var (id, statuses) in expected)
            foreach (var (key, value) in statuses)
                Check(reopened.InitialStatuses(id).Single(s => s.Key == key) is { Editable: true } actual && actual.Value == value,
                    "native packed flag " + id + "/" + key);
        foreach (string language in source.AvailableLanguages)
        {
            Check(Hash(Path.Combine(source.DirectoryPath, language + ".csv")) == localeHashes[language], "batch leaves source locale untouched " + language);
            foreach (int id in ids.Where(id => source.HasText(id, language, "tooltip")))
                Check(reopened.Text(id, language, "tooltip") == source.Text(id, language, "tooltip"), "batch localized description round-trip " + id + "/" + language);
            int sample = ids.FirstOrDefault(id => source.HasText(id, language, "tooltip") && source.InitialStatuses(id).Any(s => s.Value));
            if (sample != 0)
            {
                var status = source.InitialStatuses(sample).First(s => s.Value);
                string term = source.GameTerm(status.Key, language) ?? source.GameTerm(status.Key, "en-us") ?? status.Key;
                Check(reopened.Text(sample, language, "tooltip").Contains($"<keyword={status.Key}>{term}</keyword>", StringComparison.Ordinal),
                    "batch writes localized status name " + language);
            }
        }
        Check(File.ReadAllBytes(Path.Combine(source.DirectoryPath, "Abilities.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(reopened.DirectoryPath, "Abilities.xml"))),
            "batch preserves ability graph");
        foreach (int _ in ids) source.Undo();
        Check(!source.HasChanges, "batch undo exact");
        Check(!source.InitialStatuses(132215).Single(s => s.Key == "doomed").Editable, "existing doomed dependency remains blocked");
        Check(!source.InitialStatuses(112103).Single(s => s.Key == "immune").Editable, "existing immunity dependency remains blocked");
        if (source.AdditionalResupply(201630) is { Value: true, Editable: true, Added: true })
        {
            var currentSettings = source.InitialStatuses(201630).ToDictionary(s => s.Key);
            Check(new[] { "resilient", "defender", "doomed" }.All(key => currentSettings[key].Editable)
                && new[] { "shield", "veil", "immune" }.All(key => !currentSettings[key].Editable),
                "verified Resupply permits only status flags independent of the ability");
            var oldStatuses = source.InitialStatuses(201630).ToDictionary(s => s.Key, s => s.Value);
            var edit = source.CurrentEdit(201630) with { Statuses = new Dictionary<string, bool> { ["resilient"] = !oldStatuses["resilient"] } };
            source.Apply(201630, edit);
            Check(source.InitialStatuses(201630).Single(s => s.Key == "resilient").Value != oldStatuses["resilient"], "status edit beside Resupply applies");
            Check(source.AdditionalResupply(201630) is { Value: true, Editable: true, Added: true }, "status edit preserves verified Resupply");
            source.Apply(201630, source.CurrentEdit(201630) with { Statuses = oldStatuses, Resupply = false });
            Check(source.AdditionalResupply(201630) is { Value: false, Editable: true, Added: false }, "Resupply can be removed after adding a status description");
            source.Undo();
            Check(source.AdditionalResupply(201630) is { Value: true, Editable: true, Added: true }, "Resupply removal undo preserves status description");
            source.Undo();
            Check(source.InitialStatuses(201630).All(s => s.Value == oldStatuses[s.Key]), "status edit beside Resupply undo restores flags");
        }
        foreach (var card in source.Cards)
        {
            var inspection = source.InspectInitialStatuses(card.Id);
            Check(JsonSerializer.Serialize(inspection.Settings) == JsonSerializer.Serialize(source.InitialStatuses(card.Id)), "inspection matches enforcement " + card.Id);
            Check(inspection.Settings.All(s => s.Editable) || inspection.Blockers.Count > 0, "blocked flags explained " + card.Id);
        }
        Check(!source.HasChanges, "diagnostics read only");
        return passed;
    }
}
