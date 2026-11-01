using System.Text.RegularExpressions;
using System.Xml.Linq;
using GwentCardEditor;

internal static class MoveCountTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        var cases = source.Cards.Where(c => c.Editable).SelectMany(c => source.NumberGroups(c.Id)
            .Where(g => g.Display.Role == "choices" && g.Display.LabelRole != "discard-selected-count" && g.Members.Count == 2 && g.Display.TextLinked
                && source.DescriptionBindings(c.Id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0))
            .Select(g => (Card: c.Id, Group: g))).ToArray();
        Check(cases.Length >= 3, "generic hand to deck move coverage " + cases.Length);
        foreach (var (id, group) in cases)
        {
            var baseline = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
            var others = source.Numbers(id).Where(n => group.Members.All(m => m.Id != n.Id)).ToDictionary(n => n.Id, n => n.Value);
            CardEdit Edit(int count) => source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => count) };
            Check(source.Preview(id, Edit(2)).Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == baseline.Count && !source.HasChanges, "move preview updates all locales without mutation " + id);
            foreach (int count in new[] { 2, 3, 5, 11, 21, 22, 99, 1 })
            {
                source.Apply(id, Edit(count));
                foreach (var (language, text) in baseline)
                {
                    string changed = source.Text(id, language, "tooltip");
                    string[] Tokens(string s) => Regex.Matches(s, @"<[^>]*>|\{[^{}]*\}").Select(m => m.Value).ToArray();
                    Check(Tokens(text).SequenceEqual(Tokens(changed)) && (count == 1 ? changed == text : changed != text), "move text round trip " + id + "/" + language + "/" + count);
                }
                Check(source.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => n.Value == others[n.Id]), "other choices and draw unchanged " + id + "/" + count);
            }
            Check(!source.HasChanges, "move exact round trip clean " + id);
        }
        foreach (int id in cases.Select(c => c.Card))
        {
            var group = cases.Single(c => c.Card == id).Group;
            string drawFragment = Regex.Match(source.Text(id, "en-us", "tooltip"), @"\bdraw\b[^,.]*", RegexOptions.IgnoreCase).Value;
            source.Apply(id, source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 2) });
            string output = Path.Combine(scratch, "move-text-export-" + id); source.Export(output);
            var reopened = ClientSource.Open(output, Path.Combine(scratch, "move-text-reopen-" + id));
            Check(reopened.NumberGroups(id).Single(g => g.Display.Id == group.Display.Id).Display.TextLinked, "move remains linked after packed export " + id);
            var xml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Abilities.xml"));
            var graph = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
            foreach (var member in group.Members) graph.Descendants().Single(e => (int?)e.Attribute("Id") == member.Id).SetAttributeValue("V", member.Value);
            Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"))), "only two choice bounds change in graph " + id);
            Check(drawFragment.Length > 0 && source.Text(id, "en-us", "tooltip").Contains(drawFragment), "neighbor draw text unchanged " + id);
            reopened.Apply(id, reopened.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 1) });
            Check(!reopened.Text(id, "ru-ru", "tooltip").Contains("2 карты"), "reopened move returns to singular " + id);
            source.Undo();
        }
        const int sample = 122403;
        var bothMove = cases.Single(c => c.Card == sample).Group;
        var bothDraw = source.NumberGroups(sample).Single(g => g.Display.Role == "draw-count");
        var simultaneous = bothMove.Members.ToDictionary(n => n.Id, _ => 2);
        simultaneous[bothDraw.Display.Id] = 3;
        source.Apply(sample, source.CurrentEdit(sample) with { Numbers = simultaneous });
        Check(source.Text(sample, "ru-ru", "tooltip").Contains("возьмите 3 карты, затем переместите 2 карты"), "draw and move counts synchronize independently in one edit");
        source.Undo();
        foreach (int id in new[] { 112205, 202187, 202498, 202782 })
        {
            var move = cases.Single(c => c.Card == id).Group;
            var selectedDraw = source.NumberGroups(id).Single(g => g.Display.LabelRole == "draw-selected-count");
            var numbers = move.Members.ToDictionary(n => n.Id, _ => 2);
            foreach (var member in selectedDraw.Members) numbers[member.Id] = 3;
            source.Apply(id, source.CurrentEdit(id) with { Numbers = numbers });
            string english = source.Text(id, "en-us", "tooltip");
            Check(english.Contains("draw 3 ", StringComparison.OrdinalIgnoreCase) && english.Contains("2 cards from your hand"), "selected draw and hand return update independently together " + id);
            source.Undo(); Check(!source.HasChanges, "combined selected draw and return undo clean " + id);
        }
        try
        {
            source.Preview(sample, source.CurrentEdit(sample) with { Numbers = new Dictionary<int, int> { [bothMove.Members[0].Id] = 2 } });
            throw new Exception("Expected unequal move bounds rejection");
        }
        catch (InvalidOperationException e) when (e.Message == "TargetCountTextConflict") { passed.Add("unequal move draft rejected atomically"); }
        var duplicate = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["en-us"] = new Dictionary<string, string> { ["tooltip"] = source.Text(sample, "en-us", "tooltip") + "\\n" + source.Text(sample, "en-us", "tooltip") }
        };
        try
        {
            source.Preview(sample, source.CurrentEdit(sample) with { Numbers = bothMove.Members.ToDictionary(n => n.Id, _ => 2), Texts = duplicate });
            throw new Exception("Expected ambiguous move text rejection");
        }
        catch (InvalidOperationException e) when (e.Message == "TargetCountTextConflict") { passed.Add("ambiguous custom move text rejected atomically"); }
        var fixture = Path.Combine(scratch, "move-text-guards"); Directory.CreateDirectory(fixture);
        var original = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var native = original.Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample && (string?)e.Attribute("Type") == "CardAbility");
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample))))
            .Save(Path.Combine(fixture, "Templates.xml"));
        foreach (string language in source.AvailableLanguages) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(fixture, language + ".csv"));
        var pair = cases.Single(c => c.Card == sample).Group;
        void Save(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(fixture, "Abilities.xml"));
        bool Linked() => new EditorProject(fixture).DescriptionBindings(sample, pair.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
        Save(new XElement(native)); Check(Linked(), "isolated native hand move recognized");
        foreach (string guard in new[] { "opponent", "other-source", "unit-only", "tier-filter", "unequal-bounds", "other-destination", "mismatched-text", "shared-selection" })
        {
            var altered = new XElement(native); var nodes = altered.Element("Nodes")!;
            var choice = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "RequestCardChoiceNode");
            var hand = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "GetCardsNode" && (string?)n.Element("LocationId")?.Attribute("V") == "8");
            var maker = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "MakeCardPositionNode");
            switch (guard)
            {
                case "opponent": foreach (var owner in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "GetOwnerPlayerIdNode")) owner.SetAttributeValue("Type", "GetOwnerOpponentPlayerIdNode"); break;
                case "other-source": hand.Element("LocationId")!.SetAttributeValue("V", 16); break;
                case "unit-only": hand.Element("CardTypes")!.SetAttributeValue("V", 2); break;
                case "tier-filter": hand.Element("CardTiers")!.SetAttributeValue("V", 2); break;
                case "unequal-bounds": choice.Element("MaxChoices")!.SetAttributeValue("V", 2); break;
                case "other-destination": maker.Element("Location")!.SetAttributeValue("V", 8); break;
                case "mismatched-text": choice.Element("MinChoices")!.SetAttributeValue("V", 2); choice.Element("MaxChoices")!.SetAttributeValue("V", 2); break;
                case "shared-selection": altered.Element("Connections")!.Add(new XElement("auditWire", new XAttribute("Type", "Connection"), new XAttribute("Id", 900000), new XAttribute("SrcId", (int)choice.Element("SelectedChoices")!.Attribute("Id")!), new XAttribute("DstId", 900001))); break;
            }
            Save(altered); Check(!Linked(), "unsafe move mapping excluded " + guard);
        }
        foreach (int id in new[] { 202197, 203211, 202121 }) Check(!source.NumberGroups(id).Any(g => g.Display.Role == "choices"
            && source.DescriptionBindings(id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0)), "deck source or filtered hand excluded " + id);
        Check(!source.HasChanges, "move tests leave client unchanged"); source.Client!.Verify();
        return passed;
    }
}
