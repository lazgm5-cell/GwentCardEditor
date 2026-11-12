using GwentCardEditor;
using System.Xml.Linq;

internal static class AlternativeSelectionClarityTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed.Add(name); }
        foreach (var (card, field, reason) in new[] { (202997, 172, "SingleStrengthSource"), (202997, 173, "SingleStrengthSource"),
            (202997, 261, "SingleStrengthSource"), (202997, 262, "SingleStrengthSource"),
            (202609, 527, "UnusedOrderCondition"), (203054, 55, "SingleRowSelection"), (203054, 56, "SingleRowSelection") })
        {
            var number = source.Numbers(card).Single(n => n.Id == field);
            Check(!number.Editable && number.Reason == reason, "Internal alternative selection locked " + card + "/" + field);
            bool denied = false;
            try { source.Preview(card, source.CurrentEdit(card) with { Numbers = new Dictionary<int, int> { [field] = 2 } }); }
            catch (InvalidOperationException e) when (e.Message == reason) { denied = true; }
            Check(denied, "Backend rejects internal selection " + card + "/" + field);
        }
        var group = source.NumberGroups(202997).Single(g => g.Display.LabelRole == "strength-transfer-targets");
        Check(group.Members.Select(n => n.Id).ToHashSet().SetEquals([384, 385, 429, 430]), "Doll boost target branches grouped together");
        Check(source.NumberGroups(202997).Count == 1, "Doll has one editable boost-target setting");
        Check(source.Numbers(202609).Any(n => n.Editable && n.Role == "damage")
            && source.Numbers(202609).Any(n => n.Editable && n.Role == "boost"), "Wraith real damage and boost remain editable");
        int[] ids = [202997, 202609, 203054];
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || ids.Contains((int?)e.Attribute("Id") ?? -1))));
        string folder = Path.Combine(scratch, "alternative-selection"); Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder, "Templates.xml"));
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv")).Where(l => ids.Any(id => l.StartsWith(id + "_", StringComparison.Ordinal))));
        EditorProject Load(XDocument doc) { doc.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        XElement Graph(XDocument doc, int id) => doc.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id);
        var project = Load(baseline);
        project.Apply(202997, project.CurrentEdit(202997) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => 2) });
        string output = Path.Combine(scratch, "alternative-selection-export"); project.Export(output);
        var reopened = new EditorProject(output);
        Check(reopened.NumberGroups(202997).Single(g => g.Display.LabelRole == "strength-transfer-targets").Members.All(n => n.Value == 2), "Doll both boost branches survive export");
        var changed = XDocument.Load(Path.Combine(output, "Abilities.xml"));
        foreach (int field in new[] { 384, 385, 429, 430 }) Graph(changed, 202997).Descendants().Single(e => (int?)e.Attribute("Id") == field).SetAttributeValue("V", 1);
        Check(XNode.DeepEquals(changed, baseline), "Only four boost-target bounds change; banish choices and amounts untouched");
        project.Undo(); Check(!project.HasChanges, "Doll branches undo in one step");
        var mismatch = new XDocument(baseline);
        foreach (int field in new[] { 429, 430 }) Graph(mismatch, 202997).Descendants().Single(e => (int?)e.Attribute("Id") == field).SetAttributeValue("V", 2);
        Check(!Load(mismatch).NumberGroups(202997).Any(g => g.Display.LabelRole == "strength-transfer-targets"), "Unequal branch target counts not silently merged");
        var differentPower = new XDocument(baseline);
        Graph(differentPower, 202997).Descendants().Single(e => (int?)e.Attribute("Id") == 359).SetAttributeValue("V", 1);
        Check(!Load(differentPower).NumberGroups(202997).Any(g => g.Display.LabelRole == "strength-transfer-targets"), "Different power semantics prevent branch merge");
        var shapedTargets = new XDocument(baseline);
        Graph(shapedTargets, 202997).Element("Connections")!.Elements().Single(e => (int?)e.Attribute("DstId") == 410).SetAttributeValue("SrcId", 390);
        Check(!Load(shapedTargets).NumberGroups(202997).Any(g => g.Display.LabelRole == "strength-transfer-targets"), "Area targets are not mislabeled as individually selected boost targets");
        var differentCharge = new XDocument(baseline);
        Graph(differentCharge, 202609).Descendants().Single(e => (int?)e.Attribute("Id") == 527).SetAttributeValue("V", 2);
        Check(Load(differentCharge).Numbers(202609).Single(n => n.Id == 527).Reason != "UnusedOrderCondition", "Nonstandard charge threshold not mistaken for unused Order");
        Check(!source.HasChanges, "Alternative selection checks do not edit client data");
        return passed;
    }
}
