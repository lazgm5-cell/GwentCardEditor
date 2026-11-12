using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record ZealSetting(bool Value, bool Editable, string Reason);

public sealed partial class EditorProject
{
    // Opaque subabilities, transformations and sickness writers are deliberately absent.
    private static readonly HashSet<string> ZealNodes = new(StringComparer.Ordinal)
    {
        "PlayedTrigger", "TappedTrigger", "RefreshTargetsTrigger", "OnPlayRefreshLocationTargetsTrigger",
        "BeforeTurnTrigger", "CardTimerElapsedTrigger", "GetVarNode", "GetCardsNode", "GetLocationsNode",
        "GetOwnerPlayerIdNode", "GetOwnerOpponentPlayerIdNode", "GetAbilityOwnerNode", "GetAbilityOwnerCardNode",
        "GetAbilityOwnerCardListNode", "GetCardPowerNode", "GetCardArmorNode", "GetTapCardChargesNode",
        "RequestCardTargetsNode", "ChangePowerNode", "ChangeArmorNode", "BalanceCardStatusTimerNode",
        "ChangeTapCardChargesNode", "ChangeCardTimerNode", "ChangeOwnerTapActiveNode", "CardListBranchNode",
        "CardListLengthNode", "IntBranchNode", "IntPlusNode", "IntMinusNode", "IntCompareNode",
        "FilterCardListNode", "FilterDefinitionNode", "FilterCardTokensNode", "FilterPowerLevelNode",
        "OwnerLocationBranchNode", "ChangeRecommendedLocationsNode", "CardListElementToListNode", "CardListConcatNode"
    };

    public ZealSetting Zeal(int id)
    {
        XElement template = Template(id);
        bool valid = bool.TryParse((string?)template.Element("SummoningSickness"), out bool sick);
        bool value = valid && !sick;
        ZealSetting Block(string reason) => new(value, false, reason);
        var card = Card(id);
        if (!card.Editable) return Block(card.BlockReason);
        if (card.Type != 4 || (string?)template.Element("TapActive") != "True" || !valid
            || Int(template, "InitalTapCharges") <= 0) return Block("ZealNeedsOrder");
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) return Block("SharedAbility");
        if (GraphValidator.Validate(graph).ErrorCount > 0) return Block("GraphHasErrors");
        if (OwnedFormation(graph)) return Block("GraphControlsZeal");
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        if (nodes.Count(n => (string?)n.Attribute("Type") == "TappedTrigger") != 1) return Block("ZealNeedsOrder");
        if (nodes.Any(n => (string?)n.Attribute("Type") == "ChangeTapCardSummoningSicknessNode")) return Block("GraphControlsZeal");
        if (nodes.Any(n => !ZealNodes.Contains((string?)n.Attribute("Type") ?? ""))
            || graph.Descendants().Attributes().Any(a => (a.Name.LocalName is "ExternalIn" or "ExternalOut")
                && a.Value.Equals("True", StringComparison.OrdinalIgnoreCase))) return Block("UnsupportedZealGraph");
        return new(value, true, "");
    }

    private bool? InitialZeal(int id) => bool.TryParse((string?)Template(id).Element("SummoningSickness"), out bool sick) ? !sick : null;
}
