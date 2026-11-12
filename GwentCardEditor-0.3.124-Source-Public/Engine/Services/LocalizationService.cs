using AbilityAnalyzer.Models;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static partial class LocalizationService
{
    public static IReadOnlyList<InjectableLocalizationBinding> FindInjectableBindings(
        AbilityDocument abilities,
        int cardAbilityId)
    {
        XElement ability = abilities.FindRequiredCardAbility(cardAbilityId);
        return ability.Element("TemporaryVariables")?
            .Elements()
            .Where(variable => string.Equals(
                (string?)variable.Attribute("Type"),
                "InjectableAbilityVar",
                StringComparison.Ordinal))
            .Select(variable => new InjectableLocalizationBinding
            {
                VariableName = (string?)variable.Attribute("Name") ?? "",
                InjectableAbilityId = (int?)variable.Attribute("V") ?? 0
            })
            .Where(binding => binding.InjectableAbilityId > 0 && binding.VariableName.Length > 0)
            .ToList()
            ?? new List<InjectableLocalizationBinding>();
    }

    public static IReadOnlyList<string> FindUnknownPlaceholders(
        AbilityDocument abilities,
        int cardAbilityId,
        string tooltip)
    {
        XElement ability = abilities.FindRequiredCardAbility(cardAbilityId);
        return FindUnknownPlaceholders(ability, tooltip);
    }

    public static IReadOnlyList<string> FindUnknownInjectablePlaceholders(
        AbilityDocument abilities,
        int injectableAbilityId,
        string tooltip)
    {
        XElement ability = abilities.Document.Root?
            .Elements("Ability")
            .FirstOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("Type"),
                    "InjectableAbility",
                    StringComparison.Ordinal)
                && (int?)element.Attribute("Id") == injectableAbilityId)
            ?? throw new InvalidOperationException(
                $"InjectableAbility Id={injectableAbilityId} не найдена в текущем Abilities.xml.");

        return FindUnknownPlaceholders(ability, tooltip);
    }

    public static IReadOnlyDictionary<int, string> ReadCardNames(string inputPath)
    {
        var result = new Dictionary<int, string>();
        if (!File.Exists(inputPath))
            return result;

        foreach (string line in File.ReadLines(inputPath, Encoding.UTF8))
        {
            int separator = line.IndexOf(';');
            if (separator <= 0)
                continue;

            string key = line[..separator].TrimStart('\uFEFF');
            if (!key.EndsWith("_name", StringComparison.Ordinal))
                continue;

            string idText = key[..^"_name".Length];
            if (!int.TryParse(idText, out int cardId) || cardId <= 0)
                continue;

            string value = line[(separator + 1)..].Trim();
            if (value.Length == 0
                || value.Equals("n/a", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Ignore.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[cardId] = value;
        }

        return result;
    }

    public static string? ReadValue(string inputPath, string key)
    {
        foreach (string line in File.ReadLines(inputPath, Encoding.UTF8))
        {
            int separator = line.IndexOf(';');
            if (separator < 0)
                continue;
            if (string.Equals(line[..separator], key, StringComparison.Ordinal))
                return line[(separator + 1)..];
        }
        return null;
    }

    private static IReadOnlyList<string> FindUnknownPlaceholders(
        XElement ability,
        string tooltip)
    {
        HashSet<string> known = ability
            .Elements()
            .Where(element => element.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
            .SelectMany(element => element.Elements())
            .Select(variable => (string?)variable.Attribute("Name") ?? "")
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Built-in placeholders supplied by Template/game systems rather than AbilityVariables.
        foreach (string builtIn in new[]
        {
            "Charges", "InitialTimer", "Provision", "Power", "Armor", "Template.Provision"
        })
        {
            known.Add(builtIn);
        }

        return PlaceholderRegex()
            .Matches(tooltip)
            .Select(match => match.Groups[1].Value)
            .Where(name => !IsKnownPlaceholder(name, known))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsKnownPlaceholder(string placeholder, HashSet<string> known)
    {
        if (known.Contains(placeholder))
            return true;
        if (placeholder.Contains("B.P.", StringComparison.OrdinalIgnoreCase))
            return true;

        int modifierSeparator = placeholder.IndexOf('-');
        if (modifierSeparator > 0 && known.Contains(placeholder[..modifierSeparator]))
            return true;

        return false;
    }

    public static void ApplyEditsAndSave(
        string inputPath,
        IReadOnlyDictionary<string, string> edits,
        string outputPath,
        bool overwrite)
    {
        if (edits.Count == 0)
            throw new InvalidOperationException("Нет изменений локализации.");

        byte[] bytes = File.ReadAllBytes(inputPath);
        bool hasBom = bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF;
        string text = File.ReadAllText(inputPath, Encoding.UTF8);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool endsWithNewline = text.EndsWith("\n", StringComparison.Ordinal);
        List<string> lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (endsWithNewline && lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var remaining = new Dictionary<string, string>(edits, StringComparer.Ordinal);
        for (int index = 0; index < lines.Count; index++)
        {
            int separator = lines[index].IndexOf(';');
            if (separator < 0)
                continue;
            string key = lines[index][..separator];
            if (!remaining.TryGetValue(key, out string? value))
                continue;
            ValidateLocalizationValue(value);
            lines[index] = key + ";" + value;
            remaining.Remove(key);
        }

        foreach ((string key, string value) in remaining)
        {
            ValidateLocalizationValue(value);
            int insertionIndex = FindInsertionIndex(lines, key);
            lines.Insert(insertionIndex, key + ";" + value);
        }

        NormalizeGeneratedKeyPlacement(lines);

        string outputText = string.Join(newline, lines) + (endsWithNewline ? newline : "");
        string fullOutput = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutput) && !overwrite)
            throw new IOException($"Выходной файл уже существует: {fullOutput}");
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput)!);

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);
        File.WriteAllText(fullOutput, outputText, encoding);
    }

    public static void ValidateLocalizationValue(string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidOperationException(
                "Значение локализации должно находиться в одной физической строке. " +
                "Используйте два символа \\n для переноса текста в игре.");
        }
    }

    private static int FindInsertionIndex(IReadOnlyList<string> lines, string key)
    {
        if (!IsGeneratedLocalizationKey(key))
            return lines.Count;

        return FindGeneratedKeyInsertionIndex(lines);
    }

    private static void NormalizeGeneratedKeyPlacement(List<string> lines)
    {
        int keywordStart = FindGeneratedKeyInsertionIndex(lines);
        if (keywordStart >= lines.Count)
            return;

        var misplaced = new List<string>();
        for (int index = lines.Count - 1; index >= keywordStart; index--)
        {
            if (!TryReadKey(lines[index], out string key) || !IsGeneratedLocalizationKey(key))
                continue;

            misplaced.Add(lines[index]);
            lines.RemoveAt(index);
        }

        if (misplaced.Count == 0)
            return;

        misplaced.Reverse();
        int insertionIndex = FindGeneratedKeyInsertionIndex(lines);
        foreach (string line in misplaced)
            lines.Insert(insertionIndex++, line);
    }

    private static int FindGeneratedKeyInsertionIndex(IReadOnlyList<string> lines)
    {
        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith("keyword_", StringComparison.Ordinal))
                continue;

            while (index > 0 && string.IsNullOrWhiteSpace(lines[index - 1]))
                index--;
            return index;
        }

        return lines.Count;
    }

    private static bool TryReadKey(string line, out string key)
    {
        int separator = line.IndexOf(';');
        if (separator <= 0)
        {
            key = "";
            return false;
        }

        key = line[..separator];
        return true;
    }

    private static bool IsGeneratedLocalizationKey(string key)
    {
        int separator = key.IndexOf('_');
        if (separator <= 0)
            return false;

        return key[..separator].All(char.IsDigit) &&
            (key.EndsWith("_name_ia", StringComparison.Ordinal) ||
             key.EndsWith("_tooltip_ia", StringComparison.Ordinal) ||
             key.EndsWith("_name_b", StringComparison.Ordinal) ||
             key.EndsWith("_tooltip_b", StringComparison.Ordinal) ||
             key.EndsWith("_name_lt", StringComparison.Ordinal) ||
             key.EndsWith("_tooltip_lt", StringComparison.Ordinal) ||
             key.EndsWith("_name_sa", StringComparison.Ordinal) ||
             key.EndsWith("_tooltip_sa", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderRegex();
}
