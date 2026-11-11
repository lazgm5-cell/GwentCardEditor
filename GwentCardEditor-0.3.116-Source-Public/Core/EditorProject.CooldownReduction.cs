using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private TimingSetting? CooldownReduction(int id, XElement graph)
    {
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        string Type(XElement? e) => (string?)e?.Attribute("Type") ?? "";
        int Id(XElement e) => (int?)e.Attribute("Id") ?? -1;
        var counts = new Dictionary<string, int> {
            ["GetCardsNode"] = 1, ["GetOwnerPlayerIdNode"] = 1, ["PlayedTrigger"] = 1,
            ["RequestCardTargetsNode"] = 1, ["ChangePowerNode"] = 1, ["GetAbilityOwnerNode"] = 1,
            ["GetVarNode"] = 3, ["ChangeCardTimerNode"] = 2, ["FilterCardListNode"] = 2,
            ["FilterHasOnTimerAbilityNode"] = 1, ["FilterHasOnTappedAbilityNode"] = 1, ["FilterHasOnPaidForAbilityNode"] = 1
        };
        if (nodes.Length != counts.Values.Sum() || counts.Any(c => nodes.Count(n => Type(n) == c.Key) != c.Value)
            || graph.DescendantsAndSelf().Attributes().Any(a => a.Name.LocalName is "ExternalIn" or "ExternalOut"
                && a.Value.Equals("True", StringComparison.OrdinalIgnoreCase)) || GraphValidator.Validate(graph).ErrorCount > 0) return null;
        XElement One(string type) => nodes.Single(n => Type(n) == type);
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var expected = new HashSet<(int, int)>();
        bool Link(XElement? source, XElement? target)
        {
            if (source == null || target == null || Id(source) < 1 || Id(target) < 1) return false;
            expected.Add((Id(source), Id(target)));
            return edges.Count(e => (int?)e.Attribute("SrcId") == Id(source) && (int?)e.Attribute("DstId") == Id(target)) == 1;
        }
        bool Literal(XElement? e, string value) => e != null && (string?)e.Attribute("V") == value
            && !edges.Any(c => (int?)c.Attribute("DstId") == Id(e));
        var played = One("PlayedTrigger"); var request = One("RequestCardTargetsNode");
        var boost = One("ChangePowerNode"); var cards = One("GetCardsNode");
        if (!Literal(played.Element("Forced"), "False") || !Literal(cards.Element("LocationId"), "3")
            || !Literal(cards.Element("CardTypes"), "4") || !Literal(request.Element("MinTargets"), "1")
            || !Literal(request.Element("MaxTargets"), "1") || (string?)request.Element("Shape")?.Element("V")?.Attribute("Mask") != "0"
            || !Literal(boost.Element("Operation"), "0") || (string?)boost.Element("Operation")?.Attribute("GArg") != "ECardPowerOp"
            || !Literal(boost.Element("TargetedPower"), "0") || !Literal(boost.Element("AttackMethod"), "1")
            || !Literal(boost.Element("EffectType"), "1")) return null;
        var variables = graph.Elements().Where(e => e.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
            .SelectMany(e => e.Elements()).ToArray();
        var match = Regex.Match(Text(id, "en-us", "tooltip"), @"reduce its <keyword=cooldown>[^<]*</keyword> by \{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.IgnoreCase);
        if (!match.Success || variables.Length != 2 || variables.Any(v => Type(v) != "IntVar" || v.Parent?.Name.LocalName != "TemporaryVariables"
            || (string?)v.Attribute("ResetInInactive") == "True")) return null;
        string name = match.Groups[1].Value;
        var aliases = variables.Where(v => (string?)v.Attribute("Name") == name).ToArray();
        if (aliases.Length != 1 || !int.TryParse((string?)aliases[0].Attribute("V"), out int value) || value is < 1 or > 99) return null;
        int aliasId = Id(aliases[0]); var boostVar = variables.Single(v => v != aliases[0]);
        var readers = nodes.Where(n => Type(n) == "GetVarNode").ToArray();
        var cooldownReaders = readers.Where(n => (int?)n.Attribute("VarId") == aliasId).ToArray();
        var boostReaders = readers.Where(n => (int?)n.Attribute("VarId") == Id(boostVar)).ToArray();
        if (cooldownReaders.Length != 2 || boostReaders.Length != 1 || readers.Any(n => Type(n.Element("Out")!) != "IntVar")) return null;
        var filters = nodes.Where(n => Type(n) == "FilterCardListNode").OrderByDescending(n => n.Element("Filters")?.Elements().Count() ?? 0).ToArray();
        var first = filters[0]; var second = filters[1];
        var firstFilters = first.Element("Filters")?.Elements().ToArray() ?? [];
        var secondFilters = second.Element("Filters")?.Elements().ToArray() ?? [];
        if (firstFilters.Length != 2 || secondFilters.Length != 1) return null;
        var timerFilter = One("FilterHasOnTimerAbilityNode"); var tapFilter = One("FilterHasOnTappedAbilityNode"); var feeFilter = One("FilterHasOnPaidForAbilityNode");
        if (new[] { timerFilter, tapFilter, feeFilter }.Any(n => !Literal(n.Element("Negate"), "False"))) return null;
        var timers = nodes.Where(n => Type(n) == "ChangeCardTimerNode").ToArray();
        var primary = timers.Where(n => edges.Any(e => (int?)e.Attribute("SrcId") == (int?)first.Element("FlowSuccess")?.Attribute("Id")
            && (int?)e.Attribute("DstId") == (int?)n.Element("FlowIn")?.Attribute("Id"))).ToArray();
        if (primary.Length != 1 || timers.Any(n => !Literal(n.Element("Operation"), "0")
            || (string?)n.Element("Operation")?.Attribute("GArg") != "ETimerOperation")) return null;
        var a = primary[0]; var b = timers.Single(n => n != a);
        bool Duration(XElement timer)
        {
            var sources = cooldownReaders.Where(r => edges.Any(e => (int?)e.Attribute("SrcId") == (int?)r.Element("Out")?.Attribute("Id")
                && (int?)e.Attribute("DstId") == (int?)timer.Element("Value")?.Attribute("Id"))).ToArray();
            return sources.Length == 1 && Link(sources[0].Element("Out"), timer.Element("Value"));
        }
        if (!Link(One("GetOwnerPlayerIdNode").Element("Out"), cards.Element("PlayerId"))
            || !Link(played.Element("FlowOut"), request.Element("FlowIn")) || !Link(cards.Element("Out"), request.Element("ValidTargets"))
            || !Link(request.Element("FlowOutSuccess"), boost.Element("FlowIn")) || !Link(request.Element("SelectedTargets"), boost.Element("Targets"))
            || !Link(One("GetAbilityOwnerNode").Element("Out"), boost.Element("Attacker")) || !Link(boostReaders[0].Element("Out"), boost.Element("Power"))
            || !Link(boost.Element("FlowOut"), first.Element("FlowIn")) || !Link(request.Element("SelectedTargets"), first.Element("Cards"))
            || !Link(timerFilter.Element("Out"), firstFilters[0]) || !Link(tapFilter.Element("Out"), firstFilters[1])
            || !Link(first.Element("FlowSuccess"), a.Element("FlowIn")) || !Link(request.Element("SelectedTargets"), a.Element("Cards"))
            || !Link(first.Element("FlowFail"), second.Element("FlowIn")) || !Link(first.Element("NotFilteredCards"), second.Element("Cards"))
            || !Link(feeFilter.Element("Out"), secondFilters[0]) || !Link(second.Element("FlowSuccess"), b.Element("FlowIn"))
            || !Link(second.Element("FilteredCards"), b.Element("Cards")) || !Duration(a) || !Duration(b)) return null;
        if (edges.Length != expected.Count || edges.Any(e => !expected.Contains(((int?)e.Attribute("SrcId") ?? -1, (int?)e.Attribute("DstId") ?? -1)))) return null;
        var permitted = readers.Select(r => r.Attribute("VarId")!).ToHashSet();
        if (graph.DescendantsAndSelf().Attributes().Any(attr => !permitted.Contains(attr)
            && attr.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(attr.Value, out int reference) && variables.Any(v => Id(v) == reference))) return null;
        if (graph.Descendants("CompareVarID").Any(e => int.TryParse(e.Value, out int reference) && variables.Any(v => Id(v) == reference))
            || graph.Descendants().Any(e => Type(e) == "IntReferenceVar" && int.TryParse((string?)e.Attribute("V"), out int reference)
                && variables.Any(v => Id(v) == reference))) return null;
        var texts = AvailableLanguages.Where(l => HasText(id, l, "tooltip")).Select(l => Text(id, l, "tooltip")).ToArray();
        if (texts.Length == 0 || texts.Any(text => !text.Contains("{" + name + "}", StringComparison.Ordinal)
            || text.Contains("{" + name + "-", StringComparison.OrdinalIgnoreCase))) return null;
        return new(-4, "cooldown-reduction", value, aliasId, []);
    }
}
