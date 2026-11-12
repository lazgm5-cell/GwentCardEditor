using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record TimingSetting(int Id, string Role, int Value, int TextVariableId, IReadOnlyList<int> GraphValues);

public sealed partial class EditorProject
{
    public IReadOnlyList<TimingSetting> Timing(int id)
    {
        var graph = CurrentAbility(id);
        if (!Card(id).Editable || Card(id).Type == 1 || graph == null || SharedAbility(id)) return [];
        if(OwnedTrigger(graph,ResupplySpec))
        {
            graph=new XElement(graph);
            if(!TriggerOwnedParts(graph,ResupplySpec,out int saved,out var moduleNodes,out var moduleConnections))return [];
            foreach(var part in moduleNodes.Concat(moduleConnections))part.Remove();
            graph.SetAttributeValue("NId",saved);
        }
        var reduction = CooldownReduction(id, graph);
        if (reduction != null) return [reduction];
        var template = Template(id);
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var connections = graph.Element("Connections")?.Elements().ToArray() ?? [];
        string Type(XElement e) => (string?)e.Attribute("Type") ?? "";
        int Id(XElement e) => (int?)e.Attribute("Id") ?? -999;
        var timers = nodes.Where(n => Type(n) == "ChangeCardTimerNode").ToArray();
        if (timers.Length is < 1 or > 2 || nodes.Count(n => Type(n) == "CardTimerElapsedTrigger") != 1
            || nodes.Any(n => Type(n).Contains("Timer", StringComparison.Ordinal)
                && Type(n) is not ("ChangeCardTimerNode" or "CardTimerElapsedTrigger"))
            || graph.DescendantsAndSelf().Attributes().Any(a => a.Name.LocalName is "ExternalIn" or "ExternalOut"
                && a.Value.Equals("True", StringComparison.OrdinalIgnoreCase))) return [];

        if (GraphValidator.Validate(graph).ErrorCount > 0) return [];
        bool Incoming(XElement e) => connections.Any(c => (int?)c.Attribute("DstId") == Id(e));
        bool Literal(XElement? e, string value) => e != null && (string?)e.Attribute("V") == value && !Incoming(e);
        bool Self(XElement? input)
        {
            if (input == null) return false;
            var edges = connections.Where(c => (int?)c.Attribute("DstId") == Id(input)).ToArray();
            return edges.Length == 1 && nodes.Any(n => Type(n) == "GetAbilityOwnerCardListNode"
                && (int?)n.Element("Out")?.Attribute("Id") == (int?)edges[0].Attribute("SrcId"));
        }
        bool From(XElement node, params string[] triggers)
        {
            var visited = new HashSet<int>();
            bool Walk(XElement current)
            {
                if (!visited.Add(Id(current))) return false;
                if (triggers.Contains(Type(current))) return true;
                if (Type(current).EndsWith("Trigger", StringComparison.Ordinal)) return false;
                var inputs = current.Elements().Where(e => Type(e) == "FlowConnector" && e.Name.LocalName == "FlowIn").Select(Id).ToHashSet();
                var parents = connections.Where(c => inputs.Contains((int?)c.Attribute("DstId") ?? -1))
                    .Select(c => nodes.SingleOrDefault(n => n.Elements().Any(e => Type(e) == "FlowConnector" && Id(e) == (int?)c.Attribute("SrcId"))))
                    .ToArray();
                return parents.Length == 1 && parents[0] != null && Walk(parents[0]!);
            }
            return Walk(node);
        }
        if (timers.Any(n => n.Element("Value") is not { } v || Type(v) != "IntVar" || Id(v) <= 0
            || !Self(n.Element("Cards")) || (string?)n.Element("Operation")?.Attribute("GArg") != "ETimerOperation"
            || Incoming(n.Element("Operation")!))) return [];
        var decrement = timers.Where(n => Literal(n.Element("Operation"), "0") && Literal(n.Element("Value"), "1")).ToArray();
        if (decrement.Length != 1 || !From(decrement[0], "BeforeTurnTrigger", "BeforeTurnEndTrigger")) return [];
        var ticks = nodes.Where(n => Type(n) is "BeforeTurnTrigger" or "BeforeTurnEndTrigger").ToArray();
        if (ticks.Length != 1) return [];
        var tick = ticks[0];
        if (!Literal(tick.Element("OwnerLocation"), "3") || !Literal(tick.Element("IsAmbushing"), "False")) return [];
        var playerFilter = tick.Element("PlayerIdFilter");
        if (playerFilter == null) return [];
        var playerEdges = connections.Where(c => (int?)c.Attribute("DstId") == Id(playerFilter)).ToArray();
        if (playerEdges.Length != 1 || !nodes.Any(n => Type(n) == "GetOwnerPlayerIdNode"
            && (int?)n.Element("Out")?.Attribute("Id") == (int?)playerEdges[0].Attribute("SrcId"))) return [];
        var elapsed = nodes.Single(n => Type(n) == "CardTimerElapsedTrigger");
        if (elapsed.Element("Card") is not { } elapsedCard || Incoming(elapsedCard) || !Literal(elapsed.Element("Forced"), "False")) return [];

        bool cooldown = Int(template, "InitialTimer") == -1 && (string?)template.Element("TapActive") == "True"
            && Int(template, "InitalTapCharges") == 1 && timers.Length == 2;
        bool initial = Int(template, "InitialTimer") is >= 1 and <= 99 && timers.Length == 1
            && (string?)template.Element("TapActive") == "False";
        if (!cooldown && !initial) return [];
        var reset = cooldown ? timers.SingleOrDefault(n => Literal(n.Element("Operation"), "2")) : null;
        if (cooldown && (reset == null || !From(reset, "TappedTrigger")
            || nodes.Count(n => Type(n) == "TappedTrigger") != 1)) return [];
        // Do not accept opaque subgraphs or secondary ways of changing the order's charges.
        if (nodes.Any(n => Type(n) == "UseSubAbilityNode" && (!initial || !IndependentLineage(n, graph)))) return [];
        if (cooldown)
        {
            var refill = nodes.Where(n => Type(n) == "ChangeTapCardChargesNode").ToArray();
            if (refill.Length != 1 || !Self(refill[0].Element("Targets")) || !Literal(refill[0].Element("Operation"), "2")
                || !Literal(refill[0].Element("Value"), "1") || !From(refill[0], "CardTimerElapsedTrigger")) return [];
        }
        string keyword = cooldown ? "cooldown" : "timer";
        var texts = AvailableLanguages.Where(l => HasText(id, l, "tooltip")).Select(l => Text(id, l, "tooltip")).ToArray();
        // Locate the semantic alias in English; other locales use different word order and punctuation.
        var match = Regex.Match(Text(id, "en-us", "tooltip"), @"<keyword=" + keyword + @">[^<]*</keyword>\s*:?\s*\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.IgnoreCase);
        if (!match.Success || texts.Length == 0) return [];
        string name = match.Groups[1].Value;
        if (texts.Any(text => !text.Contains("{" + name + "}", StringComparison.Ordinal)
            || text.Contains("{" + name + "-", StringComparison.OrdinalIgnoreCase))) return [];
        var variables = graph.Elements().Where(e => e.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
            .SelectMany(e => e.Elements()).Where(e => (string?)e.Attribute("Name") == name).ToArray();
        if (variables.Length != 1 || variables[0].Parent?.Name.LocalName != "TemporaryVariables") return [];
        var alias = variables[0];
        if (Type(alias) != "IntVar" || !int.TryParse((string?)alias.Attribute("V"), out int value) || value is < 1 or > 99
            || initial && value != Int(template, "InitialTimer")
            || (string?)alias.Attribute("ResetInInactive") == "True") return [];
        int aliasId = Id(alias);
        bool Referenced(int variableId, HashSet<XAttribute>? permitted = null) => graph.DescendantsAndSelf().Attributes().Any(a =>
                permitted?.Contains(a) != true &&
                (a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName is "SrcId" or "DstId")
                && int.TryParse(a.Value, out int target) && target == variableId)
            || graph.Descendants("CompareVarID").Any(e => int.TryParse(e.Value, out int target) && target == variableId)
            || graph.Descendants().Any(e => Type(e) == "IntReferenceVar" && (int?)e.Attribute("V") == variableId);
        if (aliasId <= 0) return [];
        int[] graphValues = [];
        if (cooldown)
        {
            var input = reset!.Element("Value")!;
            var edges = connections.Where(c => (int?)c.Attribute("DstId") == Id(input)).ToArray();
            if (edges.Length == 0)
            {
                if (Referenced(aliasId) || Referenced(Id(input))
                    || !Literal(input, value.ToString(System.Globalization.CultureInfo.InvariantCulture))) return [];
                graphValues = [Id(input)];
            }
            else
            {
                if (edges.Length != 1) return [];
                var reader = nodes.SingleOrDefault(n => Type(n) == "GetVarNode" && (int?)n.Attribute("VarId") == aliasId
                    && (int?)n.Element("Out")?.Attribute("Id") == (int?)edges[0].Attribute("SrcId"));
                if (reader?.Element("Out") is not { } output || Type(output) != "IntVar") return [];
                var permitted = new HashSet<XAttribute> { reader.Attribute("VarId")!, edges[0].Attribute("SrcId")!, edges[0].Attribute("DstId")! };
                if (Referenced(aliasId, permitted) || Referenced(Id(output), permitted) || Referenced(Id(input), permitted)) return [];
            }
        }
        else if (Referenced(aliasId)) return [];
        return [new(initial ? -2 : -3, initial ? "initial-timer" : "cooldown", value, aliasId,
            graphValues)];
    }

    private IReadOnlyList<(TimingSetting Setting, int Value)> TimingEdits(int id, IReadOnlyDictionary<int, int>? edits)
    {
        var result = new List<(TimingSetting, int)>();
        foreach (var edit in edits?.Where(e => e.Key is -2 or -3 or -4) ?? [])
        {
            var setting = Timing(id).SingleOrDefault(s => s.Id == edit.Key) ?? throw new InvalidOperationException("InitialValueLocked");
            if (edit.Value is < 1 or > 99) throw new InvalidOperationException("AbilityRange");
            result.Add((setting, edit.Value));
        }
        return result;
    }
}
