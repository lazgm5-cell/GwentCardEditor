using System.Globalization;
using System.Text.RegularExpressions;

namespace GwentCardEditor;

public enum CardGroup { Cards, Tokens, All, Leaders }
public enum CardSort { Name, Rarity, Power, Provision, Id }

public static class CardCatalog
{
    private static readonly HashSet<int> HiddenIds = [202290, 202395, 202394, 203190, 202570, 202571];
    private static readonly HashSet<int> CatalogTokenIds =
    [
        112403,112404,200320,201725,201731,201737,202566,202662,
        202700,202701,202702,202788,202789,202790,202926,203021,
        203077,203122,203124,203127,203170,203179,203180,203181,
        203182,203183,203184,203185,203188,203203,203237,203238
    ];
    // Catalog grouping must not change the game's token bit or editing contracts.
    public static bool IsCatalogToken(CardEntry card)=>card.IsToken || CatalogTokenIds.Contains(card.Id);
    public static bool IsService(CardEntry card)=>card.Type==0;
    public static bool IsHidden(CardEntry card) => IsService(card) || IsHidden(card.Id, card.DebugName);
    internal static bool IsHidden(int id, string debugName) => HiddenIds.Contains(id)
        || Regex.IsMatch(debugName, @"\bdata[\s_-]*only[\s_-]*cards?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static string NameSortKey(string name)
    {
        const string quotes = "\"'\u00ab\u00bb\u2018\u2019\u201a\u201b\u201c\u201d\u201e\u201f\u2039\u203a\u300c\u300d\u300e\u300f\uff02\uff07";
        string key = name.Trim();
        while (key.Length > 0 && quotes.Contains(key[0])) key = key[1..].TrimStart();
        while (key.Length > 0 && quotes.Contains(key[^1])) key = key[..^1].TrimEnd();
        return key;
    }

    public static IReadOnlyList<CardEntry> Query(EditorProject project, int faction, string language,
        string query = "", CardGroup group = CardGroup.Cards, bool showHidden = false,
        CardSort sort = CardSort.Name, bool descending = false, Func<CardEntry, CardEntry>? projection = null)
    {
        StringComparer comparer = StringComparer.Create(CultureInfo.GetCultureInfo(language), true);
        var rows = project.Cards.Where(c => !IsService(c) && !c.IsTutorial && (c.Faction == faction || c.SecondaryFaction == faction))
            .Where(c => group switch { CardGroup.All => true, CardGroup.Tokens => IsCatalogToken(c),
                CardGroup.Leaders => c.Type == 1 && !IsCatalogToken(c), _ => !IsCatalogToken(c) && c.Type != 1 })
            .Where(c => showHidden || !IsHidden(c))
            .Where(c => query.Length == 0 || c.Id.ToString(CultureInfo.InvariantCulture).Contains(query)
                || comparer.Compare(project.Name(c.Id, language), query) == 0
                || CultureInfo.GetCultureInfo(language).CompareInfo.IndexOf(project.Name(c.Id, language), query, CompareOptions.IgnoreCase) >= 0
                || c.DebugName.Contains(query, StringComparison.OrdinalIgnoreCase));
        if (projection != null) rows = rows.Select(projection);
        Func<CardEntry, int> number = sort switch
        {
            CardSort.Rarity => c => c.Rarity, CardSort.Power => c => c.Power,
            CardSort.Provision => c => c.Provision, _ => c => c.Id
        };
        IOrderedEnumerable<CardEntry> ordered = sort == CardSort.Name
            ? descending ? rows.OrderByDescending(c => NameSortKey(project.Name(c.Id, language)), comparer) : rows.OrderBy(c => NameSortKey(project.Name(c.Id, language)), comparer)
            : descending ? rows.OrderByDescending(number) : rows.OrderBy(number);
        return ordered.ThenBy(c => c.Id).ToArray();
    }
}
