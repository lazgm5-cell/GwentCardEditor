using AbilityAnalyzer.Models;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class AbilityGraphComposer
{
    public static CompositionResult Compose(
        AbilityDocument document,
        ModPackOperationRecipe operation)
    {
        if (!operation.Type.Equals("ComposeAbility", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Операция не является ComposeAbility.");
        if (operation.TargetCardAbilityId <= 0)
            throw new InvalidOperationException("Не указан targetCardAbilityId.");
        if (operation.Fragments.Count == 0)
            throw new InvalidOperationException("ComposeAbility не содержит fragments.");

        XElement target = document.FindRequiredCardAbility(operation.TargetCardAbilityId);

        // Источники снимаются до очистки цели: цель тоже может быть источником.
        Dictionary<string, XElement> sourceSnapshots = operation.Fragments.ToDictionary(
            fragment => fragment.Name,
            fragment => new XElement(document.FindRequiredCardAbility(fragment.SourceCardAbilityId)),
            StringComparer.OrdinalIgnoreCase);

        XElement nodes = target.Element("Nodes")
            ?? throw new InvalidDataException("Target Ability не содержит Nodes.");
        XElement connections = target.Element("Connections")
            ?? throw new InvalidDataException("Target Ability не содержит Connections.");
        XElement temporary = target.Element("TemporaryVariables")
            ?? throw new InvalidDataException("Target Ability не содержит TemporaryVariables.");
        XElement persistent = target.Element("PersistentVariables")
            ?? throw new InvalidDataException("Target Ability не содержит PersistentVariables.");

        nodes.RemoveNodes();
        connections.RemoveNodes();
        if (operation.ClearTemporaryVariables)
            temporary.RemoveNodes();
        if (operation.ClearPersistentVariables)
            persistent.RemoveNodes();

        int nextId = Math.Max(1, ParseInt((string?)target.Attribute("NId"), "target NId"));
        var occupiedIds = new HashSet<int>();
        var fragmentIdMaps = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        var result = new CompositionResult { TargetCardAbilityId = operation.TargetCardAbilityId };
        int nodeIndex = 0;
        int connectionIndex = 0;

        foreach (ComposeAbilityFragmentRecipe fragment in operation.Fragments)
        {
            ValidateFragmentName(fragment, fragmentIdMaps);
            XElement source = sourceSnapshots[fragment.Name];
            XElement sourceNodes = source.Element("Nodes")
                ?? throw new InvalidDataException($"Источник {fragment.SourceCardAbilityId} не содержит Nodes.");
            XElement sourceConnections = source.Element("Connections")
                ?? throw new InvalidDataException($"Источник {fragment.SourceCardAbilityId} не содержит Connections.");

            HashSet<int> requested = fragment.NodeIds.ToHashSet();
            List<XElement> selectedSourceNodes = sourceNodes.Elements()
                .Where(node => requested.Contains(ParseInt((string?)node.Attribute("Id"), "node Id")))
                .ToList();
            HashSet<int> found = selectedSourceNodes
                .Select(node => ParseInt((string?)node.Attribute("Id"), "node Id"))
                .ToHashSet();
            int[] missing = requested.Except(found).OrderBy(value => value).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Fragment '{fragment.Name}': в CardAbility {fragment.SourceCardAbilityId} " +
                    $"не найдены Node Id: {string.Join(", ", missing)}.");
            }

            HashSet<int> selectedEndpointIds = selectedSourceNodes
                .SelectMany(node => node.DescendantsAndSelf())
                .Select(element => ParseNullableInt((string?)element.Attribute("Id")))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToHashSet();

            List<BoundaryIssue> boundaryIssues = AnalyzeIncomingBoundary(
                operation,
                fragment,
                sourceNodes,
                sourceConnections,
                selectedEndpointIds);
            result.BoundaryIssues.AddRange(boundaryIssues);

            List<BoundaryIssue> unresolved = boundaryIssues.Where(issue => !issue.Resolved).ToList();
            if (operation.StrictBoundaryValidation && unresolved.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Fragment '{fragment.Name}' потерял входящие связи:" + Environment.NewLine +
                    string.Join(Environment.NewLine, unresolved.Select(issue =>
                        $"  {issue.SourceEndpointId} -> {issue.TargetEndpointId} ({issue.TargetField})")) +
                    Environment.NewLine +
                    "Добавьте отсутствующий Node в fragment, создайте link, fragmentPatch " +
                    "или явно укажите allowDroppedIncomingEndpointIds.");
            }

            List<XElement> clonedNodes = selectedSourceNodes.Select(node => new XElement(node)).ToList();
            ApplyFragmentPatches(operation, fragment, clonedNodes, result);

            List<XElement> clonedConnections = sourceConnections.Elements()
                .Where(connection =>
                {
                    int src = ParseInt((string?)connection.Attribute("SrcId"), "SrcId");
                    int dst = ParseInt((string?)connection.Attribute("DstId"), "DstId");
                    return selectedEndpointIds.Contains(src) && selectedEndpointIds.Contains(dst);
                })
                .Select(connection => new XElement(connection))
                .ToList();

            var oldIds = clonedNodes
                .Concat(clonedConnections)
                .SelectMany(element => element.DescendantsAndSelf())
                .Select(element => ParseNullableInt((string?)element.Attribute("Id")))
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();
            int? duplicate = oldIds.GroupBy(value => value).FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicate.HasValue)
                throw new InvalidOperationException($"Fragment '{fragment.Name}' содержит повторяющийся Id={duplicate}.");

            var idMap = new Dictionary<int, int>();
            foreach (int oldId in oldIds)
            {
                while (occupiedIds.Contains(nextId))
                    nextId++;
                idMap[oldId] = nextId;
                occupiedIds.Add(nextId);
                nextId++;
            }

            HashSet<int> sourceVariableIds = GetSourceVariableIds(source);
            foreach (XElement clone in clonedNodes.Concat(clonedConnections))
            {
                RemapElement(clone, idMap, fragment.ExternalVariableMap, sourceVariableIds, fragment.Name);
                OffsetCoordinates(clone, fragment.XOffset, fragment.YOffset);
            }

            foreach (XElement clone in clonedNodes)
            {
                clone.Name = "e" + nodeIndex++;
                nodes.Add(clone);
                result.AddedNodeCount++;
            }
            foreach (XElement clone in clonedConnections)
            {
                clone.Name = "e" + connectionIndex++;
                connections.Add(clone);
                result.AddedConnectionCount++;
            }

            fragmentIdMaps[fragment.Name] = idMap;
        }

        foreach (ComposeAbilityLinkRecipe link in operation.Links)
        {
            Dictionary<int, int> sourceMap = GetFragmentMap(fragmentIdMaps, link.SourceFragment);
            Dictionary<int, int> targetMap = GetFragmentMap(fragmentIdMaps, link.TargetFragment);
            if (!sourceMap.TryGetValue(link.SourceEndpointId, out int sourceId))
                throw new InvalidOperationException(
                    $"Link: endpoint {link.SourceEndpointId} не найден во fragment '{link.SourceFragment}'.");
            if (!targetMap.TryGetValue(link.TargetEndpointId, out int targetId))
                throw new InvalidOperationException(
                    $"Link: endpoint {link.TargetEndpointId} не найден во fragment '{link.TargetFragment}'.");

            while (occupiedIds.Contains(nextId))
                nextId++;
            var connection = new XElement("e" + connectionIndex++,
                new XAttribute("Type", "Connection"),
                new XAttribute("Id", nextId),
                new XAttribute("SrcId", sourceId),
                new XAttribute("DstId", targetId));
            occupiedIds.Add(nextId);
            nextId++;
            connections.Add(connection);
            result.AddedConnectionCount++;
        }

        int maxId = target.Elements()
            .Where(element => element.Name.LocalName is
                "TemporaryVariables" or "PersistentVariables" or "Nodes" or "Connections")
            .SelectMany(element => element.DescendantsAndSelf())
            .Select(element => ParseNullableInt((string?)element.Attribute("Id")))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(0)
            .Max();
        target.SetAttributeValue("NId", Math.Max(nextId, maxId + 1));

        result.Validation = GraphValidator.Validate(target);
        if (!result.Validation.IsValid)
        {
            throw new InvalidOperationException(
                "Составленный граф INVALID:" + Environment.NewLine +
                string.Join(Environment.NewLine, result.Validation.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error)
                    .Select(issue => $"  {issue.Code}: {issue.Message}")));
        }

        return result;
    }

    private static List<BoundaryIssue> AnalyzeIncomingBoundary(
        ModPackOperationRecipe operation,
        ComposeAbilityFragmentRecipe fragment,
        XElement sourceNodes,
        XElement sourceConnections,
        HashSet<int> selectedEndpointIds)
    {
        Dictionary<int, XElement> endpointMap = sourceNodes
            .DescendantsAndSelf()
            .Where(element => element.Attribute("Id") != null)
            .ToDictionary(
                element => ParseInt((string?)element.Attribute("Id"), "endpoint Id"),
                element => element);

        var targetLinks = operation.Links
            .Where(link => link.TargetFragment.Equals(fragment.Name, StringComparison.OrdinalIgnoreCase))
            .Select(link => link.TargetEndpointId)
            .ToHashSet();

        var patchedEndpoints = new HashSet<int>();
        foreach (FragmentPatchRecipe patch in operation.FragmentPatches
                     .Where(patch => patch.Fragment.Equals(fragment.Name, StringComparison.OrdinalIgnoreCase)))
        {
            XElement? sourceNode = sourceNodes.Elements()
                .FirstOrDefault(node => (int?)node.Attribute("Id") == patch.SourceNodeId);
            XElement? field = ResolveField(sourceNode, patch.FieldName);
            int? fieldId = ParseNullableInt((string?)field?.Attribute("Id"));
            if (fieldId.HasValue)
                patchedEndpoints.Add(fieldId.Value);
        }

        var issues = new List<BoundaryIssue>();
        foreach (XElement connection in sourceConnections.Elements())
        {
            int src = ParseInt((string?)connection.Attribute("SrcId"), "SrcId");
            int dst = ParseInt((string?)connection.Attribute("DstId"), "DstId");
            if (!selectedEndpointIds.Contains(dst) || selectedEndpointIds.Contains(src))
                continue;

            bool resolvedByLink = targetLinks.Contains(dst);
            bool resolvedByPatch = patchedEndpoints.Contains(dst);
            bool explicitlyAllowed = fragment.AllowDroppedIncomingEndpointIds.Contains(dst);
            string resolution = resolvedByLink
                ? "explicit link"
                : resolvedByPatch
                    ? "fragmentPatch replaces the input"
                    : explicitlyAllowed
                        ? "allowDroppedIncomingEndpointIds"
                        : "unresolved";

            endpointMap.TryGetValue(dst, out XElement? targetElement);
            issues.Add(new BoundaryIssue
            {
                Fragment = fragment.Name,
                SourceEndpointId = src,
                TargetEndpointId = dst,
                TargetField = targetElement?.Name.LocalName ?? "?",
                Resolved = resolvedByLink || resolvedByPatch || explicitlyAllowed,
                Resolution = resolution
            });
        }
        return issues;
    }

    private static void ApplyFragmentPatches(
        ModPackOperationRecipe operation,
        ComposeAbilityFragmentRecipe fragment,
        List<XElement> clonedNodes,
        CompositionResult result)
    {
        foreach (FragmentPatchRecipe patch in operation.FragmentPatches
                     .Where(patch => patch.Fragment.Equals(fragment.Name, StringComparison.OrdinalIgnoreCase)))
        {
            XElement node = clonedNodes.FirstOrDefault(element => (int?)element.Attribute("Id") == patch.SourceNodeId)
                ?? throw new InvalidOperationException(
                    $"fragmentPatch: Node {patch.SourceNodeId} не найден во fragment '{fragment.Name}'.");
            XElement field = ResolveField(node, patch.FieldName)
                ?? throw new InvalidOperationException(
                    $"fragmentPatch: поле '{patch.FieldName}' не найдено в Node {patch.SourceNodeId}.");
            string oldValue = (string?)field.Attribute(patch.AttributeName) ?? "<missing>";
            field.SetAttributeValue(patch.AttributeName, patch.Value);
            result.AppliedPatches.Add(
                $"{fragment.Name}: Node {patch.SourceNodeId}/{patch.FieldName}/@{patch.AttributeName}: " +
                $"{oldValue} -> {patch.Value}");
        }
    }

    private static XElement? ResolveField(XElement? node, string path)
    {
        if (node == null || string.IsNullOrWhiteSpace(path))
            return null;
        XElement? current = node;
        foreach (string part in path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            current = current?.Element(part);
        return current;
    }

    private static void RemapElement(
        XElement root,
        IReadOnlyDictionary<int, int> idMap,
        IReadOnlyDictionary<int, int> externalVariableMap,
        HashSet<int> sourceVariableIds,
        string fragmentName)
    {
        foreach (XElement element in root.DescendantsAndSelf())
        {
            XAttribute? idAttribute = element.Attribute("Id");
            if (idAttribute != null)
            {
                int oldId = ParseInt(idAttribute.Value, "Id");
                if (idMap.TryGetValue(oldId, out int newId))
                    idAttribute.Value = newId.ToString();
            }

            foreach (string referenceName in new[] { "SrcId", "DstId", "VarId" })
            {
                XAttribute? reference = element.Attribute(referenceName);
                if (reference == null)
                    continue;
                int oldReference = ParseInt(reference.Value, referenceName);
                if (idMap.TryGetValue(oldReference, out int remapped))
                {
                    reference.Value = remapped.ToString();
                    continue;
                }
                if (referenceName == "VarId" && externalVariableMap.TryGetValue(oldReference, out int mappedVariable))
                {
                    reference.Value = mappedVariable.ToString();
                    continue;
                }
                if (referenceName == "VarId" && sourceVariableIds.Contains(oldReference))
                {
                    throw new InvalidOperationException(
                        $"Fragment '{fragmentName}' ссылается на внешнюю переменную VarId={oldReference}. " +
                        "Добавьте externalVariableMap или включите нужную переменную в целевую Ability.");
                }
            }
        }
    }

    private static HashSet<int> GetSourceVariableIds(XElement source)
    {
        return source.Elements()
            .Where(element => element.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
            .SelectMany(element => element.Elements())
            .Select(element => ParseNullableInt((string?)element.Attribute("Id")))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToHashSet();
    }

    private static void OffsetCoordinates(XElement root, int xOffset, int yOffset)
    {
        XAttribute? x = root.Attribute("X");
        XAttribute? y = root.Attribute("Y");
        if (x != null && int.TryParse(x.Value, out int xValue))
            x.Value = (xValue + xOffset).ToString();
        if (y != null && int.TryParse(y.Value, out int yValue))
            y.Value = (yValue + yOffset).ToString();
    }

    private static void ValidateFragmentName(
        ComposeAbilityFragmentRecipe fragment,
        Dictionary<string, Dictionary<int, int>> maps)
    {
        if (string.IsNullOrWhiteSpace(fragment.Name))
            throw new InvalidOperationException("Fragment name не может быть пустым.");
        if (maps.ContainsKey(fragment.Name))
            throw new InvalidOperationException($"Повторяющееся fragment name: {fragment.Name}");
    }

    private static Dictionary<int, int> GetFragmentMap(
        Dictionary<string, Dictionary<int, int>> maps,
        string name)
    {
        return maps.TryGetValue(name, out Dictionary<int, int>? map)
            ? map
            : throw new InvalidOperationException($"Fragment не найден: {name}");
    }

    private static int ParseInt(string? value, string context)
    {
        return int.TryParse(value, out int parsed)
            ? parsed
            : throw new InvalidDataException($"{context} не является целым числом: {value}");
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out int parsed) ? parsed : null;
}
