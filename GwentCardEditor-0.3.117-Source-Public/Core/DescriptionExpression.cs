using System.Text.RegularExpressions;

namespace GwentCardEditor;

public static class DescriptionExpression
{
    private static readonly Regex Identifiers = new(@"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*", RegexOptions.CultureInvariant);
    private static readonly Regex Tokens = new(@"\G\s*(?:[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*|[0-9]+(?:\.[0-9]+)?|[()+*/-])", RegexOptions.CultureInvariant);

    public static string Binding(string text, IReadOnlyCollection<string> aliases)
    {
        bool direct = false, formula = false, variant = false, unknown = false;
        foreach (Match placeholder in Regex.Matches(text, @"\{([^{}]+)\}"))
        {
            string expression = placeholder.Groups[1].Value.Trim();
            string[] names = Identifiers.Matches(expression).Select(m => m.Value).ToArray();
            bool exact = names.Any(name => aliases.Contains(name, StringComparer.Ordinal));
            bool differentCase = names.Any(name => !aliases.Contains(name, StringComparer.Ordinal)
                && aliases.Contains(name, StringComparer.OrdinalIgnoreCase));
            if (!exact && !differentCase) continue;
            if (!Valid(expression)) { unknown = true; continue; }
            variant |= differentCase;
            if (exact)
            {
                if (aliases.Contains(expression, StringComparer.Ordinal)) direct = true;
                else formula = true;
            }
        }
        return unknown ? "UnknownExpression" : variant ? "CaseMismatch" : formula ? "Formula" : direct ? "Direct" : "Unlinked";
    }

    private static bool Valid(string expression)
    {
        if (expression.Length is 0 or > 2048) return false;
        var tokens = new List<string>();
        int offset = 0;
        while (offset < expression.Length)
        {
            var match = Tokens.Match(expression, offset);
            if (!match.Success || match.Index != offset || tokens.Count >= 256) return false;
            tokens.Add(match.Value.Trim()); offset += match.Length;
        }
        int position = 0, depth = 0;
        bool Take(string symbol)
        {
            if (position >= tokens.Count || tokens[position] != symbol) return false;
            position++; return true;
        }
        bool Primary()
        {
            while (Take("+") || Take("-")) { }
            if (Take("("))
            {
                if (++depth > 32 || !Sum() || !Take(")")) return false;
                depth--; return true;
            }
            if (position >= tokens.Count || tokens[position] is ")" or "*" or "/") return false;
            position++; return true;
        }
        bool Product()
        {
            if (!Primary()) return false;
            while (Take("*") || Take("/")) if (!Primary()) return false;
            return true;
        }
        bool Sum()
        {
            if (!Product()) return false;
            while (Take("+") || Take("-")) if (!Product()) return false;
            return true;
        }
        return Sum() && position == tokens.Count;
    }
}
