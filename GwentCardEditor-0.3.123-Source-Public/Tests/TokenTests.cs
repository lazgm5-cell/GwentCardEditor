using System.Xml.Linq;
using GwentCardEditor;

internal static class TokenTests
{
    public static IReadOnlyList<string> Real(EditorProject project, string scratch)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); passed.Add(name); }
        var tokens = project.Cards.Where(c => c.IsToken && c.Editable && !c.IsTutorial).ToArray();
        Check(tokens.Length >= 40, "verified editable token templates " + tokens.Length);
        foreach (var token in tokens)
        {
            Check(CardCatalog.Query(project, token.Faction, "ru-ru", group: CardGroup.Tokens).Any(c => c.Id == token.Id), "token catalog includes " + token.Id);
            Check(project.Preview(token.Id, project.CurrentEdit(token.Id)).Count == 0, "token no-op exact " + token.Id);
            project.Apply(token.Id, project.CurrentEdit(token.Id) with { Power = token.Type == 4 ? token.Power + 1 : token.Power,
                Armor = token.Type == 4 ? token.Armor + 1 : token.Armor, Provision = token.Provision + 1 });
        }
        string output = Path.Combine(scratch, "real-token-export"); project.Export(output);
        var reopened = ClientSource.Open(output, Path.Combine(scratch, "real-token-cache"));
        var templates = XDocument.Load(Path.Combine(reopened.DirectoryPath, "Templates.xml"));
        foreach (var token in tokens)
        {
            var actual = reopened.Card(token.Id);
            Check(actual.Provision == token.Provision + 1 && actual.Power == token.Power + (token.Type == 4 ? 1 : 0)
                && actual.Armor == token.Armor + (token.Type == 4 ? 1 : 0) && actual.IsToken, "token packed stats round-trip " + token.Id);
            var template = templates.Root!.Elements().Single(t => (int?)t.Attribute("Id") == token.Id);
            template.SetElementValue("Power", token.Power); template.SetElementValue("Armor", token.Armor); template.SetElementValue("Provision", token.Provision);
        }
        Check(XNode.DeepEquals(templates, XDocument.Load(Path.Combine(project.DirectoryPath, "Templates.xml"))), "token stats preserve every unrelated template field");
        foreach (string file in project.AvailableLanguages.Select(l => l + ".csv").Append("Abilities.xml"))
            Check(File.ReadAllBytes(Path.Combine(project.DirectoryPath, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(reopened.DirectoryPath, file))), "token stats preserve " + file);
        foreach (var _ in tokens) project.Undo();
        Check(!project.HasChanges, "all token stats undo exactly");
        foreach (var artifact in tokens.Where(c => c.Type == 8))
        {
            bool rejected = false;
            try { project.Apply(artifact.Id, project.CurrentEdit(artifact.Id) with { Power = 1, Provision = artifact.Provision + 1 }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected && !project.HasChanges, "artifact token power blocked atomically " + artifact.Id);
        }
        return passed;
    }
}
