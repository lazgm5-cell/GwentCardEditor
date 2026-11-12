using System.Xml.Linq;

namespace GwentCardEditor;

internal sealed class GraphMeaningReader(XElement graph)
{
    public XElement[] Nodes { get; } = graph.Element("Nodes")?.Elements().ToArray() ?? [];
    public XElement[] Edges { get; } = graph.Element("Connections")?.Elements().ToArray() ?? [];
    public static string Type(XElement? node) => (string?)node?.Attribute("Type") ?? "";
    public XElement? Source(XElement? field)
    {
        if (field == null || (string?)field.Attribute("ExternalIn") == "True") return null;
        var links = Edges.Where(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray();
        if (links.Length != 1) return null;
        var matches = Nodes.SelectMany(n => n.Descendants()).Where(e => (int?)e.Attribute("Id") == (int?)links[0].Attribute("SrcId")).ToArray();
        return matches.Length == 1 && (string?)matches[0].Attribute("ExternalIn") != "True" ? matches[0] : null;
    }
    public XElement? Producer(XElement? field) => Source(field)?.Ancestors().FirstOrDefault(n => n.Parent?.Name.LocalName == "Nodes");
    public bool Fixed(XElement? field, string value) => field != null && (string?)field.Attribute("V") == value
        && (string?)field.Attribute("ExternalIn") != "True" && !Edges.Any(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id"));
    public bool Linked(XElement? output, XElement? input) => output != null && input != null
        && (int?)Source(input)?.Attribute("Id") == (int?)output.Attribute("Id");
    public string? Owner(XElement? field) => Type(Producer(field)) switch
    { "GetAbilityOwnerNode" or "GetAbilityOwnerCardNode" or "GetAbilityOwnerCardListNode" => "Self", _ => null };
}
