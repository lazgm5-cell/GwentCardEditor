using AbilityAnalyzer.Models;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class TimingService
{
    private static readonly string[] Terms = { "cooldown", "timer", "duration", "charge", "overrideTime", "deltaTime" };

    private static readonly HashSet<string> KnownTimerFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "ChangeCardTimerNode/Value",
        "ChangeCardStatusTimerNode/Value",
        "BalanceCardStatusTimerNode/DeltaTime",
        "ChangeLocationTimerNode/Value",
        "ChangeTurnTimerNode/DurationInMs",
        "ChangeTurnTimerNode/ShownAtThresholdInMs",
        "ChangeMulliganTimerNode/DurationInMs",
        "ChangeMulliganTimerNode/ShownAtThresholdInMs",
        "FilterCardTimerNode/Timer",
        "ChangeLocationTokensNode/OverrideTime"
    };

    public static IReadOnlyList<TimingParameter> FindParameters(XElement ability)
    {
        HashSet<int> connectedDestinations = ability.Element("Connections")?
            .Elements()
            .Select(connection => ParseNullableInt((string?)connection.Attribute("DstId")))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToHashSet()
            ?? new HashSet<int>();

        var result = new List<TimingParameter>();
        foreach (XElement variable in ability.Descendants())
        {
            if (!string.Equals((string?)variable.Attribute("Type"), "IntVar", StringComparison.Ordinal))
                continue;
            string name = (string?)variable.Attribute("Name") ?? variable.Name.LocalName;
            bool explicitName = Terms.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase));
            string parentType = (string?)variable.Parent?.Attribute("Type") ?? "";
            bool knownField = Terms.Any(term => variable.Name.LocalName.Contains(term, StringComparison.OrdinalIgnoreCase))
                || KnownTimerFields.Contains(parentType + "/" + variable.Name.LocalName);
            if (!explicitName && !knownField)
                continue;
            if (!int.TryParse((string?)variable.Attribute("Id"), out int id))
                continue;
            if (!int.TryParse((string?)variable.Attribute("V"), out int value))
                continue;

            XElement? parent = variable.Parent;
            string path = parent?.Name.LocalName is "TemporaryVariables" or "PersistentVariables"
                ? $"{parent.Name.LocalName}/{variable.Name.LocalName}[Id={id},Name={name}]"
                : $"Nodes/{(string?)parent?.Attribute("Type") ?? parent?.Name.LocalName}[Id={(string?)parent?.Attribute("Id") ?? "?"}]/{variable.Name.LocalName}";
            result.Add(new TimingParameter
            {
                VariableId = id,
                Name = name,
                Path = path,
                Value = value,
                HasIncomingConnection = connectedDestinations.Contains(id)
            });
        }

        result = result
            .GroupBy(parameter => parameter.VariableId)
            .Select(group => group.First())
            .OrderBy(parameter => parameter.VariableId)
            .ToList();
        for (int index = 0; index < result.Count; index++)
            result[index].Index = index + 1;
        return result;
    }

    public static void PatchAndSave(
        string abilitiesPath,
        int cardAbilityId,
        int variableId,
        int newValue,
        string outputPath,
        bool overwrite)
    {
        AbilityDocument working = AbilityDocument.Load(abilitiesPath).DeepClone();
        XElement ability = working.FindRequiredCardAbility(cardAbilityId);
        XElement variable = ability.Descendants()
            .FirstOrDefault(element =>
                string.Equals((string?)element.Attribute("Type"), "IntVar", StringComparison.Ordinal)
                && (int?)element.Attribute("Id") == variableId)
            ?? throw new InvalidOperationException($"IntVar Id={variableId} не найдена.");
        variable.SetAttributeValue("V", newValue);

        GraphValidationReport report = GraphValidator.Validate(ability);
        if (!report.IsValid)
            throw new InvalidOperationException("После изменения граф INVALID.");
        working.SaveAs(outputPath, overwrite);
        AbilityDocument saved = AbilityDocument.Load(outputPath);
        if (!GraphValidator.Validate(saved.FindRequiredCardAbility(cardAbilityId)).IsValid)
            throw new InvalidOperationException("Сохранённый граф INVALID.");
    }

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(value, out int parsed) ? parsed : null;
}
