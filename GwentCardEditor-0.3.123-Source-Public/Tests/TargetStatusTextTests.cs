using System.Text.RegularExpressions;
using System.Xml.Linq;
using GwentCardEditor;

internal static class TargetStatusTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        foreach (int id in new[] { 142202, 202227, 202463, 202258 })
        {
            var groups = source.NumberGroups(id).Where(g => g.Display.Role == "targets" && g.Display.TextLinked
                && source.DescriptionBindings(id, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0)).ToArray();
            Check(groups.Length > 0, "status/armor target binding confirmed " + id + "/" + source.Name(id, "en-us"));
            foreach (var group in groups)
            {
                var baseline = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
                var others = source.Numbers(id).Where(n => !group.Members.Any(m => m.Id == n.Id)).ToDictionary(n => n.Id, n => n.Value);
                CardEdit Edit(int count) => source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => count) };
                Check(source.Preview(id, Edit(2)).Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == baseline.Count && !source.HasChanges, "localized status preview is non-mutating " + id + "/" + group.Display.Id);
                source.Apply(id, Edit(2));
                foreach (var (language, text) in baseline)
                {
                    string changed = source.Text(id, language, "tooltip");
                    string[] Tokens(string s) => Regex.Matches(s, @"<[^>]*>|\{[^{}]*\}").Select(m => m.Value).ToArray();
                    Check(changed != text && Tokens(text).SequenceEqual(Tokens(changed)), "status count changes without changing tags or duration aliases " + id + "/" + language);
                }
                string expected = id == 202227 ? "каждому из 2 вражеских отрядов" : "каждому из 2 дружественных отрядов";
                Check(source.Text(id, "ru-ru", "tooltip").Contains(expected), "Russian recipient case correct " + id);
                Check(source.Numbers(id).Where(n => others.ContainsKey(n.Id)).All(n => others[n.Id] == n.Value), "duration, armor, charges and other numbers unchanged " + id);
                string output = Path.Combine(scratch, $"status-target-{id}-{group.Display.Id}"); source.Export(output);
                var reopened = ClientSource.Open(output, Path.Combine(scratch, $"status-target-cache-{id}-{group.Display.Id}"));
                var xml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Abilities.xml"));
                var graph = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
                foreach (var member in group.Members) graph.Descendants().Single(e => (int?)e.Attribute("Id") == member.Id).SetAttributeValue("V", member.Value);
                Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"))), "only target bounds changed in status graph " + id);
                reopened.Apply(id, reopened.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 1) });
                Check(baseline.All(p => reopened.Text(id, p.Key, "tooltip") == p.Value), "status text reopens and restores exact singular " + id);
                foreach (int count in new[] { 3, 21, 1 }) source.Apply(id, Edit(count));
                Check(!source.HasChanges, "status target round trip exact " + id);
            }
        }
        string fixture = Path.Combine(scratch, "status-target-guards"); Directory.CreateDirectory(fixture);
        const int shieldId = 202258;
        var template = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == shieldId);
        var native = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == shieldId && (string?)e.Attribute("Type") == "CardAbility");
        new XDocument(new XElement("Templates", new XElement(template))).Save(Path.Combine(fixture, "Templates.xml"));
        void Save(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(fixture, "Abilities.xml"));
        foreach (string language in new[] { "en-us", "ru-ru", "zh-cn" }) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(fixture, language + ".csv"));
        Save(new XElement(native));
        var original = new EditorProject(fixture);
        var pair = original.NumberGroups(shieldId).Single(g => g.Display.Role == "targets");
        bool Linked() => new EditorProject(fixture).DescriptionBindings(shieldId, pair.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
        Check(Linked(), "isolated shield recognized");
        foreach (int token in new[] { 4, 18 })
        {
            var altered = new XElement(native);
            altered.Element("Nodes")!.Elements().Single(n => (string?)n.Attribute("Type") == "ChangeTokensNode").Element("Tokens")!.SetAttributeValue("V", token);
            Save(altered); Check(!Linked(), "lock or combined token mask is not treated as shield " + token);
        }
        var removal = new XElement(native);
        removal.Element("Nodes")!.Elements().Single(n => (string?)n.Attribute("Type") == "ChangeTokensNode").Element("Operation")!.SetAttributeValue("V", 1);
        Save(removal); Check(!Linked(), "shield removal is not treated as shield addition");
        Check(!source.NumberGroups(202502).Any(g => g.Display.Role == "targets" && g.Display.TextLinked
            && source.DescriptionBindings(202502, g.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked && b.Variables.Length == 0)), "combined boost and shield is not silently synchronized");
        Check(!source.HasChanges, "status tests leave original client clean");
        source.Client!.Verify();
        return passed;
    }
}
