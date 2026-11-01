using System.Xml.Linq;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

internal static class DeckSwapMeaning
{
    private sealed record Capture(XElement Store, XElement Filter, string Player);
    public static bool BeforeReveal(XElement graph, XElement reveal)
    {
        var r = new GraphMeaningReader(graph);
        if (!r.Fixed(reveal.Element("PostReveal"), "0")) return false;
        string? Player(XElement? field) => Type(r.Producer(field)) switch
        { "GetOwnerPlayerIdNode" => "Owner", "GetOwnerOpponentPlayerIdNode" => "Opponent", _ => null };
        Capture? Captured(XElement move)
        {
            var getter = r.Producer(move.Element("Cards"));
            if (Type(getter) != "GetVarNode" || (int?)getter!.Attribute("VarId") is not int variable) return null;
            var writers = r.Nodes.Where(n => Type(n) != "GetVarNode" && (int?)n.Attribute("VarId") == variable).ToArray();
            if (writers.Length != 1 || Type(writers[0]) != "SetVarNode") return null;
            var store = writers[0]; var filter = r.Producer(store.Element("In"));
            if (Type(filter) != "FilterCardListNode" || !r.Linked(filter!.Element("FlowSuccess"), store.Element("FlowIn"))) return null;
            var query = r.Producer(filter.Element("Cards"));
            if (Type(query) != "GetCardsNode" || !r.Fixed(query!.Element("LocationId"), "16") || !r.Fixed(query.Element("CardTypes"), "14")
                || !r.Fixed(query.Element("CardTiers"), "10") || Player(query.Element("PlayerId")) is not string player) return null;
            var filters = filter.Element("Filters")?.Elements().ToArray() ?? [];
            if (filters.Length != 1) return null;
            var index = r.Producer(filters[0]);
            if (Type(index) != "FilterCardIndexNode" || !r.Fixed(index!.Element("Index"), "0") || !r.Fixed(index.Element("Op"), "0")
                || !r.Fixed(index.Element("PositionType"), "0") || !r.Fixed(index.Element("Negate"), "False")) return null;
            return new(store, filter, player);
        }
        var last = r.Producer(reveal.Element("FlowIn"));
        if (Type(last) == "RevealCardsNode")
        {
            if (!r.Fixed(last!.Element("PostReveal"), "0")) return false;
            last = r.Producer(last.Element("FlowIn"));
        }
        if (Type(last) != "MoveCardsNode" || CardMoveMeaning.Inspect(graph, last!) is not { Place: "DeckTop", Player: not null } end) return false;
        var maker = r.Producer(last!.Element("FlowIn"));
        var first = r.Producer(maker?.Element("FlowIn"));
        if (Type(maker) != "MakeCardPositionNode" || !r.Linked(maker!.Element("CardPosition"), last.Element("Position"))
            || Type(first) != "MoveCardsNode" || CardMoveMeaning.Inspect(graph, first!) is not { Place: "DeckTop", Player: not null } start
            || start.Player == end.Player) return false;
        var firstSource = Captured(first!); var lastSource = Captured(last);
        if (firstSource == null || lastSource == null || firstSource.Player != end.Player || lastSource.Player != start.Player) return false;
        var firstMaker = r.Producer(first!.Element("FlowIn"));
        return Type(firstMaker) == "MakeCardPositionNode" && r.Linked(firstMaker!.Element("CardPosition"), first.Element("Position"))
            && r.Linked(lastSource.Store.Element("FlowOut"), firstMaker.Element("FlowIn"))
            && r.Linked(firstSource.Store.Element("FlowOut"), lastSource.Filter.Element("FlowIn"));
    }
}
