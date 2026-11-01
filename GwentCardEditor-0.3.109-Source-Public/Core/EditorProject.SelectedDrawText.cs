using System.Globalization;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private sealed record SelectedDrawText(int AmountId, int MinimumId, int MaximumId, int Count, string Family);

    private SelectedDrawText? SelectedDrawTextFor(int id)
    {
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) return null;
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        XElement? Source(XElement? field)
        {
            if (field == null || (string?)field.Attribute("ExternalIn") == "True") return null;
            var links = edges.Where(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray();
            if (links.Length != 1) return null;
            var matches = nodes.SelectMany(n => n.Elements()).Where(e => (int?)e.Attribute("Id") == (int?)links[0].Attribute("SrcId")).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        bool Fixed(XElement? field, string value) => field != null && (string?)field.Attribute("ExternalIn") != "True"
            && (string?)field.Attribute("V") == value && !edges.Any(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id") || (int?)e.Attribute("SrcId") == (int?)field.Attribute("Id"));
        bool Wire(XElement? from, XElement? to) => from != null && to != null && Source(to) == from
            && edges.Count(e => (int?)e.Attribute("SrcId") == (int?)from.Attribute("Id")) == 1;
        bool Owner(XElement? field) => Source(field) is { Name.LocalName: "Out" } output && (string?)output.Parent?.Attribute("Type") == "GetOwnerPlayerIdNode";
        var draws = nodes.Where(n => (string?)n.Attribute("Type") == "DrawCardsNode").ToArray();
        if (draws.Length != 1) return null;
        var draw = draws[0];
        var selected = Source(draw.Element("ValidCards")); var choice = selected?.Parent;
        if (selected?.Name.LocalName != "SelectedChoices" || (string?)choice?.Attribute("Type") != "RequestCardChoiceNode"
            || !Wire(selected, draw.Element("ValidCards")) || !Wire(choice.Element("FlowOutSuccess"), draw.Element("FlowIn"))
            || !Fixed(choice.Element("ChoiceType"), "5") || !(Owner(draw.Element("PlayerId")) || Fixed(draw.Element("PlayerId"), "3")) || !Fixed(draw.Element("Random"), "False")
            || draw.Element("Filters")?.Elements().Any(f => !Fixed(f, "False")) != false) return null;
        if (choice.Element("DisabledChoices") is { } disabled && (Source(disabled) != null || disabled.HasElements || (string?)disabled.Attribute("ExternalIn") == "True")) return null;
        var amount = draw.Element("Num"); var min = choice.Element("MinChoices"); var max = choice.Element("MaxChoices");
        if ((int?)amount?.Attribute("Id") is not int amountId || (int?)min?.Attribute("Id") is not int minId || (int?)max?.Attribute("Id") is not int maxId
            || (int?)amount.Attribute("V") is not int count || count is < 1 or > 99
            || new[] { amount, min, max }.Any(f => (string?)f.Attribute("Type") != "IntVar" || !Fixed(f, count.ToString(CultureInfo.InvariantCulture)))) return null;
        var cards = Source(choice.Element("ValidChoices"));
        XElement? snapshotSetter = null;
        if ((string?)cards?.Parent?.Attribute("Type") == "GetVarNode")
        {
            int? variableId = (int?)cards.Parent.Attribute("VarId");
            var accesses = nodes.Where(n => (int?)n.Attribute("VarId") == variableId).ToArray();
            var setters = accesses.Where(n => (string?)n.Attribute("Type") == "SetVarNode").ToArray();
            var variable = graph.Element("TemporaryVariables")?.Elements().SingleOrDefault(v => (int?)v.Attribute("Id") == variableId);
            string context = AbilityNumbers.ContextFor(graph, [(int)choice.Attribute("Id")!]);
            if (context is not ("Deploy" or "Order") || !Wire(cards, choice.Element("ValidChoices"))
                || variableId == null || accesses.Length != 2 || setters.Length != 1 || (string?)variable?.Attribute("Type") != "CardListVar"
                || variable.HasElements || edges.Count(e => (int?)e.Attribute("DstId") == (int?)setters[0].Element("FlowIn")?.Attribute("Id")) != 1
                || AbilityNumbers.ContextFor(graph, [(int)setters[0].Attribute("Id")!]) != context) return null;
            cards = Source(setters[0].Element("In"));
            snapshotSetter = setters[0];
        }
        string family = "Any";
        if ((string?)cards?.Parent?.Attribute("Type") == "FilterCardListNode")
        {
            var filter = cards.Parent;
            if (cards.Name.LocalName != "FilteredCards" || !Wire(filter.Element("FlowSuccess"), (snapshotSetter ?? choice).Element("FlowIn"))) return null;
            var filters = filter.Element("Filters")?.Elements().ToArray() ?? [];
            if (filters.Length != 1 || Source(filters[0]) is not { Name.LocalName: "Out" } predicateOut) return null;
            var predicate = predicateOut.Parent!;
            var category = predicate.Element("CardCategory");
            family = (string?)predicate.Attribute("Type") switch
            {
                "FilterCardCategoryNode" when Fixed(predicate.Element("Negate"), "False") && Fixed(predicate.Element("CompareMethod"), "1")
                    && Source(category) == null && (string?)category?.Attribute("ExternalIn") != "True"
                    && (category?.Element("V")?.Elements().Select(e => (string?)e.Attribute("V")).SequenceEqual(new[] { "9223372036854775808", "0", "0" }) ?? false) => "Dragon",
                "FilterCardFactionNode" when Fixed(predicate.Element("Negate"), "True") && Fixed(predicate.Element("Faction"), "1") => "NonNeutral",
                "FilterHasKeywordNode" when Fixed(predicate.Element("Negate"), "False") && Fixed(predicate.Element("Keyword"), "1") => "Adrenaline",
                _ => ""
            };
            if (family.Length == 0) return null;
            cards = Source(filter.Element("Cards"));
        }
        var deck = cards?.Parent;
        if (cards?.Name.LocalName != "Out" || (string?)deck?.Attribute("Type") != "GetCardsNode" || !Owner(deck.Element("PlayerId"))
            || !Fixed(deck.Element("LocationId"), "16") || !Fixed(deck.Element("CardTiers"), "10") || !Fixed(deck.Element("Ignore"), "0")
            || !Fixed(deck.Element("IgnoreOwner"), "True") || Source(deck.Element("WithinRangeOf")) != null
            || deck.Element("WithinRangeOf")?.HasElements != false || (string?)deck.Element("WithinRangeOf")?.Attribute("ExternalIn") == "True"
            || !(family == "Dragon" ? Fixed(deck.Element("CardTypes"), "4") : Fixed(deck.Element("CardTypes"), "14") || Fixed(deck.Element("CardTypes"), "30"))) return null;
        var binding = new SelectedDrawText(amountId, minId, maxId, count, family);
        return HasText(id, "en-us", "tooltip") && AvailableLanguages.Where(l => HasText(id, l, "tooltip"))
            .All(l => SelectedDrawPhrase(Text(id, l, "tooltip"), l, binding, count) != null) ? binding : null;
    }

    private static (int Index, int Length, string Replacement)? SelectedDrawPhrase(string text, string language, SelectedDrawText binding, int count, int? replacement = null)
    {
        string[] Phrases(int value)
        {
            string n = value.ToString(CultureInfo.InvariantCulture);
            string ru = value % 10 == 1 && value % 100 != 11 ? "карту" : value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14) ? "карты" : "карт";
            bool plFew = value % 10 is >= 2 and <= 4 && value % 100 is not (>= 12 and <= 14);
            return (language, binding.Family) switch
            {
                ("ru-ru", "NonNeutral") => [value == 1 ? "возьмите любую не нейтральную карту" : $"возьмите {n} " + (ru == "карту" ? "не нейтральную" : ru == "карты" ? "не нейтральные" : "не нейтральных") + " " + ru],
                ("en-us", "NonNeutral") => [value == 1 ? "draw a non-Neutral card of your choice" : $"draw {n} non-Neutral cards of your choice"],
                ("pl-pl", "NonNeutral") => [value == 1 ? "dobierz wybraną nieneutralną kartę" : $"dobierz {n} " + (plFew ? "wybrane nieneutralne karty" : "wybranych nieneutralnych kart")],
                ("de-de", "NonNeutral") => [value == 1 ? "ziehe eine nicht neutrale Karte deiner Wahl" : $"ziehe {n} nicht neutrale Karten deiner Wahl"],
                ("pt-br", "NonNeutral") => [value == 1 ? "compra uma carta não Neutra de sua escolha" : $"compra {n} cartas não Neutras de sua escolha"],
                ("fr-fr", "NonNeutral") => [value == 1 ? "tirez une carte non neutre de votre choix" : $"tirez {n} cartes non neutres de votre choix"],
                ("it-it", "NonNeutral") => [value == 1 ? "pesca una carta non Neutrale dal tuo mazzo" : $"pesca {n} carte non Neutrali dal tuo mazzo"],
                ("es-es", "NonNeutral") or ("es-mx", "NonNeutral") => [value == 1 ? "saca la carta no neutral que elijas" : $"saca las {n} cartas no neutrales que elijas"],
                ("zh-cn", "NonNeutral") => [$"选择并抽 {n} 张非中立牌"],
                ("ja-jp", "NonNeutral") => [$"ニュートラル以外の勢力に属する任意のカード{n}枚を引"],
                ("ko-kr", "NonNeutral") => [$"중립이 아닌 카드 {n}장을 선택하여 뽑고"],
                ("ru-ru", "Adrenaline") => [value == 1 ? "возьмите любую карту с <keyword=adrenaline>адреналином</keyword>" : $"возьмите {n} {ru} с <keyword=adrenaline>адреналином</keyword>"],
                ("en-us", "Adrenaline") => [value == 1 ? "draw a card with <keyword=adrenaline>Adrenaline</keyword> of your choice" : $"draw {n} cards with <keyword=adrenaline>Adrenaline</keyword> of your choice"],
                ("pl-pl", "Adrenaline") => [value == 1 ? "dobierz wybraną kartę z <keyword=adrenaline>adrenaliną</keyword>" : $"dobierz {n} " + (plFew ? "wybrane karty" : "wybranych kart") + " z <keyword=adrenaline>adrenaliną</keyword>"],
                ("de-de", "Adrenaline") => [value == 1 ? "ziehe eine Karte deiner Wahl mit <keyword=adrenaline>Adrenalin</keyword>" : $"ziehe {n} Karten deiner Wahl mit <keyword=adrenaline>Adrenalin</keyword>"],
                ("pt-br", "Adrenaline") => [value == 1 ? "compre uma carta com <keyword=adrenaline>Adrenalina</keyword> de sua escolha" : $"compre {n} cartas com <keyword=adrenaline>Adrenalina</keyword> de sua escolha"],
                ("fr-fr", "Adrenaline") => [value == 1 ? "tirez une carte de votre choix possédant une capacité <keyword=adrenaline>Adrénaline</keyword>" : $"tirez {n} cartes de votre choix possédant une capacité <keyword=adrenaline>Adrénaline</keyword>"],
                ("it-it", "Adrenaline") => [value == 1 ? "pesca una carta a tua scelta con <keyword=adrenaline>Adrenalina</keyword>" : $"pesca {n} carte a tua scelta con <keyword=adrenaline>Adrenalina</keyword>"],
                ("es-es", "Adrenaline") => [value == 1 ? "saca una carta con <keyword=adrenaline>adrenalina</keyword> de tu elección" : $"saca {n} cartas con <keyword=adrenaline>adrenalina</keyword> de tu elección"],
                ("es-mx", "Adrenaline") => [value == 1 ? "roba una carta con <keyword=adrenaline>Adrenalina</keyword> de tu elección" : $"roba {n} cartas con <keyword=adrenaline>Adrenalina</keyword> de tu elección"],
                ("zh-cn", "Adrenaline") => [$"选择抽 {n} 张带有<keyword=adrenaline>亢奋</keyword>能力的牌"],
                ("ja-jp", "Adrenaline") => [$"<keyword=adrenaline>熱狂</keyword>アビリティを持つカードを{n}枚選んで引き"],
                ("ko-kr", "Adrenaline") => ["<keyword=adrenaline>아드레날린</keyword> 키워드가 있는 카드를" + (value == 1 ? "" : $" {n}장") + " 선택하여 뽑고"],
                ("ru-ru", "Any") => [value == 1 ? "возьмите любую карту из вашей колоды" : $"возьмите {n} {ru} из вашей колоды по выбору"],
                ("en-us", "Any") => [value == 1 ? "draw a card of your choice" : $"draw {n} cards of your choice"],
                ("pl-pl", "Any") => [value == 1 ? "dodaj kartę ze swojej talii do ręki" : $"dodaj {n} " + (plFew ? "karty" : "kart") + " ze swojej talii do ręki"],
                ("de-de", "Any") => [value == 1 ? "nimm eine Karte aus deinem Deck auf deine Hand" : $"nimm {n} Karten aus deinem Deck auf deine Hand"],
                ("pt-br", "Any") => [value == 1 ? "adiciona uma carta do seu baralho a sua mão" : $"adiciona {n} cartas do seu baralho a sua mão"],
                ("fr-fr", "Any") => [value == 1 ? "ajoutez une carte de votre jeu dans votre main" : $"ajoutez {n} cartes de votre jeu dans votre main"],
                ("it-it", "Any") => [value == 1 ? "aggiungi una carta dal tuo mazzo nella tua mano" : $"aggiungi {n} carte dal tuo mazzo nella tua mano"],
                ("es-es", "Any") => [value == 1 ? "añade una carta de tu baraja a tu mano" : $"añade {n} cartas de tu baraja a tu mano"],
                ("es-mx", "Any") => [value == 1 ? "roba una carta de tu elección" : $"roba {n} cartas de tu elección"],
                ("zh-cn", "Any") => [$"从牌组添加 {n} 张牌至手牌"],
                ("ko-kr", "Any") => [$"내 덱에서 카드 {n}장을 선택하여 뽑고"],
                ("ja-jp", "Any") => [$"自軍デッキにある任意のカード{n}枚を自軍手札に追加した"],
                ("ru-ru", "Dragon") => [value == 1 ? "возьмите Дракона" : $"возьмите {n} {ru} с категорией «Дракон»"],
                ("en-us", "Dragon") => [value == 1 ? "draw a Dragon" : $"draw {n} Dragons"],
                ("pl-pl", "Dragon") => [value == 1 ? "dobierz Smoka" : $"dobierz {n} " + (plFew ? "Smoki" : "Smoków")],
                ("de-de", "Dragon") => [value == 1 ? "ziehe einen Drachen" : $"ziehe {n} Drachen"],
                ("pt-br", "Dragon") => [value == 1 ? "compra um Dragão" : $"compra {n} Dragões"],
                ("fr-fr", "Dragon") => [value == 1 ? "tirez un Dragon" : $"tirez {n} Dragons"],
                ("it-it", "Dragon") => [value == 1 ? "pesca un Drago" : $"pesca {n} Draghi"],
                ("es-es", "Dragon") or ("es-mx", "Dragon") => [value == 1 ? "saca un dragón" : $"saca {n} dragones"],
                ("zh-cn", "Dragon") => [$"抽 {n} 张“巨龙”牌"],
                ("ja-jp", "Dragon") => [$"「ドラゴン」ユニット{n}体を引"],
                ("ko-kr", "Dragon") => [$"용을 {n}장 뽑고"],
                _ => []
            };
        }
        return FindCountPhrase(text, language, Phrases(count), Phrases(replacement ?? count));
    }

    private CardEdit NormalizeSelectedDrawText(int id, CardEdit edit)
    {
        if (edit.Numbers == null || edit.Numbers.Count == 0 || SelectedDrawTextFor(id) is not { } binding) return edit;
        var ids = new[] { binding.AmountId, binding.MinimumId, binding.MaximumId };
        var counts = ids.Select(key => edit.Numbers.GetValueOrDefault(key, binding.Count)).ToArray();
        if (counts.All(c => c == binding.Count)) return edit;
        if (counts.Distinct().Count() != 1 || counts[0] is < 1 or > 99) throw new InvalidOperationException("TargetCountTextConflict");
        var texts = edit.Texts.ToDictionary(p => p.Key, p => p.Value);
        foreach (string language in AvailableLanguages.Where(l => HasText(id, l, "tooltip")))
        {
            var fields = texts.TryGetValue(language, out var current) ? new Dictionary<string, string>(current) : new();
            string text = fields.GetValueOrDefault("tooltip") ?? Text(id, language, "tooltip");
            if (SelectedDrawPhrase(text, language, binding, binding.Count, counts[0]) is { } match)
                fields["tooltip"] = text[..match.Index] + match.Replacement + text[(match.Index + match.Length)..];
            else if (SelectedDrawPhrase(text, language, binding, counts[0]) == null) throw new InvalidOperationException("TargetCountTextConflict");
            texts[language] = fields;
        }
        return edit with { Texts = texts };
    }

    private bool SelectedDrawTextLinked(int id, int[] requested, string language, string text) => requested.Length == 3 && SelectedDrawTextFor(id) is { } b
        && new[] { b.AmountId, b.MinimumId, b.MaximumId }.All(requested.Contains) && SelectedDrawPhrase(text, language, b, b.Count) != null;
}
