using GwentCardEditor;
using System.Xml.Linq;

internal static class AbilityClarityTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        foreach (var (card, field) in new[] { (202402, 259), (202399, 363) })
        {
            var number = source.Numbers(card).Single(n => n.Id == field);
            Check(!number.Editable && number.Reason == "ImplicitPowerCondition", "Internal armor condition hidden " + card);
            Check(number.Value == 0 && number.MeaningFacts.Contains(new("Comparison", ">")) && number.MeaningFacts.Contains(new("PowerType", "Armor")), "Positive armor requirement remains unchanged " + card);
        }
        var survival = source.Numbers(200078).Single(n => n.Id == 133);
        Check(!survival.Editable && survival.Reason == "SurvivalCheck", "Trial of the Grasses survival guard is hidden");
        Check(source.Numbers(200078).Any(n => n.Editable && n.Role == "set-power") && source.Numbers(200078).Any(n => n.Editable && n.Role == "damage"), "Trial main effects remain editable");
        bool blocked = false;
        try { source.Preview(200078, source.CurrentEdit(200078) with { Numbers = new Dictionary<int, int> { [133] = 1 } }); }
        catch (InvalidOperationException e) when (e.Message == "SurvivalCheck") { blocked = true; }
        Check(blocked, "Survival check protected in backend");
        var draw = source.NumberGroups(112205).Single(g => g.Display.LabelRole == "draw-selected-count");
        Check(draw.Members.Select(n => n.Id).ToHashSet().SetEquals([628, 648, 649]), "Myrgtabrakke selection and actual draw grouped");
        var drawChanges = source.Preview(112205, source.CurrentEdit(112205) with { Numbers = draw.Members.ToDictionary(n => n.Id, _ => 2) });
        Check(drawChanges.Count(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == 3,
            "Changing draw updates all three numeric endpoints");
        Check(drawChanges.Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == source.AvailableLanguages.Count(l => source.HasText(112205, l, "tooltip")),
            "Changing selected Dragon draw also updates every existing localized description");
        var bottom = source.NumberGroups(112205).Single(g => g.Display.LabelRole == "move-to-deck-bottom-count");
        Check(bottom.Members.Select(n => n.Id).ToHashSet().SetEquals([999, 1000]) && bottom.Display.MeaningFacts.Contains(new("MovePlace", "DeckBottom")), "Myrgtabrakke hand-return selection labelled separately");
        var returnChanges = source.Preview(112205, source.CurrentEdit(112205) with { Numbers = bottom.Members.ToDictionary(n => n.Id, _ => 2) });
        Check(returnChanges.Count(c => !c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == 2, "Return count does not change draw count");
        Check(returnChanges.Count(c => c.Field.EndsWith("/tooltip", StringComparison.Ordinal)) == source.AvailableLanguages.Count(l => source.HasText(112205, l, "tooltip")), "Return count also updates every existing localized description");

        int[] ids = [202402, 202399, 200078, 112205];
        string folder = Path.Combine(scratch, "ability-clarity"); Directory.CreateDirectory(folder);
        var templates = XDocument.Load(Path.Combine(source.DirectoryPath, "Templates.xml"));
        var abilities = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        var baseline = new XDocument(new XElement("Abilities", abilities.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility" || ids.Contains((int?)e.Attribute("Id") ?? -1))));
        new XDocument(new XElement("Templates", templates.Root!.Elements().Where(e => ids.Contains((int?)e.Attribute("Id") ?? -1)))).Save(Path.Combine(folder, "Templates.xml"));
        foreach (string locale in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder, locale + ".csv"), File.ReadLines(Path.Combine(source.DirectoryPath, locale + ".csv")).Where(l => ids.Any(id => l.StartsWith(id + "_", StringComparison.Ordinal))));
        EditorProject Load(XDocument doc) { doc.Save(Path.Combine(folder, "Abilities.xml")); return new EditorProject(folder); }
        var project = Load(baseline);
        project.Apply(112205, project.CurrentEdit(112205) with { Numbers = draw.Members.ToDictionary(n => n.Id, _ => 2) });
        string output = Path.Combine(scratch, "clarity-export"); project.Export(output);
        var reopened = new EditorProject(output);
        Check(reopened.NumberGroups(112205).Single(g => g.Display.LabelRole == "draw-selected-count").Members.All(n => n.Value == 2), "Grouped draw survives export");
        Check(reopened.NumberGroups(112205).Single(g => g.Display.LabelRole == "move-to-deck-bottom-count").Display.Value == 1, "Draw export preserves return count");
        project.Undo(); Check(!project.HasChanges, "Grouped draw has one atomic undo");
        var mismatch = new XDocument(baseline);
        mismatch.Descendants().Single(e => (int?)e.Attribute("Id") == 628).SetAttributeValue("V", 2);
        Check(!Load(mismatch).NumberGroups(112205).Any(g => g.Display.LabelRole == "draw-selected-count"), "Different selection and draw counts are not automatically merged");
        var nonzero = new XDocument(baseline);
        nonzero.Root!.Elements().Single(e => (int?)e.Attribute("Id") == 200078).Descendants().Single(e => (int?)e.Attribute("Id") == 133).SetAttributeValue("V", 1);
        Check(Load(nonzero).Numbers(200078).Single(n => n.Id == 133).Reason != "SurvivalCheck", "Nonzero custom condition is not called an ordinary survival guard");
        Check(!source.HasChanges, "Clarity audit leaves original project unchanged");
        return passed;
    }
}
