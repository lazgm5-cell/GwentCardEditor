using System.Xml.Linq;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

internal static class BanishStrengthTransferMeaning
{
    internal sealed record Plan(int[] Choices, int[] Targets);
    private sealed record Branch(XElement Gate, XElement Choice, XElement Target, string Tier, string Exit);

    public static Plan? Inspect(XElement graph)
    {
        var r = new GraphMeaningReader(graph);
        if (!r.Nodes.Any(n => Type(n) == "BanishCardsNode")) return null;
        var branches = new List<Branch>();
        bool Query(XElement? node, string player, string location) => Type(node) == "GetCardsNode"
            && Type(r.Producer(node!.Element("PlayerId"))) == player && r.Fixed(node.Element("LocationId"), location)
            && r.Fixed(node.Element("CardTypes"), "4");
        foreach (var boost in r.Nodes.Where(n => Type(n) == "ChangePowerNode"))
        {
            if (!r.Fixed(boost.Element("Operation"), "0") || !r.Fixed(boost.Element("TargetedPower"), "0")) continue;
            var target = r.Producer(boost.Element("Targets"));
            var power = r.Producer(boost.Element("Power"));
            var first = r.Producer(power?.Element("DS"));
            var choice = r.Producer(first?.Element("DS"));
            var banish = r.Producer(target?.Element("FlowIn"));
            var gate = r.Producer(choice?.Element("FlowIn"));
            var graveyard = r.Producer(choice?.Element("ValidChoices"));
            if (Type(target) != "RequestCardTargetsNode" || !r.Linked(target!.Element("FlowOutSuccess"), boost.Element("FlowIn"))
                || !r.Linked(target.Element("SelectedTargets"), boost.Element("Targets"))
                || Type(power) != "GetCardPowerNode" || !r.Fixed(power!.Element("PowerType"), "0")
                || Type(first) != "CardListGetElementNode" || !r.Fixed(first!.Element("I"), "0")
                || Type(choice) != "RequestCardChoiceNode" || !r.Linked(choice!.Element("SelectedChoices"), first.Element("DS"))
                || !r.Fixed(choice.Element("MinChoices"), "1") || !r.Fixed(choice.Element("MaxChoices"), "1")
                || Type(banish) != "BanishCardsNode" || !r.Linked(choice.Element("FlowOutSuccess"), banish!.Element("FlowIn"))
                || !r.Linked(choice.Element("SelectedChoices"), banish.Element("Targets"))
                || !r.Linked(banish.Element("FlowOut"), target.Element("FlowIn"))
                || Type(gate) != "FilterCardListNode" || !Query(graveyard, "GetOwnerOpponentPlayerIdNode", "32")
                || !Query(r.Producer(target.Element("ValidTargets")), "GetOwnerPlayerIdNode", "3")) continue;
            var selected = choice.Element("SelectedChoices");
            if (r.Edges.Count(e => (int?)e.Attribute("SrcId") == (int?)selected?.Attribute("Id")) != 2) continue;
            string? tier = (string?)graveyard!.Element("CardTiers")?.Attribute("V");
            if (tier is not ("2" or "10") || !r.Fixed(graveyard.Element("CardTiers"), tier)) continue;
            string? exit = r.Linked(gate!.Element("FlowSuccess"), choice.Element("FlowIn")) ? "Success"
                : r.Linked(gate.Element("FlowFail"), choice.Element("FlowIn")) ? "Fail" : null;
            if (exit != null) branches.Add(new(gate, choice, target, tier, exit));
        }
        if (branches.Count != 2 || branches[0].Gate != branches[1].Gate
            || !branches.Select(b => b.Tier).ToHashSet().SetEquals(["2", "10"])
            || !branches.Select(b => b.Exit).ToHashSet().SetEquals(["Success", "Fail"])) return null;
        return new(branches.Select(b => (int)b.Choice.Attribute("Id")!).ToArray(),
            branches.Select(b => (int)b.Target.Attribute("Id")!).ToArray());
    }
}
