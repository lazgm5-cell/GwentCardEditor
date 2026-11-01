using System.Security.Cryptography;
using System.Text.Json;
using GwentCardEditor;

internal static class CharacteristicsIntegrationTests
{
    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add("mixed characteristics: " + name); }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Check(!project.HasChanges, "starts clean");
        var sourceHashes = Directory.GetFiles(project.DirectoryPath)
            .Where(p => Path.GetExtension(p) is ".xml" or ".csv")
            .ToDictionary(p => Path.GetFileName(p)!, Hash);
        var token = project.Cards.First(c => c.Editable && c.IsToken && c.Type == 4
            && project.Numbers(c.Id).Any(n => n.Editable && n.Id > 0 && n.Value < n.Maximum));
        var tokenNumber = project.Numbers(token.Id).First(n => n.Editable && n.Id > 0 && n.Value < n.Maximum);
        var edits = new Dictionary<int, CardEdit>
        {
            [112101] = project.CurrentEdit(112101) with { Power = project.Card(112101).Power + 1, Armor = project.Card(112101).Armor + 1 },
            [122105] = project.CurrentEdit(122105) with { Provision = project.Card(122105).Provision + 1,
                Numbers = new Dictionary<int, int> { [-1] = project.Numbers(122105).Single(n => n.Id == -1).Value + 1 } },
            [token.Id] = project.CurrentEdit(token.Id) with { Power = token.Power + 1,
                Numbers = new Dictionary<int, int> { [tokenNumber.Id] = tokenNumber.Value + 1 } }
        };
        foreach (var (id, role) in new[] { (152202, "base-damage"), (202122, "alternate-damage") })
        {
            var number = project.Numbers(id).Single(n => n.Editable && n.Role == role);
            edits.Add(id, project.CurrentEdit(id) with { Numbers = new Dictionary<int, int> { [number.Id] = number.Value + 1 } });
        }
        foreach (var (id, edit) in edits)
        {
            Check(project.Preview(id, edit).Count > 0, "preview " + id);
            Check(project.Apply(id, edit), "apply " + id);
        }
        string changes = JsonSerializer.Serialize(project.Changes());
        string warnings = JsonSerializer.Serialize(project.PendingDescriptionWarnings());
        Check(project.PendingDescriptionWarnings().Any(w => w.CardId == 202122 && w.Language == "en-us"), "literal description warning retained");
        project.Reset();
        Check(!project.HasChanges && project.PendingDescriptionWarnings().Count == 0, "reset entire mixed batch");
        project.Undo();
        Check(JsonSerializer.Serialize(project.Changes()) == changes && JsonSerializer.Serialize(project.PendingDescriptionWarnings()) == warnings,
            "undo reset restores all changes and warnings");
        string output = Path.Combine(scratch, "mixed-characteristics-export");
        project.Export(output);
        Check(Directory.GetFiles(output).Select(Path.GetFileName).SequenceEqual(new[] { "data_definitions" }), "compact root has only installable archive");
        var reopened = ClientSource.Open(output, Path.Combine(scratch, "mixed-characteristics-cache"));
        foreach (var (id, edit) in edits)
        {
            Check(reopened.Card(id).Power == edit.Power && reopened.Card(id).Armor == edit.Armor
                && reopened.Card(id).Provision == edit.Provision, "packed stats " + id);
            foreach (var (key, value) in edit.Numbers ?? new Dictionary<int, int>())
                Check(reopened.Numbers(id).Single(n => n.Id == key) is { Editable: true } n && n.Value == value, "packed numeric " + id + "/" + key);
        }
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "_editor", "card-editor-changes.json"))))
            Check(JsonSerializer.Serialize(manifest.RootElement.GetProperty("DescriptionWarnings").Deserialize<DescriptionWarning[]>()) == warnings,
                "manifest retains exact warnings");
        foreach (var (name, hash) in sourceHashes)
        {
            Check(Hash(Path.Combine(project.DirectoryPath, name!)) == hash, "source unchanged " + name);
            if (name is not ("Templates.xml" or "Abilities.xml"))
                Check(Hash(Path.Combine(reopened.DirectoryPath, name!)) == hash, "unrelated file unchanged " + name);
        }
        foreach (var _ in edits) project.Undo();
        Check(!project.HasChanges && project.PendingDescriptionWarnings().Count == 0, "undo each edit restores clean project");
        return passed;
    }
}
