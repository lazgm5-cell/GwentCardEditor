using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static bool IsInitialStatusDefinitionNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "GetCardDefinitionNode" || node.Elements().Count() != 2
            || node.Elements("DS").Count() != 1 || node.Elements("DV").Count() != 1) return false;
        bool Field(XElement field, string type) => (string?)field.Attribute("Type") == type
            && field.Attribute("GArg") == null && (int?)field.Attribute("Id") is > 0;
        bool Definition(XElement field)
        {
            if (!Field(field, "CardDefinitionVar") || field.Elements().Count() != 1 || field.Elements("V").Count() != 1) return false;
            var value = field.Element("V")!;
            return !value.HasElements && string.IsNullOrWhiteSpace(value.Value) && value.Attributes().Count() == 3
                && (string?)value.Attribute("Type") == "CardDefinition" && (string?)value.Attribute("TemplateId") == "0"
                && (string?)value.Attribute("IsPremium") == "False";
        }
        var input = node.Element("DS")!; var output = node.Element("DV")!;
        if (!Field(input, "CardVar") || input.HasElements || !Definition(output)) return false;
        int inputId = (int)input.Attribute("Id")!, outputId = (int)output.Attribute("Id")!;
        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var incoming = links.Where(e => (int?)e.Attribute("DstId") == inputId).ToArray();
        var outgoing = links.Where(e => (int?)e.Attribute("SrcId") == outputId).ToArray();
        if (incoming.Length != 1 || outgoing.Length == 0
            || links.Any(e => (int?)e.Attribute("SrcId") == inputId || (int?)e.Attribute("DstId") == outputId)
            || !graph.Element("Nodes")!.Descendants().Any(e => (int?)e.Attribute("Id") == (int?)incoming[0].Attribute("SrcId")
                && Field(e, "CardVar") && !e.HasElements)) return false;
        // This contract covers identity filtering, not stored definitions, spawning or transformations.
        return outgoing.All(link => graph.Element("Nodes")!.Elements().Any(filter =>
            (string?)filter.Attribute("Type") == "FilterDefinitionNode" && filter.Elements().Count() == 3
            && new[] { "Negate", "Out", "Definition" }.All(name => filter.Elements(name).Count() == 1)
            && Field(filter.Element("Negate")!, "BoolVar") && !filter.Element("Negate")!.HasElements
            && bool.TryParse((string?)filter.Element("Negate")!.Attribute("V"), out _)
            && Field(filter.Element("Out")!, "CardFilterVar") && !filter.Element("Out")!.HasElements
            && Definition(filter.Element("Definition")!)
            && (int?)filter.Element("Definition")!.Attribute("Id") == (int?)link.Attribute("DstId")));
    }
}
