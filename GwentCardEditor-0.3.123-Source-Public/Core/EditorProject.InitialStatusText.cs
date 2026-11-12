namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static readonly IReadOnlyDictionary<string, string> InitialStatusHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru-ru"] = "Начальные статусы",
            ["en-us"] = "Starting statuses",
            ["zh-cn"] = "初始状态",
            ["pl-pl"] = "Statusy początkowe",
            ["de-de"] = "Anfängliche Status",
            ["pt-br"] = "Status iniciais",
            ["fr-fr"] = "Statuts initiaux",
            ["ja-jp"] = "初期ステータス",
            ["it-it"] = "Status iniziali",
            ["es-es"] = "Estados iniciales",
            ["es-mx"] = "Estados iniciales",
            ["zh-tw"] = "初始狀態",
            ["ko-kr"] = "초기 상태"
        };

    private string InitialStatusPrefix(string language, IReadOnlyDictionary<string, bool> values)
    {
        string[] terms = InitialStatusMasks.Where(s => values.GetValueOrDefault(s.Key))
            .Select(s => $"<keyword={s.Key}>{GameTerm(s.Key, language) ?? GameTerm(s.Key, "en-us") ?? s.Key}</keyword>")
            .ToArray();
        if (terms.Length == 0) return "";
        string header = InitialStatusHeaders.GetValueOrDefault(language, InitialStatusHeaders["en-us"]);
        return header + ": " + string.Join(", ", terms) + "\\n";
    }

    private string CurrentInitialStatusPrefix(int id, string language)
    {
        int mask = Int(Template(id), "Tokens");
        return InitialStatusPrefix(language, InitialStatusMasks.ToDictionary(s => s.Key, s => mask >= 0 && (mask & s.Value) != 0));
    }

    private string StripCurrentInitialStatusPrefix(int id, string language, string text, out bool stripped)
    {
        string prefix = CurrentInitialStatusPrefix(id, language);
        stripped = prefix.Length > 0 && text.StartsWith(prefix, StringComparison.Ordinal);
        return stripped ? text[prefix.Length..] : text;
    }

    private CardEdit NormalizeInitialStatusText(int id, CardEdit edit)
    {
        if (edit.Statuses == null || edit.Statuses.Count == 0) return edit;
        var current = InitialStatuses(id).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (edit.Statuses.Keys.Any(key => !current.ContainsKey(key))) throw new InvalidOperationException("UnsupportedFields");
        var desired = new Dictionary<string, bool>(current, StringComparer.Ordinal);
        foreach (var (key, value) in edit.Statuses) desired[key] = value;
        if (current.All(pair => desired[pair.Key] == pair.Value)) return edit;

        var texts = edit.Texts.ToDictionary(p => p.Key,
            p => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(p.Value), StringComparer.OrdinalIgnoreCase);
        foreach (string language in AvailableLanguages.Where(l => HasText(id, l, "tooltip")))
        {
            var fields = texts.TryGetValue(language, out var existing)
                ? new Dictionary<string, string>(existing, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            string tooltip = fields.GetValueOrDefault("tooltip") ?? Text(id, language, "tooltip");
            string oldPrefix = InitialStatusPrefix(language, current);
            if (oldPrefix.Length > 0 && tooltip.StartsWith(oldPrefix, StringComparison.Ordinal))
                tooltip = tooltip[oldPrefix.Length..];
            fields["tooltip"] = InitialStatusPrefix(language, desired) + tooltip;
            texts[language] = fields;
        }
        return edit with { Texts = texts };
    }

    private string InitialStatusTextBaseline(int id, string language, string baseline, CardEdit edit)
    {
        if (edit.Statuses == null) return baseline;
        var current = InitialStatuses(id).ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        if (edit.Statuses.Keys.Any(key => !current.ContainsKey(key))) throw new InvalidOperationException("UnsupportedFields");
        var desired = new Dictionary<string, bool>(current, StringComparer.Ordinal);
        foreach (var (key, value) in edit.Statuses) desired[key] = value;

        string oldPrefix = InitialStatusPrefix(language, current);
        bool containsGeneratedPrefix = oldPrefix.Length > 0 && baseline.StartsWith(oldPrefix, StringComparison.Ordinal);
        bool changed = current.Any(pair => desired[pair.Key] != pair.Value);
        if (!changed && !containsGeneratedPrefix) return baseline;
        if (containsGeneratedPrefix) baseline = baseline[oldPrefix.Length..];
        return InitialStatusPrefix(language, desired) + baseline;
    }
}
