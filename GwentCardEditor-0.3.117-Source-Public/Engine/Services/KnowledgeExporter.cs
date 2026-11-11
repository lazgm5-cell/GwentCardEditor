using System.Text;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class KnowledgeExporter
{
    public static string Export(string abilitiesPath, string templatesPath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        TemplateDocument templates = TemplateDocument.Load(templatesPath);
        AbilityDocument abilities = AbilityDocument.Load(abilitiesPath);

        string cardsPath = Path.Combine(outputDirectory, "cards.csv");
        var cards = new StringBuilder("TemplateId,DebugName\n");
        foreach (var card in templates.Cards.OrderBy(card => card.Id))
            cards.AppendLine($"{card.Id},{Csv(card.DebugName)}");
        File.WriteAllText(cardsPath, cards.ToString(), Encoding.UTF8);

        string nodesPath = Path.Combine(outputDirectory, "node-types.csv");
        var counts = abilities.Document.Descendants()
            .Where(element => element.Parent?.Name.LocalName == "Nodes")
            .Select(element => (string?)element.Attribute("Type") ?? "")
            .Where(type => type.Length > 0)
            .GroupBy(type => type)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Type, StringComparer.Ordinal);
        var nodes = new StringBuilder("NodeType,Count\n");
        foreach (var item in counts)
            nodes.AppendLine($"{Csv(item.Type)},{item.Count}");
        File.WriteAllText(nodesPath, nodes.ToString(), Encoding.UTF8);

        return Path.GetFullPath(outputDirectory);
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
