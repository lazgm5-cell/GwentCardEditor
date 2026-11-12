using AbilityAnalyzer.Models;
using System.Globalization;
using System.Text;

namespace AbilityAnalyzer.Services;

public static class CardResolver
{
    public static CardRecord ResolveUnique(TemplateDocument templates, string input)
    {
        string value = input.Trim().Trim('"');
        if (int.TryParse(value, out int id))
        {
            CardRecord? byId = templates.Cards.FirstOrDefault(card => card.Id == id);
            return byId ?? throw new InvalidOperationException($"Template Id={id} не найден.");
        }

        List<CardRecord> exact = FindExactMatches(templates, value);
        if (exact.Count == 1)
            return exact[0];
        if (exact.Count > 1)
        {
            throw new InvalidOperationException(
                "Точное имя соответствует нескольким картам. Используйте ID: " +
                string.Join(", ", exact.Select(FormatCard)));
        }

        List<CardRecord> contains = FindPartialMatches(templates, value, 20);
        if (contains.Count > 0)
        {
            throw new InvalidOperationException(
                "Точного совпадения нет. Частичное имя не выбирается автоматически. " +
                "Используйте точное английское/русское название или ID. Кандидаты: " +
                string.Join(", ", contains.Select(FormatCard)));
        }

        IReadOnlyList<CardRecord> suggestions = FindSuggestions(templates, value, 5);
        string suffix = suggestions.Count == 0
            ? ""
            : " Возможно: " + string.Join(", ", suggestions.Select(FormatCard));
        throw new InvalidOperationException($"Карта не найдена: {input}.{suffix}");
    }

    public static List<CardRecord> FindExactMatches(TemplateDocument templates, string input)
    {
        string normalized = NormalizeName(input);
        return templates.Cards
            .Where(card => SearchNames(card).Any(name => NormalizeName(name) == normalized))
            .OrderBy(card => card.DebugName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(card => card.Id)
            .ToList();
    }

    public static List<CardRecord> FindPartialMatches(
        TemplateDocument templates,
        string input,
        int limit = 20)
    {
        string normalized = NormalizeName(input);
        if (string.IsNullOrWhiteSpace(normalized))
            return new List<CardRecord>();

        string[] queryTokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return templates.Cards
            .Select(card =>
            {
                string[] names = SearchNames(card)
                    .Select(NormalizeName)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                int rank = names.Any(name => name.StartsWith(normalized, StringComparison.Ordinal))
                    ? 0
                    : names.Any(name => name.Contains(normalized, StringComparison.Ordinal))
                        ? 1
                        : names.Any(name => queryTokens.All(token => name.Contains(token, StringComparison.Ordinal)))
                            ? 2
                            : 3;
                int bestLength = names.Where(name => rank < 3).Select(name => name.Length).DefaultIfEmpty(int.MaxValue).Min();
                return new { Card = card, Rank = rank, BestLength = bestLength };
            })
            .Where(item => item.Rank < 3)
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.BestLength)
            .ThenBy(item => item.Card.DebugName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Card.Id)
            .Take(limit)
            .Select(item => item.Card)
            .ToList();
    }

    public static IReadOnlyList<CardRecord> FindSuggestions(
        TemplateDocument templates,
        string input,
        int limit = 5)
    {
        string normalized = NormalizeName(input);
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<CardRecord>();

        int threshold = Math.Max(2, normalized.Length / 3);
        return templates.Cards
            .Select(card => new
            {
                Card = card,
                Distance = SearchNames(card)
                    .Select(name => LevenshteinDistance(normalized, NormalizeName(name)))
                    .DefaultIfEmpty(int.MaxValue)
                    .Min()
            })
            .Where(item => item.Distance <= threshold)
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Card.DebugName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Card.Id)
            .Take(limit)
            .Select(item => item.Card)
            .ToList();
    }

    public static string FormatCard(CardRecord card)
    {
        if (string.IsNullOrWhiteSpace(card.RussianName)
            || NormalizeName(card.RussianName) == NormalizeName(card.DebugName))
        {
            return $"{card.Id} | {card.DebugName}";
        }

        return $"{card.Id} | {card.DebugName} | {card.RussianName}";
    }

    public static string NormalizeName(string value)
    {
        string decomposed = value.Trim().Trim('"').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool previousWasSpace = true;
        foreach (char character in decomposed)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<string> SearchNames(CardRecord card)
    {
        if (!string.IsNullOrWhiteSpace(card.DebugName))
            yield return card.DebugName;
        if (!string.IsNullOrWhiteSpace(card.RussianName))
            yield return card.RussianName;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        if (left.Length == 0)
            return right.Length;
        if (right.Length == 0)
            return left.Length;

        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= right.Length; j++)
            {
                int substitution = previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1);
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
