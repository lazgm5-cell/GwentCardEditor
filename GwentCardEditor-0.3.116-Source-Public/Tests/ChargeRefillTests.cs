using System.Xml.Linq;
using GwentCardEditor;

internal static class ChargeRefillTests
{
    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var cards = project.Cards.Select(c => (c.Id, Setting: project.ChargeRefill(c.Id)))
            .Where(c => c.Setting != null).Select(c => (c.Id, Setting: c.Setting!)).ToArray();
        Check(cards.Select(c => c.Id).Order().SequenceEqual(new[] { 162306, 200041, 202150, 202155, 202256 }), "five verified fixed refills");
        Check(project.ChargeRefill(152311) == null && project.InitialCharges(152311).Editable, "computed refill hidden without hiding initial charges");
        bool rejectedComputed = false;
        try { project.Apply(152311, project.CurrentEdit(152311) with { Numbers = new Dictionary<int, int> { [-5] = 2 } }); }
        catch (InvalidOperationException e) when (e.Message == "InitialValueLocked") { rejectedComputed = true; }
        Check(rejectedComputed && !project.HasChanges, "computed refill cannot be edited through virtual field");
        foreach (var (id, setting) in cards)
        {
            Check(project.Numbers(id).Single(n => n.Id == -5) is { Editable: true, TextLinked: true, Minimum: 1, Maximum: 9 }, "one synchronized refill field " + id);
            Check(!project.Apply(id, project.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [-5] = setting.Value } }), "refill no-op exact " + id);
            foreach (int invalid in new[] { 0, 10 })
            {
                bool rejected = false;
                try { project.Apply(id, project.CurrentEdit(id) with { Power = project.Card(id).Power + 1,
                    Numbers = new Dictionary<int, int> { [-1] = 2, [-5] = invalid } }); }
                catch (InvalidOperationException e) when (e.Message == "AbilityRange") { rejected = true; }
                Check(rejected && !project.HasChanges, "combined invalid refill atomic " + id + ":" + invalid);
            }
        }
        var oldInitial = project.InitialCharges(162306);
        int oldPower = project.Card(162306).Power;
        project.Apply(162306, project.CurrentEdit(162306) with { Power = oldPower + 1,
            Numbers = new Dictionary<int, int> { [-1] = 3, [-5] = 2 } });
        Check(project.InitialCharges(162306).Value == 3 && project.ChargeRefill(162306)?.Value == 2
            && project.Card(162306).Power == oldPower + 1, "initial charges refill and power apply independently in one transaction");
        project.Undo();
        Check(!project.HasChanges && project.InitialCharges(162306).Value == oldInitial.Value, "combined charge refill undo exact");
        foreach (var (id, setting) in cards)
            project.Apply(id, project.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [-5] = setting.Value + 1 } });
        Check(project.Changes().Count == cards.Sum(c => c.Setting.LiteralVariableId.HasValue ? 2 : 1), "refill edits touch only effective source and required alias");
        string output = Path.Combine(scratch, "real-charge-refill-export"); project.Export(output);
        var result = ClientSource.Open(output, Path.Combine(scratch, "real-charge-refill-cache"));
        Check(cards.All(c => result.ChargeRefill(c.Id)?.Value == c.Setting.Value + 1
            && result.InitialCharges(c.Id).Value == project.InitialCharges(c.Id).Value), "all fixed refills packed round-trip without changing starting reserve");
        var abilities = XDocument.Load(Path.Combine(result.DirectoryPath, "Abilities.xml"));
        foreach (var (id, setting) in cards)
        {
            var graph = abilities.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
            foreach (int variable in new int?[] { setting.TextVariableId, setting.LiteralVariableId }.OfType<int>())
                graph.Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V", setting.Value);
        }
        Check(XNode.DeepEquals(abilities, XDocument.Load(Path.Combine(project.DirectoryPath, "Abilities.xml"))), "refill export preserves triggers filters fallbacks and all unrelated graph data");
        Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, "Templates.xml")).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, "Templates.xml"))), "refill-only edit preserves exact template bytes");
        foreach (string language in project.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(result.DirectoryPath, language + ".csv"))), "refill preserves locale " + language);
        foreach (var _ in cards) project.Undo();
        Check(!project.HasChanges, "all refill changes undo exactly");
        return passed;
    }
}
