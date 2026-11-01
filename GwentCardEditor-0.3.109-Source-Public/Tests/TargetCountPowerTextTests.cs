using System.Text.RegularExpressions;
using System.Xml.Linq;
using GwentCardEditor;

internal static class TargetCountPowerTextTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        foreach (int id in new[] { 122302, 122214, 113310 })
        {
            var groups = source.NumberGroups(id).Where(g => g.Display.Role == "targets" && g.Members.Count == 2).ToArray();
            Check(groups.Length == 1 && groups[0].Display.TextLinked, "verified power target phrase " + id);
            var group = groups[0];
            var numbers = source.Numbers(id).Where(n => !group.Members.Any(m => m.Id == n.Id)).ToDictionary(n => n.Id, n => n.Value);
            var baseline = source.AvailableLanguages.Where(l => source.HasText(id, l, "tooltip")).ToDictionary(l => l, l => source.Text(id, l, "tooltip"));
            CardEdit Edit(int count) => source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => count) };
            source.Apply(id, Edit(2));
            Check(source.Numbers(id).Where(n => numbers.ContainsKey(n.Id)).All(n => numbers[n.Id] == n.Value), "effect amount, timing and charges unchanged " + id);
            foreach (var (language, before) in baseline)
            {
                string after = source.Text(id, language, "tooltip");
                Check(before != after, "power target count localized " + id + "/" + language);
                string[] Tokens(string text) => Regex.Matches(text, @"<[^>]*>|\{[^{}]*\}").Select(m => m.Value).ToArray();
                Check(Tokens(before).SequenceEqual(Tokens(after)), "all formatting and effect aliases preserved " + id + "/" + language);
                foreach (string key in new[] { "cooldown", "charge" })
                {
                    string pattern = @"<keyword=" + key + @">[^<]*</keyword>[^\\]*$";
                    var a = Regex.Match(before, pattern); var b = Regex.Match(after, pattern);
                    if (a.Success) Check(b.Success && a.Value == b.Value, "following timing/charges preserved " + id + "/" + language + "/" + key);
                }
            }
            string expected = id switch
            {
                122302 => "урона каждому из 2 отрядов",
                122214 => "каждый из 2 дружественных отрядов",
                _ => "каждый из 2 отрядов"
            };
            Check(source.Text(id, "ru-ru", "tooltip").Contains(expected), "Russian target family correct " + id);
            if (id == 113310) Check(source.Text(id, "zh-tw", "tooltip").Contains("2 張單位牌"), "traditional Chinese boost count synchronized");
            string output = Path.Combine(scratch, "power-target-text-" + id);
            source.Export(output);
            var reopened = ClientSource.Open(output, Path.Combine(scratch, "power-target-cache-" + id));
            Check(baseline.Keys.All(l => reopened.Text(id, l, "tooltip") == source.Text(id, l, "tooltip")), "power target text survives packed export " + id);
            foreach (int count in new[] { 3, 21, 1 }) source.Apply(id, Edit(count));
            Check(!source.HasChanges && baseline.All(p => source.Text(id, p.Key, "tooltip") == p.Value), "exact power target text round trip " + id);
        }
        string fixture = Path.Combine(scratch, "target-phrase-guards"); Directory.CreateDirectory(fixture);
        var template = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == 113310);
        var ability = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements().Single(e => (int?)e.Attribute("Id") == 113310 && (string?)e.Attribute("Type") == "CardAbility");
        new XDocument(new XElement("Templates", new XElement(template))).Save(Path.Combine(fixture, "Templates.xml"));
        void SaveGraph(XElement graph) => new XDocument(new XElement("Abilities", graph)).Save(Path.Combine(fixture, "Abilities.xml"));
        foreach (string language in new[] { "en-us", "ru-ru", "fr-fr" }) File.Copy(Path.Combine(source.DirectoryPath, language + ".csv"), Path.Combine(fixture, language + ".csv"));
        SaveGraph(new XElement(ability));
        var clean = new EditorProject(fixture);
        var selector = clean.NumberGroups(113310).Single(g => g.Display.Role == "targets");
        bool Linked() => new EditorProject(fixture).DescriptionBindings(113310, selector.Members.Select(n => n.Id)).Any(b => b.Language == "en-us" && b.Linked);
        Check(Linked(), "isolated native boost fixture recognized");
        var shaped = new XElement(ability);
        shaped.Descendants("Shape").First().Element("V")!.SetAttributeValue("Mask", 1);
        SaveGraph(shaped);
        Check(!Linked(), "shaped target selection is not treated as independent targets");
        SaveGraph(new XElement(ability));
        string english = Path.Combine(fixture, "en-us.csv"); string originalEnglish = File.ReadAllText(english);
        void English(string value) => File.WriteAllText(english, Regex.Replace(originalEnglish, @"^113310_tooltip;[^\r\n]*", _ => "113310_tooltip;" + value, RegexOptions.Multiline));
        English("Boost a unit by {Boost} and damage it by {Template.Power}.");
        Check(!Linked(), "two expressions in the same effect clause remain ambiguous");
        English("Boost a unit by {Boost}. Repeat {Boost}.");
        Check(!Linked(), "repeated effect alias remains ambiguous");
        English("Boost a unit by {Boost}. Derived value: {Boost+1}.");
        Check(!Linked(), "effect alias reused by a formula remains ambiguous");
        English("Boost a unit by {Boost}. Provision: {Template.Provision}.");
        Check(Linked(), "independent following sentence does not block target binding");
        var extra = new EditorProject(fixture);
        extra.Apply(113310, extra.CurrentEdit(113310) with { Numbers = selector.Members.ToDictionary(n => n.Id, _ => 2) });
        Check(extra.Text(113310, "en-us", "tooltip").EndsWith(". Provision: {Template.Provision}.", StringComparison.Ordinal), "following sentence is preserved exactly");
        English(source.Text(113310, "en-us", "tooltip"));
        string french = Path.Combine(fixture, "fr-fr.csv");
        File.WriteAllText(french, File.ReadAllText(french).Replace("113310_tooltip;Améliorez une unité de", "113310_tooltip;Améliorez une unité alliée de", StringComparison.Ordinal));
        Check(!Linked(), "neutral target phrase cannot consume part of a localized allied phrase");
        return passed;
    }
}
