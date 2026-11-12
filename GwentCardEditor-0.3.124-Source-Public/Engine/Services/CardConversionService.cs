using AbilityAnalyzer.Models;
using System.Text;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class CardConversionService
{
    private const int SpecialType = 2;
    private const int UnitType = 4;
    private const int ArtifactType = 8;

    public static CardConversionPreview BuildPreview(
        string abilitiesPath,
        string templatesPath,
        string localizationPath,
        CardConversionRecipe recipe)
    {
        ValidateRecipe(recipe);

        TemplateDocument sourceTemplates = TemplateDocument.Load(templatesPath);
        CardRecord card = CardResolver.ResolveUnique(sourceTemplates, recipe.Card);
        TemplateDocument templates = sourceTemplates.DeepClone();
        AbilityDocument abilities = AbilityDocument.Load(abilitiesPath).DeepClone();

        XElement template = templates.FindById(card.Id)
            ?? throw new InvalidOperationException($"Template {card.Id} не найден.");
        XElement ability = abilities.FindRequiredCardAbility(card.Id);

        int sourceTypeValue = ParseRequiredInt(template.Element("Type"), "Type", card);
        string sourceType = CardTypeName(sourceTypeValue);
        string targetType = recipe.TargetType.Trim().ToLowerInvariant();

        if (!targetType.Equals("artifact", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Safe conversion 0.27.3 поддерживает targetType='artifact'. " +
                "Unit/Special будут добавлены после проверки первого преобразования.");
        }
        if (!recipe.Activation.Equals("order", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Safe conversion в Artifact пока поддерживает activation='order'.");
        if (!recipe.ReplaceTrigger.Equals("deploy", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("0.27.3 поддерживает replaceTrigger='deploy'.");
        if (!recipe.PreserveExistingEffect)
            throw new NotSupportedException("0.27.3 требует preserveExistingEffect=true.");
        if (sourceTypeValue != SpecialType)
        {
            throw new InvalidOperationException(
                $"Safe Special→Artifact conversion ожидала Type={SpecialType}, " +
                $"но карта {card.DebugName} имеет Type={sourceTypeValue} ({sourceType}).");
        }

        var changes = new List<string>();
        bool zealEnabled = ResolveZeal(recipe.Zeal, targetType, recipe.Activation);
        ApplyArtifactTemplate(template, card, recipe, zealEnabled, changes);
        ReplaceDeployWithOrder(ability, card, changes);

        GraphValidationReport validation = GraphValidator.Validate(ability);
        if (!validation.IsValid)
        {
            string details = string.Join(
                Environment.NewLine,
                validation.Issues
                    .Where(issue => issue.Severity == ValidationSeverity.Error)
                    .Select(issue => $"[{issue.Code}] {issue.Message}"));
            throw new InvalidOperationException(
                "После преобразования граф Ability недействителен:" +
                Environment.NewLine + details);
        }

        ValidateTemplateAbilityAgreement(template, ability, card, zealEnabled);

        string tooltipKey = $"{card.Id}_tooltip";
        string? existingTooltip = LocalizationService.ReadValue(localizationPath, tooltipKey);
        if (recipe.UpdateTooltip
            && string.IsNullOrWhiteSpace(recipe.Tooltip)
            && existingTooltip == null)
        {
            throw new InvalidOperationException(
                $"В локализации не найден ключ {tooltipKey}. " +
                "Укажите tooltip в conversion recipe или добавьте исходную строку.");
        }

        string newTooltip = recipe.UpdateTooltip
            ? (string.IsNullOrWhiteSpace(recipe.Tooltip)
                ? BuildOrderTooltip(existingTooltip!, zealEnabled)
                : recipe.Tooltip)
            : existingTooltip ?? "";
        if (recipe.UpdateTooltip)
        {
            changes.Add(
                $"{tooltipKey}: {existingTooltip ?? "<missing>"} -> {newTooltip}");
        }

        var preview = new CardConversionPreview
        {
            Card = card,
            SourceType = sourceType,
            TargetType = "Artifact",
            ZealEnabled = zealEnabled,
            TooltipKey = tooltipKey,
            TooltipValue = newTooltip,
            Abilities = abilities,
            Templates = templates,
            GraphValidation = validation
        };
        preview.Changes.AddRange(changes);

        foreach (ValidationIssue issue in validation.Issues
                     .Where(issue => issue.Severity == ValidationSeverity.Warning))
        {
            preview.Warnings.Add($"[{issue.Code}] {issue.Message}");
        }
        preview.Warnings.Add("Карта изменена из одноразовой Special в постоянный объект на поле.");
        if (zealEnabled)
            preview.Warnings.Add("Order получает Zeal автоматически, так как у Artifact нет Deploy.");

        return preview;
    }

    public static CardConversionResult ApplyAndSave(
        string abilitiesPath,
        string templatesPath,
        string localizationPath,
        CardConversionRecipe recipe,
        string outputDirectory,
        bool overwrite)
    {
        CardConversionPreview preview = BuildPreview(
            abilitiesPath,
            templatesPath,
            localizationPath,
            recipe);

        string fullOutput = Path.GetFullPath(outputDirectory.Trim().Trim('"'));
        if (Directory.Exists(fullOutput) && !overwrite)
            throw new IOException($"Выходной каталог уже существует: {fullOutput}");
        if (File.Exists(fullOutput))
            throw new IOException($"Путь занят файлом: {fullOutput}");

        string parent = Path.GetDirectoryName(fullOutput)
            ?? throw new InvalidOperationException("Не удалось определить родительский каталог.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(
            parent,
            "." + Path.GetFileName(fullOutput) + "." + Guid.NewGuid().ToString("N") + ".staging");

        string abilitiesOutput = Path.Combine(staging, "Abilities.modified.xml");
        string templatesOutput = Path.Combine(staging, "Templates.modified.xml");
        string localizationOutput = Path.Combine(staging, "ru-ru.modified.csv");
        string reportOutput = Path.Combine(staging, "conversion-report.txt");

        try
        {
            Directory.CreateDirectory(staging);
            preview.Abilities.SaveAs(abilitiesOutput, overwrite: false);
            preview.Templates.SaveAs(templatesOutput, overwrite: false);

            IReadOnlyDictionary<string, string> edits = recipe.UpdateTooltip
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [preview.TooltipKey] = preview.TooltipValue
                }
                : new Dictionary<string, string>(StringComparer.Ordinal);

            if (edits.Count > 0)
            {
                LocalizationService.ApplyEditsAndSave(
                    localizationPath,
                    edits,
                    localizationOutput,
                    overwrite: false);
            }
            else
            {
                File.Copy(localizationPath, localizationOutput, overwrite: false);
            }

            File.WriteAllText(
                reportOutput,
                BuildReport(
                    abilitiesPath,
                    templatesPath,
                    localizationPath,
                    recipe,
                    preview),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Repeat parsing before publishing the directory.
            AbilityDocument.Load(abilitiesOutput);
            TemplateDocument.Load(templatesOutput);
            string? storedTooltip = LocalizationService.ReadValue(
                localizationOutput,
                preview.TooltipKey);
            if (!string.Equals(storedTooltip, preview.TooltipValue, StringComparison.Ordinal))
                throw new InvalidDataException("Проверка сохранённой локализации не пройдена.");

            string? backup = null;
            try
            {
                if (Directory.Exists(fullOutput))
                {
                    backup = fullOutput + "." + Guid.NewGuid().ToString("N") + ".backup";
                    Directory.Move(fullOutput, backup);
                }

                Directory.Move(staging, fullOutput);
                if (backup != null && Directory.Exists(backup))
                    Directory.Delete(backup, recursive: true);
            }
            catch
            {
                if (!Directory.Exists(fullOutput)
                    && backup != null
                    && Directory.Exists(backup))
                {
                    Directory.Move(backup, fullOutput);
                }
                throw;
            }
        }
        catch
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            throw;
        }

        return new CardConversionResult
        {
            OutputDirectory = fullOutput,
            AbilitiesFile = Path.Combine(fullOutput, "Abilities.modified.xml"),
            TemplatesFile = Path.Combine(fullOutput, "Templates.modified.xml"),
            LocalizationFile = Path.Combine(fullOutput, "ru-ru.modified.csv"),
            ReportFile = Path.Combine(fullOutput, "conversion-report.txt"),
            Preview = preview
        };
    }

    private static void ValidateRecipe(CardConversionRecipe recipe)
    {
        if (recipe.FormatVersion != 1)
            throw new InvalidDataException($"Неподдерживаемый FormatVersion={recipe.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(recipe.Card))
            throw new InvalidDataException("Conversion recipe не содержит card.");
        if (!recipe.ConversionMode.Equals("safe", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("0.27.3 выполняет только conversionMode='safe'.");
        if (recipe.Charges < 1)
            throw new InvalidDataException("charges должен быть не меньше 1.");
        if (string.IsNullOrWhiteSpace(recipe.OutputDirectoryName))
            throw new InvalidDataException("outputDirectoryName не может быть пустым.");
    }

    private static void ApplyArtifactTemplate(
        XElement template,
        CardRecord card,
        CardConversionRecipe recipe,
        bool zealEnabled,
        List<string> changes)
    {
        SetElement(template, "Type", ArtifactType.ToString(), card, changes);
        SetElement(template, "Power", "0", card, changes);
        SetElement(template, "Armor", "0", card, changes);
        SetElement(template, "InitialTimer", "-1", card, changes);
        SetElement(template, "ResetInInactive", "True", card, changes);
        SetElement(template, "InitalTapCharges", recipe.Charges.ToString(), card, changes);
        SetElement(template, "TapActive", "True", card, changes);
        SetElement(template, "SummoningSickness", zealEnabled ? "False" : "True", card, changes);
        SetElement(template, "VisibleTapCharges", recipe.VisibleCharges ? "True" : "False", card, changes);
        SetElement(template, "Madness", "False", card, changes);

        XElement placement = template.Element("Placement")
            ?? throw new InvalidDataException($"Template {card.Id} не содержит Placement.");
        string oldPlacement = $"{(string?)placement.Attribute("PlayerSide")}/" +
                              $"{(string?)placement.Attribute("OpponentSide")}";
        placement.SetAttributeValue("PlayerSide", "3");
        placement.SetAttributeValue("OpponentSide", "0");
        changes.Add($"{card.Id} {card.DebugName}: Placement {oldPlacement} -> 3/0");
    }

    private static void ReplaceDeployWithOrder(
        XElement ability,
        CardRecord card,
        List<string> changes)
    {
        XElement nodes = ability.Element("Nodes")
            ?? throw new InvalidDataException($"CardAbility {card.Id} не содержит Nodes.");
        XElement connections = ability.Element("Connections")
            ?? throw new InvalidDataException($"CardAbility {card.Id} не содержит Connections.");

        List<XElement> deployTriggers = nodes.Elements()
            .Where(node => string.Equals(
                (string?)node.Attribute("Type"),
                "PlayedTrigger",
                StringComparison.Ordinal))
            .ToList();
        if (deployTriggers.Count != 1)
        {
            throw new InvalidOperationException(
                $"Safe conversion требует ровно один PlayedTrigger; найдено {deployTriggers.Count}.");
        }
        if (nodes.Elements().Any(node =>
                string.Equals((string?)node.Attribute("Type"), "TappedTrigger", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "CardAbility уже содержит TappedTrigger. Safe converter не создаёт второй Order.");
        }

        XElement deploy = deployTriggers[0];
        XElement priority = deploy.Element("Priority")
            ?? throw new InvalidDataException("PlayedTrigger не содержит Priority.");
        XElement flowOut = deploy.Element("FlowOut")
            ?? throw new InvalidDataException("PlayedTrigger не содержит FlowOut.");

        HashSet<int> retainedIds = new()
        {
            ParseRequiredId(deploy),
            ParseRequiredId(priority),
            ParseRequiredId(flowOut)
        };
        HashSet<int> removedEndpointIds = deploy.DescendantsAndSelf()
            .Select(element => (int?)element.Attribute("Id"))
            .Where(id => id.HasValue && !retainedIds.Contains(id.Value))
            .Select(id => id!.Value)
            .ToHashSet();

        List<XElement> referencesToRemovedEndpoints = connections.Elements()
            .Where(connection =>
            {
                int? src = (int?)connection.Attribute("SrcId");
                int? dst = (int?)connection.Attribute("DstId");
                return (src.HasValue && removedEndpointIds.Contains(src.Value))
                    || (dst.HasValue && removedEndpointIds.Contains(dst.Value));
            })
            .ToList();
        if (referencesToRemovedEndpoints.Count > 0)
        {
            string ids = string.Join(
                ", ",
                referencesToRemovedEndpoints.Select(connection =>
                    $"{(string?)connection.Attribute("SrcId")}->{(string?)connection.Attribute("DstId")}"));
            throw new InvalidOperationException(
                "PlayedTrigger имеет используемые выходы CardPlayed/From/To/Forced. " +
                "Автоматическая замена на TappedTrigger небезопасна. Connections: " + ids);
        }

        var order = new XElement(
            deploy.Name,
            deploy.Attributes().Select(attribute => new XAttribute(attribute)));
        order.SetAttributeValue("Type", "TappedTrigger");
        order.Add(new XElement(priority));
        order.Add(new XElement(flowOut));
        deploy.ReplaceWith(order);

        changes.Add(
            $"{card.Id} {card.DebugName}: PlayedTrigger Id={ParseRequiredId(order)} " +
            $"-> TappedTrigger; FlowOut Id={ParseRequiredId(flowOut)} preserved");
    }

    private static void ValidateTemplateAbilityAgreement(
        XElement template,
        XElement ability,
        CardRecord card,
        bool zealEnabled)
    {
        int type = ParseRequiredInt(template.Element("Type"), "Type", card);
        bool tapActive = ParseRequiredBool(template.Element("TapActive"), "TapActive", card);
        int charges = ParseRequiredInt(template.Element("InitalTapCharges"), "InitalTapCharges", card);
        bool summoningSickness = ParseRequiredBool(
            template.Element("SummoningSickness"),
            "SummoningSickness",
            card);
        XElement placement = template.Element("Placement")
            ?? throw new InvalidDataException($"Template {card.Id} не содержит Placement.");

        int tappedTriggers = ability.Element("Nodes")?.Elements()
            .Count(node => (string?)node.Attribute("Type") == "TappedTrigger") ?? 0;
        int deployTriggers = ability.Element("Nodes")?.Elements()
            .Count(node => (string?)node.Attribute("Type") == "PlayedTrigger") ?? 0;

        if (type != ArtifactType)
            throw new InvalidOperationException("Conversion invariant failed: Type != Artifact.");
        if (!tapActive || charges < 1 || tappedTriggers != 1)
        {
            throw new InvalidOperationException(
                "Conversion invariant failed: Artifact Order требует TapActive=True, " +
                "charges>=1 и ровно один TappedTrigger.");
        }
        if (deployTriggers != 0)
            throw new InvalidOperationException("Conversion invariant failed: старый PlayedTrigger остался в графе.");
        if (zealEnabled && summoningSickness)
            throw new InvalidOperationException("Conversion invariant failed: Zeal требует SummoningSickness=False.");
        if ((string?)placement.Attribute("PlayerSide") != "3"
            || (string?)placement.Attribute("OpponentSide") != "0")
        {
            throw new InvalidOperationException("Conversion invariant failed: Artifact Placement должен быть 3/0.");
        }
    }

    private static string BuildOrderTooltip(string oldTooltip, bool zealEnabled)
    {
        string body = oldTooltip.Trim();
        const string deployPrefix = "<keyword=deploy>Размещение</keyword>:";
        if (body.StartsWith(deployPrefix, StringComparison.OrdinalIgnoreCase))
            body = body[deployPrefix.Length..].TrimStart();

        string prefix = zealEnabled
            ? "<keyword=zeal>Рвение</keyword>. <keyword=order>Приказ</keyword>: "
            : "<keyword=order>Приказ</keyword>: ";
        return prefix + LowercaseFirst(body);
    }

    private static string LowercaseFirst(string value)
    {
        if (value.Length == 0)
            return value;
        char first = value[0];
        char lowered = char.ToLowerInvariant(first);
        return lowered == first ? value : lowered + value[1..];
    }

    private static bool ResolveZeal(string value, string targetType, string activation)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "true" or "yes" or "y" or "1" => true,
            "false" or "no" or "n" or "0" => false,
            "auto" or "" => targetType == "artifact" && activation == "order",
            _ => throw new InvalidDataException("zeal должен быть auto, true или false.")
        };
    }

    private static string BuildReport(
        string abilitiesPath,
        string templatesPath,
        string localizationPath,
        CardConversionRecipe recipe,
        CardConversionPreview preview)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Card conversion: {recipe.Name}");
        builder.AppendLine($"Card: {preview.Card.Id} {preview.Card.DebugName}");
        builder.AppendLine($"Type: {preview.SourceType} -> {preview.TargetType}");
        builder.AppendLine($"Activation: {recipe.ReplaceTrigger} -> {recipe.Activation}");
        builder.AppendLine($"Charges: {recipe.Charges}");
        builder.AppendLine($"Zeal: {preview.ZealEnabled}");
        builder.AppendLine($"Abilities input: {Path.GetFullPath(abilitiesPath)}");
        builder.AppendLine($"Templates input: {Path.GetFullPath(templatesPath)}");
        builder.AppendLine($"Localization input: {Path.GetFullPath(localizationPath)}");
        builder.AppendLine();
        builder.AppendLine("Changes:");
        foreach (string change in preview.Changes)
            builder.AppendLine("- " + change);
        builder.AppendLine();
        builder.AppendLine(
            $"Graph validation: errors={preview.GraphValidation.ErrorCount}, " +
            $"warnings={preview.GraphValidation.WarningCount}");
        foreach (ValidationIssue issue in preview.GraphValidation.Issues)
            builder.AppendLine($"- [{issue.Severity}] {issue.Code}: {issue.Message}");
        builder.AppendLine();
        builder.AppendLine("Conversion warnings:");
        foreach (string warning in preview.Warnings)
            builder.AppendLine("- " + warning);
        builder.AppendLine();
        builder.AppendLine($"Localization: {preview.TooltipKey};{preview.TooltipValue}");
        builder.AppendLine("Publication: all outputs were staged and validated before directory publish.");
        return builder.ToString();
    }

    private static void SetElement(
        XElement template,
        string name,
        string value,
        CardRecord card,
        List<string> changes)
    {
        XElement element = template.Element(name)
            ?? throw new InvalidDataException($"Template {card.Id} не содержит {name}.");
        string oldValue = element.Value;
        element.Value = value;
        if (!string.Equals(oldValue, value, StringComparison.Ordinal))
            changes.Add($"{card.Id} {card.DebugName}: {name} {oldValue} -> {value}");
    }

    private static int ParseRequiredInt(XElement? element, string field, CardRecord card)
    {
        if (element == null || !int.TryParse(element.Value, out int value))
            throw new InvalidDataException($"Template {card.Id}: {field} не содержит целое значение.");
        return value;
    }

    private static bool ParseRequiredBool(XElement? element, string field, CardRecord card)
    {
        if (element == null || !bool.TryParse(element.Value, out bool value))
            throw new InvalidDataException($"Template {card.Id}: {field} не содержит bool.");
        return value;
    }

    private static int ParseRequiredId(XElement element)
    {
        return (int?)element.Attribute("Id")
            ?? throw new InvalidDataException($"Element {element.Name} не содержит Id.");
    }

    private static string CardTypeName(int value) => value switch
    {
        SpecialType => "Special",
        UnitType => "Unit",
        ArtifactType => "Artifact",
        1 => "Leader",
        16 => "Stratagem",
        _ => $"Unknown({value})"
    };
}
