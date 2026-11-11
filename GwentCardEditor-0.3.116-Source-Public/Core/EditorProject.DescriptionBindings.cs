using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Net;

namespace GwentCardEditor;

public sealed record DescriptionBinding(string Language, string Kind, string[] Variables)
{
    public bool Linked => Kind is "Direct" or "Formula";
    public string? Keyword { get; init; }
    public string? KeywordAlias { get; init; }
}
public sealed record DescriptionWarning(int CardId, string Role, int[] NumberIds, string Language, string Kind, bool TextEdited);

public sealed partial class EditorProject
{
    public string DescriptionExcerpt(int id, IEnumerable<int> numberIds, string language)
    {
        var binding = DescriptionBindings(id, numberIds).FirstOrDefault(b => b.Language == language);
        return binding == null ? "" : DescriptionExcerpt(id, binding);
    }

    private string DescriptionExcerpt(int id, DescriptionBinding binding)
    {
        if (binding is not { Linked: true }) return "";
        if(binding.Keyword is { } keyword)
        {
            var match=NativeKeywordText(Text(id,binding.Language,"tooltip"),keyword,binding.KeywordAlias);
            return match.Success?WebUtility.HtmlDecode(Regex.Replace(match.Groups["tag"].Value,"<[^>]+>",""))+" [[value]]":"";
        }
        string text = Text(id, binding.Language, "tooltip").Replace("\\n", "\n");
        var fragments = Regex.Split(text, @"\r?\n|(?<=[.!?])\s+")
            .Where(part => DescriptionExpression.Binding(part, binding.Variables) is "Direct" or "Formula")
            .Select(part => Regex.Replace(part, @"\{[^{}]+\}", match => DescriptionExpression.Binding(match.Value, binding.Variables) switch
            { "Direct" => "[[value]]", "Formula" => "[[formula]]", _ => "..." }))
            .Select(part => WebUtility.HtmlDecode(Regex.Replace(part, "<[^>]+>", "")).Trim())
            .Where(part => part.Length > 0).Distinct().ToArray();
        return string.Join(Environment.NewLine, fragments);
    }

    private int[] DescriptionVariableIds(int id, AbilityNumber number)
    {
        if (number.Id is 110 or 182 && BanArdDescriptionContract(id)) return [1];
        if (number.Id > 0) return [number.Id];
        if (number.Id == -1) return InitialCharges(id).TextVariableId is int initial ? [initial] : [];
        if (number.Id == -6) return InitialPatience(id) is { } patience ? [patience.VariableId] : [];
        if (number.Id is -7 or -8 or -9 or -11 or -12) return VerifiedEffects(id).FirstOrDefault(s => s.Id == number.Id) is { } effect ? [effect.TextVariableId] : [];
        if (number.Id == -5 && ChargeRefill(id) is { } refill)
            return new int?[] { refill.TextVariableId, refill.LiteralVariableId }.OfType<int>().ToArray();
        var timing = Timing(id).FirstOrDefault(s => s.Id == number.Id);
        return timing == null ? [] : timing.GraphValues.Append(timing.TextVariableId).Distinct().ToArray();
    }

    public IReadOnlyList<DescriptionBinding> DescriptionBindings(int id, IEnumerable<int> numberIds,
        IReadOnlyDictionary<string, string>? descriptions = null)
    {
        var requested=numberIds.Distinct().ToArray();
        if(GigaChoice(id) && requested is [1] && GigaContract())
            return AvailableLanguages.Select(language=>
            {
                string text=descriptions?.GetValueOrDefault(language)??Text(id,language,"tooltip");
                string[] aliases=Regex.Matches(text,@"\{(Damage)\}",RegexOptions.IgnoreCase).Select(m=>m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToArray();
                return new DescriptionBinding(language,!HasText(id,language,"tooltip")?"MissingText":DescriptionExpression.Binding(text,aliases),aliases);
            }).ToArray();
        if(requested.Length==1 && NativeKeywordValues(id).SingleOrDefault(v=>v.Id==requested[0]) is { } native)
            return AvailableLanguages.Select(language=>new DescriptionBinding(language,!HasText(id,language,"tooltip")?"MissingText"
                :NativeKeywordText(descriptions?.GetValueOrDefault(language)??Text(id,language,"tooltip"),native.Spec.Keyword,native.Alias).Success?"Direct":"Unlinked",[])
                {Keyword=native.Spec.Keyword,KeywordAlias=native.Alias}).ToArray();
        var settings = Numbers(id).ToDictionary(n => n.Id);
        var graph = CurrentAbility(id);
        var aliases = numberIds.Distinct().Where(settings.ContainsKey).SelectMany(key => DescriptionVariableIds(id, settings[key]))
            .Select(key => graph?.Descendants().FirstOrDefault(e => (int?)e.Attribute("Id") == key))
            .Where(e => e?.Parent?.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
            .Select(e => (string?)e!.Attribute("Name") ?? "").Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return AvailableLanguages.Select(language => new DescriptionBinding(language,
            !HasText(id, language, "tooltip") ? "MissingText" : SelectedDrawTextLinked(id, requested, language,
                descriptions?.GetValueOrDefault(language) ?? Text(id, language, "tooltip")) ? "Direct" : MoveCountTextLinked(id, requested, language,
                descriptions?.GetValueOrDefault(language) ?? Text(id, language, "tooltip")) ? "Direct" : DrawCountTextLinked(id, requested, language,
                descriptions?.GetValueOrDefault(language) ?? Text(id, language, "tooltip")) ? "Direct" : TargetCountTextLinked(id, requested, language,
                descriptions?.GetValueOrDefault(language) ?? Text(id, language, "tooltip")) ? "Direct" : DescriptionExpression.Binding(
                descriptions?.GetValueOrDefault(language) ?? Text(id, language, "tooltip"), aliases), aliases)).ToArray();
    }

    public IReadOnlyList<DescriptionWarning> DescriptionWarnings(int id, CardEdit edit)
    {
        var current = Numbers(id).ToDictionary(n => n.Id);
        var changed = edit.Numbers?.Where(e => current.TryGetValue(e.Key, out var n) && e.Value != n.Value)
            .Select(e => e.Key).ToHashSet() ?? [];
        var texts = edit.Texts.Where(e => e.Value.ContainsKey("tooltip")).ToDictionary(e => e.Key, e => e.Value["tooltip"]);
        return NumberGroups(id).Where(g => g.Members.Any(n => changed.Contains(n.Id)))
            .SelectMany(g => DescriptionBindings(id, g.Members.Select(n => n.Id), texts).Where(b => !b.Linked)
                .Select(b => new DescriptionWarning(id, g.Display.Role, g.Members.Select(n => n.Id).ToArray(), b.Language, b.Kind,
                    texts.TryGetValue(b.Language, out var text) && text != Text(id, b.Language, "tooltip")))).ToArray();
    }

    public IReadOnlyList<DescriptionWarning> PendingDescriptionWarnings()
    {
        var result = new List<DescriptionWarning>();
        foreach (var (id, graph) in abilityOverrides)
        {
            var before = OriginalAbility(id)!.Descendants().Where(e => (int?)e.Attribute("Id") != null)
                .GroupBy(e => (int)e.Attribute("Id")!).ToDictionary(g => g.Key, g => (string?)g.First().Attribute("V"));
            var after = graph.Descendants().Where(e => (int?)e.Attribute("Id") != null)
                .GroupBy(e => (int)e.Attribute("Id")!).ToDictionary(g => g.Key, g => (string?)g.First().Attribute("V"));
            foreach (var group in NumberGroups(id).Where(g => g.Members.Any(n => DescriptionVariableIds(id, n)
                .Any(key => before.GetValueOrDefault(key) != after.GetValueOrDefault(key)))))
                result.AddRange(DescriptionBindings(id, group.Members.Select(n => n.Id)).Where(b => !b.Linked)
                    .Select(b => new DescriptionWarning(id, group.Display.Role, group.Members.Select(n => n.Id).ToArray(), b.Language, b.Kind,
                        textEdits.TryGetValue(b.Language, out var edits) && edits.ContainsKey(id + "_tooltip"))));
        }
        return result;
    }
}
