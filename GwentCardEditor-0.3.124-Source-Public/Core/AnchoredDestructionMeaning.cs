using System.Xml.Linq;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

internal static class AnchoredDestructionMeaning
{
    internal sealed record Plan(int Anchor, int Additional);
    public static IReadOnlyList<Plan> Inspect(XElement graph)
    {
        var r = new GraphMeaningReader(graph);
        if (!r.Nodes.Any(n => Type(n) == "GetCardTierNode") || !r.Nodes.Any(n => Type(n) == "CardListAppendNode")) return [];
        var plans = new List<Plan>();
        bool OwnerUnits(XElement? node) => Type(node) == "GetCardsNode" && r.Fixed(node!.Element("CardTypes"), "4")
            && r.Fixed(node.Element("LocationId"), "3") && Type(r.Producer(node.Element("PlayerId"))) == "GetOwnerPlayerIdNode";
        foreach (var append in r.Nodes.Where(n => Type(n) == "CardListAppendNode"))
        {
            var additional = r.Producer(append.Element("ListToAppend"));
            var getter = r.Producer(append.Element("List"));
            if (Type(additional) != "RequestCardTargetsNode" || !r.Linked(additional!.Element("FlowOutSuccess"), append.Element("FlowIn"))
                || Type(getter) != "GetVarNode" || (int?)getter!.Attribute("VarId") is not int variable) continue;
            var writers = r.Nodes.Where(n => Type(n) != "GetVarNode" && (int?)n.Attribute("VarId") == variable).ToArray();
            if (writers.Length != 1 || Type(writers[0]) != "SetVarNode") continue;
            var store = writers[0]; var anchor = r.Producer(store.Element("In"));
            if (Type(anchor) != "RequestCardTargetsNode" || !r.Fixed(anchor!.Element("MinTargets"), "1") || !r.Fixed(anchor.Element("MaxTargets"), "1")
                || !r.Linked(anchor.Element("FlowOutSuccess"), store.Element("FlowIn")) || !OwnerUnits(r.Producer(anchor.Element("ValidTargets")))) continue;
            var candidates = r.Producer(additional.Element("ValidTargets"));
            var pool = r.Producer(candidates?.Element("Cards"));
            var tier = r.Producer(pool?.Element("CardTiers"));
            var first = r.Producer(tier?.Element("DS"));
            if (Type(candidates) != "FilterCardListNode" || !OwnerUnits(pool) || Type(tier) != "GetCardTierNode" || Type(first) != "CardListGetElementNode"
                || !r.Fixed(first!.Element("I"), "0") || !r.Linked(anchor.Element("SelectedTargets"), first.Element("DS"))
                || !r.Linked(store.Element("FlowOut"), candidates!.Element("FlowIn")) || !r.Linked(candidates.Element("FlowSuccess"), additional.Element("FlowIn"))) continue;
            var filters = candidates.Element("Filters")?.Elements().ToArray() ?? [];
            if (filters.Length != 1) continue;
            var exclude = r.Producer(filters[0]); var identity = r.Producer(exclude?.Element("CardId"));
            if (Type(exclude) != "FilterCardIdNode" || !r.Fixed(exclude!.Element("Negate"), "True") || Type(identity) != "GetCardIdNode"
                || !r.Linked(first.Element("DV"), identity!.Element("DS"))) continue;
            var destroys = r.Nodes.Where(n => Type(n) == "DestroyCardsNode" && r.Linked(append.Element("FlowOut"), n.Element("FlowIn"))
                && Type(r.Producer(n.Element("Targets"))) == "GetVarNode"
                && (int?)r.Producer(n.Element("Targets"))!.Attribute("VarId") == variable).ToArray();
            if (destroys.Length != 1) continue;
            plans.Add(new((int)anchor.Attribute("Id")!, (int)additional.Attribute("Id")!));
        }
        return plans;
    }
}
