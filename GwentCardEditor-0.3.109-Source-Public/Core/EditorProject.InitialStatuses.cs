using System.Globalization;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record InitialStatusSetting(string Key, bool Value, bool Editable, string Reason);
public sealed record InitialStatusDiagnostic(string Code, string Detail);
public sealed record InitialStatusInspection(int CardId, IReadOnlyList<InitialStatusSetting> Settings,
    IReadOnlyList<InitialStatusDiagnostic> Blockers);

public sealed partial class EditorProject
{
    public static readonly IReadOnlyDictionary<string, int> InitialStatusMasks = new Dictionary<string, int>(StringComparer.Ordinal)
        { ["shield"] = 2, ["veil"] = 8192, ["resilient"] = 1, ["immune"] = 256,
          ["defender"] = 1024, ["doomed"] = 512 }.AsReadOnly();

    // Independent of Order contracts. Static reads and additive/removal writes are checked per flag; opaque logic remains excluded.
    private static readonly HashSet<string> InitialStatusNodes = new(StringComparer.Ordinal)
    {
        "PlayedTrigger", "TappedTrigger", "RefreshTargetsTrigger", "OnPlayRefreshLocationTargetsTrigger",
        "BeforeTurnTrigger", "BeforeTurnEndTrigger", "AfterTurnTrigger", "AfterMovedTrigger", "CardTimerElapsedTrigger",
        "GetVarNode", "GetCardsNode", "GetLocationsNode", "GetOwnerPlayerIdNode", "GetOwnerOpponentPlayerIdNode",
        "GetAbilityOwnerNode", "GetAbilityOwnerCardNode", "GetAbilityOwnerCardListNode", "GetCardPowerNode", "GetCardArmorNode",
        "GetTapCardChargesNode", "RequestCardTargetsNode", "ChangePowerNode", "ChangeArmorNode", "DrawCardsNode",
        "ChangeOwnerTapActiveNode", "ChangeTapCardSummoningSicknessNode", "ChangeTapCardChargesNode", "ChangeCardTimerNode",
        "CardListBranchNode", "CardListLengthNode", "IntBranchNode", "IntPlusNode", "IntMinusNode", "IntCompareNode",
        "FilterCardListNode", "FilterDefinitionNode", "FilterPowerLevelNode", "FilterCardPowerStateNode",
        "FilterCardCategoryNode", "FilterCardFactionNode", "FilterTapCardChargesNode", "FilterHasOnTappedAbilityNode",
        "OwnerLocationBranchNode", "ChangeRecommendedLocationsNode", "CardListElementToListNode", "CardListConcatNode",
        "CardListGetElementNode", "CardListGetRandomElementNode", "GetCardPositionNode", "GetCardsInShapeNode"
    };

    private static readonly IReadOnlyDictionary<string, (string Name, string Type, string? Argument)[]> InitialStatusSchemaNodes
        = new Dictionary<string, (string, string, string?)[]>(StringComparer.Ordinal)
    {
        ["FilterCardTypeNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("Type", "EnumVar`1", "ECardType")],
        ["FilterCardPowerNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("PowerType", "EnumVar`1", "EPowerType"),
            ("Op", "EnumVar`1", "ECompareOp"), ("Power", "IntVar", null)],
        ["FilterCardPlayerIdNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("PlayerId", "PlayerIdVar", null)],
        ["GetOwnerLocationNode"] = [("Out", "ELocationVar", null)],
        ["GetCardTemplateProvisionNode"] = [("DS", "CardVar", null), ("DV", "IntVar", null)],
        ["FilterCardTokensNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("Tokens", "CardTokensVar", null)],
        ["AfterChangedCardStatusTrigger"] = [("Priority", "EnumVar`1", "ETriggerPriority"), ("FlowOut", "FlowConnector", null),
            ("ActionOnTrigger", "EnumVar`1", "EActionOnTrigger"), ("OwnerLocation", "ELocationVar", null), ("IsAmbushing", "BoolVar", null),
            ("TargetCard", "EnumVar`1", "EPlayerSide"), ("TriggerPlayer", "EnumVar`1", "ETriggerPlayer"),
            ("OperationValue", "EnumVar`1", "EStatusOperationTrigger"), ("StatusMask", "EnumVar`1", "ECardToken"),
            ("PlayerMask", "PlayerIdVar", null), ("CardStatusAttack", "CardStatusAttackVar", null)],
        ["IntMultiplyNode"] = [("Value", "IntVar", null), ("Factor", "IntVar", null), ("Rounding", "EnumVar`1", "ERounding"), ("Result", "IntVar", null)],
        ["IntDivideNode"] = [("Value", "IntVar", null), ("Divisor", "IntVar", null), ("Rounding", "EnumVar`1", "ERounding"), ("Result", "IntVar", null)],
        ["BranchNode"] = [("FlowIn", "FlowConnector", null), ("FlowOutTrue", "FlowConnector", null), ("FlowOutFalse", "FlowConnector", null), ("Condition", "BoolVar", null)],
        ["CardHasArmorBranchNode"] = [("FlowIn", "FlowConnector", null), ("FlowOutTrue", "FlowConnector", null), ("FlowOutFalse", "FlowConnector", null), ("Card", "CardVar", null)],
        ["FilterCardProvisionNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("Provision", "IntVar", null), ("Op", "EnumVar`1", "ECompareOp")],
        ["FilterHasOnPlayedAbilityNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null)],
        ["FilterTapCardVisibleChargesNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("VisibleCharges", "BoolVar", null)],
        ["FilterTapCardStateNode"] = [("Negate", "BoolVar", null), ("Out", "CardFilterVar", null), ("TapCardState", "EnumVar`1", "ETapCardState")],
        ["ChangeOwnerMoneyNode"] = [("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null), ("Operation", "EnumVar`1", "EMoneyOperation"),
            ("Value", "IntVar", null), ("IsTribute", "BoolVar", null)],
        ["ChangeTokensNode"] = [("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null), ("Attacker", "EntityVar", null),
            ("Targets", "CardListVar", null), ("ElementType", "EnumVar`1", "EElementType"), ("AttackMethod", "EnumVar`1", "EAttackMethod"),
            ("EffectType", "EnumVar`1", "EEffectType"), ("Operation", "EnumVar`1", "ETokenOp"), ("Tokens", "CardTokensVar", null)]
    };

    private static readonly (string Name, string Value)[] InitialStatusTriggerConstants =
        [("Priority", "0"), ("ActionOnTrigger", "0"), ("OwnerLocation", "3"), ("IsAmbushing", "False"),
         ("TargetCard", "1"), ("TriggerPlayer", "3"), ("OperationValue", "1"), ("PlayerMask", "3")];

    private static bool IsInitialStatusSchemaNode(XElement node)
    {
        string type = (string?)node.Attribute("Type") ?? "";
        if (!InitialStatusSchemaNodes.TryGetValue(type, out var fields)) return false;
        // Both four-port and five-port money nodes are serialized by the original client.
        if (type == "ChangeOwnerMoneyNode" && !node.Elements("IsTribute").Any())
            fields = fields.Where(f => f.Name != "IsTribute").ToArray();
        if (node.Elements().Count() != fields.Length) return false;
        return fields.All(field => node.Elements(field.Name).Count() == 1
            && !node.Element(field.Name)!.HasElements
            && (string?)node.Element(field.Name)!.Attribute("Type") == field.Type
            && (string?)node.Element(field.Name)!.Attribute("GArg") == field.Argument);
    }

    private bool IsIndependentInitialStatusResupplyCall(int id, XElement node)
    {
        if ((string?)node.Attribute("Type") != "UseSubAbilityNode"
            || (int?)node.Element("SubAbilityID") != ResupplySpec.Sub) return false;
        // Only accept the editor-owned, fingerprint-verified Resupply module; its effect does not read card status bits.
        return AdditionalResupply(id) is { Value: true, Editable: true, Added: true }
            && TriggerDependency(ResupplySpec);
    }

    public IReadOnlyList<InitialStatusSetting> InitialStatuses(int id)
        => EvaluateInitialStatuses(id, null);

    public InitialStatusInspection InspectInitialStatuses(int id)
    {
        var blockers = new List<InitialStatusDiagnostic>();
        return new(id, EvaluateInitialStatuses(id, blockers), blockers);
    }

    private IReadOnlyList<InitialStatusSetting> EvaluateInitialStatuses(int id, List<InitialStatusDiagnostic>? blockers)
    {
        var template = Template(id);
        bool valid = template.Elements("Tokens").Count() == 1
            && int.TryParse((string?)template.Element("Tokens"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            && Int(template, "Tokens") >= 0;
        int mask = valid ? Int(template, "Tokens") : 0;
        var card = Card(id);
        IReadOnlyList<InitialStatusSetting> Result(string reason, int dependencies = 0) => InitialStatusMasks.Select(s =>
        {
            string statusReason = reason.Length > 0 ? reason
                : card.Type == 8 && s.Key != "resilient" ? "StatusNeedsUnit"
                : (dependencies & (s.Key is "shield" or "veil" ? 8194 : s.Value)) != 0 ? "InitialStatusesUnsupported" : "";
            return new InitialStatusSetting(s.Key, (mask & s.Value) != 0, statusReason.Length == 0, statusReason);
        }).ToArray();
        IReadOnlyList<InitialStatusSetting> Block(string reason, string code, string detail)
        {
            blockers?.Add(new(code, detail));
            return Result(reason);
        }
        if (!card.Editable) return Block(card.BlockReason, "CardEligibility", card.BlockReason);
        if (card.Type is not (4 or 8) || card.IsToken) return Block("StatusNeedsUnitOrArtifact", "CardEligibility", card.IsToken ? "Token" : "CardType");
        if (!valid) return Block("InitialStatusesUnsupported", "TemplateTokens", "InvalidMask");
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) return Block("SharedAbility", "AbilityOwnership", graph == null ? "Missing" : "Shared");
        // Vanilla abilities omit Nodes and Connections entirely, leaving exactly two empty variable containers.
        bool vanilla = graph.Elements().Count() == 2
            && new[] { "TemporaryVariables", "PersistentVariables" }.All(k => graph.Elements(k).Count() == 1
                && !graph.Element(k)!.HasElements && string.IsNullOrWhiteSpace(graph.Element(k)!.Value));
        if (!vanilla && GraphValidator.Validate(graph).ErrorCount > 0) return Block("GraphHasErrors", "GraphValidation", "Errors");
        var unsupported = (graph.Element("Nodes")?.Elements() ?? []).Where(n => !InitialStatusNodes.Contains((string?)n.Attribute("Type") ?? "")
                && !IsIndependentInitialStatusResupplyCall(id, n)
                && !IsInitialStatusSchemaNode(n) && !IsInitialStatusVariableNode(n, graph)
                && !IsInitialStatusEconomyNode(n, graph) && !IsInitialStatusDefinitionNode(n, graph)
                && !IsInitialStatusMathNode(n, graph) && !IsInitialStatusRemovalNode(n, graph)
                && !IsInitialStatusTimerNode(n, graph)).ToArray();
        bool external = graph.DescendantsAndSelf().Attributes().Any(a => a.Name.LocalName is "ExternalIn" or "ExternalOut"
                && a.Value.Equals("True", StringComparison.OrdinalIgnoreCase));
        foreach (string type in unsupported.Select(n => (string?)n.Attribute("Type") == "UseSubAbilityNode"
                ? "UseSubAbilityNode/" + (int.TryParse((string?)n.Element("SubAbilityID"), out int subId) && subId > 0 ? subId.ToString(CultureInfo.InvariantCulture) : "invalid")
                : (string?)n.Attribute("Type") ?? "<missing>").Distinct().Order(StringComparer.Ordinal))
            blockers?.Add(new("NodeContract", type));
        if (external) blockers?.Add(new("ExternalDependency", "ExternalInOrOut"));
        if (unsupported.Length > 0 || external) return Result("InitialStatusesUnsupported");
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        foreach (var node in graph.Element("Nodes")?.Elements() ?? [])
        {
            string? type = (string?)node.Attribute("Type");
            if (type is not ("FilterCardTokensNode" or "AfterChangedCardStatusTrigger" or "ChangeTokensNode")) continue;
            var statusMask = node.Element(type == "AfterChangedCardStatusTrigger" ? "StatusMask" : "Tokens")!;
            if (!int.TryParse((string?)statusMask.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int readMask)
                || readMask <= 0 || (readMask & ~65535) != 0) return Block("InitialStatusesUnsupported", "StatusMask", type + ":Invalid");
            if (type == "FilterCardTokensNode" && !bool.TryParse((string?)node.Element("Negate")!.Attribute("V"), out _))
                return Block("InitialStatusesUnsupported", "StatusFilter", "InvalidNegate");
            if (type == "ChangeTokensNode" &&
                (!int.TryParse((string?)node.Element("Operation")!.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int operation)
                    || operation is not (0 or 1)
                    || edges.Any(c => (int?)c.Attribute("DstId") == (int?)node.Element("Operation")!.Attribute("Id"))))
                return Block("InitialStatusesUnsupported", "StatusWrite", "DynamicOrUnsupportedOperation");
            if (type == "AfterChangedCardStatusTrigger" && InitialStatusTriggerConstants.Any(field =>
                !string.Equals((string?)node.Element(field.Name)!.Attribute("V"), field.Value, StringComparison.OrdinalIgnoreCase)
                || edges.Any(c => (int?)c.Attribute("DstId") == (int?)node.Element(field.Name)!.Attribute("Id"))))
                return Block("InitialStatusesUnsupported", "StatusTrigger", "UnsupportedConstants");
        }
        var statusFields = graph.Descendants().Where(e => (string?)e.Attribute("Type") == "CardTokensVar"
            || (string?)e.Attribute("GArg") == "ECardToken"
            || e.Name.LocalName == "Ignore" && (string?)e.Parent?.Attribute("Type") == "GetCardsNode");
        var fields = statusFields.ToArray();
        if (fields.Any(e => e.Name.LocalName == "Ignore" && (string?)e.Parent?.Attribute("Type") == "GetCardsNode"
                && (string?)e.Attribute("Type") != "CardTokensVar"
            || !int.TryParse((string?)e.Attribute("V"), out _)
            || edges.Any(c => (int?)c.Attribute("DstId") == (int?)e.Attribute("Id")))) return Block("InitialStatusesUnsupported", "StatusDependency", "DynamicOrInvalidMask");
        int dependencies = fields.Aggregate(0, (bits, e) => bits | int.Parse((string)e.Attribute("V")!, CultureInfo.InvariantCulture));
        var result = Result("", dependencies);
        foreach (var setting in result.Where(s => !s.Editable))
            blockers?.Add(new(setting.Reason == "StatusNeedsUnit" ? "StatusNeedsUnit" : "FlagDependency", setting.Key));
        return result;
    }

    private void PrepareInitialStatuses(int id, XElement copy, IReadOnlyDictionary<string, bool>? edits)
    {
        if (edits == null || edits.Count == 0) return;
        if (edits.Keys.Any(k => !InitialStatusMasks.ContainsKey(k))) throw new InvalidOperationException("UnsupportedFields");
        var settings = InitialStatuses(id).ToDictionary(s => s.Key);
        var changed = edits.Where(e => e.Value != settings[e.Key].Value).ToArray();
        foreach (var (key, _) in changed)
            if (!settings[key].Editable) throw new InvalidOperationException(settings[key].Reason);
        if (changed.Length == 0) return;
        int mask = Int(copy, "Tokens");
        foreach (var (key, enabled) in changed)
            mask = enabled ? mask | InitialStatusMasks[key] : mask & ~InitialStatusMasks[key];
        copy.Element("Tokens")!.Value = mask.ToString(CultureInfo.InvariantCulture);
    }
}
