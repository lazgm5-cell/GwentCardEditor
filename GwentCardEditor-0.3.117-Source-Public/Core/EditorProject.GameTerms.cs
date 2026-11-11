using System.Text.RegularExpressions;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static readonly IReadOnlyDictionary<string, int> CategoryIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["nature"] = 89, ["warfare"] = 102, ["alchemy"] = 33, ["potion"] = 69, ["organic"] = 53,
        ["crime"] = 92, ["raid"] = 103, ["tactic"] = 11, ["spell"] = 20,
        ["human"] = 78, ["bandit"] = 83, ["mage"] = 14, ["soldier"] = 26, ["warrior"] = 86,
        ["witcher"] = 31, ["elf"] = 39, ["dwarf"] = 47, ["dryad"] = 37, ["naiad"] = 110,
        ["beast"] = 12, ["vampire"] = 16, ["insectoid"] = 18, ["wild hunt"] = 10,
        ["ogroid"] = 6, ["relict"] = 7, ["cursed"] = 25, ["knight"] = 79, ["pirate"] = 76,
        ["ship"] = 77, ["druid"] = 85, ["agent"] = 75, ["aristocrat"] = 106, ["treant"] = 90,
        ["machine"] = 41, ["construct"] = 2, ["specter"] = 24, ["dragon"] = 63, ["harpy"] = 64,
        ["firesworn"] = 94, ["crownsplitters"] = 95, ["cutups"] = 97, ["tidecloaks"] = 98,
        ["blindeyes"] = 99, ["salamandra"] = 105
    };

    public string? CategoryTerm(string category,string language)
    {
        if(!CategoryIds.TryGetValue(category, out int id))return null;
        string? text=locales.TryGetValue(language,out var locale)?locale.Values.GetValueOrDefault("card_category_"+id):null;
        return !string.IsNullOrWhiteSpace(text)?text:locales.TryGetValue("en-us",out var english)?english.Values.GetValueOrDefault("card_category_"+id):null;
    }
    private readonly Dictionary<(string Language, string Keyword), string?> gameTerms = new();

    public string? GameTerm(string keyword, string language)
    {
        var key = (language.ToLowerInvariant(), keyword.ToLowerInvariant());
        if (gameTerms.TryGetValue(key, out var known)) return known;
        string? term = null;
        if (locales.TryGetValue(key.Item1, out var locale) && locale.Values.TryGetValue("keyword_" + key.Item2, out string? text))
        {
            var title = Regex.Match(text, @"^\s*(?:<b>\s*)?<[^>]+>([^<]+)</color>", RegexOptions.CultureInvariant);
            if (title.Success) term = title.Groups[1].Value.Trim().TrimEnd(':', '\uFF1A').Trim();
        }
        return gameTerms[key] = string.IsNullOrWhiteSpace(term) ? null : term;
    }
}
