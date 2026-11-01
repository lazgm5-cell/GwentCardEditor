using System.Security.Cryptography;
using System.Xml.Linq;
using GwentCardEditor;

internal static class BanArdDescriptionTests
{
    public static IReadOnlyList<string> Run(EditorProject source, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        const int id = 200048;
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var hashes = Directory.GetFiles(source.DirectoryPath).Where(p => Path.GetExtension(p) is ".xml" or ".csv").ToDictionary(p => p, Hash);
        Check(source.HasDescriptionRepair(id), "modified Ban Ard description mismatch detected");
        var group = source.NumberGroups(id).Single(g => g.Display.LabelRole == "both-row-boost");
        Check(group.Members.Select(n => n.Id).ToHashSet().SetEquals([110, 182]) && group.Display.Value == 2, "shared boost shows actual gameplay value and groups both rows");
        Check(source.DescriptionBindings(id, group.Members.Select(n => n.Id)).Where(b => b.Language is "en-us" or "ru-ru").All(b => b.Linked && b.Variables.Contains("Boost")), "literal boosts map to verified presentation variable");
        var preview = source.Preview(id, source.CurrentEdit(id));
        Check(preview.Count == 1 && preview[0].Field.EndsWith("/#1", StringComparison.Ordinal) && !source.HasChanges, "repair preview changes only unused presentation number");
        source.Apply(id, source.CurrentEdit(id));
        Check(!source.HasDescriptionRepair(id) && source.Numbers(id).Where(n => n.Id is 110 or 182).All(n => n.Value == 2), "repair preserves gameplay boost");
        string repaired = Path.Combine(scratch, "ban-ard-repaired"); source.Export(repaired);
        var reopened = ClientSource.Open(repaired, Path.Combine(scratch, "ban-ard-repaired-cache"));
        var xml = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Abilities.xml"));
        var graph = xml.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility");
        Check((int?)graph.Descendants().Single(e => (int?)e.Attribute("Id") == 1).Attribute("V") == 2, "packed tooltip alias repaired to actual boost");
        graph.Descendants().Single(e => (int?)e.Attribute("Id") == 1).SetAttributeValue("V", 1);
        Check(XNode.DeepEquals(xml, XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"))), "repair preserves every other ability field");
        foreach (string language in source.AvailableLanguages)
            Check(File.ReadAllBytes(Path.Combine(source.DirectoryPath, language + ".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(reopened.DirectoryPath, language + ".csv"))), "presentation repair preserves localization bytes " + language);
        source.Undo(); Check(!source.HasChanges && source.HasDescriptionRepair(id), "repair undo exact");
        foreach (int amount in new[] { 3, 5, 2 })
        {
            var edit = source.CurrentEdit(id) with { Numbers = group.Members.ToDictionary(n => n.Id, _ => amount) };
            Check(source.Preview(id, edit).Any(c => c.Field.EndsWith("/#1", StringComparison.Ordinal)), "boost preview includes presentation synchronization " + amount);
            source.Apply(id, edit);
            Check(!source.HasDescriptionRepair(id) && source.Numbers(id).Where(n => n.Id is 110 or 182).All(n => n.Value == amount), "both boosts and description synchronized " + amount);
        }
        bool rejected = false;
        try { source.Apply(id, source.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [110] = 4 } }); }
        catch (InvalidOperationException e) when (e.Message == "SharedBoostDescriptionConflict") { rejected = true; }
        Check(rejected && source.Numbers(id).Where(n => n.Id is 110 or 182).All(n => n.Value == 2), "inconsistent shared boost edit rejected atomically");
        source.Undo(); source.Undo(); source.Undo();
        Check(!source.HasChanges, "numeric changes undo exact including original stale alias");
        Check(hashes.All(p => Hash(p.Key) == p.Value), "source XML and localization files untouched");
        source.Client!.Verify();
        string fixture = Path.Combine(scratch, "ban-ard-foreign"); Directory.CreateDirectory(fixture);
        foreach (string file in Directory.GetFiles(source.DirectoryPath).Where(p => Path.GetExtension(p) is ".xml" or ".csv")) File.Copy(file, Path.Combine(fixture, Path.GetFileName(file)));
        var foreign = XDocument.Load(Path.Combine(fixture, "Abilities.xml"));
        foreign.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility")
            .Descendants().Single(e => (int?)e.Attribute("Id") == 108).SetAttributeValue("V", 1);
        foreign.Save(Path.Combine(fixture, "Abilities.xml"));
        Check(!new EditorProject(fixture).HasDescriptionRepair(id), "foreign graph is not repaired based on card ID alone");
        foreign = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml"));
        foreign.Root!.Elements().Single(e => (int?)e.Attribute("Id") == id && (string?)e.Attribute("Type") == "CardAbility")
            .Descendants().Single(e => (int?)e.Attribute("Id") == 110).SetAttributeValue("V", "invalid");
        foreign.Save(Path.Combine(fixture, "Abilities.xml"));
        Check(!new EditorProject(fixture).HasDescriptionRepair(id), "invalid numeric source is not offered a repair");
        return passed;
    }
}
