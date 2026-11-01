using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record InitialChargeSetting(int Value, bool Editable, string Reason, int? TextVariableId = null,
    IReadOnlyList<int>? RefillValues = null);

public sealed partial class EditorProject
{
    // Keep this contract independent of future changes to Zeal's allowed nodes.
    private static readonly HashSet<string> InitialChargeNodes = new(StringComparer.Ordinal)
    {
        "PlayedTrigger", "TappedTrigger", "RefreshTargetsTrigger", "OnPlayRefreshLocationTargetsTrigger",
        "GetVarNode", "GetCardsNode", "GetLocationsNode", "GetOwnerPlayerIdNode", "GetOwnerOpponentPlayerIdNode",
        "GetAbilityOwnerNode", "GetAbilityOwnerCardListNode", "RequestCardTargetsNode", "ChangePowerNode", "ChangeArmorNode",
        "ChangeOwnerTapActiveNode", "ChangeTapCardSummoningSicknessNode", "CardListBranchNode", "CardListLengthNode",
        "IntBranchNode", "FilterCardListNode", "FilterCardCategoryNode", "OwnerLocationBranchNode",
        "ChangeRecommendedLocationsNode", "BloodthirstBranchNode"
    };

    // No nested abilities, blackboards, card play, timers or explicit charge writers.
    private static readonly HashSet<string> LeaderInitialChargeNodes = new(InitialChargeNodes, StringComparer.Ordinal)
    {
        "AfterChangedCardStatusTrigger", "BeforeChangedCardStatusTrigger", "AfterChangePowerTrigger", "AfterMovedTrigger",
        "AfterSpawnedTrigger", "CleanupTrigger", "BranchNode", "BreakLocationNode", "CardAttackGetTargetsNode",
        "CardListContainsElementNode", "CardListElementToListNode", "CardListGetElementNode", "CardListGetRandomElementNode",
        "CardPowerAttackForEachNode", "CardPowerAttackGetCardPreviousPowerNode", "ChangeLocationTokensNode",
        "ChangeOwnerMoneyNode", "ChangeTokensNode", "FilterCardTypeNode", "FilterCardFactionNode", "FilterCardPlayerIdNode",
        "FilterCardPowerNode", "FilterCardPowerStateNode", "FilterTapCardChargesNode", "GetAbilityOwnerCardNode",
        "GetCardLocationIdNode", "GetCardLocationNode", "GetCardPlayerIdNode", "GetCardPositionNode", "GetCardPowerNode",
        "GetLastAvailableIndexNode", "GetRandomLastAvailablePositionNode", "IntForeachNode", "IntMinNode", "IntMinusNode",
        "IntPlusNode", "IsBoardSideFull", "IsLocationFull", "LocationGetIdNode", "LocationIdNegateNode",
        "LocationListBranchNode", "LocationListGetElementNode", "MakeCardPositionNode", "MakeLocationNode",
        "MoveCardsNode", "RequestLocationTargetsNode", "SetVarNode", "SpawnCardNode"
    };

    public InitialChargeSetting InitialCharges(int id)
    {
        var card = Card(id);
        var template = Template(id);
        int value = Int(template, "InitalTapCharges");
        InitialChargeSetting Block(string reason) => new(value, false, reason);
        if (!card.Editable) return Block(card.BlockReason);
        bool leader = card.Type == 1;
        if (card.Type is not (1 or 4) || (string?)template.Element("TapActive") != "True"
            || (string?)template.Element("VisibleTapCharges") != "True" || Int(template, "InitialTimer") != -1
            || value is < 1 or > 9) return Block("InitialChargesUnsupported");
        if (leader && new[] { "InitalTapCharges", "TapActive", "VisibleTapCharges", "InitialTimer" }.Any(name =>
            template.Elements(name).Count() != 1 || template.Element(name)!.HasAttributes || template.Element(name)!.HasElements))
            return Block("InitialChargesUnsupported");
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) return Block("SharedAbility");
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        bool replenishing = nodes.Any(n => (string?)n.Attribute("Type") == "ChangeTapCardChargesNode");
        bool formation = !leader && FormationInitialChargePath(graph);
        if (nodes.Count(n => (string?)n.Attribute("Type") == "TappedTrigger") != 1
            || nodes.Any(n => !(leader ? LeaderInitialChargeNodes : InitialChargeNodes).Contains((string?)n.Attribute("Type") ?? "")
                && !(!leader && replenishing && (string?)n.Attribute("Type") is "AfterMovedTrigger" or "ChangeTapCardChargesNode" or "FilterHasOnTappedAbilityNode")
                && !(formation && (string?)n.Attribute("Type") is "AfterMovedTrigger" or "UseSubAbilityNode"))
                && !(leader && VerifiedLeaderChargeGraph(id))
            || graph.DescendantsAndSelf().Attributes().Any(a => (a.Name.LocalName is "ExternalIn" or "ExternalOut")
                && a.Value.Equals("True", StringComparison.OrdinalIgnoreCase))) return Block("InitialChargesUnsupported");
        if (GraphValidator.Validate(graph).ErrorCount > 0) return Block("GraphHasErrors");
        var variables = new[] { graph.Element("TemporaryVariables"), graph.Element("PersistentVariables") }
            .Where(e => e != null).SelectMany(e => e!.Elements())
            .Where(e => Regex.IsMatch((string?)e.Attribute("Name") ?? "", "^Charges?[0-9]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (variables.Length != (replenishing ? 2 : 1)) return Block("ChargeTextMismatch");
        var initial = variables.Where(v => Regex.IsMatch((string?)v.Attribute("Name") ?? "", "^Charges?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        if (initial.Length != 1) return Block("ChargeTextMismatch");
        var variable = initial[0];
        string name = (string?)variable.Attribute("Name") ?? "";
        if (variable.Parent?.Name.LocalName != "TemporaryVariables" || (string?)variable.Attribute("Type") != "IntVar"
            || !int.TryParse((string?)variable.Attribute("Id"), out int variableId) || variableId <= 0
            || !int.TryParse((string?)variable.Attribute("V"), out int shown) || shown != value
            || !Regex.IsMatch(name, "^Charges?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || string.Equals((string?)variable.Attribute("ResetInInactive"), "True", StringComparison.OrdinalIgnoreCase)) return Block("ChargeTextMismatch");
        if (leader && (variable.HasElements || variable.Attribute("GArg") != null)) return Block("ChargeTextMismatch");
        // This alias is presentation-only; any graph consumer makes it a different contract.
        if (graph.DescendantsAndSelf().Attributes().Any(a =>
                (a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                    || a.Name.LocalName.EndsWith("VariableId", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName is "SrcId" or "DstId")
                && int.TryParse(a.Value, out int target) && target == variableId)
            || graph.Descendants("CompareVarID").Any(e => int.TryParse(e.Value, out int target) && target == variableId)
            || graph.Descendants().Any(e => ((string?)e.Attribute("Type"))?.EndsWith("ReferenceVar", StringComparison.Ordinal) == true
                && int.TryParse((string?)e.Attribute("V"), out int target) && target == variableId)) return Block("InitialChargesUnsupported");
        var texts = AvailableLanguages.Where(l => HasText(id, l, "tooltip")).Select(l => Text(id, l, "tooltip")).ToArray();
        string? refillName = null;
        int[]? refillValues = null;
        if (replenishing)
        {
            var refill = variables.Single(v => v != variable);
            refillName = (string?)refill.Attribute("Name");
            if (refillName != name + "2" || (refillValues = AdditiveRefillValues(graph, refill)) == null)
                return Block("InitialChargesUnsupported");
        }
        if (texts.Length == 0 || texts.Any(text => !text.Contains("{" + name + "}", StringComparison.Ordinal)
            || refillName != null && !text.Contains("{" + refillName + "}", StringComparison.Ordinal)
            || Regex.Matches(text, @"\{([^{}]+)\}").Select(m => m.Groups[1].Value)
                .Any(alias => alias.StartsWith(name, StringComparison.OrdinalIgnoreCase) && alias != name && alias != refillName))) return Block("ChargeTextMismatch");
        return new(value, true, "", variableId, refillValues);
    }

    private (int Value, int VariableId)? InitialChargeEdit(int id, IReadOnlyDictionary<int, int>? edits)
    {
        if (edits == null || !edits.TryGetValue(-1, out int value)) return null;
        var setting = InitialCharges(id);
        if (!setting.Editable) throw new InvalidOperationException(setting.Reason);
        if (value is < 1 or > 9) throw new InvalidOperationException("AbilityRange");
        return (value, setting.TextVariableId!.Value);
    }
}
