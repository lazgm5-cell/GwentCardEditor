using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private enum TargetTextFamily { EnemyDamage, AnyDamage, AlliedBoost, AnyBoost, AlliedRecipient, AnyRecipient }
    private sealed record TargetTextSelection(int MinimumId, int MaximumId, int Count, string EffectAlias, TargetTextFamily Family, string? EffectKeyword = null);

    // Only direct, single-recipient actions are supported. Shapes and shared effect
    // aliases must not be mistaken for an independent target count.
    private IEnumerable<TargetTextSelection> TargetTextSelections(int id)
    {
        var graph = CurrentAbility(id);
        if (graph == null || SharedAbility(id)) yield break;
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var wires = graph.Element("Connections")?.Elements().ToArray() ?? [];
        bool Wire(XElement? from, XElement? to) => from != null && to != null
            && wires.Count(w => (int?)w.Attribute("SrcId") == (int?)from.Attribute("Id")) == 1
            && wires.Count(w => (int?)w.Attribute("DstId") == (int?)to.Attribute("Id")) == 1
            && wires.Any(w => (int?)w.Attribute("SrcId") == (int?)from.Attribute("Id") && (int?)w.Attribute("DstId") == (int?)to.Attribute("Id"));
        bool Fixed(XElement node, string name, string type, int value, string? argument = null) => node.Element(name) is { } field
            && (string?)field.Attribute("Type") == type && (string?)field.Attribute("GArg") == argument && (int?)field.Attribute("V") == value
            && !wires.Any(w => (int?)w.Attribute("DstId") == (int?)field.Attribute("Id"));
        bool Power(XElement n) => (string?)n.Attribute("Type") == "ChangePowerNode"
            && (int?)n.Element("Operation")?.Attribute("V") is 0 or 1 && (int?)n.Element("TargetedPower")?.Attribute("V") == 0;
        bool Armor(XElement n) => (string?)n.Attribute("Type") == "ChangeArmorNode"
            && Fixed(n, "Operation", "EnumVar`1", 0, "ECardPowerOp") && (string?)n.Element("Armor")?.Attribute("Type") == "IntVar";
        bool Timer(XElement n) => IsInitialStatusTimerNode(n, graph)
            && ((int?)n.Element("Acceptor")?.Attribute("V"), (int?)n.Element("Donor")?.Attribute("V")) is (16, 32) or (32, 16);
        bool Shield(XElement n) => (string?)n.Attribute("Type") == "ChangeTokensNode"
            && Fixed(n, "Operation", "EnumVar`1", 0, "ETokenOp") && Fixed(n, "Tokens", "CardTokensVar", 2);
        foreach (var selection in nodes.Where(n => (string?)n.Attribute("Type") == "RequestCardTargetsNode"))
        {
            var min = selection.Element("MinTargets"); var max = selection.Element("MaxTargets");
            if ((int?)min?.Attribute("Id") is not int minId || (int?)max?.Attribute("Id") is not int maxId
                || (int?)min.Attribute("V") is not int count || count < 1 || count > 99 || (int?)max.Attribute("V") != count
                || wires.Any(w => new[] { minId, maxId }.Contains((int?)w.Attribute("DstId") ?? -1)
                    || new[] { minId, maxId }.Contains((int?)w.Attribute("SrcId") ?? -1))) continue;
            var shape = selection.Element("Shape")?.Element("V");
            if ((int?)shape?.Attribute("Mask") != 0 || (string?)shape.Attribute("ApplyToBothSides") != "False") continue;
            var actions = nodes.Where(n => (Power(n) || Armor(n) || Timer(n) || Shield(n))
                && Wire(selection.Element("SelectedTargets"), n.Element(Timer(n) ? "Cards" : "Targets"))
                && Wire(selection.Element("FlowOutSuccess"), n.Element("FlowIn"))).ToArray();
            if (actions.Length != 1) continue;
            var action = actions[0];
            string alias = "";
            string? keyword = Shield(action) ? "shield" : Armor(action) ? "armor" : Timer(action)
                ? (int?)action.Element("Acceptor")?.Attribute("V") == 16 ? "bleeding" : "vitality" : null;
            if (!Shield(action))
            {
                string field = Armor(action) ? "Armor" : Timer(action) ? "DeltaTime" : "Power";
                var variables = nodes.Where(n => (string?)n.Attribute("Type") == "GetVarNode" && Wire(n.Element("Out"), action.Element(field))).ToArray();
                if (variables.Length != 1) continue;
                int? variableId = (int?)variables[0].Attribute("VarId");
                var variable = graph.Element("TemporaryVariables")?.Elements().SingleOrDefault(v => (int?)v.Attribute("Id") == variableId);
                alias = (string?)variable?.Attribute("Name") ?? "";
                if (!Regex.IsMatch(alias, @"^[A-Za-z][A-Za-z0-9_]*$")
                    || nodes.Count(n => (string?)n.Attribute("Type") == "GetVarNode" && (int?)n.Attribute("VarId") == variableId) != 1) continue;
            }
            var families = !Power(action) ? new[] { TargetTextFamily.AlliedRecipient, TargetTextFamily.AnyRecipient, TargetTextFamily.EnemyDamage }
                : (int?)action.Element("Operation")?.Attribute("V") == 1
                    ? new[] { TargetTextFamily.EnemyDamage, TargetTextFamily.AnyDamage }
                    : new[] { TargetTextFamily.AlliedBoost, TargetTextFamily.AnyBoost };
            foreach (var family in families)
            {
                var binding = new TargetTextSelection(minId, maxId, count, alias, family, keyword);
                if (HasText(id, "en-us", "tooltip") && TargetCountPhrase(Text(id, "en-us", "tooltip"), "en-us", binding, count) != null
                    && AvailableLanguages.Where(l => HasText(id, l, "tooltip")).All(l => TargetCountPhrase(Text(id, l, "tooltip"), l, binding, count) != null))
                { yield return binding; break; }
            }
        }
    }

    private static string? TargetPhrase(string language, int count, TargetTextFamily family)
    {
        string n = count.ToString(CultureInfo.InvariantCulture);
        bool singularGenitive = count % 10 == 1 && count % 100 != 11;
        if (family == TargetTextFamily.AnyRecipient) return language switch
        {
            "ru-ru" => count == 1 ? "отряду" : $"каждому из {n} " + (singularGenitive ? "отряда" : "отрядов"),
            "ko-kr" => count == 1 ? "유닛에" : $"유닛 {n}장 각각에",
            _ => TargetPhrase(language, count, TargetTextFamily.AnyDamage)
        };
        if (family == TargetTextFamily.AlliedRecipient) return language switch
        {
            "ru-ru" => count == 1 ? "дружественному отряду" : $"каждому из {n} " + (singularGenitive ? "дружественного отряда" : "дружественных отрядов"),
            "pl-pl" => count == 1 ? "sojuszniczej jednostce" : $"każdej z {n} sojuszniczych jednostek",
            "de-de" => count == 1 ? "einer verbündeten Einheit" : $"jeder von {n} verbündeten Einheiten",
            "pt-br" => count == 1 ? "a uma unidade aliada" : $"a cada uma de {n} unidades aliadas",
            "fr-fr" => count == 1 ? "à une unité alliée" : $"à chacune de {n} unités alliées",
            "ko-kr" => count == 1 ? "아군에" : $"아군 유닛 {n}장 각각에",
            _ => TargetPhrase(language, count, TargetTextFamily.AlliedBoost)
        };
        if (family == TargetTextFamily.AnyDamage) return language switch
        {
            "ru-ru" => count == 1 ? "урона отряду" : $"урона каждому из {n} " + (singularGenitive ? "отряда" : "отрядов"),
            "en-us" => count == 1 ? "a unit" : $"each of {n} units",
            "pl-pl" => count == 1 ? "jednostce" : $"każdej z {n} jednostek",
            "de-de" => count == 1 ? "einer Einheit" : $"jeder von {n} Einheiten",
            "pt-br" => count == 1 ? "a uma unidade" : $"a cada uma de {n} unidades",
            "fr-fr" => count == 1 ? "à une unité" : $"à chacune de {n} unités",
            "zh-cn" => $"{n} 个单位", "zh-tw" => $"{n} 張單位牌",
            "ja-jp" => $"ユニット{n}体",
            "it-it" => count == 1 ? "un'unità" : $"ciascuna di {n} unità",
            "es-es" or "es-mx" => count == 1 ? "a una unidad" : $"a cada una de {n} unidades",
            // The source Korean neutral-damage wording also uses this phrase.
            "ko-kr" => count == 1 ? "적에게" : $"유닛 {n}장 각각에",
            _ => null
        };
        if (family == TargetTextFamily.AlliedBoost) return language switch
        {
            "ru-ru" => count == 1 ? "дружественный отряд" : $"каждый из {n} " + (singularGenitive ? "дружественного отряда" : "дружественных отрядов"),
            "en-us" => count == 1 ? "an allied unit" : $"each of {n} allied units",
            "pl-pl" => count == 1 ? "sojuszniczą jednostkę" : $"każdą z {n} sojuszniczych jednostek",
            "de-de" => count == 1 ? "eine verbündete Einheit" : $"jede von {n} verbündeten Einheiten",
            "pt-br" => count == 1 ? "uma unidade aliada" : $"cada uma de {n} unidades aliadas",
            "fr-fr" => count == 1 ? "une unité alliée" : $"chacune de {n} unités alliées",
            "zh-cn" => $"{n} 个友军单位", "zh-tw" => $"{n} 張友軍單位牌",
            "ja-jp" => $"自軍ユニット{n}体",
            "it-it" => count == 1 ? "un'unità alleata" : $"ciascuna di {n} unità alleate",
            "es-es" or "es-mx" => count == 1 ? "a una unidad aliada" : $"a cada una de {n} unidades aliadas",
            "ko-kr" => count == 1 ? "아군을" : $"아군 유닛 {n}장 각각을",
            _ => null
        };
        if (family == TargetTextFamily.AnyBoost) return language switch
        {
            "ru-ru" => count == 1 ? "отряд" : $"каждый из {n} " + (singularGenitive ? "отряда" : "отрядов"),
            "en-us" => count == 1 ? "a unit" : $"each of {n} units",
            "pl-pl" => count == 1 ? "jednostkę" : $"każdą z {n} jednostek",
            "de-de" => count == 1 ? "eine Einheit" : $"jede von {n} Einheiten",
            "pt-br" => count == 1 ? "uma unidade" : $"cada uma de {n} unidades",
            "fr-fr" => count == 1 ? "une unité" : $"chacune de {n} unités",
            "zh-cn" => $"{n} 个单位", "zh-tw" => $"{n} 張單位牌",
            "ja-jp" => $"ユニット{n}体",
            "it-it" => count == 1 ? "un'unità" : $"ciascuna di {n} unità",
            "es-es" or "es-mx" => count == 1 ? "a una unidad" : $"a cada una de {n} unidades",
            "ko-kr" => count == 1 ? "유닛을" : $"유닛 {n}장 각각을",
            _ => null
        };
        return language switch
        {
            "ru-ru" => count == 1 ? "вражескому отряду" : count % 10 == 1 && count % 100 != 11
                ? $"каждому из {n} вражеского отряда" : $"каждому из {n} вражеских отрядов",
            "en-us" => count == 1 ? "an enemy unit" : $"each of {n} enemy units",
            "pl-pl" => count == 1 ? "wrogiej jednostce" : $"każdej z {n} wrogich jednostek",
            "de-de" => count == 1 ? "einer gegnerischen Einheit" : $"jeder von {n} gegnerischen Einheiten",
            "pt-br" => count == 1 ? "a uma unidade inimiga" : $"a cada uma de {n} unidades inimigas",
            "fr-fr" => count == 1 ? "à une unité ennemie" : $"à chacune de {n} unités ennemies",
            "zh-cn" => $"{n} 个敌军单位",
            "zh-tw" => $"{n} 個敵軍單位",
            "ja-jp" => $"敵軍ユニット{n}体",
            "it-it" => count == 1 ? "un'unità nemica" : $"ciascuna di {n} unità nemiche",
            "es-es" or "es-mx" => count == 1 ? "a una unidad enemiga" : $"a cada una de {n} unidades enemigas",
            "ko-kr" => count == 1 ? "적에게" : $"적 유닛 {n}장 각각에",
            _ => null
        };
    }

    private static (int Index, int Length)? TargetCountPhrase(string text, string language, TargetTextSelection binding, int count)
    {
        string? phrase = TargetPhrase(language, count, binding.Family);
        if (phrase == null) return null;
        bool numeric = binding.EffectAlias.Length > 0;
        var aliases = Regex.Matches(text, numeric ? @"\{\s*" + Regex.Escape(binding.EffectAlias) + @"\s*\}"
            : @"<keyword=" + Regex.Escape(binding.EffectKeyword!) + @">[^<]*</keyword>", RegexOptions.IgnoreCase);
        if (aliases.Count != 1) return null;
        if (numeric && Regex.Matches(text, @"\{[^{}]*\}").Count(m => DescriptionExpression.Binding(m.Value, [binding.EffectAlias]) != "Unlinked") != 1) return null;
        int position = aliases[0].Index;
        int start = text.LastIndexOf("\\n", position, StringComparison.Ordinal);
        start = start < 0 ? 0 : start + 2;
        int end = text.IndexOf("\\n", position, StringComparison.Ordinal);
        if (end < 0) end = text.Length;
        string tokenPattern = language == "ru-ru" ? @"<[^>]*>|\{[^{}]*\}|ед\.|[.!?。]" : @"<[^>]*>|\{[^{}]*\}|[.!?。]";
        foreach (Match token in Regex.Matches(text[start..end], tokenPattern, RegexOptions.IgnoreCase))
        {
            if (token.Length != 1 || !".!?。".Contains(token.Value, StringComparison.Ordinal)) continue;
            int boundary = start + token.Index;
            if (boundary < position) continue;
            end = boundary + 1; break;
        }
        int sentenceStart = start;
        foreach (Match token in Regex.Matches(text[start..position], tokenPattern, RegexOptions.IgnoreCase))
            if (token.Length == 1 && ".!?。".Contains(token.Value, StringComparison.Ordinal)) sentenceStart = start + token.Index + 1;
        start = sentenceStart;
        string paragraph = text[start..end];
        if (binding.EffectKeyword != null && Regex.Matches(paragraph, @"<keyword=" + Regex.Escape(binding.EffectKeyword) + @">[^<]*</keyword>", RegexOptions.IgnoreCase).Count != 1) return null;
        if (binding.Family is TargetTextFamily.AnyDamage or TargetTextFamily.AnyBoost or TargetTextFamily.AnyRecipient
            && Regex.IsMatch(paragraph, @"\b(?:unité|unidade|unidad|unità)\s+(?:ennemie|alliée|inimiga|aliada|enemiga|nemica|alleata)\b", RegexOptions.IgnoreCase)) return null;
        int found = paragraph.IndexOf(phrase, StringComparison.Ordinal);
        if (found < 0 || paragraph.IndexOf(phrase, found + phrase.Length, StringComparison.Ordinal) >= 0) return null;
        if (language is not ("ja-jp" or "zh-cn" or "zh-tw")
            && (found > 0 && char.IsLetterOrDigit(phrase[0]) && char.IsLetterOrDigit(paragraph[found - 1])
                || found + phrase.Length < paragraph.Length && char.IsLetterOrDigit(phrase[^1]) && char.IsLetterOrDigit(paragraph[found + phrase.Length]))) return null;
        // Multiple numeric expressions or sentences require a more specific recipe.
        if (Regex.Matches(paragraph, @"\{[^{}]*\}").Count != (numeric ? 1 : 0)) return null;
        string plain = Regex.Replace(Regex.Replace(paragraph, @"<[^>]*>|\{[^{}]*\}", ""), @"[.!?。]\s*$", "");
        if (language == "ru-ru") plain = plain.Replace("ед.", "ед", StringComparison.Ordinal);
        if (Regex.IsMatch(plain, @"[.!?。]")) return null;
        return (start + found, phrase.Length);
    }

    private CardEdit NormalizeTargetCountText(int id, CardEdit edit)
    {
        if (edit.Numbers == null || edit.Numbers.Count == 0) return edit;
        var texts = edit.Texts.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(p.Value));
        foreach (var binding in TargetTextSelections(id))
        {
            int min = edit.Numbers.GetValueOrDefault(binding.MinimumId, binding.Count);
            int max = edit.Numbers.GetValueOrDefault(binding.MaximumId, binding.Count);
            if (min == binding.Count && max == binding.Count) continue;
            if (min != max || min is < 1 or > 99) throw new InvalidOperationException("TargetCountTextConflict");
            foreach (string language in AvailableLanguages.Where(l => HasText(id, l, "tooltip")))
            {
                var fields = texts.TryGetValue(language, out var current) ? new Dictionary<string, string>(current) : new();
                string text = fields.GetValueOrDefault("tooltip") ?? Text(id, language, "tooltip");
                if (TargetCountPhrase(text, language, binding, binding.Count) is { } match)
                    fields["tooltip"] = text[..match.Index] + TargetPhrase(language, min, binding.Family) + text[(match.Index + match.Length)..];
                else if (TargetCountPhrase(text, language, binding, min) == null)
                    throw new InvalidOperationException("TargetCountTextConflict");
                texts[language] = fields;
            }
        }
        return edit with { Texts = texts };
    }

    private bool TargetCountTextLinked(int id, int[] requested, string language, string text)
    {
        if (requested.Length != 2) return false;
        return TargetTextSelections(id).Any(b => requested.Contains(b.MinimumId) && requested.Contains(b.MaximumId)
            && TargetCountPhrase(text, language, b, b.Count) != null);
    }
}
