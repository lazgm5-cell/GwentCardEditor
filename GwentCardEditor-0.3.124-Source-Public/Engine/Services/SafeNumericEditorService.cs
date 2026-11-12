using AbilityAnalyzer.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class SafeNumericEditorService
{
    private const string ProductVersion = "0.29.1";

    private sealed record TemplateFieldDefinition(
        string ElementName,
        string Role,
        string DisplayName,
        int Minimum,
        int Maximum);

    private sealed record NodeFieldDefinition(
        string NodeType,
        string FieldName,
        string Role,
        string DisplayName,
        int Minimum,
        int Maximum);

    private static readonly TemplateFieldDefinition[] TemplateFields =
    {
        new("Power", "power", "Сила", 0, 999),
        new("Provision", "provision", "Стоимость найма", 0, 99),
        new("Armor", "armor", "Броня", 0, 999),
        new("InitialTimer", "timer", "Начальный таймер", -1, 999),
        new("InitalTapCharges", "charges", "Начальные заряды", 0, 99)
    };

    private static readonly NodeFieldDefinition[] NodeFields =
    {
        new("ChangeArmorNode", "Armor", "armor-change", "Изменение брони", 0, 9999),
        new("ChangeCardTimerNode", "Value", "timer-change", "Изменение таймера", 0, 9999),
        new("ChangeLocationTimerNode", "Value", "row-timer-change", "Изменение таймера ряда", 0, 9999),
        new("ChangeTapCardChargesNode", "Value", "charge-change", "Изменение зарядов", 0, 9999),
        new("RequestCardTargetsNode", "MinTargets", "min-targets", "Минимум целей", 0, 99),
        new("RequestCardTargetsNode", "MaxTargets", "max-targets", "Максимум целей", 0, 99),
        new("RequestCardChoiceNode", "MinChoices", "min-choices", "Минимум выбранных карт", 0, 99),
        new("RequestCardChoiceNode", "MaxChoices", "max-choices", "Максимум выбранных карт", 0, 99),
        new("RequestTemplateChoiceNode", "MinChoices", "min-template-choices", "Минимум выбранных шаблонов", 0, 99),
        new("RequestTemplateChoiceNode", "MaxChoices", "max-template-choices", "Максимум выбранных шаблонов", 0, 99),
        new("RequestLocationTargetsNode", "MinTargets", "min-row-targets", "Минимум выбранных рядов", 0, 9),
        new("RequestLocationTargetsNode", "MaxTargets", "max-row-targets", "Максимум выбранных рядов", 0, 9),
        new("SpawnCardNode", "Count", "spawn-count", "Количество создаваемых карт", 0, 99),
        new("DrawCardsNode", "Num", "draw-count", "Количество добираемых карт", 0, 99),
        new("RevealCardsNode", "Num", "reveal-count", "Количество просматриваемых карт", 0, 99),
        new("FilterCardProvisionNode", "Provision", "provision-limit", "Предел стоимости найма", 0, 99),
        new("FilterCardDefinitionListProvision", "Provision", "definition-provision-limit", "Предел стоимости найма определений", 0, 99),
        new("FilterCardPowerNode", "Power", "power-limit", "Предел силы", -9999, 9999),
        new("FilterPowerLevelNode", "Num", "power-level", "Уровень силы", -9999, 9999),
        new("FilterTapCardChargesNode", "Charges", "charges-limit", "Предел зарядов", 0, 99),
        new("FilterCardTimerNode", "Timer", "timer-limit", "Предел таймера", -1, 999),
        new("BloodthirstBranchNode", "Count", "bloodthirst", "Жажда крови", 0, 99),
    };

    private static readonly Dictionary<string, (string Role, string DisplayName, int Minimum, int Maximum)>
        NamedVariableRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            ["damage"] = ("damage", "Урон", 0, 9999),
            ["boost"] = ("boost", "Усиление", 0, 9999),
            ["strengthen"] = ("strengthen", "Увеличение изначальной силы", 0, 9999),
            ["heal"] = ("heal", "Восстановление", 0, 9999),
            ["healing"] = ("heal", "Восстановление", 0, 9999),
            ["restoration"] = ("heal", "Восстановление", 0, 9999),
            ["armor"] = ("ability-armor", "Броня способности", 0, 9999),
            ["charges"] = ("ability-charges", "Заряды способности", 0, 9999),
            ["charge"] = ("ability-charge", "Заряд способности", 0, 9999),
            ["cooldown"] = ("cooldown", "Перезарядка", 0, 9999),
            ["timer"] = ("ability-timer", "Таймер способности", -1, 9999),
            ["duration"] = ("duration", "Длительность", 0, 9999),
            ["bleed"] = ("bleeding-duration", "Длительность Кровотечения", 0, 9999),
            ["bleeding"] = ("bleeding-duration", "Длительность Кровотечения", 0, 9999),
            ["vitality"] = ("vitality-duration", "Длительность Выносливости", 0, 9999),
            ["count"] = ("count", "Количество", 0, 9999),
            ["counter"] = ("counter", "Счётчик", -9999, 9999),
            ["maxtargets"] = ("max-targets", "Максимум целей", 0, 99),
            ["mintargets"] = ("min-targets", "Минимум целей", 0, 99),
            ["provision"] = ("ability-provision", "Стоимость найма способности", 0, 99),
            ["provisions"] = ("ability-provision", "Стоимость найма способности", 0, 999),
            ["power"] = ("ability-power", "Сила способности", -9999, 9999),
            ["cost"] = ("cost", "Стоимость", 0, 9999),
            ["amount"] = ("amount", "Количество", -9999, 9999),
            ["value"] = ("value", "Значение", -9999, 9999)
        };

    public static NumericEditInspection Inspect(
        string abilitiesPath,
        CardRecord card)
    {
        string fullAbilitiesPath = Path.GetFullPath(abilitiesPath);
        string templatesPath = FilePathResolver.ResolveSiblingXml(fullAbilitiesPath, "Templates.xml");
        TemplateDocument templates = TemplateDocument.Load(templatesPath);
        XElement template = templates.FindById(card.Id)
            ?? throw new InvalidOperationException($"Template Id={card.Id} не найден.");

        var values = new List<NumericValueDescriptor>();
        foreach (TemplateFieldDefinition field in TemplateFields)
        {
            XElement? element = template.Element(field.ElementName);
            if (element == null || !int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int current))
                continue;

            values.Add(new NumericValueDescriptor
            {
                Role = field.Role,
                DisplayName = field.DisplayName,
                Source = "Templates.xml",
                Path = $"Template[{card.Id}]/{field.ElementName}",
                CurrentValue = current,
                MinimumValue = field.Minimum,
                MaximumValue = field.Maximum,
                Editable = true,
                TargetKind = "TemplateField",
                TemplateField = field.ElementName,
                FieldName = field.ElementName
            });
        }

        AbilityDocument abilityDocument = AbilityDocument.Load(fullAbilitiesPath);
        XElement? ability = abilityDocument.FindCardAbility(card.Id);
        if (ability != null)
            AddAbilityValues(ability, values);

        AssignIndexesAndKeys(values);
        return new NumericEditInspection
        {
            Card = card,
            AbilitiesPath = fullAbilitiesPath,
            TemplatesPath = templatesPath,
            AbilitiesSha256 = ComputeSha256(fullAbilitiesPath),
            TemplatesSha256 = ComputeSha256(templatesPath),
            Values = values
        };
    }

    public static IReadOnlyList<NumericValueDescriptor> InspectGraph(XElement ability)
    {
        var values = new List<NumericValueDescriptor>();
        AddAbilityValues(ability, values);
        AssignIndexesAndKeys(values);
        return values;
    }

    public static XElement PrepareGraphEdit(XElement template, XElement ability, IReadOnlyDictionary<int, int> edits)
    {
        IReadOnlyList<NumericValueDescriptor> values = InspectGraph(ability);
        var changes = edits.Select(edit => new NumericEditChange
        {
            Target = values.Single(v => v.VariableId == edit.Key), NewValue = edit.Value
        }).ToList();
        foreach (var change in changes)
        {
            if (!change.Target.Editable) throw new InvalidOperationException("ComputedValue");
            ValidateRange(change.Target, change.NewValue);
        }
        ValidatePairedLimits(values, changes);
        var copy = new XElement(ability);
        foreach (var change in changes) ApplyOne(template, copy, change);
        ValidatePairedLimitsAfterApply(template, copy);
        var before = GraphValidator.Validate(ability);
        var after = GraphValidator.Validate(copy);
        if (before.ErrorCount > 0 || after.ErrorCount > 0) throw new InvalidOperationException("GraphHasErrors");
        return copy;
    }

    public static IReadOnlyList<NumericEditChange> ParseChanges(
        NumericEditInspection inspection,
        string input)
    {
        string[] assignments = input
            .Split(new[] { ' ', '\t', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (assignments.Length == 0)
            throw new InvalidOperationException("Не указаны изменения. Пример: 1=6 2=8 damage=4");

        var result = new List<NumericEditChange>();
        var selectedIndexes = new HashSet<int>();
        foreach (string assignment in assignments)
        {
            int equals = assignment.IndexOf('=');
            if (equals <= 0 || equals == assignment.Length - 1)
                throw new InvalidOperationException($"Некорректное изменение '{assignment}'. Используйте параметр=значение.");

            string selector = assignment[..equals].Trim().TrimStart('#');
            string newText = assignment[(equals + 1)..].Trim();
            if (!int.TryParse(newText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int newValue))
                throw new InvalidOperationException($"Значение '{newText}' не является целым числом.");

            NumericValueDescriptor target = ResolveTarget(inspection.Values, selector);
            if (!target.Editable)
            {
                throw new InvalidOperationException(
                    $"Параметр [{target.Index}] недоступен для прямого изменения: {target.ReadOnlyReason ?? "вычисляется графом"}.");
            }

            ValidateRange(target, newValue);
            if (!selectedIndexes.Add(target.Index))
                throw new InvalidOperationException($"Параметр [{target.Index}] указан более одного раза.");

            if (newValue == target.CurrentValue)
                continue;

            result.Add(new NumericEditChange
            {
                Target = target,
                NewValue = newValue
            });
        }

        if (result.Count == 0)
            throw new InvalidOperationException("Ни одно значение не изменяется.");

        ValidatePairedLimits(inspection.Values, result);
        return result;
    }

    public static NumericEditApplyResult Apply(
        NumericEditInspection inspection,
        IReadOnlyList<NumericEditChange> changes,
        bool inPlace,
        string? requestedOutputDirectory = null)
    {
        if (changes.Count == 0)
            throw new InvalidOperationException("Список изменений пуст.");

        string originalAbilitiesSha256 = ComputeSha256(inspection.AbilitiesPath);
        string originalTemplatesSha256 = ComputeSha256(inspection.TemplatesPath);
        if (!string.Equals(originalAbilitiesSha256, inspection.AbilitiesSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(originalTemplatesSha256, inspection.TemplatesSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Исходные XML изменились после показа значений. Откройте карту заново; сохранение остановлено.");
        }
        AbilityDocument originalAbilities = AbilityDocument.Load(inspection.AbilitiesPath);
        TemplateDocument originalTemplates = TemplateDocument.Load(inspection.TemplatesPath);
        AbilityDocument workingAbilities = originalAbilities.DeepClone();
        TemplateDocument workingTemplates = originalTemplates.DeepClone();

        XElement? originalAbility = originalAbilities.FindCardAbility(inspection.Card.Id);
        XElement? workingAbility = workingAbilities.FindCardAbility(inspection.Card.Id);
        XElement workingTemplate = workingTemplates.FindById(inspection.Card.Id)
            ?? throw new InvalidOperationException($"Template Id={inspection.Card.Id} не найден при повторной проверке.");

        foreach (NumericEditChange change in changes)
            ApplyOne(workingTemplate, workingAbility, change);

        ValidatePairedLimitsAfterApply(workingTemplate, workingAbility);

        if (workingAbility != null)
        {
            GraphValidationReport beforeReport = originalAbility == null
                ? new GraphValidationReport()
                : GraphValidator.Validate(originalAbility);
            GraphValidationReport afterReport = GraphValidator.Validate(workingAbility);
            if (afterReport.ErrorCount > beforeReport.ErrorCount)
            {
                string details = string.Join(
                    Environment.NewLine,
                    afterReport.Issues
                        .Where(issue => issue.Severity == ValidationSeverity.Error)
                        .Select(issue => $"[{issue.Code}] {issue.Message}"));
                throw new InvalidOperationException(
                    "После изменения граф получил новые ошибки." +
                    (string.IsNullOrWhiteSpace(details) ? "" : Environment.NewLine + details));
            }
        }

        string rootDirectory = Path.GetDirectoryName(inspection.AbilitiesPath)
            ?? throw new InvalidOperationException("Не удалось определить каталог проекта.");
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string safeName = SanitizeFileName(inspection.Card.DebugName);

        string outputDirectory;
        string abilitiesOutput;
        string templatesOutput;
        string? backupDirectory = null;

        if (inPlace)
        {
            outputDirectory = rootDirectory;
            abilitiesOutput = inspection.AbilitiesPath;
            templatesOutput = inspection.TemplatesPath;
            backupDirectory = CreateBackup(
                rootDirectory,
                timestamp,
                inspection,
                changes);
        }
        else
        {
            outputDirectory = string.IsNullOrWhiteSpace(requestedOutputDirectory)
                ? Path.Combine(rootDirectory, "AbilityEditor_Output", $"{timestamp}_{inspection.Card.Id}_{safeName}")
                : Path.GetFullPath(requestedOutputDirectory);
            if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
                throw new IOException($"Выходной каталог уже существует и не пуст: {outputDirectory}");
            Directory.CreateDirectory(outputDirectory);
            abilitiesOutput = Path.Combine(outputDirectory, "Abilities.modified.xml");
            templatesOutput = Path.Combine(outputDirectory, "Templates.modified.xml");
        }

        string reportFile = Path.Combine(outputDirectory, $"numeric-edit-{timestamp}.report.txt");
        string recipeFile = Path.Combine(outputDirectory, $"numeric-edit-{timestamp}.json");
        try
        {
            SaveDocumentAtomic(workingAbilities.Document, abilitiesOutput);
            SaveDocumentAtomic(workingTemplates.Document, templatesOutput);
            WriteReport(reportFile, inspection, changes, inPlace, backupDirectory);
            WriteRecipe(
                recipeFile,
                inspection,
                changes,
                inPlace,
                backupDirectory,
                originalAbilitiesSha256,
                originalTemplatesSha256);
        }
        catch
        {
            if (File.Exists(reportFile))
                File.Delete(reportFile);
            if (File.Exists(recipeFile))
                File.Delete(recipeFile);
            if (inPlace && backupDirectory != null)
            {
                RestoreBackupDirectory(backupDirectory, overwrite: true);
            }
            else if (!inPlace && Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
            throw;
        }

        return new NumericEditApplyResult
        {
            OutputDirectory = outputDirectory,
            AbilitiesFile = abilitiesOutput,
            TemplatesFile = templatesOutput,
            ReportFile = reportFile,
            RecipeFile = recipeFile,
            BackupDirectory = backupDirectory,
            AppliedInPlace = inPlace
        };
    }

    public static string? FindLatestBackup(string abilitiesPath)
    {
        string rootDirectory = Path.GetDirectoryName(Path.GetFullPath(abilitiesPath))
            ?? throw new InvalidOperationException("Не удалось определить каталог проекта.");
        string backupsRoot = Path.Combine(rootDirectory, "Backups");
        if (!Directory.Exists(backupsRoot))
            return null;

        return Directory.GetDirectories(backupsRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "numeric-edit-backup.json")))
            .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static void RestoreBackupDirectory(string backupDirectory, bool overwrite)
    {
        string manifestPath = Path.Combine(backupDirectory, "numeric-edit-backup.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Манифест резервной копии не найден.", manifestPath);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        JsonElement root = document.RootElement;
        string abilitiesTarget = root.GetProperty("abilitiesTarget").GetString()
            ?? throw new InvalidOperationException("В манифесте отсутствует abilitiesTarget.");
        string templatesTarget = root.GetProperty("templatesTarget").GetString()
            ?? throw new InvalidOperationException("В манифесте отсутствует templatesTarget.");
        string abilitiesBackup = Path.Combine(backupDirectory, "Abilities.xml");
        string templatesBackup = Path.Combine(backupDirectory, "Templates.xml");

        if (!File.Exists(abilitiesBackup) || !File.Exists(templatesBackup))
            throw new InvalidOperationException("Резервная копия неполна.");
        if (!overwrite && (File.Exists(abilitiesTarget) || File.Exists(templatesTarget)))
            throw new IOException("Целевые файлы существуют. Требуется явное разрешение на восстановление.");

        CopyFileAtomic(abilitiesBackup, abilitiesTarget);
        CopyFileAtomic(templatesBackup, templatesTarget);
    }

    private static void AddAbilityValues(XElement ability, List<NumericValueDescriptor> values)
    {
        HashSet<int> incomingIds = ability.Element("Connections")?
            .Elements()
            .Select(connection => (int?)connection.Attribute("DstId"))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet()
            ?? new HashSet<int>();

        var seenVariableIds = new HashSet<int>();
        foreach (string containerName in new[] { "TemporaryVariables", "PersistentVariables" })
        {
            XElement? container = ability.Element(containerName);
            if (container == null)
                continue;

            foreach (XElement variable in container.Elements()
                         .Where(element => string.Equals((string?)element.Attribute("Type"), "IntVar", StringComparison.Ordinal)))
            {
                AddNamedVariable(
                    variable,
                    values,
                    incomingIds,
                    seenVariableIds,
                    source: "Abilities.xml",
                    path: $"CardAbility[{(string?)ability.Attribute("Id")}]/{containerName}");
            }
        }

        XElement? nodes = ability.Element("Nodes");
        if (nodes == null)
            return;

        foreach (XElement node in nodes.Elements())
        {
            string nodeType = (string?)node.Attribute("Type") ?? "?";
            int? nodeId = (int?)node.Attribute("Id");

            if (ProfitNumericContract.Input(ability, node) is XElement profit)
                AddNodeField(profit, node, "profit", "Выгода", 0, 9, values, incomingIds, seenVariableIds);
            if (PaymentNumericContract.TributeInput(ability, node) is XElement tribute)
                AddNodeField(tribute, node, "tribute-cost", "Стоимость дани", 1, 9, values, incomingIds, seenVariableIds);
            if (ThresholdNumericContract.BerserkInput(ability, node) is XElement berserk)
                AddNodeField(berserk, node, "berserk-threshold", "Порог берсерка", 1, 99, values, incomingIds, seenVariableIds);
            if (SubAbilityNumericContract.Inspect(ability, node) is { } subNumber)
                AddNodeField(subNumber.Field, node, subNumber.Role, subNumber.Label, subNumber.Minimum, subNumber.Maximum, values, incomingIds, seenVariableIds);

            // These operations need fixed semantics, not a guess from the variable name.
            if (nodeType == "BalanceCardStatusTimerNode"
                && FixedField(node, "AttackMethod", "EnumVar`1", "EAttackMethod", 0, 1)
                && FixedField(node, "EffectType", "EnumVar`1", "EEffectType", 0, 1)
                && FixedField(node, "ElementType", "EnumVar`1", "EElementType", 1, 512, 2048, 16384))
            {
                bool bleeding = FixedField(node, "Acceptor", "CardTokensVar", null, 16)
                    && FixedField(node, "Donor", "CardTokensVar", null, 32);
                bool vitality = FixedField(node, "Acceptor", "CardTokensVar", null, 32)
                    && FixedField(node, "Donor", "CardTokensVar", null, 16);
                if ((bleeding || vitality) && node.Element("DeltaTime") is XElement duration)
                    AddNodeField(duration, node, bleeding ? "bleeding-duration" : "vitality-duration",
                        bleeding ? "Длительность Кровотечения" : "Длительность Выносливости", 0, 99, values, incomingIds, seenVariableIds);
            }
            if (nodeType == "ChangeOwnerMoneyNode"
                && FixedField(node, "Operation", "EnumVar`1", "EMoneyOperation", 0)
                && (node.Element("IsTribute") == null || FixedFalse(node.Element("IsTribute")!))
                && node.Element("Value") is XElement money)
                AddNodeField(money, node, "coin-gain", "Получение монет", 0, 9, values, incomingIds, seenVariableIds);

            if (string.Equals(nodeType, "ChangePowerNode", StringComparison.Ordinal))
            {
                XElement? power = node.Element("Power");
                if (power != null)
                {
                    int operation = ReadIntChild(node, "Operation", -1);
                    (string role, string label, int min, int max) = operation switch
                    {
                        0 => ("boost", "Усиление", 0, 9999),
                        1 => ("damage", "Урон", 0, 9999),
                        2 => ("set-power", "Установленная сила", -9999, 9999),
                        4 => ("heal", "Восстановление", 0, 9999),
                        _ => ("power-change", "Изменение силы", -9999, 9999)
                    };
                    AddNodeField(power, node, role, label, min, max, values, incomingIds, seenVariableIds);
                }
            }

            foreach (NodeFieldDefinition definition in NodeFields.Where(definition =>
                         string.Equals(definition.NodeType, nodeType, StringComparison.Ordinal)))
            {
                XElement? field = node.Element(definition.FieldName);
                if (field == null)
                    continue;
                AddNodeField(
                    field,
                    node,
                    definition.Role,
                    definition.DisplayName,
                    definition.Minimum,
                    definition.Maximum,
                    values,
                    incomingIds,
                    seenVariableIds);
            }

            foreach (XElement namedVariable in node.Descendants()
                         .Where(element => string.Equals((string?)element.Attribute("Type"), "IntVar", StringComparison.Ordinal))
                         .Where(_ => !(nodeType == "UseSubAbilityNode" && (string?)node.Element("SubAbilityID") is "9" or "17" or "18" or "30"))
                         .Where(element => !(nodeType == "BalanceCardStatusTimerNode" && element.Name.LocalName == "DeltaTime")
                             && !(nodeType == "ChangeOwnerMoneyNode" && element.Name.LocalName == "Value"))
                         .Where(element => !string.IsNullOrWhiteSpace((string?)element.Attribute("Name"))))
            {
                AddNamedVariable(
                    namedVariable,
                    values,
                    incomingIds,
                    seenVariableIds,
                    source: "Abilities.xml",
                    path: $"CardAbility[{(string?)ability.Attribute("Id")}]/Nodes/{nodeType}[{nodeId?.ToString() ?? "?"}]");
            }
        }

        bool IsFixed(XElement field) => !incomingIds.Contains((int?)field.Attribute("Id") ?? -1)
            && !string.Equals((string?)field.Attribute("ExternalIn"), "True", StringComparison.OrdinalIgnoreCase)
            && !string.Equals((string?)field.Attribute("ExternalOut"), "True", StringComparison.OrdinalIgnoreCase);
        bool FixedFalse(XElement field) => (string?)field.Attribute("Type") == "BoolVar" && IsFixed(field)
            && bool.TryParse((string?)field.Attribute("V"), out bool value) && !value;
        bool FixedField(XElement node, string name, string type, string? argument, params int[] allowed)
        {
            XElement? field = node.Element(name);
            return field != null && (string?)field.Attribute("Type") == type && IsFixed(field)
                && (argument == null || (string?)field.Attribute("GArg") == argument)
                && int.TryParse((string?)field.Attribute("V"), out int value) && allowed.Contains(value);
        }
    }

    private static void AddNamedVariable(
        XElement variable,
        List<NumericValueDescriptor> values,
        HashSet<int> incomingIds,
        HashSet<int> seenVariableIds,
        string source,
        string path)
    {
        int? variableId = (int?)variable.Attribute("Id");
        if (!variableId.HasValue || !seenVariableIds.Add(variableId.Value))
            return;
        if (!int.TryParse((string?)variable.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int current))
            return;

        string rawName = ((string?)variable.Attribute("Name") ?? variable.Name.LocalName).Trim();
        string baseName = Regex.Replace(rawName, "[0-9]+$", "", RegexOptions.CultureInvariant).Trim();
        string normalized = NormalizeSelector(baseName);
        (string role, string displayName, int min, int max) = NamedVariableRoles.TryGetValue(normalized, out var known)
            ? known
            : ($"variable-{NormalizeSelector(rawName)}", $"Переменная {rawName}", -999999, 999999);

        bool externalIn = string.Equals((string?)variable.Attribute("ExternalIn"), "True", StringComparison.OrdinalIgnoreCase);
        bool incoming = incomingIds.Contains(variableId.Value);
        bool editable = !externalIn && !incoming;
        string? reason = externalIn
            ? "значение поступает во внешнюю способность (ExternalIn)"
            : incoming
                ? "значение поступает по Connection"
                : null;

        values.Add(new NumericValueDescriptor
        {
            Role = role,
            DisplayName = displayName + (rawName.Equals(displayName, StringComparison.OrdinalIgnoreCase) ? "" : $" ({rawName})"),
            Source = source,
            Path = $"{path}/IntVar[{variableId.Value}]",
            CurrentValue = current,
            MinimumValue = min,
            MaximumValue = max,
            Editable = editable,
            ReadOnlyReason = reason,
            TargetKind = "AbilityIntVar",
            VariableId = variableId,
            FieldName = variable.Name.LocalName
        });
    }

    private static void AddNodeField(
        XElement field,
        XElement node,
        string role,
        string displayName,
        int minimum,
        int maximum,
        List<NumericValueDescriptor> values,
        HashSet<int> incomingIds,
        HashSet<int> seenVariableIds)
    {
        if (!string.Equals((string?)field.Attribute("Type"), "IntVar", StringComparison.Ordinal))
            return;
        int? variableId = (int?)field.Attribute("Id");
        if (!variableId.HasValue || !seenVariableIds.Add(variableId.Value))
            return;
        if (!int.TryParse((string?)field.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int current))
            return;

        string nodeType = (string?)node.Attribute("Type") ?? "?";
        int? nodeId = (int?)node.Attribute("Id");
        bool externalIn = string.Equals((string?)field.Attribute("ExternalIn"), "True", StringComparison.OrdinalIgnoreCase);
        bool incoming = incomingIds.Contains(variableId.Value);
        bool editable = !externalIn && !incoming;
        string? reason = externalIn
            ? "значение поступает во внешнюю способность (ExternalIn)"
            : incoming
                ? "значение вычисляется другим узлом через Connection"
                : null;

        values.Add(new NumericValueDescriptor
        {
            Role = role,
            DisplayName = $"{displayName} — {nodeType} #{nodeId?.ToString() ?? "?"}",
            Source = "Abilities.xml",
            Path = $"CardAbility/Nodes/{nodeType}[{nodeId?.ToString() ?? "?"}]/{field.Name.LocalName}",
            CurrentValue = current,
            MinimumValue = minimum,
            MaximumValue = maximum,
            Editable = editable,
            ReadOnlyReason = reason,
            TargetKind = "AbilityIntVar",
            VariableId = variableId,
            NodeId = nodeId,
            NodeType = nodeType,
            FieldName = field.Name.LocalName
        });
    }

    private static void AssignIndexesAndKeys(List<NumericValueDescriptor> values)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < values.Count; index++)
        {
            NumericValueDescriptor value = values[index];
            value.Index = index + 1;
            counts.TryGetValue(value.Role, out int count);
            count++;
            counts[value.Role] = count;
            value.Key = count == 1 ? value.Role : $"{value.Role}#{count}";
        }
    }

    private static NumericValueDescriptor ResolveTarget(
        IReadOnlyList<NumericValueDescriptor> values,
        string selector)
    {
        if (int.TryParse(selector, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
        {
            NumericValueDescriptor? byIndex = values.FirstOrDefault(value => value.Index == index);
            return byIndex ?? throw new InvalidOperationException($"Параметр [{index}] не найден.");
        }

        string normalized = NormalizeSelector(selector);
        List<NumericValueDescriptor> byKey = values
            .Where(value => NormalizeSelector(value.Key) == normalized)
            .ToList();
        if (byKey.Count == 1)
            return byKey[0];

        List<NumericValueDescriptor> semantic = values
            .Where(value =>
                NormalizeSelector(value.Role) == normalized
                || NormalizeSelector(value.FieldName ?? "") == normalized)
            .ToList();
        List<NumericValueDescriptor> editable = semantic.Where(value => value.Editable).ToList();
        if (editable.Count == 1)
            return editable[0];
        if (semantic.Count == 1)
            return semantic[0];
        if (semantic.Count > 1)
        {
            throw new InvalidOperationException(
                $"Параметр '{selector}' неоднозначен. Используйте номер: " +
                string.Join(", ", semantic.Select(value => $"[{value.Index}] {value.DisplayName}")));
        }

        throw new InvalidOperationException(
            $"Параметр '{selector}' не найден. Используйте номер из показанного списка.");
    }

    private static void ApplyOne(
        XElement template,
        XElement? ability,
        NumericEditChange change)
    {
        NumericValueDescriptor target = change.Target;
        if (string.Equals(target.TargetKind, "TemplateField", StringComparison.Ordinal))
        {
            XElement element = template.Element(target.TemplateField!)
                ?? throw new InvalidOperationException($"Поле шаблона {target.TemplateField} исчезло.");
            int current = ParseElementValue(element, target.Path);
            EnsureExpectedValue(current, target);
            element.Value = change.NewValue.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (ability == null)
            throw new InvalidOperationException("CardAbility отсутствует, но запрошено изменение Abilities.xml.");
        XElement variable = ability.DescendantsAndSelf()
            .FirstOrDefault(element =>
                string.Equals((string?)element.Attribute("Type"), "IntVar", StringComparison.Ordinal)
                && (int?)element.Attribute("Id") == target.VariableId)
            ?? throw new InvalidOperationException($"IntVar Id={target.VariableId} исчез из CardAbility.");
        int currentVariable = ParseAttributeValue(variable, "V", target.Path);
        EnsureExpectedValue(currentVariable, target);
        variable.SetAttributeValue("V", change.NewValue.ToString(CultureInfo.InvariantCulture));
    }

    private static void EnsureExpectedValue(int current, NumericValueDescriptor target)
    {
        if (current != target.CurrentValue)
        {
            throw new InvalidOperationException(
                $"Безопасная проверка не пройдена для [{target.Index}] {target.DisplayName}: " +
                $"ожидалось {target.CurrentValue}, обнаружено {current}. Исходные файлы изменились.");
        }
    }

    private static void ValidateRange(NumericValueDescriptor target, int value)
    {
        (int minimum, int maximum) = ResolveRange(target);
        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"Значение {value} для '{target.DisplayName}' вне безопасного диапазона {minimum}..{maximum}.");
        }
    }

    private static (int Minimum, int Maximum) ResolveRange(NumericValueDescriptor target)
    {
        return (target.MinimumValue, target.MaximumValue);
    }

    private static void ValidatePairedLimits(
        IReadOnlyList<NumericValueDescriptor> values,
        IReadOnlyList<NumericEditChange> changes)
    {
        var effective = values.ToDictionary(value => value.Index, value => value.CurrentValue);
        foreach (NumericEditChange change in changes)
            effective[change.Target.Index] = change.NewValue;

        foreach (IGrouping<int?, NumericValueDescriptor> group in values
                     .Where(value => value.NodeId.HasValue)
                     .GroupBy(value => value.NodeId))
        {
            NumericValueDescriptor? min = group.FirstOrDefault(value =>
                value.Role.StartsWith("min-", StringComparison.OrdinalIgnoreCase));
            NumericValueDescriptor? max = group.FirstOrDefault(value =>
                value.Role.StartsWith("max-", StringComparison.OrdinalIgnoreCase));
            if (min != null && max != null && effective[min.Index] > effective[max.Index])
            {
                throw new InvalidOperationException(
                    $"Минимум не может быть больше максимума: [{min.Index}]={effective[min.Index]}, " +
                    $"[{max.Index}]={effective[max.Index]}.");
            }
        }
    }

    private static void ValidatePairedLimitsAfterApply(XElement template, XElement? ability)
    {
        _ = template;
        if (ability == null)
            return;
        foreach (XElement node in ability.Element("Nodes")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            foreach ((string minName, string maxName) in new[]
                     {
                         ("MinTargets", "MaxTargets"),
                         ("MinChoices", "MaxChoices")
                     })
            {
                XElement? min = node.Element(minName);
                XElement? max = node.Element(maxName);
                if (min == null || max == null)
                    continue;
                if (!int.TryParse((string?)min.Attribute("V"), out int minValue)
                    || !int.TryParse((string?)max.Attribute("V"), out int maxValue))
                    continue;
                if (minValue > maxValue)
                {
                    throw new InvalidOperationException(
                        $"В узле {(string?)node.Attribute("Type")} Id={(string?)node.Attribute("Id")} " +
                        $"{minName}={minValue} больше {maxName}={maxValue}.");
                }
            }
        }
    }

    private static string CreateBackup(
        string rootDirectory,
        string timestamp,
        NumericEditInspection inspection,
        IReadOnlyList<NumericEditChange> changes)
    {
        string safeName = SanitizeFileName(inspection.Card.DebugName);
        string backupDirectory = Path.Combine(
            rootDirectory,
            "Backups",
            $"{timestamp}_{inspection.Card.Id}_{safeName}");
        Directory.CreateDirectory(backupDirectory);
        File.Copy(inspection.AbilitiesPath, Path.Combine(backupDirectory, "Abilities.xml"), overwrite: false);
        File.Copy(inspection.TemplatesPath, Path.Combine(backupDirectory, "Templates.xml"), overwrite: false);

        var manifest = new
        {
            product = "AbilityEditor",
            version = ProductVersion,
            createdAt = DateTimeOffset.Now,
            abilitiesTarget = inspection.AbilitiesPath,
            templatesTarget = inspection.TemplatesPath,
            card = new { inspection.Card.Id, inspection.Card.DebugName },
            changes = changes.Select(change => new
            {
                change.Target.Index,
                change.Target.Key,
                change.Target.DisplayName,
                oldValue = change.Target.CurrentValue,
                newValue = change.NewValue
            })
        };
        File.WriteAllText(
            Path.Combine(backupDirectory, "numeric-edit-backup.json"),
            JsonSerializer.Serialize(manifest, JsonOptions()),
            new UTF8Encoding(false));
        return backupDirectory;
    }

    private static void WriteReport(
        string path,
        NumericEditInspection inspection,
        IReadOnlyList<NumericEditChange> changes,
        bool inPlace,
        string? backupDirectory)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"AbilityEditor {ProductVersion} numeric edit report");
        builder.AppendLine($"Created: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Card: {inspection.Card.Id} | {inspection.Card.DebugName}");
        builder.AppendLine($"Mode: {(inPlace ? "in-place with backup" : "safe copy")}");
        builder.AppendLine($"Abilities source: {inspection.AbilitiesPath}");
        builder.AppendLine($"Templates source: {inspection.TemplatesPath}");
        if (!string.IsNullOrWhiteSpace(backupDirectory))
            builder.AppendLine($"Backup: {backupDirectory}");
        builder.AppendLine();
        builder.AppendLine("Changes:");
        foreach (NumericEditChange change in changes)
        {
            builder.AppendLine(
                $"[{change.Target.Index}] {change.Target.DisplayName}: " +
                $"{change.Target.CurrentValue} -> {change.NewValue}");
            builder.AppendLine($"    {change.Target.Source}: {change.Target.Path}");
        }
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
    }

    private static void WriteRecipe(
        string path,
        NumericEditInspection inspection,
        IReadOnlyList<NumericEditChange> changes,
        bool inPlace,
        string? backupDirectory,
        string originalAbilitiesSha256,
        string originalTemplatesSha256)
    {
        var recipe = new
        {
            format = "AbilityEditor.NumericEdit/v1",
            productVersion = ProductVersion,
            createdAt = DateTimeOffset.Now,
            card = new { inspection.Card.Id, inspection.Card.DebugName },
            mode = inPlace ? "in-place" : "safe-copy",
            backupDirectory,
            sources = new
            {
                abilities = inspection.AbilitiesPath,
                abilitiesSha256 = originalAbilitiesSha256,
                templates = inspection.TemplatesPath,
                templatesSha256 = originalTemplatesSha256
            },
            changes = changes.Select(change => new
            {
                operation = "SetNumericValue",
                change.Target.Index,
                change.Target.Key,
                change.Target.Role,
                change.Target.Source,
                change.Target.Path,
                change.Target.TargetKind,
                change.Target.TemplateField,
                change.Target.VariableId,
                change.Target.NodeId,
                change.Target.NodeType,
                change.Target.FieldName,
                expectedOldValue = change.Target.CurrentValue,
                newValue = change.NewValue
            })
        };
        File.WriteAllText(path, JsonSerializer.Serialize(recipe, JsonOptions()), new UTF8Encoding(false));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static int ReadIntChild(XElement element, string childName, int fallback)
    {
        XElement? child = element.Element(childName);
        return child != null && int.TryParse((string?)child.Attribute("V"), out int value)
            ? value
            : fallback;
    }

    private static int ParseElementValue(XElement element, string path)
    {
        if (!int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new InvalidOperationException($"Значение по пути {path} перестало быть целым числом.");
        return value;
    }

    private static int ParseAttributeValue(XElement element, string attributeName, string path)
    {
        if (!int.TryParse((string?)element.Attribute(attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new InvalidOperationException($"Значение {attributeName} по пути {path} перестало быть целым числом.");
        return value;
    }

    private static void SaveDocumentAtomic(XDocument document, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Не удалось определить выходной каталог.");
        Directory.CreateDirectory(directory);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            document.Save(temporary, SaveOptions.DisableFormatting);
            _ = XDocument.Load(temporary, LoadOptions.PreserveWhitespace);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void CopyFileAtomic(string source, string target)
    {
        string fullTarget = Path.GetFullPath(target);
        string directory = Path.GetDirectoryName(fullTarget)
            ?? throw new InvalidOperationException("Не удалось определить каталог восстановления.");
        Directory.CreateDirectory(directory);
        string temporary = fullTarget + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            _ = XDocument.Load(temporary, LoadOptions.PreserveWhitespace);
            File.Move(temporary, fullTarget, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string NormalizeSelector(string value)
    {
        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray());
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string sanitized = new(value
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        sanitized = Regex.Replace(sanitized, @"\s+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "card" : sanitized;
    }
}
