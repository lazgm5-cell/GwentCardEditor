using System.Text.Json;
using System.Xml.Linq;
using GwentCardEditor;

internal static class CardMovementTextAudit
{
    public static void Write(EditorProject source, string output)
    {
        var graphs = XDocument.Load(Path.Combine(source.DirectoryPath, "Abilities.xml")).Root!.Elements()
            .Where(e => (string?)e.Attribute("Type") == "CardAbility").ToDictionary(e => (int)e.Attribute("Id")!);
        var rows = source.Cards.Where(c => c.Editable).SelectMany(card => source.NumberGroups(card.Id)
            .Where(g => g.Display.Role.Contains("draw", StringComparison.Ordinal)
                || g.Display.Role.Contains("choice", StringComparison.Ordinal)
                || g.Display.Role.Contains("discard", StringComparison.Ordinal))
            .Select(group => new
            {
                CardId = card.Id,
                Name = source.Name(card.Id, "ru-ru"),
                group.Display.Role,
                group.Display.LabelRole,
                group.Display.Value,
                NumberIds = group.Members.Select(n => n.Id).ToArray(),
                Bindings = source.DescriptionBindings(card.Id, group.Members.Select(n => n.Id))
                    .Where(b => source.HasText(card.Id, b.Language, "tooltip")).ToArray(),
                Russian = source.Text(card.Id, "ru-ru", "tooltip"),
                English = source.Text(card.Id, "en-us", "tooltip")
            })).ToArray();
        source.Client!.Verify();
        if (source.HasChanges) throw new Exception("Movement audit changed the source.");
        var report = new
        {
            Passed = true,
            Groups = rows.Length,
            Cards = rows.Select(r => r.CardId).Distinct().Count(),
            FullyLinked = rows.Count(r => r.Bindings.Length > 0 && r.Bindings.All(b => b.Linked)),
            DrawSamples = new[] { 112110, 201776, 152308, 122403, 202215, 202498, 202197, 152107, 201781, 202512, 202883, 112205, 202187, 202782, 122107, 202326 }.Select(id => new
            {
                CardId = id,
                Graph = graphs[id].ToString(),
                Texts = source.AvailableLanguages.ToDictionary(l => l, l => source.Text(id, l, "tooltip"))
            }).ToArray(),
            Rows = rows
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Audited {report.Groups} groups on {report.Cards} cards; {report.FullyLinked} fully linked.");
    }
}
