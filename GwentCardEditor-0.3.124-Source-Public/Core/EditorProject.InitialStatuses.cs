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
        ["CardListForeachNode"] = [("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null),
            ("FlowFor", "FlowConnector", null), ("List", "CardListVar", null), ("Element", "CardVar", null)],
        ["BloodthirstBranchNode"] = [("FlowIn", "FlowConnector", null), ("Player", "PlayerIdVar", null),
            ("Count", "IntVar", null), ("True", "FlowConnector", null), ("False", "FlowConnector", null)],
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

    private static readonly (string Name, string Type, string? Argument)[][] InitialStatusChoiceSchemas =
    [
        [("FlowIn", "FlowConnector", null), ("ChoiceType", "EnumVar`1", "ERequestChoiceType"), ("MinChoices", "IntVar", null),
            ("MaxChoices", "IntVar", null), ("AIHint", "SemanticTagIdVar", null), ("CanCancelRequest", "BoolVar", null),
            ("FlowOutSuccess", "FlowConnector", null), ("FlowOutFailure", "FlowConnector", null), ("ValidChoices", "CardListVar", null),
            ("RevealChoices", "BoolVar", null), ("SelectedChoices", "CardListVar", null)],
        [("FlowIn", "FlowConnector", null), ("ChoiceType", "EnumVar`1", "ERequestChoiceType"), ("MinChoices", "IntVar", null),
            ("MaxChoices", "IntVar", null), ("AIHint", "SemanticTagIdVar", null), ("CanCancelRequest", "BoolVar", null),
            ("AllowZeroChoices", "BoolVar", null), ("SelectedIndexes", "IntListVar", null), ("FlowOutSuccess", "FlowConnector", null),
            ("FlowOutFailure", "FlowConnector", null), ("ValidChoices", "CardListVar", null), ("DisabledChoices", "CardListVar", null),
            ("RevealChoices", "BoolVar", null), ("ShuffleChoices", "BoolVar", null), ("SelectedChoices", "CardListVar", null)],
        [("FlowIn", "FlowConnector", null), ("ChoiceType", "EnumVar`1", "ERequestChoiceType"), ("MinChoices", "IntVar", null),
            ("MaxChoices", "IntVar", null), ("AIHint", "SemanticTagIdVar", null), ("CanCancelRequest", "BoolVar", null),
            ("SelectedIndexes", "IntListVar", null), ("FlowOutSuccess", "FlowConnector", null), ("FlowOutFailure", "FlowConnector", null),
            ("ValidChoices", "CardListVar", null), ("DisabledChoices", "CardListVar", null), ("RevealChoices", "BoolVar", null),
            ("ShuffleChoices", "BoolVar", null), ("SelectedChoices", "CardListVar", null)]
    ];

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

    private static bool IsInitialStatusCurrentPlayerIdNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "GetCurrentPlayerIdNode" || node.Elements().Count() != 1
            || node.Elements("Out").Count() != 1) return false;
        var output = node.Element("Out")!;
        if (node.Descendants().Count() != 1 || (string?)output.Attribute("Type") != "PlayerIdVar"
            || output.Attribute("GArg") != null || (int?)output.Attribute("Id") is not > 0
            || (string?)output.Attribute("V") != "3"
            || new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)output.Attribute(a), "True", StringComparison.OrdinalIgnoreCase)))
            return false;
        // This read-only getter has no inputs; its output default must remain the client-serialized player ID.
        return !(graph.Element("Connections")?.Elements().Any(c => (int?)c.Attribute("DstId") == (int?)output.Attribute("Id")) ?? false);
    }

    private static bool IsInitialStatusCardLocationReadNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "GetCardLocationIdNode" || node.Elements().Count() != 3
            || !new[] { "DS", "DV", "PositionType" }.All(name => node.Elements(name).Count() == 1)) return false;
        var card = node.Element("DS")!;
        var location = node.Element("DV")!;
        var position = node.Element("PositionType")!;
        bool PlainField(XElement field, string type, string? argument = null)
            => (string?)field.Attribute("Type") == type && (string?)field.Attribute("GArg") == argument
                && (int?)field.Attribute("Id") is > 0 && !field.HasElements
                && !new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        if (!PlainField(card, "CardVar") || card.Attribute("V") != null
            || !PlainField(location, "ELocationVar") || (string?)location.Attribute("V") != "0"
            || !PlainField(position, "EnumVar`1", "ECardPositionType")
            || (string?)position.Attribute("V") is not ("0" or "1" or "2")) return false;

        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        int cardId = (int)card.Attribute("Id")!;
        int locationId = (int)location.Attribute("Id")!;
        int positionId = (int)position.Attribute("Id")!;
        var cardInputs = links.Where(c => (int?)c.Attribute("DstId") == cardId).ToArray();
        return cardInputs.Length == 1
            && !links.Any(c => (int?)c.Attribute("SrcId") == cardId
                || (int?)c.Attribute("DstId") == locationId
                || (int?)c.Attribute("DstId") == positionId)
            && links.Any(c => (int?)c.Attribute("SrcId") == locationId)
            && graph.Element("Nodes")?.Elements().Any(n => n.Elements().Any(f => (int?)f.Attribute("Id") == (int?)cardInputs[0].Attribute("SrcId")
                && (string?)f.Attribute("Type") == "CardVar" && !f.HasElements)) == true;
    }

    private static bool IsInitialStatusWoundedReadNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "GetCardIsWoundedNode" || node.Elements().Count() != 3
            || !new[] { "DS", "DV", "PowerState" }.All(name => node.Elements(name).Count() == 1)) return false;
        var card = node.Element("DS")!;
        var result = node.Element("DV")!;
        var powerState = node.Element("PowerState")!;
        bool PlainField(XElement field, string type, string? argument = null)
            => (string?)field.Attribute("Type") == type && (string?)field.Attribute("GArg") == argument
                && (int?)field.Attribute("Id") is > 0 && !field.HasElements
                && !new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        if (!PlainField(card, "CardVar") || card.Attribute("V") != null
            || !PlainField(result, "BoolVar") || (string?)result.Attribute("V") != "False"
            || !PlainField(powerState, "EnumVar`1", "EPowerState")
            || (string?)powerState.Attribute("V") is not ("1" or "2")) return false;

        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        int cardId = (int)card.Attribute("Id")!;
        int resultId = (int)result.Attribute("Id")!;
        int powerStateId = (int)powerState.Attribute("Id")!;
        var cardInputs = links.Where(c => (int?)c.Attribute("DstId") == cardId).ToArray();
        var resultOutputs = links.Where(c => (int?)c.Attribute("SrcId") == resultId).ToArray();
        if (cardInputs.Length != 1 || resultOutputs.Length != 1
            || links.Any(c => (int?)c.Attribute("SrcId") == cardId
                || (int?)c.Attribute("DstId") == resultId
                || (int?)c.Attribute("DstId") == powerStateId)) return false;
        int sourceId = (int?)cardInputs[0].Attribute("SrcId") ?? -1;
        int destinationId = (int?)resultOutputs[0].Attribute("DstId") ?? -1;
        var nodes = graph.Element("Nodes")?.Elements() ?? [];
        var sourceField = nodes.SelectMany(n => n.Elements().Select(f => (Node: n, Field: f)))
            .FirstOrDefault(x => (int?)x.Field.Attribute("Id") == sourceId);
        var destinationField = nodes.SelectMany(n => n.Elements().Select(f => (Node: n, Field: f)))
            .FirstOrDefault(x => (int?)x.Field.Attribute("Id") == destinationId);
        // This read-only query checks power state only; status tokens do not enter the node.
        return sourceField.Field != null && (string?)sourceField.Field.Attribute("Type") == "CardVar" && !sourceField.Field.HasElements
            && destinationField.Field != null && (string?)destinationField.Field.Attribute("Type") == "BoolVar";
    }

    private static bool IsInitialStatusProvisionFilterNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "FilterProvisionLevelNode" || node.Elements().Count() != 7
            || !new[] { "FlowIn", "FlowSuccess", "FlowFail", "Cards", "ProvisionFilter", "Num", "FilteredCards" }
                .All(name => node.Elements(name).Count() == 1)) return false;
        var flowIn = node.Element("FlowIn")!;
        var flowSuccess = node.Element("FlowSuccess")!;
        var flowFail = node.Element("FlowFail")!;
        var cards = node.Element("Cards")!;
        var provision = node.Element("ProvisionFilter")!;
        var number = node.Element("Num")!;
        var filtered = node.Element("FilteredCards")!;
        bool PlainField(XElement field, string type, string? argument = null)
            => (string?)field.Attribute("Type") == type && (string?)field.Attribute("GArg") == argument
                && (int?)field.Attribute("Id") is > 0 && !field.HasElements
                && !new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        if (!PlainField(flowIn, "FlowConnector") || !PlainField(flowSuccess, "FlowConnector")
            || !PlainField(flowFail, "FlowConnector") || !PlainField(cards, "CardListVar")
            || !PlainField(provision, "EnumVar`1", "EProvisionFilter")
            || (string?)provision.Attribute("V") is not ("1" or "2" or "3")
            || !PlainField(number, "IntVar") || (string?)number.Attribute("V") != "1"
            || !PlainField(filtered, "CardListVar")) return false;

        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        int flowInId = (int)flowIn.Attribute("Id")!;
        int cardsId = (int)cards.Attribute("Id")!;
        int provisionId = (int)provision.Attribute("Id")!;
        int numberId = (int)number.Attribute("Id")!;
        int filteredId = (int)filtered.Attribute("Id")!;
        var flowInputs = links.Where(c => (int?)c.Attribute("DstId") == flowInId).ToArray();
        var cardInputs = links.Where(c => (int?)c.Attribute("DstId") == cardsId).ToArray();
        var filteredOutputs = links.Where(c => (int?)c.Attribute("SrcId") == filteredId).ToArray();
        int successOutputs = links.Count(c => (int?)c.Attribute("SrcId") == (int?)flowSuccess.Attribute("Id"));
        int failOutputs = links.Count(c => (int?)c.Attribute("SrcId") == (int?)flowFail.Attribute("Id"));
        if (flowInputs.Length == 0 || cardInputs.Length != 1 || filteredOutputs.Length != 1
            || successOutputs != 1 || failOutputs > 1
            || links.Any(c => (int?)c.Attribute("DstId") == provisionId || (int?)c.Attribute("DstId") == numberId
                || (int?)c.Attribute("SrcId") == cardsId)) return false;

        var nodes = graph.Element("Nodes")?.Elements() ?? [];
        bool TypedSource(int id, string type) => nodes.Any(n => n.Elements().Any(f => (int?)f.Attribute("Id") == id
            && (string?)f.Attribute("Type") == type && !f.HasElements));
        bool TypedFlowSource(int id) => nodes.Any(n => n.Elements().Any(f => (int?)f.Attribute("Id") == id
            && (string?)f.Attribute("Type") == "FlowConnector"));
        int cardSourceId = (int?)cardInputs[0].Attribute("SrcId") ?? -1;
        int filteredDestinationId = (int?)filteredOutputs[0].Attribute("DstId") ?? -1;
        return flowInputs.All(c => TypedFlowSource((int?)c.Attribute("SrcId") ?? -1))
            && TypedSource(cardSourceId, "CardListVar") && TypedSource(filteredDestinationId, "CardListVar");
    }

    private static bool IsInitialStatusProphetLockWriteNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "SetBlackboardVarNode"
            || (int?)node.Attribute("VarId") != 33 || node.Elements().Count() != 5
            || !new[] { "FlowIn", "FlowOut", "In", "Out", "BlackboardType" }.All(name => node.Elements(name).Count() == 1))
            return false;
        var input = node.Element("In")!;
        var output = node.Element("Out")!;
        var boardType = node.Element("BlackboardType")!;
        bool StaticScalar(XElement field, string type, string value)
            => (string?)field.Attribute("Type") == type && field.Attribute("GArg") == null && !field.HasElements
                && (int?)field.Attribute("Id") is > 0 && (string?)field.Attribute("V") == value
                && !new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        if (!StaticScalar(input, "BoolVar", "True") || !StaticScalar(output, "BoolVar", "False")
            || (string?)boardType.Attribute("Type") != "BlackboardVar" || (string?)boardType.Attribute("V") != "3"
            || (int?)boardType.Attribute("Id") is not > 0 || boardType.Elements().Count() != 1
            || boardType.Elements("VarType").Count() != 1 || boardType.Element("VarType")!.HasAttributes
            || boardType.Element("VarType")!.HasElements || (string?)boardType.Element("VarType") != "BoolVar") return false;

        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        foreach (var field in new[] { input, output, boardType })
            if (links.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id")
                || (int?)c.Attribute("SrcId") == (int?)field.Attribute("Id"))) return false;
        var flowIn = node.Element("FlowIn")!;
        var incomingFlow = links.Where(c => (int?)c.Attribute("DstId") == (int?)flowIn.Attribute("Id")).ToArray();
        if (incomingFlow.Length != 1 || links.Any(c => (int?)c.Attribute("SrcId") == (int?)node.Element("FlowOut")!.Attribute("Id"))) return false;
        int sourceId = (int?)incomingFlow[0].Attribute("SrcId") ?? -1;
        var source = graph.Element("Nodes")?.Elements().FirstOrDefault(n => n.Elements().Any(f => (int?)f.Attribute("Id") == sourceId));
        var sourceField = source?.Elements().FirstOrDefault(f => (int?)f.Attribute("Id") == sourceId);
        // This exact GOG entry writes BB_ProphetLock (Blackboard 3, BoolVar 33) as a static true marker
        // after the supported hand-token action. Other blackboard writes remain opaque.
        return source != null && (string?)source.Attribute("Type") == "ChangeTokensNode"
            && sourceField?.Name.LocalName == "FlowOut" && (string?)sourceField.Attribute("Type") == "FlowConnector";
    }

    private static bool IsInitialStatusAdrenalineBranchNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "IsOwnerKeywordActiveAndEmitMessageBranchNode"
            || ThresholdNumericContract.AdrenalineUses(graph).Count == 0) return false;
        var fields = new (string Name, string Type, string? Argument)[]
        {
            ("FlowIn", "FlowConnector", null), ("FlowOutTrue", "FlowConnector", null),
            ("FlowOutFalse", "FlowConnector", null), ("Keyword", "EnumVar`1", "ECardKeyword"),
            ("EmitKeywordMessageIfTrue", "BoolVar", null), ("EmitKeywordMessageIfFalse", "BoolVar", null)
        };
        if (node.Elements().Count() != fields.Length || fields.Any(field => node.Elements(field.Name).Count() != 1
            || node.Element(field.Name)!.HasElements
            || (string?)node.Element(field.Name)!.Attribute("Type") != field.Type
            || (string?)node.Element(field.Name)!.Attribute("GArg") != field.Argument)) return false;
        var keyword = node.Element("Keyword")!;
        if ((string?)keyword.Attribute("V") != "1" || !bool.TryParse((string?)node.Element("EmitKeywordMessageIfTrue")!.Attribute("V"), out _)
            || !bool.TryParse((string?)node.Element("EmitKeywordMessageIfFalse")!.Attribute("V"), out _)) return false;
        // The keyword and message flags must be static. Only the flow input may be connected.
        foreach (string name in new[] { "Keyword", "EmitKeywordMessageIfTrue", "EmitKeywordMessageIfFalse" })
        {
            var field = node.Element(name)!;
            if (new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase))
                || graph.Element("Connections")?.Elements().Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id")) == true)
                return false;
        }
        var input = node.Element("FlowIn")!;
        var incoming = graph.Element("Connections")?.Elements().Where(c => (int?)c.Attribute("DstId") == (int?)input.Attribute("Id")).ToArray() ?? [];
        return incoming.Length > 0 && incoming.All(c => graph.Descendants().Any(e => (int?)e.Attribute("Id") == (int?)c.Attribute("SrcId")
            && (string?)e.Attribute("Type") == "FlowConnector"));
    }

    private static bool IsInitialStatusExcludedOwnerActionNode(XElement node, XElement graph)
    {
        string actionType = (string?)node.Attribute("Type") ?? "";
        if (actionType is not ("TransformCardsNode" or "ResetCardsNode")) return false;
        var transformFields = new (string Name, string Type, string? Argument)[]
        {
            ("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null), ("Attacker", "EntityVar", null),
            ("Targets", "CardListVar", null), ("ElementType", "EnumVar`1", "EElementType"),
            ("AttackMethod", "EnumVar`1", "EAttackMethod"), ("EffectType", "EnumVar`1", "EEffectType"),
            ("ResetMode", "EnumVar`1", "EResetMode"), ("Definition", "CardDefinitionVar", null)
        };
        var resetFields = transformFields[..^1];
        if (!HasFields(node, actionType == "TransformCardsNode" ? transformFields : resetFields)) return false;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];

        XElement? Producer(XElement field, out string port)
        {
            port = "";
            var incoming = graph.Element("Connections")?.Elements().Where(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray() ?? [];
            if (incoming.Length != 1) return null;
            int sourceId = (int?)incoming[0].Attribute("SrcId") ?? -1;
            var sourceField = graph.Element("Nodes")?.Elements().SelectMany(n => n.Elements().Select(p => (Node: n, Field: p)))
                .FirstOrDefault(p => (int?)p.Field.Attribute("Id") == sourceId);
            if (sourceField is not { } source) return null;
            port = source.Field.Name.LocalName;
            return source.Node;
        }

        bool TryFixedInt(XElement field, out int value)
        {
            value = 0;
            if (int.TryParse((string?)field.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                && !edges.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id"))) return true;
            var source = Producer(field, out string port);
            if (source == null || (string?)source.Attribute("Type") != "GetVarNode" || port != "Out"
                || !int.TryParse((string?)source.Attribute("VarId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0
                || !HasFields(source, [("Out", "IntVar", null)])
                || string.Equals((string?)source.Element("Out")!.Attribute("ExternalOut"), "True", StringComparison.OrdinalIgnoreCase)) return false;
            var variables = new[] { "TemporaryVariables", "PersistentVariables" }.SelectMany(name => graph.Element(name)?.Elements() ?? [])
                .Where(v => (int?)v.Attribute("Id") == id && (string?)v.Attribute("Type") == "IntVar").ToArray();
            if (variables.Length != 1 || !int.TryParse((string?)variables[0].Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                || new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)variables[0].Attribute(a), "True", StringComparison.OrdinalIgnoreCase))
                || (graph.Element("Nodes")?.Elements() ?? []).Any(n => (string?)n.Attribute("Type") == "SetVarNode"
                    && (int?)n.Attribute("VarId") == id)) return false;
            return true;
        }

        var request = Producer(node.Element("Targets")!, out string targetPort);
        if (actionType == "ResetCardsNode" && request != null && (string?)request.Attribute("Type") == "GetVarNode"
            && targetPort == "Out" && request.Elements().Count() == 1 && request.Element("Out") is { } listOutput
            && (string?)listOutput.Attribute("Type") == "CardListVar" && listOutput.Attribute("GArg") == null
            && int.TryParse((string?)request.Attribute("VarId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int listVariableId)
            && listVariableId > 0)
        {
            var listVariables = new[] { "TemporaryVariables", "PersistentVariables" }
                .SelectMany(name => graph.Element(name)?.Elements() ?? [])
                .Where(v => (int?)v.Attribute("Id") == listVariableId && (string?)v.Attribute("Type") == "CardListVar").ToArray();
            var writers = graph.Element("Nodes")?.Elements().Where(n => (string?)n.Attribute("Type") == "SetVarNode"
                && (int?)n.Attribute("VarId") == listVariableId).ToArray() ?? [];
            bool variableValid = listVariables.Length == 1 && listVariables[0].Attribute("V") == null
                && !new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)listVariables[0].Attribute(a), "True", StringComparison.OrdinalIgnoreCase))
                && writers.Length == 1 && IsInitialStatusVariableNode(writers[0], graph);
            bool ownerExcludedPool = variableValid && OwnerExcludedTargetPool(writers[0].Element("In")!, []);
            return variableValid && ownerExcludedPool;
        }
        if (request == null || (string?)request.Attribute("Type") != "RequestCardTargetsNode" || targetPort != "SelectedTargets")
            return false;
        var requestFields = new (string Name, string Type, string? Argument)[]
        {
            ("FlowIn", "FlowConnector", null), ("FlowOutSuccess", "FlowConnector", null), ("FlowOutFailure", "FlowConnector", null),
            ("MinTargets", "IntVar", null), ("MaxTargets", "IntVar", null), ("Shape", "ShapeVar", null),
            ("AIHint", "SemanticTagIdVar", null), ("ValidTargets", "CardListVar", null),
            ("SelectedTargets", "CardListVar", null), ("TargetsInShape", "CardListVar", null)
        };
        if (!HasFields(request, requestFields)) return false;
        if (edges.Count(c => (int?)c.Attribute("SrcId") == (int?)request.Element("FlowOutSuccess")!.Attribute("Id")
            && (int?)c.Attribute("DstId") == (int?)node.Element("FlowIn")!.Attribute("Id")) != 1) return false;
        foreach (string name in new[] { "MinTargets", "MaxTargets", "Shape", "AIHint" })
        {
            var field = request.Element(name)!;
            if (new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase))) return false;
            if (name is "MinTargets" or "MaxTargets")
            {
                if (!TryFixedInt(field, out _)) return false;
            }
            else if (edges.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id"))) return false;
        }
        if (!TryFixedInt(request.Element("MinTargets")!, out int min) || min < 0
            || !TryFixedInt(request.Element("MaxTargets")!, out int max) || max < min) return false;

        bool OwnerExcludedTargetPool(XElement list, HashSet<int> visited)
        {
            if (!int.TryParse((string?)list.Attribute("Id"), out int listId) || !visited.Add(listId)) return false;
            var source = Producer(list, out string outputPort);
            if (source == null) return false;
            if ((string?)source.Attribute("Type") == "GetCardsNode" && outputPort == "Out")
            {
                var queryFields = new (string Name, string Type, string? Argument)[]
                {
                    ("Out", "CardListVar", null), ("PlayerId", "PlayerIdVar", null), ("LocationId", "ELocationVar", null),
                    ("CardTypes", "EnumVar`1", "ECardType"), ("CardTiers", "EnumVar`1", "ECardTier"),
                    ("Ignore", "CardTokensVar", null), ("WithinRangeOf", "CardVar", null), ("IgnoreOwner", "BoolVar", null)
                };
                string cardTypes = (string?)source.Element("CardTypes")!.Attribute("V") ?? "";
                string ignoredTokens = (string?)source.Element("Ignore")!.Attribute("V") ?? "";
                bool supportedQuery = actionType == "TransformCardsNode"
                    ? cardTypes == "4" && ignoredTokens == "264"
                    : cardTypes is "4" or "8" && ignoredTokens is "0" or "8" or "264";
                if (!HasFields(source, queryFields) || (string?)source.Element("LocationId")!.Attribute("V") is not ("3" or "16")
                    || !supportedQuery || (string?)source.Element("CardTiers")!.Attribute("V") != "10"
                    || (string?)source.Element("IgnoreOwner")!.Attribute("V") != "True"
                    || source.Element("WithinRangeOf")!.Attribute("Id") == null) return false;
                var player = source.Element("PlayerId")!;
                var ownerPlayer = Producer(player, out string playerPort);
                bool fixedOwnerPlayer = ownerPlayer == null && (string?)player.Attribute("V") == "3"
                    && !edges.Any(c => (int?)c.Attribute("DstId") == (int?)player.Attribute("Id"));
                bool ownerPlayerNode = ownerPlayer != null && (string?)ownerPlayer.Attribute("Type") == "GetOwnerPlayerIdNode"
                    && playerPort == "Out" && HasFields(ownerPlayer, [("Out", "PlayerIdVar", null)]);
                if (!fixedOwnerPlayer && !ownerPlayerNode) return false;
                foreach (string name in new[] { "LocationId", "CardTypes", "CardTiers", "Ignore", "IgnoreOwner", "WithinRangeOf" })
                    if (edges.Any(c => (int?)c.Attribute("DstId") == (int?)source.Element(name)!.Attribute("Id"))) return false;
                return true;
            }
            if ((string?)source.Attribute("Type") != "FilterCardListNode") return false;
            var filterFields = new (string Name, string Type, string? Argument)[]
            {
                ("FlowIn", "FlowConnector", null), ("FlowSuccess", "FlowConnector", null), ("FlowFail", "FlowConnector", null),
                ("Cards", "CardListVar", null), ("FilteredCards", "CardListVar", null), ("NotFilteredCards", "CardListVar", null)
            };
            if (source.Elements().Count() != filterFields.Length + 1 || source.Elements("Filters").Count() != 1
                || filterFields.Any(field => source.Elements(field.Name).Count() != 1
                    || (string?)source.Element(field.Name)!.Attribute("Type") != field.Type
                    || (string?)source.Element(field.Name)!.Attribute("GArg") != field.Argument)) return false;
            return outputPort is "FilteredCards" or "NotFilteredCards"
                && OwnerExcludedTargetPool(source.Element("Cards")!, visited);
        }

        return OwnerExcludedTargetPool(request.Element("ValidTargets")!, [])
            && !edges.Any(c => (int?)c.Attribute("DstId") == (int?)request.Element("SelectedTargets")!.Attribute("Id"))
            && !edges.Any(c => (int?)c.Attribute("DstId") == (int?)request.Element("TargetsInShape")!.Attribute("Id"));

        static bool HasFields(XElement node, (string Name, string Type, string? Argument)[] fields)
            => node.Elements().Count() == fields.Length && fields.All(field => node.Elements(field.Name).Count() == 1
                && (string?)node.Element(field.Name)!.Attribute("Type") == field.Type
                && (string?)node.Element(field.Name)!.Attribute("GArg") == field.Argument);
    }

    private static bool IsInitialStatusChoiceNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "RequestCardChoiceNode") return false;
        var schema = InitialStatusChoiceSchemas.FirstOrDefault(fields => node.Elements().Count() == fields.Length
            && fields.All(field => node.Elements(field.Name).Count() == 1
                && (string?)node.Element(field.Name)!.Attribute("Type") == field.Type
                && (string?)node.Element(field.Name)!.Attribute("GArg") == field.Argument));
        if (schema == null) return false;

        // Choice nodes only present a selection; accept the known card-choice enum value and static AI hint.
        var choiceType = node.Element("ChoiceType")!;
        if ((string?)choiceType.Attribute("V") != "0"
            || graph.Element("Connections")?.Elements().Any(c => (int?)c.Attribute("DstId") == (int?)choiceType.Attribute("Id")) == true)
            return false;

        foreach (var field in schema.Where(f => f.Name != "AIHint"))
        {
            var port = node.Element(field.Name)!;
            if (port.HasElements) return false;
            if (field.Name is "MinChoices" or "MaxChoices")
            {
                if (!int.TryParse((string?)port.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
            }
            else if (field.Type == "BoolVar" && !bool.TryParse((string?)port.Attribute("V"), out _)) return false;
            else if (field.Type is "IntListVar" or "CardListVar")
            {
                if (port.Attribute("V") != null) return false;
            }
        }

        var aiHint = node.Element("AIHint")!;
        var hintValue = aiHint.Elements().SingleOrDefault();
        if (aiHint.Elements().Count() != 1 || hintValue?.Name.LocalName != "V"
            || (string?)hintValue.Attribute("Type") != "CardSemanticTag" || hintValue.Elements().Count() != 3
            || !new[] { "e0", "e1", "e2" }.All(name => hintValue.Elements(name).Count() == 1
                && hintValue.Element(name)!.Elements().Count() == 0
                && ulong.TryParse((string?)hintValue.Element(name)!.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            return false;

        return !aiHint.DescendantsAndSelf().Attributes("Id").Any(a =>
            graph.Element("Connections")?.Elements().Any(c => (int?)c.Attribute("DstId") == (int?)a) == true);
    }

    private static bool IsInitialStatusRevealNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "RevealCardsNode") return false;
        (string Name, string Type, string? Argument)[] fields =
        [
            ("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null), ("PlayerId", "PlayerIdVar", null),
            ("Num", "IntVar", null), ("Random", "BoolVar", null), ("ValidCards", "CardListVar", null),
            ("Filters", "", null), ("PostReveal", "EnumVar`1", "EPostRevealAction"), ("RevealedCards", "CardListVar", null)
        ];
        if (node.Elements().Count() != fields.Length) return false;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        foreach (var field in fields.Where(f => f.Name != "Filters"))
        {
            var port = node.Element(field.Name);
            if (port == null || node.Elements(field.Name).Count() != 1 || port.HasElements
                || (string?)port.Attribute("Type") != field.Type || (string?)port.Attribute("GArg") != field.Argument
                || (int?)port.Attribute("Id") is not > 0) return false;
            if (field.Name == "Random")
            {
                if (!bool.TryParse((string?)port.Attribute("V"), out _)
                    || edges.Any(c => (int?)c.Attribute("DstId") == (int?)port.Attribute("Id"))) return false;
            }
            else if (field.Name == "PostReveal")
            {
                if (!int.TryParse((string?)port.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int action)
                    || action is not (0 or 1) || edges.Any(c => (int?)c.Attribute("DstId") == (int?)port.Attribute("Id"))) return false;
            }
            else if ((field.Name is "PlayerId" or "Num") && port.Attribute("V") != null
                && !int.TryParse((string?)port.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
        }

        var filters = node.Element("Filters");
        if (filters == null || node.Elements("Filters").Count() != 1 || filters.HasAttributes || filters.Elements().Count() != 1) return false;
        var emptyFilter = filters.Elements().Single();
        if (emptyFilter.Name.LocalName != "e0" || emptyFilter.HasElements
            || (string?)emptyFilter.Attribute("Type") != "CardFilterVar" || (string?)emptyFilter.Attribute("V") != "False"
            || emptyFilter.Attribute("GArg") != null || (int?)emptyFilter.Attribute("Id") is not > 0
            || (string?)emptyFilter.Attribute("ExternalIn") != "False" || (string?)emptyFilter.Attribute("ExternalOut") != "False"
            || (string?)emptyFilter.Attribute("ResetInInactive") != "False"
            || edges.Any(c => (int?)c.Attribute("DstId") == (int?)emptyFilter.Attribute("Id"))) return false;
        return true;
    }

    private static bool IsInitialStatusKilledTriggerNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "KilledTrigger") return false;
        (string Name, string Type, string? Argument)[] fields =
        [
            ("Priority", "EnumVar`1", "ETriggerPriority"), ("CardKilled", "CardVar", null), ("Forced", "BoolVar", null),
            ("HasDoomedStatus", "BoolVar", null), ("FlowOutDeath", "FlowConnector", null), ("FlowOutBanish", "FlowConnector", null)
        ];
        if (node.Elements().Count() != fields.Length) return false;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        foreach (var field in fields)
        {
            var port = node.Element(field.Name);
            if (port == null || node.Elements(field.Name).Count() != 1 || port.HasElements
                || (string?)port.Attribute("Type") != field.Type || (string?)port.Attribute("GArg") != field.Argument
                || (int?)port.Attribute("Id") is not > 0) return false;
            if (field.Name == "Priority")
            {
                if ((string?)port.Attribute("V") != "0"
                    || edges.Any(c => (int?)c.Attribute("DstId") == (int?)port.Attribute("Id"))) return false;
            }
            else if (field.Name is "Forced" or "HasDoomedStatus")
            {
                if (!bool.TryParse((string?)port.Attribute("V"), out _)
                    || edges.Any(c => (int?)c.Attribute("DstId") == (int?)port.Attribute("Id"))) return false;
            }
            else if (field.Name == "CardKilled" && port.Attribute("V") != null) return false;
            else if (field.Type == "FlowConnector" && port.Attribute("V") != null) return false;
        }
        return true;
    }

    private static bool KilledTriggerDependsOnDoomed(XElement node, XElement graph)
    {
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        int hasDoomedId = (int)node.Element("HasDoomedStatus")!.Attribute("Id")!;
        if (edges.Any(c => (int?)c.Attribute("SrcId") == hasDoomedId)) return true;

        int deathId = (int)node.Element("FlowOutDeath")!.Attribute("Id")!;
        int banishId = (int)node.Element("FlowOutBanish")!.Attribute("Id")!;
        int[] deathTargets = edges.Where(c => (int?)c.Attribute("SrcId") == deathId)
            .Select(c => (int)c.Attribute("DstId")!).Order().ToArray();
        int[] banishTargets = edges.Where(c => (int?)c.Attribute("SrcId") == banishId)
            .Select(c => (int)c.Attribute("DstId")!).Order().ToArray();
        return !deathTargets.SequenceEqual(banishTargets);
    }

    private static bool IsInitialStatusBlackboardReadNode(XElement node)
    {
        if ((string?)node.Attribute("Type") != "GetBlackboardVarNode"
            || !int.TryParse((string?)node.Attribute("VarId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int variableId)
            || variableId <= 0 || node.Elements().Count() != 2
            || node.Elements("BlackboardType").Count() != 1 || node.Elements("Out").Count() != 1)
            return false;

        var boardType = node.Element("BlackboardType")!;
        var output = node.Element("Out")!;
        // Initial-status editing may pass a typed scalar blackboard read through an otherwise verified graph.
        // Lists, card references, status masks, and unknown blackboard enum values remain blocked.
        return boardType.Elements().Count() == 1 && boardType.Elements("VarType").Count() == 1
            && !boardType.Element("VarType")!.HasAttributes && !boardType.Element("VarType")!.HasElements
            && (string?)boardType.Element("VarType") == "IntVar"
            && (string?)boardType.Attribute("Type") == "BlackboardVar"
            && (string?)boardType.Attribute("V") == "2"
            && int.TryParse((string?)boardType.Attribute("Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int boardFieldId)
            && boardFieldId > 0
            && (string?)output.Attribute("Type") == "IntVar" && output.Attribute("GArg") == null
            && !output.HasElements
            && int.TryParse((string?)output.Attribute("Id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int outputId)
            && outputId > 0
            && int.TryParse((string?)output.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsInitialStatusKilledThisTurnReadNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "GetBlackboardVarNode" || (int?)node.Attribute("VarId") != 34
            || node.Elements().Count() != 2 || node.Elements("BlackboardType").Count() != 1
            || node.Elements("Out").Count() != 1) return false;
        var boardType = node.Element("BlackboardType")!;
        var output = node.Element("Out")!;
        if ((string?)boardType.Attribute("Type") != "BlackboardVar" || (string?)boardType.Attribute("V") != "2"
            || (int?)boardType.Attribute("Id") is not > 0 || boardType.Elements().Count() != 1
            || boardType.Elements("VarType").Count() != 1 || boardType.Element("VarType")!.HasAttributes
            || boardType.Element("VarType")!.HasElements || (string?)boardType.Element("VarType") != "BoolVar"
            || (string?)output.Attribute("Type") != "BoolVar" || output.Attribute("GArg") != null
            || (string?)output.Attribute("V") != "False"
            || (int?)output.Attribute("Id") is not > 0 || output.HasElements
            || new[] { boardType, output }.Any(field => new[] { "ExternalIn", "ExternalOut" }
                .Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase)))) return false;

        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        if (links.Any(c => (int?)c.Attribute("DstId") == (int?)boardType.Attribute("Id")
            || (int?)c.Attribute("DstId") == (int?)output.Attribute("Id"))) return false;
        var consumers = links.Where(c => (int?)c.Attribute("SrcId") == (int?)output.Attribute("Id")).ToArray();
        if (consumers.Length != 1) return false;
        int destination = (int?)consumers[0].Attribute("DstId") ?? -1;
        var branch = graph.Element("Nodes")?.Elements().FirstOrDefault(n => n.Elements().Any(f => (int?)f.Attribute("Id") == destination));
        var condition = branch?.Elements().FirstOrDefault(f => (int?)f.Attribute("Id") == destination);
        // This exact GOG read is the BoolVar BB_KilledThisTurn (Blackboard 2, variable 34),
        // used only as a BranchNode condition. Other BoolVar and non-scalar reads remain blocked.
        return branch != null && (string?)branch.Attribute("Type") == "BranchNode"
            && condition?.Name.LocalName == "Condition" && (string?)condition.Attribute("Type") == "BoolVar";
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
                && !IsInitialStatusSchemaNode(n) && !IsInitialStatusBlackboardReadNode(n)
                && !IsInitialStatusKilledThisTurnReadNode(n, graph)
                && !IsInitialStatusProphetLockWriteNode(n, graph)
                && !IsInitialStatusCurrentPlayerIdNode(n, graph)
                && !IsInitialStatusCardLocationReadNode(n, graph)
                && !IsInitialStatusWoundedReadNode(n, graph)
                && !IsInitialStatusProvisionFilterNode(n, graph)
                && !IsInitialStatusAdrenalineBranchNode(n, graph)
                && !IsInitialStatusExcludedOwnerActionNode(n, graph)
                && !IsInitialStatusChoiceNode(n, graph)
                && !IsInitialStatusRevealNode(n, graph)
                && !IsInitialStatusKilledTriggerNode(n, graph)
                && !IsInitialStatusVariableNode(n, graph)
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
        if ((graph.Element("Nodes")?.Elements() ?? []).Where(n => (string?)n.Attribute("Type") == "KilledTrigger")
            .Any(n => KilledTriggerDependsOnDoomed(n, graph))) dependencies |= InitialStatusMasks["doomed"];
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
