using AbilityAnalyzer.Models;

namespace AbilityAnalyzer.Services;

public static class CardTypeRegistry
{
    private static readonly IReadOnlyList<CardTypeDefinition> Definitions =
        new List<CardTypeDefinition>
        {
            new()
            {
                Name = "faction",
                Value = 0,
                Description = "Faction/header placeholder used by built-in faction templates.",
                Aliases = new[] { "none", "header", "фракция" }
            },
            new()
            {
                Name = "leader",
                Value = 1,
                Description = "Leader ability card.",
                Aliases = new[] { "лидер" }
            },
            new()
            {
                Name = "special",
                Value = 2,
                Description = "One-shot special card.",
                Aliases = new[] { "spell", "особая", "особая карта" }
            },
            new()
            {
                Name = "unit",
                Value = 4,
                Description = "Unit card with power.",
                Aliases = new[] { "отряд", "существо" }
            },
            new()
            {
                Name = "artifact",
                Value = 8,
                Description = "Persistent artifact card.",
                Aliases = new[] { "артефакт" }
            },
            new()
            {
                Name = "stratagem",
                Value = 16,
                Description = "Stratagem card.",
                Aliases = new[] { "stratagems", "стратагема" }
            }
        };

    private static readonly Dictionary<string, CardTypeDefinition> ByName = BuildNameMap();
    private static readonly Dictionary<int, CardTypeDefinition> ByValue =
        Definitions.ToDictionary(definition => definition.Value);

    public static IReadOnlyList<CardTypeDefinition> All => Definitions;

    public static bool TryResolve(string input, out CardTypeDefinition definition)
    {
        string normalized = input.Trim();
        if (ByName.TryGetValue(normalized, out definition!))
            return true;

        if (int.TryParse(normalized, out int numeric))
        {
            if (numeric < 0)
                throw new InvalidOperationException("Card Type не может быть отрицательным.");

            if (ByValue.TryGetValue(numeric, out definition!))
                return true;

            definition = new CardTypeDefinition
            {
                Name = $"numeric-{numeric}",
                Value = numeric,
                Description = "Unknown or experimental numeric card type."
            };
            return true;
        }

        definition = null!;
        return false;
    }

    public static CardTypeDefinition Resolve(string input)
    {
        if (TryResolve(input, out CardTypeDefinition definition))
            return definition;

        throw new InvalidOperationException(
            "Неизвестный тип карты. Используйте " +
            string.Join(", ", Definitions.Select(definition => definition.Name)) +
            " или числовое значение.");
    }

    public static CardTypeDefinition Resolve(int value)
    {
        if (value < 0)
            throw new InvalidOperationException("Card Type не может быть отрицательным.");

        return ByValue.TryGetValue(value, out CardTypeDefinition? definition)
            ? definition
            : new CardTypeDefinition
            {
                Name = $"numeric-{value}",
                Value = value,
                Description = "Unknown or experimental numeric card type."
            };
    }

    public static string Describe(CardTypeDefinition definition) =>
        $"{definition.Name} -> Type={definition.Value}";

    private static Dictionary<string, CardTypeDefinition> BuildNameMap()
    {
        var result = new Dictionary<string, CardTypeDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (CardTypeDefinition definition in Definitions)
        {
            result[definition.Name] = definition;
            foreach (string alias in definition.Aliases)
                result[alias] = definition;
        }
        return result;
    }
}
