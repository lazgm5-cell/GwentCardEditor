using AbilityAnalyzer.Models;
using System.Text;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public sealed class TemplateModPackResult
{
    public string OutputFile { get; set; } = "";
    public string ReportFile { get; set; } = "";
    public List<string> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
}

public static class TemplateModPackService
{
    private static readonly Dictionary<string, long> StatusMasks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["shield"] = 2,
        ["immunity"] = 256,
        ["doomed"] = 512,
        ["defender"] = 1024,
        ["veil"] = 8192
    };

    private static readonly Dictionary<string, int> Rarities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = 0,
        ["common"] = 1,
        ["rare"] = 2,
        ["epic"] = 4,
        ["legendary"] = 8
    };

    private static readonly Dictionary<string, int> CardSets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["generated"] = 0,
        ["base"] = 1,
        ["starter"] = 10,
        ["master-mirror"] = 15,
        ["master mirror"] = 15
    };

    public static long ResolveStatusMask(string input, long? explicitMask = null)
    {
        if (explicitMask.HasValue)
            return explicitMask.Value;
        if (long.TryParse(input, out long numeric))
            return numeric;
        return StatusMasks.TryGetValue(input.Trim(), out long mask)
            ? mask
            : throw new InvalidOperationException(
                "Неизвестный innate status. Поддерживаются: " +
                string.Join(", ", StatusMasks.Keys.OrderBy(key => key)) +
                ". Также можно указать числовую маску.");
    }

    public static int ResolveRarity(string input)
    {
        if (int.TryParse(input, out int numeric))
            return numeric;
        return Rarities.TryGetValue(input.Trim(), out int rarity)
            ? rarity
            : throw new InvalidOperationException("Неизвестная rarity.");
    }


    public static int ResolveAvailability(string input)
    {
        if (int.TryParse(input, out int numeric))
            return numeric;
        return CardSets.TryGetValue(input.Trim(), out int availability)
            ? availability
            : throw new InvalidOperationException(
                "Неизвестный card set. Используйте generated/base/starter/master-mirror или числовой Availability.");
    }

    public static TemplateModPackResult ApplyAndSave(
        string templatesPath,
        TemplateModPackRecipe recipe,
        string? outputOverride,
        bool overwrite)
    {
        TemplateDocument working = TemplateDocument.Load(templatesPath).DeepClone();
        var result = new TemplateModPackResult();

        foreach (TemplateOperationRecipe operation in recipe.Operations.Where(operation => operation.Enabled))
            ApplyOperation(working, operation, result.Changes, result.Warnings);

        string directory = Path.GetDirectoryName(Path.GetFullPath(templatesPath))
            ?? AppContext.BaseDirectory;
        string output = string.IsNullOrWhiteSpace(outputOverride)
            ? Path.Combine(directory, recipe.OutputFileName)
            : outputOverride;
        string report = Path.GetFullPath(output) + ".report.txt";

        if (!overwrite)
        {
            string[] existing = new[] { Path.GetFullPath(output), report }.Where(File.Exists).ToArray();
            if (existing.Length > 0)
                throw new IOException("Уже существуют выходные файлы:" + Environment.NewLine + string.Join(Environment.NewLine, existing));
        }

        working.SaveAs(output, overwrite);
        TemplateDocument.Load(output); // повторное чтение

        var builder = new StringBuilder();
        builder.AppendLine($"Template mod-pack: {recipe.Name}");
        builder.AppendLine($"Input: {Path.GetFullPath(templatesPath)}");
        builder.AppendLine($"Output: {Path.GetFullPath(output)}");
        builder.AppendLine();
        foreach (string change in result.Changes)
            builder.AppendLine(change);
        if (result.Warnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Warnings:");
            foreach (string warning in result.Warnings)
                builder.AppendLine("WARNING: " + warning);
        }
        File.WriteAllText(report, builder.ToString(), Encoding.UTF8);

        result.OutputFile = Path.GetFullPath(output);
        result.ReportFile = report;
        return result;
    }

    public static void ApplyOperation(
        TemplateDocument document,
        TemplateOperationRecipe operation,
        List<string> changes,
        List<string>? warnings = null)
    {
        if (operation.Cards.Count == 0)
            throw new InvalidOperationException($"Template operation '{operation.Id}' не содержит cards.");

        foreach (string cardInput in operation.Cards)
        {
            CardRecord card = CardResolver.ResolveUnique(document, cardInput);
            XElement template = document.FindById(card.Id)
                ?? throw new InvalidOperationException($"Template {card.Id} не найден.");
            string type = operation.Type.Trim().ToLowerInvariant();

            switch (type)
            {
                case "addinnatestatus":
                case "addtokenmask":
                {
                    long mask = ResolveStatusMask(operation.Status, operation.Mask);
                    XElement tokens = template.Element("Tokens")
                        ?? throw new InvalidDataException($"Template {card.Id} не содержит Tokens.");
                    long oldValue = long.Parse(tokens.Value);
                    long newValue = oldValue | mask;
                    tokens.Value = newValue.ToString();
                    changes.Add($"{card.Id} {card.DebugName}: Tokens {oldValue} -> {newValue} (add {mask})");
                    break;
                }

                case "removeinnatestatus":
                case "removetokenmask":
                {
                    long mask = ResolveStatusMask(operation.Status, operation.Mask);
                    XElement tokens = template.Element("Tokens")
                        ?? throw new InvalidDataException($"Template {card.Id} не содержит Tokens.");
                    long oldValue = long.Parse(tokens.Value);
                    long newValue = oldValue & ~mask;
                    tokens.Value = newValue.ToString();
                    changes.Add($"{card.Id} {card.DebugName}: Tokens {oldValue} -> {newValue} (remove {mask})");
                    break;
                }

                case "setrarity":
                {
                    int rarity = operation.Rarity
                        ?? (!string.IsNullOrWhiteSpace(operation.RarityName)
                            ? ResolveRarity(operation.RarityName)
                            : throw new InvalidOperationException("SetRarity требует rarity или rarityName."));
                    XElement field = template.Element("Rarity")
                        ?? throw new InvalidDataException($"Template {card.Id} не содержит Rarity.");
                    string oldValue = field.Value;
                    field.Value = rarity.ToString();
                    changes.Add($"{card.Id} {card.DebugName}: Rarity {oldValue} -> {rarity}");
                    break;
                }

                case "setavailability":
                {
                    int availability = operation.Availability
                        ?? (!string.IsNullOrWhiteSpace(operation.CardSet)
                            ? ResolveAvailability(operation.CardSet)
                            : throw new InvalidOperationException("SetAvailability требует availability или cardSet."));
                    string oldValue = (string?)template.Attribute("Availability") ?? "";
                    template.SetAttributeValue("Availability", availability);
                    changes.Add($"{card.Id} {card.DebugName}: Availability {oldValue} -> {availability}");
                    break;
                }

                case "addcategorymask":
                case "removecategorymask":
                case "addcategory":
                case "removecategory":
                {
                    CategoryDefinition? category = null;
                    if (!string.IsNullOrWhiteSpace(operation.Category))
                        category = CategoryRegistry.Resolve(operation.Category);

                    int categorySlot = category?.Slot
                        ?? operation.CategorySlot
                        ?? throw new InvalidOperationException(
                            "Укажите Category canonical name или CategorySlot.");
                    if (categorySlot < 0 || categorySlot > 2)
                        throw new InvalidOperationException("CategorySlot должен быть 0, 1 или 2.");

                    ulong categoryMask = category?.Mask
                        ?? operation.CategoryMask
                        ?? throw new InvalidOperationException(
                            "Укажите Category canonical name или CategoryMask.");

                    string fieldName = operation.CategoryField.Trim().ToLowerInvariant() switch
                    {
                        "primary" => "PrimaryCategory",
                        "primarycategory" => "PrimaryCategory",
                        "additional" => "Categories",
                        "categories" => "Categories",
                        _ => throw new InvalidOperationException(
                            "CategoryField должен быть PrimaryCategory/primary или Categories/additional.")
                    };
                    XElement field = template.Element(fieldName)
                        ?? throw new InvalidDataException($"Template {card.Id} не содержит {fieldName}.");
                    XElement slot = field.Element("e" + categorySlot)
                        ?? throw new InvalidDataException($"{fieldName}/e{categorySlot} не найден.");
                    ulong oldValue = ulong.Parse((string?)slot.Attribute("V") ?? "0");
                    bool add = type is "addcategorymask" or "addcategory";
                    ulong newValue = add
                        ? oldValue | categoryMask
                        : oldValue & ~categoryMask;
                    slot.SetAttributeValue("V", newValue);
                    string categoryLabel = category == null
                        ? $"mask {categoryMask}"
                        : $"{category.Name} (mask {categoryMask})";
                    changes.Add(
                        $"{card.Id} {card.DebugName}: {fieldName}/e{categorySlot} " +
                        $"{oldValue} -> {newValue} ({(add ? "add" : "remove")} {categoryLabel})");
                    break;
                }

                case "setcardtype":
                {
                    CardTypeDefinition targetType = operation.CardTypeValue.HasValue
                        ? CardTypeRegistry.Resolve(operation.CardTypeValue.Value)
                        : CardTypeRegistry.Resolve(operation.CardType);
                    XElement field = template.Element("Type")
                        ?? throw new InvalidDataException($"Template {card.Id} не содержит Type.");
                    int oldValue = int.TryParse(field.Value, out int parsedOldValue)
                        ? parsedOldValue
                        : throw new InvalidDataException(
                            $"Template {card.Id} содержит некорректный Type='{field.Value}'.");
                    CardTypeDefinition oldType = CardTypeRegistry.Resolve(oldValue);
                    field.Value = targetType.Value.ToString();
                    changes.Add(
                        $"{card.Id} {card.DebugName}: Type {oldValue} ({oldType.Name}) -> " +
                        $"{targetType.Value} ({targetType.Name})");
                    AddCardTypeWarnings(template, card, oldType, targetType, warnings);
                    break;
                }

                case "configureorder":
                {
                    int charges = operation.InitialTapCharges
                        ?? throw new InvalidOperationException("ConfigureOrder требует initialTapCharges.");
                    SetElement(template, "TapActive", "True", changes, card);
                    SetElement(template, "InitalTapCharges", charges.ToString(), changes, card);
                    SetElement(template, "SummoningSickness", operation.Zeal == true ? "False" : "True", changes, card);
                    SetElement(template, "VisibleTapCharges", operation.VisibleTapCharges == true ? "True" : "False", changes, card);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Неподдерживаемый Template operation Type='{operation.Type}'.");
            }
        }
    }

    private static void AddCardTypeWarnings(
        XElement template,
        CardRecord card,
        CardTypeDefinition oldType,
        CardTypeDefinition targetType,
        List<string>? warnings)
    {
        if (warnings == null)
            return;

        warnings.Add(
            $"{card.Id} {card.DebugName}: изменён только Templates.xml/Type. " +
            "Ability, localization и остальные поля Template оставлены без изменений.");

        int power = ParseElementInt(template, "Power");
        int armor = ParseElementInt(template, "Armor");
        int charges = ParseElementInt(template, "InitalTapCharges");
        bool tapActive = ParseElementBool(template, "TapActive");

        if (targetType.Value == 2)
        {
            if (power != 0 || armor != 0)
                warnings.Add(
                    $"{card.Id} {card.DebugName}: Special сохраняет Power={power} и Armor={armor}; " +
                    "при необходимости обнулите их отдельной операцией.");
            if (tapActive || charges > 0)
                warnings.Add(
                    $"{card.Id} {card.DebugName}: Special сохраняет Order-параметры " +
                    $"TapActive={tapActive}, InitalTapCharges={charges}.");
        }
        else if (targetType.Value == 4)
        {
            if (power <= 0)
                warnings.Add(
                    $"{card.Id} {card.DebugName}: новый Unit имеет Power={power}. " +
                    "Задайте положительную силу отдельной операцией.");
        }
        else if (targetType.Value == 8)
        {
            if (power != 0 || armor != 0)
                warnings.Add(
                    $"{card.Id} {card.DebugName}: Artifact сохраняет Power={power} и Armor={armor}; " +
                    "обычно эти значения равны 0.");
        }
        else if (targetType.Value is 0 or 1 or 16)
        {
            warnings.Add(
                $"{card.Id} {card.DebugName}: тип {targetType.Name} специализирован. " +
                "Проверьте Placement, способность и игровые системные поля вручную.");
        }
        else
        {
            warnings.Add(
                $"{card.Id} {card.DebugName}: Type={targetType.Value} не входит в известный реестр. " +
                "Это экспериментальное числовое значение.");
        }

        if (oldType.Value == targetType.Value)
            warnings.Add($"{card.Id} {card.DebugName}: исходный и целевой тип совпадают.");
    }

    private static int ParseElementInt(XElement template, string name)
    {
        XElement? element = template.Element(name);
        return element != null && int.TryParse(element.Value, out int value) ? value : 0;
    }

    private static bool ParseElementBool(XElement template, string name)
    {
        XElement? element = template.Element(name);
        return element != null && bool.TryParse(element.Value, out bool value) && value;
    }

    private static void SetElement(
        XElement template,
        string name,
        string value,
        List<string> changes,
        CardRecord card)
    {
        XElement field = template.Element(name)
            ?? throw new InvalidDataException($"Template {card.Id} не содержит {name}.");
        string oldValue = field.Value;
        field.Value = value;
        changes.Add($"{card.Id} {card.DebugName}: {name} {oldValue} -> {value}");
    }
}
