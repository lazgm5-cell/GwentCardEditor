using System.Text.RegularExpressions;
using System.Xml.Linq;
using GwentCardEditor;

internal static class DrawCountTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        var cases = source.Cards.Where(c => c.Editable).SelectMany(c => source.NumberGroups(c.Id)
            .Where(g => g.Display.Role == "draw-count" && g.Members.Count == 1 && g.Display.TextLinked
                && source.DescriptionBindings(c.Id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0))
            .Select(g => (Card: c.Id, Group: g))).ToArray();
        Check(cases.Length >= 3, "generic literal draw coverage " + cases.Length);
        foreach (var (id, group) in cases)
        {
            var baseline = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
            var others = source.Numbers(id).Where(n => n.Id != group.Display.Id).ToDictionary(n => n.Id, n => n.Value);
            CardEdit Edit(int count) => source.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [group.Display.Id] = count } };
            Check(source.Preview(id, Edit(2)).Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == baseline.Count && !source.HasChanges,
                "draw preview updates every locale without mutation " + id);
            foreach (int count in new[] { 2, 3, 5, 11, 21, 22, 99, 1 })
            {
                source.Apply(id, Edit(count));
                foreach (var (language, text) in baseline)
                {
                    string changed = source.Text(id, language, "tooltip");
                    string[] Tokens(string s) => Regex.Matches(s, @"<[^>]*>|\{[^{}]*\}").Select(m => m.Value).ToArray();
                    Check(Tokens(text).SequenceEqual(Tokens(changed)) && (count == 1 ? changed == text : changed != text), "draw text round trip " + id + "/" + language + "/" + count);
                }
                Check(source.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => n.Value == others[n.Id]), "only draw amount changed " + id + "/" + count);
            }
            Check(!source.HasChanges, "draw exact round trip clean " + id);
        }
        var sample = cases.First(c => c.Card == 201776);
        source.Apply(sample.Card, source.CurrentEdit(sample.Card) with { Numbers = new Dictionary<int, int> { [sample.Group.Display.Id] = 2 } });
        string output = Path.Combine(scratch, "draw-text-export"); source.Export(output);
        var reopened = ClientSource.Open(output, Path.Combine(scratch, "draw-text-reopen"));
        var exportedXml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Abilities.xml"));
        exportedXml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample.Card && (string?)e.Attribute("Type") == "CardAbility")
            .Descendants().Single(e => (int?)e.Attribute("Id") == sample.Group.Display.Id).SetAttributeValue("V", sample.Group.Display.Value);
        Check(XNode.DeepEquals(exportedXml, XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"))), "only one draw count changes in exported graphs");
        Check(reopened.NumberGroups(sample.Card).Single(g => g.Display.Id == sample.Group.Display.Id).Display.TextLinked, "draw remains linked after packed export");
        Check(reopened.Text(sample.Card, "ru-ru", "tooltip").Contains("возьмите 2 карты, затем сыграйте карту"), "play clause not changed by draw count");
        source.Undo();
        foreach (int id in new[] { 122203, 202480, 162107 })
            Check(!source.NumberGroups(id).Any(g => g.Display.Role == "draw-count" && source.DescriptionBindings(id, g.Members.Select(n => n.Id))
                .Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0)), "multiple, filtered or opponent draw excluded " + id);
        string fixture = Path.Combine(scratch, "draw-text-guards"); Directory.CreateDirectory(fixture);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var original = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var native = original.Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample.Card && (string?)e.Attribute("Type") == "CardAbility");
        new XDocument(new XElement("Templates", new XElement(templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == sample.Card))))
            .Save(Path.Combine(fixture, "Templates.xml"));
        foreach (string language in source.AvailableLanguages) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(fixture, language + ".csv"));
        void Save(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(fixture, "Abilities.xml"));
        bool Linked() => new EditorProject(fixture).DescriptionBindings(sample.Card, [sample.Group.Display.Id]).Any(b => b.Language == "en-us" && b.Linked);
        Save(new XElement(native)); Check(Linked(), "isolated native draw recognized");
        foreach (string guard in new[] { "opponent", "filter", "random", "other-location", "unit-only", "multiple-draw", "mismatched-text" })
        {
            var altered = new XElement(native);
            var nodes = altered.Element("Nodes")!;
            var draw = nodes.Elements().Single(n => (string?)n.Attribute("Type") == "DrawCardsNode");
            switch (guard)
            {
                case "opponent":
                    foreach (var owner in nodes.Elements().Where(n => (string?)n.Attribute("Type") == "GetOwnerPlayerIdNode")) owner.SetAttributeValue("Type", "GetOpponentPlayerIdNode");
                    break;
                case "filter": draw.Element("Filters")!.Elements().First().SetAttributeValue("V", "True"); break;
                case "random": draw.Element("Random")!.SetAttributeValue("V", "True"); break;
                case "other-location": nodes.Elements().First(n => (string?)n.Attribute("Type") == "GetCardsNode").Element("LocationId")!.SetAttributeValue("V", 32); break;
                case "unit-only": nodes.Elements().First(n => (string?)n.Attribute("Type") == "GetCardsNode").Element("CardTypes")!.SetAttributeValue("V", 2); break;
                case "multiple-draw": var extra = new XElement(draw); extra.Name = "auditExtra"; extra.SetAttributeValue("Id", 900000); nodes.Add(extra); break;
                case "mismatched-text": draw.Element("Num")!.SetAttributeValue("V", 2); break;
            }
            Save(altered); Check(!Linked(), "unsafe draw text mapping rejected " + guard);
        }
        Check(!source.HasChanges, "draw tests leave original client clean");
        source.Client!.Verify();
        return passed;
    }
}
