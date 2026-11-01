using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GwentCardEditor;

internal static class LeaderTests
{
    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var leaders = project.Cards.Where(c => c.Type == 1 && c.Editable).ToArray();
        Check(leaders.Length == 49 && leaders.Count(c => c.Availability > 0) == 42, "42 ordinary and 7 restored leaders supported");
        Check(project.Cards.Where(c => c.Type == 1 && (c.IsTutorial || CardCatalog.IsHidden(c))).All(c => !c.Editable), "explicitly hidden and tutorial leaders stay locked");
        foreach (int faction in EditorProject.Factions)
        {
            var rows = CardCatalog.Query(project, faction, "ru-ru", group: CardGroup.Leaders);
            Check(rows.Count == 7 && rows.All(c => c.Type == 1 && !c.IsTutorial), "leader faction catalog " + faction);
            Check(CardCatalog.Query(project, faction, "ru-ru").All(c => c.Type != 1), "leaders excluded from main cards " + faction);
            var up = CardCatalog.Query(project, faction, "ru-ru", group: CardGroup.Leaders, sort: CardSort.Provision);
            Check(up.Select(c => c.Provision).SequenceEqual(up.Select(c => c.Provision).Order()), "leader provision sorting " + faction);
        }
        foreach (var leader in leaders)
        {
            void Reject(CardEdit edit, string name)
            {
                bool rejected = false;
                try { project.Apply(leader.Id, edit); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && !project.HasChanges, $"leader edit atomic rejection {leader.Id}/{name}");
            }
            var original = project.CurrentEdit(leader.Id);
            Check(project.Preview(leader.Id, original).Count == 0, "leader no-op exact " + leader.Id);
            Reject(original with { Provision = -1 }, "negative provision");
            Reject(original with { Provision = 100 }, "large provision");
            Reject(original with { Provision = leader.Provision + 1, Power = 1 }, "power");
            Reject(original with { Provision = leader.Provision + 1, Armor = 1 }, "armor");
            Reject(original with { Provision = leader.Provision + 1, Tier = 8 }, "tier");
            Reject(original with { Provision = leader.Provision + 1, Rarity = 1 }, "rarity");
            Reject(original with { Provision = leader.Provision + 1, Faction = leader.Faction == 1 ? 2 : 1 }, "faction");
            Reject(original with { Provision = leader.Provision + 1, PrimaryCategory = original.PrimaryCategory == null ? "" : null }, "primary category");
            if (!project.InitialCharges(leader.Id).Editable)
                Reject(original with { Provision = leader.Provision + 1, Numbers = new Dictionary<int, int> { [-1] = 2 } }, "initial charges");
            Reject(original with { Provision = leader.Provision + 1, Statuses = new Dictionary<string, bool> { ["shield"] = true } }, "status");
            foreach (var setting in project.Numbers(leader.Id).Where(n => !n.Editable))
                Reject(original with { Provision = leader.Provision + 1, Numbers = new Dictionary<int, int> { [setting.Id] = setting.Value + 1 } }, "blocked number " + setting.Id);
            Check(project.Timing(leader.Id).Count == 0, "leader timing remains locked " + leader.Id);
        }
        var changes = leaders.ToDictionary(c => c.Id, c => project.Numbers(c.Id).Where(n => n.Editable && n.Id > 0).ToDictionary(n => n.Id,
            n => (Old: n.Value, Next: n.Value < n.Maximum ? n.Value + 1 : n.Value - 1)));
        foreach (var leader in leaders)
            project.Apply(leader.Id, project.CurrentEdit(leader.Id) with { Provision = leader.Provision + 1,
                Numbers = changes[leader.Id].ToDictionary(e => e.Key, e => e.Value.Next) });
        string output = Path.Combine(scratch, "real-leaders-export"); project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-leaders-cache"));
        var templates = XDocument.Load(Path.Combine(result.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(result.DirectoryPath, "Abilities.xml"));
        foreach (var leader in leaders)
        {
            Check(result.Card(leader.Id).Provision == leader.Provision + 1, "leader provision packed round-trip " + leader.Id);
            templates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == leader.Id).SetElementValue("Provision", leader.Provision);
            foreach (var (key, value) in changes[leader.Id])
            {
                Check(result.Numbers(leader.Id).Single(n => n.Id == key) is { Editable: true } actual && actual.Value == value.Next,
                    $"leader number packed round-trip {leader.Id}/{key}");
                abilities.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == leader.Id)
                    .Descendants().Single(e => (int?)e.Attribute("Id") == key).SetAttributeValue("V", value.Old);
            }
        }
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml")), templates), "leader export changes only provision templates");
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml")), abilities), "leader export changes only requested numeric attributes");
        foreach (string language in project.AvailableLanguages)
        {
            var keys = leaders.Select(l => l.Id + "_tooltip").ToHashSet();
            string Mask(string path) => Regex.Replace(Encoding.UTF8.GetString(File.ReadAllBytes(path)), @"^([^;\r\n]+);([^\r\n]*)",
                m => keys.Contains(m.Groups[1].Value) ? m.Groups[1].Value + ";" : m.Value, RegexOptions.Multiline);
            Check(Mask(Path.Combine(project.DirectoryPath, language + ".csv")) == Mask(Path.Combine(result.DirectoryPath, language + ".csv")), "leader export preserves unrelated locale bytes " + language);
            Check(leaders.All(l => result.Text(l.Id, language, "tooltip") == project.Text(l.Id, language, "tooltip")), "leader descriptions survive packed export " + language);
        }
        foreach (var _ in leaders) project.Undo();
        Check(!project.HasChanges, "all leader edits undo exactly");
        Check(changes.Values.Sum(v => v.Count) > 80, "leader editable numeric values " + changes.Values.Sum(v => v.Count));
        return passed;
    }
}
