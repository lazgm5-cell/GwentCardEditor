using AbilityAnalyzer.Models;
using System.Text;

namespace AbilityAnalyzer.Services;

public sealed class AbilitySessionPreview
{
    public AbilityDocument Document { get; set; } = null!;
    public List<AbilityApplyResult> Results { get; } = new();
    public List<int> ChangedAbilityIds { get; } = new();
}

public static class AbilitySessionService
{
    public static AbilitySessionPreview BuildPreview(
        string sourceAbilitiesPath,
        IReadOnlyList<AbilitySessionItem> items)
    {
        AbilityDocument working = AbilityDocument.Load(sourceAbilitiesPath).DeepClone();
        var preview = new AbilitySessionPreview { Document = working };

        foreach (AbilitySessionItem item in items)
        {
            AbilityApplyResult result = AbilityModPackService.ApplyToDocument(working, item.Recipe);
            preview.Results.Add(result);
            foreach (int abilityId in result.ChangedAbilityIds)
            {
                if (!preview.ChangedAbilityIds.Contains(abilityId))
                    preview.ChangedAbilityIds.Add(abilityId);
            }
        }

        return preview;
    }

    public static (string OutputFile, string ReportFile) Save(
        string sourceAbilitiesPath,
        IReadOnlyList<AbilitySessionItem> items,
        string outputPath,
        bool overwrite)
    {
        if (items.Count == 0)
            throw new InvalidOperationException("Ability session пуста.");
        if (string.Equals(
                Path.GetFullPath(sourceAbilitiesPath),
                Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Исходный Abilities.xml нельзя перезаписывать.");
        }

        string fullOutput = Path.GetFullPath(outputPath);
        string reportPath = fullOutput + ".report.txt";
        if (!overwrite)
        {
            string[] existing = new[] { fullOutput, reportPath }.Where(File.Exists).ToArray();
            if (existing.Length > 0)
            {
                throw new IOException(
                    "Уже существуют выходные файлы:" + Environment.NewLine +
                    string.Join(Environment.NewLine, existing));
            }
        }

        AbilitySessionPreview preview = BuildPreview(sourceAbilitiesPath, items);
        preview.Document.SaveAs(fullOutput, overwrite);
        AbilityDocument saved = AbilityDocument.Load(fullOutput);
        foreach (int abilityId in preview.ChangedAbilityIds)
        {
            GraphValidationReport validation =
                GraphValidator.Validate(saved.FindRequiredCardAbility(abilityId));
            if (!validation.IsValid)
                throw new InvalidOperationException(
                    $"Сохранённый CardAbility {abilityId} INVALID.");
        }

        File.WriteAllText(
            reportPath,
            BuildReport(sourceAbilitiesPath, fullOutput, items, preview),
            Encoding.UTF8);
        return (fullOutput, reportPath);
    }

    private static string BuildReport(
        string input,
        string output,
        IReadOnlyList<AbilitySessionItem> items,
        AbilitySessionPreview preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ability edit session");
        builder.AppendLine($"Input: {Path.GetFullPath(input)}");
        builder.AppendLine($"Output: {Path.GetFullPath(output)}");
        builder.AppendLine($"Items: {items.Count}");
        builder.AppendLine($"Changed CardAbility: {string.Join(", ", preview.ChangedAbilityIds)}");
        List<int> repeated = preview.Results
            .SelectMany(result => result.ChangedAbilityIds)
            .GroupBy(abilityId => abilityId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (repeated.Count > 0)
            builder.AppendLine($"Conflicting CardAbility: {string.Join(", ", repeated)}");
        builder.AppendLine();

        for (int index = 0; index < items.Count; index++)
        {
            AbilitySessionItem item = items[index];
            AbilityApplyResult result = preview.Results[index];
            builder.AppendLine($"[{index + 1}] {item.DisplayName}");
            builder.AppendLine($"    Kind: {item.Kind}");
            builder.AppendLine($"    Source: {item.SourceFile}");
            builder.AppendLine($"    Changed: {string.Join(", ", result.ChangedAbilityIds)}");
            foreach (CompositionResult composition in result.Compositions)
            {
                builder.AppendLine(
                    $"    CardAbility {composition.TargetCardAbilityId}: " +
                    $"nodes={composition.AddedNodeCount}, " +
                    $"connections={composition.AddedConnectionCount}, " +
                    $"warnings={composition.Validation.WarningCount}");
            }
            builder.AppendLine();
        }
        return builder.ToString();
    }
}
