using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private sealed record MoveTextSelection(int MinimumId, int MaximumId, int Count, string Place, string? Context = null);

    private IEnumerable<MoveTextSelection> MoveTextSelections(int id)
    {
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) yield break;
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var wires = graph.Element("Connections")?.Elements().ToArray() ?? [];
        XElement? Source(XElement? field)
        {
            if (field == null || (string?)field.Attribute("ExternalIn") == "True") return null;
            var incoming = wires.Where(w => (int?)w.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray();
            if (incoming.Length != 1) return null;
            var outputs = nodes.SelectMany(n => n.Elements()).Where(e => (int?)e.Attribute("Id") == (int?)incoming[0].Attribute("SrcId")).ToArray();
            return outputs.Length == 1 ? outputs[0] : null;
        }
        bool Fixed(XElement? field, string value) => field != null && (string?)field.Attribute("ExternalIn") != "True"
            && (string?)field.Attribute("V") == value && !wires.Any(w => (int?)w.Attribute("DstId") == (int?)field.Attribute("Id") || (int?)w.Attribute("SrcId") == (int?)field.Attribute("Id"));
        bool Wire(XElement? from, XElement? to) => from != null && to != null && Source(to) == from
            && wires.Count(w => (int?)w.Attribute("SrcId") == (int?)from.Attribute("Id")) == 1;
        CardMoveMeaning.Meaning? Destination(XElement move)
        {
            if (CardMoveMeaning.Inspect(graph, move) is { } deck) return deck;
            var position = Source(move.Element("Position")); var maker = position?.Parent;
            return position?.Name.LocalName == "CardPosition" && (string?)maker?.Attribute("Type") == "MakeCardPositionNode"
                && Fixed(maker.Element("Location"), "32") && Fixed(maker.Element("Index"), "0")
                && Source(maker.Element("PlayerId")) is { Name.LocalName: "Out" } player
                && (string?)player.Parent?.Attribute("Type") == "GetOwnerPlayerIdNode" ? new("Discard", "Owner") : null;
        }
        foreach (var selection in nodes.Where(n => (string?)n.Attribute("Type") == "RequestCardChoiceNode"))
        {
            var min = selection.Element("MinChoices"); var max = selection.Element("MaxChoices");
            if ((int?)min?.Attribute("Id") is not int minId || (int?)max?.Attribute("Id") is not int maxId
                || (int?)min.Attribute("V") is not int count || count is < 1 or > 99
                || !Fixed(min, count.ToString(CultureInfo.InvariantCulture)) || !Fixed(max, count.ToString(CultureInfo.InvariantCulture))
                || !new[] { "3", "4" }.Any(value => Fixed(selection.Element("ChoiceType"), value))) continue;
            var handOut = Source(selection.Element("ValidChoices")); var hand = handOut?.Parent;
            if (handOut?.Name.LocalName != "Out" || (string?)hand?.Attribute("Type") != "GetCardsNode"
                || !Fixed(hand.Element("LocationId"), "8") || !Fixed(hand.Element("CardTypes"), "14")
                || !Fixed(hand.Element("CardTiers"), "10") || !Fixed(hand.Element("Ignore"), "0")
                || !Fixed(hand.Element("IgnoreOwner"), "True")
                || Source(hand.Element("WithinRangeOf")) != null || hand.Element("WithinRangeOf")?.HasElements != false
                || (string?)hand.Element("WithinRangeOf")?.Attribute("ExternalIn") == "True"
                || selection.Element("DisabledChoices") is { } disabled && (Source(disabled) != null || disabled.HasElements || (string?)disabled.Attribute("ExternalIn") == "True")
                || (string?)Source(hand.Element("PlayerId"))?.Parent?.Attribute("Type") != "GetOwnerPlayerIdNode") continue;
            var moves = nodes.Where(n => (string?)n.Attribute("Type") == "MoveCardsNode" && Wire(selection.Element("SelectedChoices"), n.Element("Cards"))).ToArray();
            if (moves.Length != 1 || Destination(moves[0]) is not { Player: "Owner" } destination
                || !Fixed(selection.Element("ChoiceType"), destination.Place == "Discard" ? "3" : "4")) continue;
            if (nodes.Count(n => (string?)n.Attribute("Type") == "MoveCardsNode"
                && Destination(n) is { Player: "Owner" } other && other.Place == destination.Place) != 1) continue;
            var maker = Source(moves[0].Element("Position"))?.Parent;
            if (maker == null || !Wire(selection.Element("FlowOutSuccess"), maker.Element("FlowIn"))
                || !Wire(maker.Element("FlowOut"), moves[0].Element("FlowIn"))) continue;
            string? context = destination.Place == "Discard" ? AbilityNumbers.ContextFor(graph, [(int)selection.Attribute("Id")!]) : null;
            if (destination.Place == "Discard" && context is not ("Deploy" or "Order")) continue;
            var binding = new MoveTextSelection(minId, maxId, count, destination.Place, context);
            if (HasText(id, "en-us", "tooltip") && AvailableLanguages.Where(l => HasText(id, l, "tooltip"))
                .All(l => MoveCountPhrase(Text(id, l, "tooltip"), l, binding, count) != null)) yield return binding;
        }
    }

    private static (int Index, int Length, string Replacement)? MoveCountPhrase(string text, string language, MoveTextSelection binding, int count, int? replacement = null)
    {
        string Cards(int value)
        {
            string n = value.ToString(CultureInfo.InvariantCulture);
            return language switch
            {
                "ru-ru" => value == 1 ? "карту" : n + " " + (value % 10 == 1 && value % 100 != 11 ? "карту" : value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14) ? "карты" : "карт"),
                "en-us" => value == 1 ? "a card" : n + " cards",
                "pl-pl" => value == 1 ? "kartę" : n + " " + (value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14) ? "karty" : "kart"),
                "de-de" => value == 1 ? "eine Karte" : n + " Karten",
                "pt-br" => value == 1 ? "uma carta" : n + " cartas",
                "fr-fr" => value == 1 ? "une carte" : n + " cartes",
                "it-it" => value == 1 ? "una carta" : n + " carte",
                "es-es" or "es-mx" => value == 1 ? "una carta" : n + " cartas",
                _ => n
            };
        }
        string[] Templates() => (language, binding.Place) switch
        {
            ("ru-ru", "Discard") => ["<keyword=discard>сбросьте</keyword> {c}"],
            ("en-us", "Discard") => ["<keyword=discard>Discard</keyword> {c}"],
            ("pl-pl", "Discard") => ["<keyword=discard>odrzuć</keyword> {c}"],
            ("de-de", "Discard") => ["<keyword=discard>Verwirf</keyword> dann {c}", "<keyword=discard>Wirf</keyword> {c} ab", "<keyword=discard>verwirf</keyword> {c}"],
            ("pt-br", "Discard") => ["<keyword=discard>descarta</keyword> {c}", "<keyword=discard>descarte</keyword> {c}"],
            ("fr-fr", "Discard") => ["<keyword=discard>défaussez</keyword> {c}", "<keyword=discard>défaussez</keyword>-en {une}", "<keyword=discard>défaussez-en</keyword> {une}"],
            ("it-it", "Discard") => ["<keyword=discard>scarta</keyword> {c}"],
            ("es-es", "Discard") or ("es-mx", "Discard") => ["<keyword=discard>descarta</keyword> {c}", "<keyword=discard>descarta</keyword> {otra}"],
            ("zh-cn", "Discard") => ["<keyword=discard>丢弃</keyword> {n} 张牌"],
            ("zh-tw", "Discard") => ["<keyword=discard>丟棄</keyword> {n} 張卡牌"],
            ("ja-jp", "Discard") => ["カード{n}枚を<keyword=discard>破棄</keyword>する", "カード{n}枚を<keyword=discard>破棄</keyword>し、"],
            ("ko-kr", "Discard") => ["{n}장을 <keyword=discard>버린다</keyword>"],
            ("ru-ru", "DeckTop") => ["переместите {c} из вашей руки на верх вашей колоды"],
            ("ru-ru", "DeckBottom") => ["переместите {c} из вашей руки в низ вашей колоды"],
            ("ru-ru", "DeckRandom") => ["затасуйте {c} из вашей руки в вашу колоду", "затасуйте {c} из вашей руки обратно в колоду"],
            ("en-us", "DeckTop") => ["move {c} from your hand to the top of your deck"],
            ("en-us", "DeckBottom") => ["move {c} from your hand to the bottom of your deck"],
            ("en-us", "DeckRandom") => ["shuffle {c} from your hand into your deck", "shuffle {c} from your hand back into your deck", "shuffle {c} from your hand back into the deck"],
            ("pl-pl", "DeckTop") => ["przenieś {c} ze swojej ręki na wierzch swojej talii"],
            ("pl-pl", "DeckBottom") => ["przenieś {c} ze swojej ręki na spód swojej talii", "przenieś {c} z ręki na spód swojej talii"],
            ("pl-pl", "DeckRandom") => ["wtasuj {c} ze swojej ręki do talii", "wtasuj {c} z ręki z powrotem do talii"],
            ("de-de", "DeckTop") => ["platziere dann {c} aus deiner Hand oben auf deinem Deck"],
            ("de-de", "DeckBottom") => ["lege dann {c} aus deiner Hand unten in dein Deck", "verschiebe dann {c} von deiner Hand ganz nach unten in dein Deck", "lege {c} von deiner Hand ganz unten in dein Deck"],
            ("de-de", "DeckRandom") => ["mische {c} von deiner Hand in dein Deck", "mische dann {c} von deiner Hand in dein Deck", "mische dann {c} von deiner Hand ins Deck"],
            ("pt-br", "DeckTop") => ["coloca {c} da sua mão no topo do seu baralho"],
            ("pt-br", "DeckBottom") => ["coloca {c} da sua mão no final do seu baralho", "move {c} da sua mão para o fundo do seu baralho", "move {c} da sua mão para o final do baralho"],
            ("pt-br", "DeckRandom") => ["embaralha {c} da sua mão no seu baralho", "embaralhe {c} da sua mão no seu baralho", "embaralhe de volta {c} da sua mão no baralho", "embaralha {c} da sua mão de volta no baralho"],
            ("fr-fr", "DeckTop") => ["placez {c} de votre main sur le dessus de votre jeu"],
            ("fr-fr", "DeckBottom") => ["placez {c} de votre main en bas de votre jeu", "déplacez {c} de votre main vers le bas de votre jeu", "déplacez-en {une} depuis votre main vers le bas de votre jeu"],
            ("fr-fr", "DeckRandom") => ["mélangez {c} de votre main dans votre jeu", "replacez {c} de votre main dans votre jeu"],
            ("it-it", "DeckTop") => ["sposta {c} della tua mano in cima al tuo mazzo"],
            ("it-it", "DeckBottom") => ["sposta {c} della tua mano in fondo al tuo mazzo", "sposta {c} dalla tua mano in fondo al tuo mazzo"],
            ("it-it", "DeckRandom") => ["rimescola nel tuo mazzo {c} che hai in mano", "rimescola {c} dalla tua mano nel tuo mazzo", "rimescola nel mazzo {c} che hai in mano"],
            ("es-es", "DeckTop") => ["coloca {c} de tu mano en el primer lugar de tu baraja"],
            ("es-es", "DeckBottom") => ["coloca {c} de tu mano en el último lugar de tu baraja", "mueve {c} de tu mano al último lugar de tu baraja"],
            ("es-es", "DeckRandom") => ["mezcla {c} de tu mano en tu baraja"],
            ("es-mx", "DeckTop") => ["coloca {c} de tu mano en la parte superior del mazo"],
            ("es-mx", "DeckBottom") => ["pon {c} de tu mano en la parte inferior del mazo", "mueve {c} de tu mano al último lugar de tu mazo", "mueve {c} de tu mano a la parte inferior del mazo"],
            ("es-mx", "DeckRandom") => ["envía {c} de tu mano a tu mazo", "mezcla {c} de tu mano en tu mazo", "mezcla de vuelta {c} de tu mano en el mazo"],
            ("zh-cn", "DeckTop") => ["将 {n} 张手牌置于己方牌组顶端"],
            ("zh-cn", "DeckBottom") => ["将 {n} 张手牌置于牌组底端", "将 {n} 张手牌置入牌组底端"],
            ("zh-cn", "DeckRandom") => ["将 {n} 张手牌洗进牌组", "将 {n} 张手牌洗回牌组"],
            ("zh-tw", "DeckTop") => ["將我方手牌中 {n} 張卡牌放到我方牌組最上方"],
            ("zh-tw", "DeckBottom") => ["將我方手牌中 {n} 張卡牌放到我方牌組最下方"],
            ("ja-jp", "DeckTop") => ["自軍手札にあるカード{n}枚を自軍デッキの一番上に移動させる"],
            ("ja-jp", "DeckBottom") => ["自軍手札にあるカード{n}枚をデッキの一番下に移動させる", "手札のカード{n}枚をデッキの一番下に置く"],
            ("ja-jp", "DeckRandom") => ["自軍手札にあるカード{n}枚をデッキの無作為な場所に戻した", "自軍手札のカード{n}枚をデッキの無作為な場所に戻す", "自軍手札にあるカード{n}枚を自軍デッキの無作為な場所に戻し"],
            ("ko-kr", "DeckTop") => ["손에 있는 카드 {n}장을 덱의 맨 위에 놓는다"],
            ("ko-kr", "DeckBottom") => ["내 손에 있는 카드 {n}장을 내 덱 맨 밑에 놓는다", "손에 있는 카드 {n}장을 덱의 맨 아래에 놓는다"],
            ("ko-kr", "DeckRandom") => ["손에 있는 카드{ko} 덱에 섞고", "내 손에 있는 카드{ko} 내 덱에 섞은 뒤", "내 손에 있는 카드 {n}장을 덱에 섞는다", "내 손에 있는 카드 {n}장을 덱에 섞은 뒤"],
            _ => []
        };
        string[] Phrases(int value) => Templates().Select(t => t.Replace("{c}", Cards(value), StringComparison.Ordinal)
            .Replace("{n}", value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{une}", value == 1 ? "une" : value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{otra}", value == 1 ? "otra" : $"otras {value.ToString(CultureInfo.InvariantCulture)} cartas", StringComparison.Ordinal)
            .Replace("{ko}", value == 1 ? "를" : $" {value.ToString(CultureInfo.InvariantCulture)}장을", StringComparison.Ordinal)).ToArray();
        int start = 0, end = text.Length;
        if (binding.Place == "Discard")
        {
            var headers = Regex.Matches(text, @"<keyword=" + (binding.Context == "Order" ? "order" : "deploy") + @">[^<]*</keyword>", RegexOptions.IgnoreCase);
            if (headers.Count != 1) return null;
            if (Regex.Matches(text, "\"[^\"]*\"|«[^»]*»").Any(q => q.Index <= headers[0].Index && q.Index + q.Length >= headers[0].Index + headers[0].Length)) return null;
            start = headers[0].Index + headers[0].Length;
            int nextLine = text.IndexOf("\\n", start, StringComparison.Ordinal);
            if (nextLine >= 0) end = nextLine;
        }
        var match = FindCountPhrase(text[start..end], language, Phrases(count), Phrases(replacement ?? count));
        return match is { } found ? (start + found.Index, found.Length, found.Replacement) : null;
    }

    private CardEdit NormalizeMoveCountText(int id, CardEdit edit)
    {
        if (edit.Numbers == null || edit.Numbers.Count == 0) return edit;
        var texts = edit.Texts.ToDictionary(p => p.Key, p => p.Value);
        foreach (var binding in MoveTextSelections(id))
        {
            int min = edit.Numbers.GetValueOrDefault(binding.MinimumId, binding.Count), max = edit.Numbers.GetValueOrDefault(binding.MaximumId, binding.Count);
            if (min == binding.Count && max == binding.Count) continue;
            if (min != max || min is < 1 or > 99) throw new InvalidOperationException("TargetCountTextConflict");
            foreach (string language in AvailableLanguages.Where(l => HasText(id, l, "tooltip")))
            {
                var fields = texts.TryGetValue(language, out var current) ? new Dictionary<string, string>(current) : new();
                string text = fields.GetValueOrDefault("tooltip") ?? Text(id, language, "tooltip");
                if (MoveCountPhrase(text, language, binding, binding.Count, min) is { } match)
                    fields["tooltip"] = text[..match.Index] + match.Replacement + text[(match.Index + match.Length)..];
                else if (MoveCountPhrase(text, language, binding, min) == null) throw new InvalidOperationException("TargetCountTextConflict");
                texts[language] = fields;
            }
        }
        return edit with { Texts = texts };
    }

    private bool MoveCountTextLinked(int id, int[] requested, string language, string text) => requested.Length == 2 && MoveTextSelections(id).Any(b =>
        requested.Contains(b.MinimumId) && requested.Contains(b.MaximumId) && MoveCountPhrase(text, language, b, b.Count) != null);
}
