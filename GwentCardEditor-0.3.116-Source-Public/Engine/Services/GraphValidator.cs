using AbilityAnalyzer.Models;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class GraphValidator
{
    private static readonly Dictionary<string, string[]> RequiredConnectedFields =
        new(StringComparer.Ordinal)
        {
            ["MoveCardsNode"] = new[] { "Cards", "Position" },
            ["RequestCardChoiceNode"] = new[] { "ValidChoices" },
            ["CardListBranchNode"] = new[] { "List" },
            ["CardListForeachNode"] = new[] { "List" },
            ["LocationListGetRandomElementNode"] = new[] { "List" },
            ["ChangeLocationTokensNode"] = new[] { "Targets" },
            ["ChangePowerNode"] = new[] { "Targets" }
        };

    public static GraphValidationReport Validate(XElement ability)
    {
        var report = new GraphValidationReport();
        XElement? nodes = ability.Element("Nodes");
        XElement? connections = ability.Element("Connections");
        if (nodes == null || connections == null)
        {
            report.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "MISSING_CONTAINER",
                Message = "Ability не содержит Nodes или Connections."
            });
            return report;
        }

        IEnumerable<XElement> localScope = ability.Elements()
            .Where(element => element.Name.LocalName is
                "TemporaryVariables" or "PersistentVariables" or "Nodes" or "Connections")
            .SelectMany(element => element.DescendantsAndSelf());

        var idElements = localScope
            .Where(element => element.Attribute("Id") != null)
            .Select(element => new
            {
                Element = element,
                Id = ParseNullableInt((string?)element.Attribute("Id"))
            })
            .Where(item => item.Id.HasValue)
            .ToList();

        foreach (var duplicate in idElements.GroupBy(item => item.Id).Where(group => group.Count() > 1))
        {
            report.Issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Code = "DUPLICATE_ID",
                Message = $"Id={duplicate.Key} используется {duplicate.Count()} раз."
            });
        }

        Dictionary<int, XElement> endpointMap = nodes
            .DescendantsAndSelf()
            .Where(element => element.Attribute("Id") != null)
            .Select(element => new
            {
                Element = element,
                Id = ParseNullableInt((string?)element.Attribute("Id"))
            })
            .Where(item => item.Id.HasValue)
            .GroupBy(item => item.Id!.Value)
            .ToDictionary(group => group.Key, group => group.First().Element);

        Dictionary<int, XElement> nodeByEndpoint = BuildNodeOwnerMap(nodes);
        var destinationCounts = new Dictionary<int, int>();
        var incomingSources = new Dictionary<int, List<int>>();
        var flowEdges = new List<(int SourceNode, int TargetNode)>();

        foreach (XElement connection in connections.Elements())
        {
            int? src = ParseNullableInt((string?)connection.Attribute("SrcId"));
            int? dst = ParseNullableInt((string?)connection.Attribute("DstId"));
            if (!src.HasValue || !dst.HasValue)
            {
                Add(report, ValidationSeverity.Error, "INVALID_CONNECTION",
                    "Connection не содержит корректные SrcId/DstId.");
                continue;
            }

            endpointMap.TryGetValue(src.Value, out XElement? srcElement);
            endpointMap.TryGetValue(dst.Value, out XElement? dstElement);
            if (srcElement == null)
                Add(report, ValidationSeverity.Error, "BROKEN_SRC", $"SrcId={src} не существует в Nodes.");
            if (dstElement == null)
                Add(report, ValidationSeverity.Error, "BROKEN_DST", $"DstId={dst} не существует в Nodes.");

            if (srcElement != null && dstElement != null)
            {
                string srcType = (string?)srcElement.Attribute("Type") ?? "";
                string dstType = (string?)dstElement.Attribute("Type") ?? "";
                if (!AreCompatibleTypes(srcType, dstType))
                {
                    Add(report, ValidationSeverity.Warning, "TYPE_MISMATCH",
                        $"Connection {src}->{dst}: необычная пара типов {srcType} -> {dstType}.");
                }

                if (srcType == "FlowConnector" && dstType == "FlowConnector" &&
                    nodeByEndpoint.TryGetValue(src.Value, out XElement? sourceNode) &&
                    nodeByEndpoint.TryGetValue(dst.Value, out XElement? targetNode))
                {
                    int sourceNodeId = ParseNullableInt((string?)sourceNode.Attribute("Id")) ?? -1;
                    int targetNodeId = ParseNullableInt((string?)targetNode.Attribute("Id")) ?? -1;
                    if (sourceNodeId >= 0 && targetNodeId >= 0)
                        flowEdges.Add((sourceNodeId, targetNodeId));
                }
            }

            destinationCounts[dst.Value] = destinationCounts.GetValueOrDefault(dst.Value) + 1;
            if (!incomingSources.TryGetValue(dst.Value, out List<int>? sources))
            {
                sources = new List<int>();
                incomingSources[dst.Value] = sources;
            }
            sources.Add(src.Value);
        }

        foreach ((int destination, int count) in destinationCounts.Where(pair => pair.Value > 1))
        {
            string type = endpointMap.TryGetValue(destination, out XElement? endpoint)
                ? (string?)endpoint.Attribute("Type") ?? ""
                : "";

            // Native GWENT graphs frequently merge several valid flow branches into one
            // FlowIn connector (for example Deploy and Order sharing one target-selection
            // chain). This is a control-flow merge, not a malformed graph. Keep it visible
            // as a warning, but never block saving solely because of that pattern.
            Add(
                report,
                ValidationSeverity.Warning,
                type == "FlowConnector" ? "FLOW_MERGE" : "MULTIPLE_INPUTS",
                $"Вход Id={destination} ({type}) получает {count} соединения." +
                (type == "FlowConnector"
                    ? " Допустимое объединение ветвей; проверьте, что оно задумано."
                    : ""));
        }

        ValidateRequiredInputs(nodes, incomingSources, report);
        ValidateWeatherDuration(nodes, incomingSources, report);
        ValidateFlowReachability(nodes, incomingSources, report);
        ValidateFlowCycles(flowEdges, report);
        ValidateTriggerFeedback(nodes, report);

        int maxId = idElements.Select(item => item.Id!.Value).DefaultIfEmpty(0).Max();
        int nId = ParseNullableInt((string?)ability.Attribute("NId")) ?? 0;
        if (nId <= maxId)
        {
            Add(report, ValidationSeverity.Warning, "NID_TOO_SMALL",
                $"NId={nId} должен быть больше максимального локального Id={maxId}.");
        }

        return report;
    }

    private static void ValidateRequiredInputs(
        XElement nodes,
        IReadOnlyDictionary<int, List<int>> incoming,
        GraphValidationReport report)
    {
        foreach (XElement node in nodes.Elements())
        {
            string nodeType = (string?)node.Attribute("Type") ?? "";
            if (RequiredConnectedFields.TryGetValue(nodeType, out string[]? fields))
            {
                foreach (string fieldName in fields)
                {
                    XElement? field = node.Element(fieldName);
                    int? fieldId = ParseNullableInt((string?)field?.Attribute("Id"));
                    if (!fieldId.HasValue || incoming.ContainsKey(fieldId.Value))
                        continue;

                    Add(report, ValidationSeverity.Error, "REQUIRED_INPUT_UNCONNECTED",
                        $"{nodeType} Id={(string?)node.Attribute("Id")}: " +
                        $"обязательный вход {fieldName} Id={fieldId} не подключён.");
                }
            }

            if (nodeType == "MakeCardPositionNode")
                WarnUnconnectedField(node, "PlayerId", incoming, report, "CARD_POSITION_PLAYER_UNCONNECTED");
            if (nodeType == "DrawCardsNode")
                WarnUnconnectedField(node, "PlayerId", incoming, report, "DRAW_PLAYER_UNCONNECTED");
        }
    }

    private static void WarnUnconnectedField(
        XElement node,
        string fieldName,
        IReadOnlyDictionary<int, List<int>> incoming,
        GraphValidationReport report,
        string code)
    {
        XElement? field = node.Element(fieldName);
        int? id = ParseNullableInt((string?)field?.Attribute("Id"));
        if (id.HasValue && !incoming.ContainsKey(id.Value))
        {
            Add(report, ValidationSeverity.Warning, code,
                $"{(string?)node.Attribute("Type")} Id={(string?)node.Attribute("Id")}: " +
                $"{fieldName} Id={id} использует только локальное V=" +
                $"{(string?)field?.Attribute("V") ?? "<missing>"}.");
        }
    }

    private static void ValidateWeatherDuration(
        XElement nodes,
        IReadOnlyDictionary<int, List<int>> incoming,
        GraphValidationReport report)
    {
        foreach (XElement node in nodes.Elements()
                     .Where(node => (string?)node.Attribute("Type") == "ChangeLocationTokensNode"))
        {
            int tokens = ParseNullableInt((string?)node.Element("Tokens")?.Attribute("V")) ?? 0;
            int operation = ParseNullableInt((string?)node.Element("Operation")?.Attribute("V")) ?? -1;
            XElement? duration = node.Element("OverrideTime");
            int? durationId = ParseNullableInt((string?)duration?.Attribute("Id"));
            int durationValue = ParseNullableInt((string?)duration?.Attribute("V")) ?? 0;
            bool connected = durationId.HasValue && incoming.ContainsKey(durationId.Value);

            if (tokens != 0 && operation == 0 && durationValue <= 0 && !connected)
            {
                Add(report, ValidationSeverity.Warning, "ZERO_WEATHER_DURATION",
                    $"ChangeLocationTokensNode Id={(string?)node.Attribute("Id")}: " +
                    $"Tokens={tokens}, но OverrideTime={durationValue}. " +
                    "Эффект может оказаться постоянным или бесконечным.");
            }
        }
    }

    private static void ValidateFlowReachability(
        XElement nodes,
        IReadOnlyDictionary<int, List<int>> incoming,
        GraphValidationReport report)
    {
        foreach (XElement node in nodes.Elements())
        {
            XElement? flowIn = node.Elements()
                .FirstOrDefault(field => field.Name.LocalName.StartsWith("FlowIn", StringComparison.Ordinal));
            if (flowIn == null)
                continue;
            int? id = ParseNullableInt((string?)flowIn.Attribute("Id"));
            if (id.HasValue && !incoming.ContainsKey(id.Value))
            {
                Add(report, ValidationSeverity.Warning, "UNREACHABLE_FLOW_NODE",
                    $"{(string?)node.Attribute("Type")} Id={(string?)node.Attribute("Id")}: " +
                    $"FlowIn Id={id} не имеет входящего flow-соединения.");
            }
        }
    }

    private static void ValidateFlowCycles(
        IReadOnlyList<(int SourceNode, int TargetNode)> edges,
        GraphValidationReport report)
    {
        var adjacency = edges
            .GroupBy(edge => edge.SourceNode)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetNode).Distinct().ToList());
        var state = new Dictionary<int, int>();
        var stack = new Stack<int>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (int node in adjacency.Keys)
            Visit(node);

        void Visit(int node)
        {
            if (state.GetValueOrDefault(node) == 2)
                return;
            if (state.GetValueOrDefault(node) == 1)
                return;

            state[node] = 1;
            stack.Push(node);
            foreach (int next in adjacency.GetValueOrDefault(node) ?? new List<int>())
            {
                if (state.GetValueOrDefault(next) == 1)
                {
                    string cycle = string.Join(" -> ", stack.Reverse().SkipWhile(value => value != next).Append(next));
                    if (reported.Add(cycle))
                    {
                        Add(report, ValidationSeverity.Warning, "FLOW_CYCLE",
                            $"Обнаружен цикл flow: {cycle}. Проверьте риск бесконечного выполнения.");
                    }
                }
                else if (state.GetValueOrDefault(next) == 0)
                {
                    Visit(next);
                }
            }
            stack.Pop();
            state[node] = 2;
        }
    }

    private static void ValidateTriggerFeedback(XElement nodes, GraphValidationReport report)
    {
        bool hasAfterMoved = nodes.Elements()
            .Any(node => (string?)node.Attribute("Type") == "AfterMovedTrigger");
        bool hasMove = nodes.Elements()
            .Any(node => (string?)node.Attribute("Type") == "MoveCardsNode");
        if (hasAfterMoved && hasMove)
        {
            Add(report, ValidationSeverity.Warning, "MOVE_TRIGGER_FEEDBACK_RISK",
                "Ability одновременно содержит AfterMovedTrigger и MoveCardsNode. " +
                "Убедитесь, что собственное перемещение не запускает бесконечную рекурсию.");
        }
    }

    private static Dictionary<int, XElement> BuildNodeOwnerMap(XElement nodes)
    {
        var result = new Dictionary<int, XElement>();
        foreach (XElement node in nodes.Elements())
        {
            foreach (XElement endpoint in node.DescendantsAndSelf())
            {
                int? id = ParseNullableInt((string?)endpoint.Attribute("Id"));
                if (id.HasValue)
                    result[id.Value] = node;
            }
        }
        return result;
    }

    private static void Add(
        GraphValidationReport report,
        ValidationSeverity severity,
        string code,
        string message)
    {
        report.Issues.Add(new ValidationIssue
        {
            Severity = severity,
            Code = code,
            Message = message
        });
    }

    private static bool AreCompatibleTypes(string sourceType, string targetType)
    {
        if (string.Equals(sourceType, targetType, StringComparison.Ordinal))
            return true;

        return (sourceType, targetType) switch
        {
            ("CardVar", "EntityVar") => true,
            ("EntityVar", "CardVar") => true,
            ("LocationVar", "EntityVar") => true,
            ("CardPowerAttackVar", "CardAttackVar") => true,
            ("CardStatusAttackVar", "CardAttackVar") => true,
            ("CardTransformAttackVar", "CardAttackVar") => true,
            ("CardTimerAttackVar", "CardAttackVar") => true,
            ("CardStatusTimerAttackVar", "CardAttackVar") => true,
            ("CardResetAttackVar", "CardAttackVar") => true,
            _ => false
        };
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out int parsed) ? parsed : null;
}
