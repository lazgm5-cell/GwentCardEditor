using AbilityAnalyzer.Models;
using System.Text;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public sealed class AbilityModPackResult
{
    public string OutputFile { get; set; } = "";
    public string ReportFile { get; set; } = "";
    public List<CompositionResult> Compositions { get; } = new();
    public List<int> ChangedAbilityIds { get; } = new();
}

public static class AbilityModPackService
{
    public static AbilityModPackResult ApplyAndSave(
        string sourceAbilitiesPath,
        string recipePath,
        string? outputOverride,
        bool overwrite)
    {
        ModPackRecipe recipe = ModPackRecipeService.LoadAbilityRecipe(recipePath);
        return ApplyAndSave(sourceAbilitiesPath, recipe, outputOverride, overwrite, recipePath);
    }

    public static AbilityModPackResult ApplyAndSave(
        string sourceAbilitiesPath,
        ModPackRecipe recipe,
        string? outputOverride,
        bool overwrite,
        string? recipeSource = null)
    {
        if (recipe.FormatVersion != 1)
            throw new InvalidDataException($"Неподдерживаемый formatVersion={recipe.FormatVersion}.");

        AbilityDocument working = AbilityDocument.Load(sourceAbilitiesPath).DeepClone();
        AbilityApplyResult applied = ApplyToDocument(working, recipe);

        string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceAbilitiesPath))
            ?? AppContext.BaseDirectory;
        string outputPath = string.IsNullOrWhiteSpace(outputOverride)
            ? Path.Combine(sourceDirectory, recipe.OutputFileName)
            : outputOverride;
        string reportPath = Path.GetFullPath(outputPath) + ".report.txt";

        EnsureOutputIsSafe(sourceAbilitiesPath, outputPath);
        if (!overwrite)
        {
            string[] existing = new[] { Path.GetFullPath(outputPath), reportPath }
                .Where(File.Exists)
                .ToArray();
            if (existing.Length > 0)
            {
                throw new IOException(
                    "Уже существуют выходные файлы:" + Environment.NewLine +
                    string.Join(Environment.NewLine, existing));
            }
        }

        working.SaveAs(outputPath, overwrite);
        AbilityDocument saved = AbilityDocument.Load(outputPath);
        foreach (int abilityId in applied.ChangedAbilityIds)
        {
            GraphValidationReport savedReport =
                GraphValidator.Validate(saved.FindRequiredCardAbility(abilityId));
            if (!savedReport.IsValid)
                throw new InvalidOperationException(
                    $"Сохранённый граф CardAbility {abilityId} INVALID.");
        }

        File.WriteAllText(
            reportPath,
            BuildReport(recipe, sourceAbilitiesPath, outputPath, applied, recipeSource),
            Encoding.UTF8);

        var result = new AbilityModPackResult
        {
            OutputFile = Path.GetFullPath(outputPath),
            ReportFile = reportPath
        };
        result.Compositions.AddRange(applied.Compositions);
        result.ChangedAbilityIds.AddRange(applied.ChangedAbilityIds);
        return result;
    }

    /// <summary>
    /// Applies a complete mod-pack to an existing in-memory document. This is
    /// used by Ability edit sessions so several recipes can be accumulated and
    /// saved as one final Abilities.xml.
    /// </summary>
    public static AbilityApplyResult ApplyToDocument(
        AbilityDocument working,
        ModPackRecipe recipe)
    {
        if (recipe.FormatVersion != 1)
            throw new InvalidDataException($"Неподдерживаемый formatVersion={recipe.FormatVersion}.");

        var result = new AbilityApplyResult();
        foreach (ModPackOperationRecipe operation in recipe.Operations.Where(operation => operation.Enabled))
        {
            switch (operation.Type.Trim().ToLowerInvariant())
            {
                case "composeability":
                    result.Compositions.Add(AbilityGraphComposer.Compose(working, operation));
                    break;

                case "setnodevalue":
                    ApplySetNodeValue(working, operation);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Операция '{operation.Id}' имеет неподдерживаемый Type='{operation.Type}'.");
            }

            if (operation.TargetCardAbilityId > 0 &&
                !result.ChangedAbilityIds.Contains(operation.TargetCardAbilityId))
            {
                result.ChangedAbilityIds.Add(operation.TargetCardAbilityId);
            }
        }

        foreach (int abilityId in result.ChangedAbilityIds)
        {
            XElement ability = working.FindRequiredCardAbility(abilityId);
            GraphValidationReport validation = GraphValidator.Validate(ability);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(
                    $"CardAbility {abilityId} INVALID:" + Environment.NewLine +
                    string.Join(Environment.NewLine, validation.Issues
                        .Where(issue => issue.Severity == ValidationSeverity.Error)
                        .Select(issue => $"  {issue.Code}: {issue.Message}")));
            }
        }

        return result;
    }

    private static void ApplySetNodeValue(
        AbilityDocument document,
        ModPackOperationRecipe operation)
    {
        XElement ability = document.FindRequiredCardAbility(operation.TargetCardAbilityId);
        XElement nodes = ability.Element("Nodes")
            ?? throw new InvalidDataException("Ability не содержит Nodes.");

        XElement node;
        if (operation.NodeId.HasValue)
        {
            node = nodes.Elements().FirstOrDefault(
                    element => (int?)element.Attribute("Id") == operation.NodeId.Value)
                ?? throw new InvalidOperationException($"Node Id={operation.NodeId} не найден.");
        }
        else
        {
            List<XElement> matches = nodes.Elements()
                .Where(element => string.Equals(
                    (string?)element.Attribute("Type"),
                    operation.NodeType,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            int occurrence = operation.NodeOccurrence ?? 1;
            if (occurrence < 1 || occurrence > matches.Count)
            {
                throw new InvalidOperationException(
                    $"NodeType={operation.NodeType}, occurrence={occurrence} не найден.");
            }
            node = matches[occurrence - 1];
        }

        XElement? field = node;
        foreach (string part in operation.FieldName.Split(
                     '/',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            field = field?.Element(part);
        }
        if (field == null)
            throw new InvalidOperationException($"Поле '{operation.FieldName}' не найдено.");
        field.SetAttributeValue(operation.AttributeName, operation.Value);
    }

    private static void EnsureOutputIsSafe(string sourcePath, string outputPath)
    {
        if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(outputPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Исходный Abilities.xml нельзя перезаписывать. Выберите новый output.");
        }
    }

    private static string BuildReport(
        ModPackRecipe recipe,
        string input,
        string output,
        AbilityApplyResult result,
        string? recipeSource)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Ability mod-pack: {recipe.Name}");
        if (!string.IsNullOrWhiteSpace(recipeSource))
            builder.AppendLine($"Recipe: {Path.GetFullPath(recipeSource)}");
        builder.AppendLine($"Input: {Path.GetFullPath(input)}");
        builder.AppendLine($"Output: {Path.GetFullPath(output)}");
        builder.AppendLine($"Changed CardAbility: {string.Join(", ", result.ChangedAbilityIds)}");
        builder.AppendLine();

        foreach (CompositionResult composition in result.Compositions)
        {
            builder.AppendLine($"CardAbility {composition.TargetCardAbilityId}");
            builder.AppendLine($"  Nodes added: {composition.AddedNodeCount}");
            builder.AppendLine($"  Connections added: {composition.AddedConnectionCount}");
            builder.AppendLine($"  Graph valid: {composition.Validation.IsValid}");
            builder.AppendLine($"  Warnings: {composition.Validation.WarningCount}");
            builder.AppendLine("  Boundary inputs:");
            foreach (BoundaryIssue issue in composition.BoundaryIssues)
            {
                builder.AppendLine(
                    $"    {issue.Fragment}: {issue.SourceEndpointId}->{issue.TargetEndpointId} " +
                    $"({issue.TargetField}) = {(issue.Resolved ? issue.Resolution : "UNRESOLVED")}");
            }
            builder.AppendLine("  Fragment patches:");
            foreach (string patch in composition.AppliedPatches)
                builder.AppendLine("    " + patch);
            builder.AppendLine("  Validation:");
            foreach (ValidationIssue issue in composition.Validation.Issues)
                builder.AppendLine($"    [{issue.Severity}] {issue.Code}: {issue.Message}");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}
