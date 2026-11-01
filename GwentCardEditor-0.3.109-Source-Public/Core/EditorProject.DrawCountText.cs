using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private sealed record DrawTextSelection(int Id, int Count);

    private DrawTextSelection? DrawTextSelectionFor(int id)
    {
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) return null;
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var wires = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var draws = nodes.Where(n => (string?)n.Attribute("Type") == "DrawCardsNode").ToArray();
        if (draws.Length != 1) return null;
        var draw = draws[0];
        bool Incoming(XElement? field) => field != null && wires.Any(w => (int?)w.Attribute("DstId") == (int?)field.Attribute("Id"));
        bool Owner(XElement? field) => field != null && wires.Count(w => (int?)w.Attribute("DstId") == (int?)field.Attribute("Id")) == 1
            && nodes.Count(n => (string?)n.Attribute("Type") == "GetOwnerPlayerIdNode"
                && wires.Any(w => (int?)w.Attribute("SrcId") == (int?)n.Element("Out")?.Attribute("Id")
                    && (int?)w.Attribute("DstId") == (int?)field.Attribute("Id"))) == 1;
        if (Incoming(draw.Element("ValidCards")))
        {
            var decks = nodes.Where(n => (string?)n.Attribute("Type") == "GetCardsNode"
                && wires.Any(w => (int?)w.Attribute("SrcId") == (int?)n.Element("Out")?.Attribute("Id")
                    && (int?)w.Attribute("DstId") == (int?)draw.Element("ValidCards")?.Attribute("Id"))).ToArray();
            if (decks.Length != 1 || wires.Count(w => (int?)w.Attribute("DstId") == (int?)draw.Element("ValidCards")?.Attribute("Id")) != 1) return null;
            var deck = decks[0];
            if (!Owner(deck.Element("PlayerId")) || Incoming(deck.Element("WithinRangeOf"))
                || new[] { ("LocationId", 16), ("CardTypes", 14), ("CardTiers", 10), ("Ignore", 0) }
                    .Any(p => (int?)deck.Element(p.Item1)?.Attribute("V") != p.Item2 || Incoming(deck.Element(p.Item1)))) return null;
        }
        if (draw.Element("Num") is not { } number || (string?)number.Attribute("Type") != "IntVar"
            || (int?)number.Attribute("Id") is not int numberId || (int?)number.Attribute("V") is not int count
            || count is < 1 or > 99 || Incoming(number)
            || wires.Any(w => (int?)w.Attribute("SrcId") == numberId)
            || draw.Element("ValidCards")?.HasElements != false
            || draw.Element("Filters")?.Elements().Any(f => (string?)f.Attribute("V") != "False" || Incoming(f)) != false
            || (string?)draw.Element("Random")?.Attribute("V") != "False" || Incoming(draw.Element("Random"))) return null;
        if (!Owner(draw.Element("PlayerId"))
            || wires.Count(w => (int?)w.Attribute("DstId") == (int?)draw.Element("FlowIn")?.Attribute("Id")) != 1) return null;
        var binding = new DrawTextSelection(numberId, count);
        return HasText(id, "en-us", "tooltip") && AvailableLanguages.Where(l => HasText(id, l, "tooltip"))
            .All(l => DrawCountPhrase(Text(id, l, "tooltip"), l, count) != null) ? binding : null;
    }

    // Match the draw verb as well as its object, so a later play/discard clause
    // mentioning another card is never rewritten with the draw count.
    private static (int Index, int Length, string Replacement)? DrawCountPhrase(string text, string language, int count, int? replacement = null)
    {
        string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        string Ru(int value) => value % 10 == 1 && value % 100 != 11 ? "карту" : value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14) ? "карты" : "карт";
        string[] Phrases(int value) => language switch
        {
            "ru-ru" => [value == 1 ? "возьмите карту" : $"возьмите {N(value)} {Ru(value)}"],
            "en-us" => [value == 1 ? "draw a card" : $"draw {N(value)} cards"],
            "pl-pl" => [value == 1 ? "dobierz kartę" : $"dobierz {N(value)} " + (value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14) ? "karty" : "kart")],
            "de-de" => [value == 1 ? "ziehe eine Karte" : $"ziehe {N(value)} Karten", value == 1 ? "zieh eine Karte" : $"zieh {N(value)} Karten"],
            "pt-br" => [value == 1 ? "compre uma carta" : $"compre {N(value)} cartas", value == 1 ? "compra uma carta" : $"compra {N(value)} cartas"],
            "fr-fr" => [value == 1 ? "tirez une carte" : $"tirez {N(value)} cartes", value == 1 ? "piochez une carte" : $"piochez {N(value)} cartes"],
            "it-it" => [value == 1 ? "pesca una carta" : $"pesca {N(value)} carte"],
            "es-es" => [value == 1 ? "saca una carta" : $"saca {N(value)} cartas"],
            "es-mx" => [value == 1 ? "roba una carta" : $"roba {N(value)} cartas", value == 1 ? "saca una carta" : $"saca {N(value)} cartas"],
            "zh-cn" => [$"抽 {N(value)} 张牌"],
            "zh-tw" => [$"抽取 {N(value)} 張卡牌"],
            "ja-jp" => [$"カード{N(value)}枚を引"],
            "ko-kr" => [value == 1 ? "카드를 뽑는다" : $"카드를 {N(value)}장 뽑는다", value == 1 ? "카드를 뽑고" : $"카드를 {N(value)}장 뽑고", $"카드 {N(value)}장을 뽑고"],
            _ => []
        };
        return FindCountPhrase(text, language, Phrases(count), Phrases(replacement ?? count));
    }

    private static (int Index, int Length, string Replacement)? FindCountPhrase(string text, string language, string[] phrases, string[] replacements)
    {
        var matches = phrases.SelectMany((phrase, variant) => Regex.Matches(text, Regex.Escape(phrase), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Where(m => (language is "ja-jp" or "zh-cn" or "zh-tw" || m.Index == 0 || !char.IsLetterOrDigit(phrase[0]) || !char.IsLetterOrDigit(text[m.Index - 1]))
                && (language is "ja-jp" or "zh-cn" or "zh-tw" || m.Index + m.Length == text.Length || !char.IsLetterOrDigit(text[m.Index + m.Length])))
            .Select(m => (Match: m, Variant: variant))).ToArray();
        if (matches.Length != 1) return null;
        var (match, index) = matches[0];
        // A phrase in a tag, quoted infusion or expression is not the native action.
        if (Regex.Matches(text, "<[^>]*>|\\{[^{}]*\\}|\"[^\"]*\"|«[^»]*»").Any(m => m.Index <= match.Index && m.Index + m.Length >= match.Index + match.Length)) return null;
        string next = replacements[index];
        var keywords = Regex.Matches(match.Value, @"<keyword=[^>]+>[^<]*</keyword>", RegexOptions.IgnoreCase).Select(m => m.Value).ToArray();
        int keywordIndex = 0;
        next = Regex.Replace(next, @"<keyword=[^>]+>[^<]*</keyword>", _ => keywords[keywordIndex++], RegexOptions.IgnoreCase);
        if (char.IsUpper(match.Value[0])) next = char.ToUpperInvariant(next[0]) + next[1..];
        return (match.Index, match.Length, next);
    }

    private CardEdit NormalizeDrawCountText(int id, CardEdit edit)
    {
        if (edit.Numbers == null || DrawTextSelectionFor(id) is not { } binding
            || !edit.Numbers.TryGetValue(binding.Id, out int count) || count == binding.Count) return edit;
        if (count is < 1 or > 99) throw new InvalidOperationException("TargetCountTextConflict");
        var texts = edit.Texts.ToDictionary(p => p.Key, p => p.Value);
        foreach (string language in AvailableLanguages.Where(l => HasText(id, l, "tooltip")))
        {
            var fields = texts.TryGetValue(language, out var current) ? new Dictionary<string, string>(current) : new();
            string text = fields.GetValueOrDefault("tooltip") ?? Text(id, language, "tooltip");
            if (DrawCountPhrase(text, language, binding.Count, count) is { } match)
                fields["tooltip"] = text[..match.Index] + match.Replacement + text[(match.Index + match.Length)..];
            else if (DrawCountPhrase(text, language, count) == null) throw new InvalidOperationException("TargetCountTextConflict");
            texts[language] = fields;
        }
        return edit with { Texts = texts };
    }

    private bool DrawCountTextLinked(int id, int[] requested, string language, string text) => requested is [var number]
        && DrawTextSelectionFor(id) is { } binding && binding.Id == number && DrawCountPhrase(text, language, binding.Count) != null;
}
