using System.Xml.Linq;

namespace GwentCardEditor;

internal static class CardMoveMeaning
{
    internal sealed record Meaning(string Place, string? Player);
    public static Meaning? Inspect(XElement graph, XElement move)
    {
        if ((string?)move.Attribute("Type") != "MoveCardsNode") return null;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        XElement? Source(XElement? field)
        {
            if (field == null || (string?)field.Attribute("ExternalIn") == "True") return null;
            var links = edges.Where(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray();
            if (links.Length != 1) return null;
            var matches = nodes.SelectMany(n => n.Descendants()).Where(e => (int?)e.Attribute("Id") == (int?)links[0].Attribute("SrcId")).ToArray();
            return matches.Length == 1 && (string?)matches[0].Attribute("ExternalIn") != "True" ? matches[0] : null;
        }
        string? Fixed(XElement? field) => field == null || (string?)field.Attribute("ExternalIn") == "True"
            || edges.Any(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id")) ? null : (string?)field.Attribute("V");
        var position = Source(move.Element("Position"));
        var maker = position?.Parent;
        if (position?.Name.LocalName != "CardPosition" || (string?)maker?.Attribute("Type") != "MakeCardPositionNode"
            || Fixed(maker.Element("Location")) != "16") return null;
        var index = Source(maker.Element("Index"));
        string? place = index?.Name.LocalName == "Out" && (string?)index.Parent?.Attribute("Type") == "GetLastAvailableIndexNode" ? "DeckBottom"
            : index?.Name.LocalName == "Out" && (string?)index.Parent?.Attribute("Type") == "GetRandomIndexNode" ? "DeckRandom"
            : Fixed(maker.Element("Index")) == "0" ? "DeckTop" : null;
        if (place == null) return null;
        var player = Source(maker.Element("PlayerId"));
        string? side = player?.Name.LocalName != "Out" ? null : (string?)player.Parent?.Attribute("Type") switch
        { "GetOwnerPlayerIdNode" => "Owner", "GetOwnerOpponentPlayerIdNode" => "Opponent", _ => null };
        return new(place, side);
    }
}
