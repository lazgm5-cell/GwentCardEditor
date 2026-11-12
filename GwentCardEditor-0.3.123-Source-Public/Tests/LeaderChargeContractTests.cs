using System.Text;
using System.Xml.Linq;
using GwentCardEditor;

internal static class LeaderChargeContractTests
{
    public static IReadOnlyList<string> Real(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed.Add(name); }
        int[] ids = [200166, 201597, 202148, 202185, 202328, 202572];
        Check(source.Cards.Count(c => c.Type == 1 && source.InitialCharges(c.Id).Editable) == 20, "20 verified leader charge contracts");
        foreach (int id in new[] { 202187, 202574 })
            Check(!source.InitialCharges(id).Editable && source.InitialCharges(id).Reason == "ChargeTextMismatch", "case-mismatched Chinese charge placeholder stays blocked " + id);
        string folder = Path.Combine(scratch, "leader-charge-contracts"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baselineTemplates = new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e))));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e =>
            (string?)e.Attribute("Type") == "SubAbility" || (string?)e.Attribute("Type") == "CardAbility" && ids.Contains((int?)e.Attribute("Id") ?? -1)).Select(e => new XElement(e))));
        baselineTemplates.Save(Path.Combine(folder, "Templates.xml"));
        string path = Path.Combine(folder, "Abilities.xml"); baseline.Save(path);
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv"))
                .Where(line => ids.Any(id => line.StartsWith(id + "_", StringComparison.Ordinal))), new UTF8Encoding(false));
        foreach (int id in ids)
        {
            var project = new EditorProject(folder);
            var setting = project.InitialCharges(id);
            Check(setting.Editable, "reviewed leader graph enabled " + id);
            foreach (int value in new[] { 1, 9 })
            {
                var effect = project.Numbers(id).FirstOrDefault(n => n.Editable && n.Id > 0 && n.Value < n.Maximum
                    && n.Role is "damage" or "boost" or "coin-gain" or "bleeding-duration" or "spawn-count");
                var edits = new Dictionary<int, int> { [-1] = value };
                if (effect != null) edits[effect.Id] = effect.Value + 1;
                project.Apply(id, project.CurrentEdit(id) with { Numbers = edits });
                Check(project.InitialCharges(id) is { Editable: true } current && current.Value == value, "charge contract survives supported edits " + id + "/" + value);
                string output = Path.Combine(scratch, "leader-contract-" + id + "-" + value); project.Export(output);
                var reopened = new EditorProject(output);
                Check(reopened.InitialCharges(id) is { Editable: true } actual && actual.Value == value, "reviewed charges reopen " + id + "/" + value);
                var exported = XDocument.Load(Path.Combine(output, "Abilities.xml"));
                var graph = exported.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id);
                var original = baseline.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id);
                foreach (int variable in edits.Keys.Where(key => key > 0).Append(setting.TextVariableId!.Value))
                    graph.Descendants().Single(e => (int?)e.Attribute("Id") == variable).SetAttributeValue("V",
                        (string?)original.Descendants().Single(e => (int?)e.Attribute("Id") == variable).Attribute("V"));
                Check(XNode.DeepEquals(exported, baseline), "all other graphs and dependencies preserved " + id + "/" + value);
                var exportedTemplates = XDocument.Load(Path.Combine(output, "Templates.xml"));
                exportedTemplates.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id).SetElementValue("InitalTapCharges", setting.Value);
                Check(XNode.DeepEquals(exportedTemplates, baselineTemplates), "other template fields preserved " + id + "/" + value);
                foreach (string locale in source.AvailableLanguages)
                    Check(File.ReadAllBytes(Path.Combine(folder, locale + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, locale + ".csv"))), "contract locale preserved " + id + "/" + value + "/" + locale);
                project.Undo(); Check(!project.HasChanges, "contract charge and effect atomic undo " + id + "/" + value);
            }
            var changed = new XDocument(baseline);
            changed.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "CardAbility" && (int?)e.Attribute("Id") == id).SetAttributeValue("Unexpected", "True");
            changed.Save(path);
            var blocked = new EditorProject(folder);
            Check(!blocked.InitialCharges(id).Editable, "unknown leader topology rejected " + id);
            bool rejected = false;
            try { blocked.Apply(id, blocked.CurrentEdit(id) with { Provision = 17, Numbers = new Dictionary<int, int> { [-1] = 7 } }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !blocked.HasChanges, "modified contract atomic refusal " + id);
            baseline.Save(path);
        }
        var dependencyChanged = new XDocument(baseline);
        dependencyChanged.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 30).SetAttributeValue("Unexpected", "True");
        dependencyChanged.Save(path);
        foreach (int id in new[] { 201597, 202572 })
            Check(!new EditorProject(folder).InitialCharges(id).Editable, "changed spawn dependency rejected " + id);
        baseline.Save(path);
        foreach (int id in new[] { 202116, 202117 })
            Check(!source.Numbers(id).Any(n => n.Editable && n.Role == "cooldown"), "unused leader cooldown stays hidden " + id);
        return passed;
    }
}
