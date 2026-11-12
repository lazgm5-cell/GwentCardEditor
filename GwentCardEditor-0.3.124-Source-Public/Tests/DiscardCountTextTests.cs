using System.Text.RegularExpressions;
using System.Xml.Linq;
using GwentCardEditor;

internal static class DiscardCountTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var cases = source.Cards.Where(c => c.Editable).SelectMany(c => source.NumberGroups(c.Id)
            .Where(g => g.Display.LabelRole == "discard-selected-count" && g.Display.TextLinked)
            .Select(g => (Card: c.Id, Group: g))).ToArray();
        Check(cases.Length >= 3, "generic selected discard coverage " + cases.Length);
        foreach (var (id, group) in cases)
        {
            var baseline = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
            var others = source.Numbers(id).Where(n => group.Members.All(m => n.Id != m.Id)).ToDictionary(n => n.Id, n => n.Value);
            CardEdit Edit(int count) => source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => count) };
            Check(group.Members.Count == 2 && group.Display.HelpKey == "DiscardChosenCards", "discard is a clearly labeled paired choice " + id);
            Check(source.Preview(id, Edit(2)).Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == baseline.Count && !source.HasChanges, "discard preview updates every locale without mutation " + id);
            foreach (int count in new[] { 2, 3, 5, 11, 21, 22, 99, 1 })
            {
                source.Apply(id, Edit(count));
                foreach (var (language, text) in baseline)
                {
                    string changed = source.Text(id, language, "tooltip");
                    string[] Tags(string s) => Regex.Matches(s, @"<keyword=[^>]+>[^<]*</keyword>|\{[^{}]*\}").Select(m => m.Value).ToArray();
                    Check(Tags(text).SequenceEqual(Tags(changed)) && (count == 1 ? changed == text : changed != text), "discard changes count but preserves exact keyword blocks and aliases " + id + "/" + language + "/" + count);
                    int suffix = text.LastIndexOf("\\n", StringComparison.Ordinal);
                    if (suffix >= 0 && id == 152107)
                        Check(changed[changed.LastIndexOf("\\n", StringComparison.Ordinal)..] == text[suffix..], "Coral reaction description remains unchanged " + language + "/" + count);
                }
                Check(source.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => n.Value == others[n.Id]), "draw, damage and spawn numbers preserved " + id + "/" + count);
            }
            Check(!source.HasChanges, "discard full round trip clean " + id);
            source.Apply(id, Edit(2));
            string output = Path.Combine(scratch, "discard-text-export-" + id); source.Export(output);
            var reopened = ClientSource.Open(output, Path.Combine(scratch, "discard-text-reopen-" + id));
            Check(reopened.NumberGroups(id).Single(g => g.Display.Id == group.Display.Id).Display.TextLinked, "discard remains linked after packed export " + id);
            var xml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Abilities.xml"));
            var graph = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
            foreach (var n in group.Members) graph.Descendants().Single(e => (int?)e.Attribute("Id") == n.Id).SetAttributeValue("V", n.Value);
            Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"))), "only discard choice bounds changed in exported graph " + id);
            reopened.Apply(id, reopened.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 1) });
            Check(baseline.All(p => reopened.Text(id, p.Key, "tooltip") == p.Value), "reopened discard restores every original locale " + id);
            source.Undo();
        }
        const int sample = 152308;
        var pair = cases.Single(c => c.Card == sample).Group;
        var draw = source.NumberGroups(sample).Single(g => g.Display.Role == "draw-count");
        var both = pair.Members.ToDictionary(n => n.Id, _ => 2); both[draw.Display.Id] = 3;
        source.Apply(sample, source.CurrentEdit(sample) with { Numbers = both });
        Check(source.Text(sample, "ru-ru", "tooltip").Contains("возьмите 3 карты, затем <keyword=discard>сбросьте</keyword> 2 карты"), "draw and discard counts change independently together");
        source.Undo();
        foreach (string guard in new[] { "quoted", "ambiguous-header" })
        {
            string text = source.Text(sample, "en-us", "tooltip");
            text = guard == "quoted" ? "\"" + text + "\"" : text + "\\n<keyword=deploy>Deploy</keyword>: Other effect.";
            var custom = source.CurrentEdit(sample) with
            {
                Numbers = pair.Members.ToDictionary(n => n.Id, _ => 2),
                Texts = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["en-us"] = new Dictionary<string, string> { ["tooltip"] = text } }
            };
            try { source.Preview(sample, custom); throw new Exception("Expected discard text conflict: " + guard); }
            catch (InvalidOperationException e) when (e.Message == "TargetCountTextConflict") { passed.Add("custom discard text rejected atomically " + guard); }
            Check(!source.HasChanges, "rejected custom discard edit preserves project " + guard);
        }
        var fixture = Path.Combine(scratch, "discard-text-guards"); Directory.CreateDirectory(fixture);
        var native = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample && (string?)e.Attribute("Type") == "CardAbility");
        var template = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample);
        new XDocument(new XElement("Templates", new XElement(template))).Save(Path.Combine(fixture, "Templates.xml"));
        foreach (string language in source.AvailableLanguages) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(fixture, language + ".csv"));
        void Save(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(fixture, "Abilities.xml"));
        bool Linked() => new EditorProject(fixture).DescriptionBindings(sample, pair.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
        Save(new XElement(native)); Check(Linked(), "isolated native discard recognized");
        foreach (string guard in new[] { "wrong-choice-type", "other-source", "opponent", "other-destination", "unequal-bounds", "shared-selection", "reaction" })
        {
            var altered = new XElement(native); var nodes = altered.Element("Nodes")!;
            var choice = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "RequestCardChoiceNode");
            var maker = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "MakeCardPositionNode");
            switch (guard)
            {
                case "wrong-choice-type": choice.Element("ChoiceType")!.SetAttributeValue("V", 4); break;
                case "other-source": foreach (var hand in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "GetCardsNode" && (int?)n.Element("LocationId")?.Attribute("V") == 8)) hand.Element("LocationId")!.SetAttributeValue("V", 16); break;
                case "opponent": foreach (var owner in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "GetOwnerPlayerIdNode")) owner.SetAttributeValue("Type", "GetOwnerOpponentPlayerIdNode"); break;
                case "other-destination": maker.Element("Location")!.SetAttributeValue("V", 16); break;
                case "unequal-bounds": choice.Element("MaxChoices")!.SetAttributeValue("V", 2); break;
                case "shared-selection": altered.Element("Connections")!.Add(new XElement("auditWire", new XAttribute("Type", "Connection"), new XAttribute("Id", 900000), new XAttribute("SrcId", (int)choice.Element("SelectedChoices")!.Attribute("Id")!), new XAttribute("DstId", 900001))); break;
                case "reaction": foreach (var played in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "PlayedTrigger")) played.SetAttributeValue("Type", "AfterCardMovedTrigger"); break;
            }
            Save(altered); Check(!Linked(), "unsafe discard text mapping excluded " + guard);
        }
        foreach (int id in new[] { 201781, 200083 }) Check(!source.NumberGroups(id).Any(g => g.Display.LabelRole == "discard-selected-count"), "dependent or random discard not enabled " + id);
        Check(!source.HasChanges, "discard tests leave original client clean"); source.Client!.Verify();
        return passed;
    }
}
