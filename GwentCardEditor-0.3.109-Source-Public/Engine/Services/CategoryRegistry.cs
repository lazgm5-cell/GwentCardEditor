using AbilityAnalyzer.Models;
using System.Text;

namespace AbilityAnalyzer.Services;

/// <summary>
/// Verified canonical English category names and their Templates.xml encodings.
/// A category's slot is part of its encoding: the same numeric mask in a
/// different e-slot may represent a different category.
/// </summary>
public static class CategoryRegistry
{
    private static readonly CategoryDefinition[] Definitions =
    {
        // Special-card categories.
        Define("nature", 1, 33_554_432UL),
        Define("warfare", 1, 274_877_906_944UL),
        Define("alchemy", 0, 8_589_934_592UL),
        Define("potion", 1, 32UL, "potions"),
        Define("organic", 0, 9_007_199_254_740_992UL),
        Define("crime", 1, 268_435_456UL),
        Define("raid", 1, 549_755_813_888UL),
        Define("tactic", 0, 2_048UL, "tactics"),
        Define("spell", 0, 1_048_576UL, "spells"),

        // Unit and tribal categories verified against bundled Templates.xml.
        Define("human", 1, 16_384UL),
        Define("bandit", 1, 524_288UL),
        Define("mage", 0, 16_384UL),
        Define("soldier", 0, 67_108_864UL),
        Define("warrior", 1, 4_194_304UL),
        Define("witcher", 0, 2_147_483_648UL),
        Define("elf", 0, 549_755_813_888UL),
        Define("dwarf", 0, 140_737_488_355_328UL),
        Define("dryad", 0, 137_438_953_472UL),
        Define("naiad", 1, 70_368_744_177_664UL),
        Define("beast", 0, 4_096UL),
        Define("vampire", 0, 65_536UL),
        Define("insectoid", 0, 262_144UL),
        Define("wild hunt", 0, 1_024UL),
        Define("ogroid", 0, 64UL),
        Define("relict", 0, 128UL),
        Define("cursed", 0, 33_554_432UL),
        Define("knight", 1, 32_768UL),
        Define("pirate", 1, 4_096UL),
        Define("ship", 1, 8_192UL),
        Define("druid", 1, 2_097_152UL),
        Define("agent", 1, 2_048UL),
        Define("aristocrat", 1, 4_398_046_511_104UL),
        Define("treant", 1, 67_108_864UL),
        Define("machine", 0, 2_199_023_255_552UL),
        Define("construct", 0, 4UL),
        Define("specter", 0, 16_777_216UL, "spectre"),
        Define("dragon", 0, 9_223_372_036_854_775_808UL),

        // Syndicate gangs: category index n maps to bit n-64 in e1.
        Define("firesworn", 1, 1_073_741_824UL),
        Define("crownsplitters", 1, 2_147_483_648UL),
        Define("cutups", 1, 8_589_934_592UL),
        Define("tidecloaks", 1, 17_179_869_184UL),
        Define("blindeyes", 1, 34_359_738_368UL),
        Define("salamandra", 1, 2_199_023_255_552UL),

        // Legacy category still understood by the game data.
        Define("harpy", 1, 1UL)
    };

    private static readonly Dictionary<string, CategoryDefinition> Lookup = BuildLookup();

    public static IReadOnlyList<CategoryDefinition> All => Definitions;

    public static bool TryResolve(string input, out CategoryDefinition category)
    {
        return Lookup.TryGetValue(Normalize(input), out category!);
    }

    public static CategoryDefinition Resolve(string input)
    {
        if (TryResolve(input, out CategoryDefinition category))
            return category;

        throw new InvalidOperationException(
            $"Неизвестная категория '{input}'. Поддерживаются canonical English names: " +
            string.Join(", ", Definitions.Select(item => item.Name).OrderBy(name => name)) +
            ". Также можно указать числовую mask и slot вручную.");
    }

    public static string Describe(CategoryDefinition category)
    {
        return $"{category.Name} -> slot e{category.Slot}, mask {category.Mask}";
    }

    private static CategoryDefinition Define(
        string name,
        int slot,
        ulong mask,
        params string[] aliases)
    {
        return new CategoryDefinition
        {
            Name = name,
            Slot = slot,
            Mask = mask,
            Aliases = aliases
        };
    }

    private static Dictionary<string, CategoryDefinition> BuildLookup()
    {
        var result = new Dictionary<string, CategoryDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (CategoryDefinition definition in Definitions)
        {
            AddKey(result, definition.Name, definition);
            foreach (string alias in definition.Aliases)
                AddKey(result, alias, definition);
        }
        return result;
    }

    private static void AddKey(
        IDictionary<string, CategoryDefinition> lookup,
        string key,
        CategoryDefinition definition)
    {
        string normalized = Normalize(key);
        if (!lookup.TryAdd(normalized, definition))
            throw new InvalidOperationException($"Duplicate category alias '{key}'.");
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }
        return builder.ToString();
    }
}
