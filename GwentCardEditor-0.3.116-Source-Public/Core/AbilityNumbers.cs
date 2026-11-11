using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;
using AbilityAnalyzer.Models;

namespace GwentCardEditor;

public sealed record AbilityNumber(int Id, string Role, string Name, int Value, int Minimum, int Maximum,
    bool Editable, string Reason, string Context, int Uses, bool TextLinked)
{
    public int DisplayOffset { get; init; }
    public string? LabelRole { get; init; }
    public string? HelpKey { get; init; }
    public IReadOnlyList<AbilityMeaningFact> MeaningFacts { get; init; } = AbilityMeaningFactList.Empty;
    public int DisplayValue => ToDisplayValue(Value);
    public int ToDisplayValue(int stored) => checked(stored + DisplayOffset);
    public int ToStoredValue(int displayed) => checked(displayed - DisplayOffset);
}

public static class AbilityNumbers
{
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
        { "amount", "value", "count", "counter", "cost", "power-change", "ability-power" };
    private static string Family(string role) => role switch
    {
        "min-targets" or "max-targets" => "targets", "min-choices" or "max-choices" => "choices",
        "min-template-choices" or "max-template-choices" => "template-choices",
        "min-row-targets" or "max-row-targets" => "row-targets", _ => role
    };

    public static IReadOnlyList<AbilityNumber> Inspect(XElement ability, bool shared, string tooltip)
    {
        var raw = SafeNumericEditorService.InspectGraph(ability);
        var elements = ability.Descendants().Where(x => (int?)x.Attribute("Id") != null)
            .GroupBy(x => (int)x.Attribute("Id")!).ToDictionary(g => g.Key, g => g.First());
        XElement[] nodes = ability.Element("Nodes")?.Elements().ToArray() ?? [];
        XElement[] connections = ability.Element("Connections")?.Elements().ToArray() ?? [];
        PowerValueSemantics.Classify(raw, nodes, connections);
        var terminals = raw.Where(v => v.NodeId.HasValue && v.VariableId.HasValue).ToDictionary(v => v.VariableId!.Value);
        var additiveBases = AdditiveBases(nodes, connections, terminals);
        var alternativeInputs = AlternativeEffectContract.Inputs(ability, terminals);
        var feeReferences = PaymentNumericContract.FeeReferences(ability);
        var adrenalineUses = ThresholdNumericContract.AdrenalineUses(ability);
        bool graphErrors = GraphValidator.Validate(ability).ErrorCount > 0;
        var results = new List<AbilityNumber>();
        foreach (var value in raw)
        {
            if (!value.VariableId.HasValue) continue;
            int id = value.VariableId.Value;
            XElement element = elements[id];
            bool named = element.Parent?.Name.LocalName is "TemporaryVariables" or "PersistentVariables";
            string role = value.Role;
            var consumers = new List<NumericValueDescriptor>();
            bool ambiguous = false;
            bool mutable = nodes.Any(n => (string?)n.Attribute("Type") != "GetVarNode"
                && n.Attributes().Any(a => a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(a.Value, out int reference) && reference == id));
            if (named)
            {
                foreach (var reference in ability.Descendants("CompareVarID").Where(e => int.TryParse(e.Value, out int target) && target == id))
                {
                    if (adrenalineUses.TryGetValue(reference, out var threshold)) consumers.Add(threshold);
                    else ambiguous = true;
                }
                foreach (var reference in ability.Descendants().Where(e => (string?)e.Attribute("Type") == "IntReferenceVar"
                    && int.TryParse((string?)e.Attribute("V"), out int target) && target == id))
                {
                    if (feeReferences.TryGetValue((int?)reference.Attribute("Id") ?? -1, out var fee)) consumers.Add(fee);
                    else ambiguous = true;
                }
                if (ability.Attributes().Any(a => a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(a.Value, out int reference) && reference == id)) ambiguous = true;
                var sources = nodes.Where(n => (int?)n.Attribute("VarId") == id && (string?)n.Attribute("Type") == "GetVarNode")
                    .Select(n => (int?)n.Element("Out")?.Attribute("Id")).Where(x => x.HasValue).Select(x => x!.Value).Append(id).ToHashSet();
                foreach (var connection in connections.Where(c => sources.Contains((int?)c.Attribute("SrcId") ?? -1)))
                {
                    int destination = (int?)connection.Attribute("DstId") ?? -1;
                    if ((terminals.TryGetValue(destination, out var terminal) || additiveBases.TryGetValue(destination, out terminal)
                            || alternativeInputs.TryGetValue(destination, out terminal))
                        && connections.Count(c => (int?)c.Attribute("DstId") == destination) == 1) consumers.Add(terminal);
                    else ambiguous = true;
                }
                string[] families = consumers.Select(c => Family(c.Role)).Distinct().ToArray();
                if (consumers.Any(c => c.Role.StartsWith("base-", StringComparison.Ordinal)
                    || c.Role.StartsWith("alternate-", StringComparison.Ordinal)) && consumers.Count != 1) ambiguous = true;
                if (families.Length == 1) role = families[0] is "targets" or "choices" or "template-choices" or "row-targets" ? families[0] : consumers[0].Role;
                else ambiguous = true;
            }
            else consumers.Add(value);
            bool recognized = !role.StartsWith("variable-", StringComparison.Ordinal) && !Generic.Contains(role);
            int displayOffset = 0;
            string name = (string?)element.Attribute("Name") ?? value.FieldName ?? "";
            if (role == "adrenaline-threshold")
            {
                string[] aliases = Regex.Matches(tooltip, @"\{([^{}]+)\}").Select(m => m.Groups[1].Value)
                    .Where(s => s.Equals(name, StringComparison.OrdinalIgnoreCase) || s.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (name.Length == 0 || aliases.Length != 1 || !aliases[0].Equals(name + "-1", StringComparison.OrdinalIgnoreCase)) ambiguous = true;
                else displayOffset = -1;
            }
            string reason = role is "timer-change" or "row-timer-change" or "timer-limit" or "cooldown" or "ability-timer" ? "InitialValueLocked"
                : role == "charge-change" && nodes.Any(n => (string?)n.Attribute("Type") == "CardTimerElapsedTrigger") ? "InitialValueLocked"
                : shared ? "SharedAbility" : graphErrors ? "GraphHasErrors"
                : !value.Editable ? "ComputedValue" : mutable ? "RuntimeVariable"
                : (string?)element.Attribute("ExternalOut") == "True" ? "ExternalValue"
                : !recognized || ambiguous ? "AmbiguousValue" : consumers.Count == 0 ? "UnusedValue" : "";
            if (value.Role.StartsWith("variable-", StringComparison.Ordinal) && !recognized) continue;
            int min = value.MinimumValue, max = value.MaximumValue;
            if (consumers.Count > 0)
            {
                min = Math.Max(min, consumers.Max(c => c.MinimumValue));
                max = Math.Min(max, consumers.Min(c => c.MaximumValue));
            }
            if (reason.Length == 0 && (role is "fee-cost" or "tribute-cost" or "berserk-threshold" or "adrenaline-threshold" or "copy-count" or "intimidate-boost"
                    or "armor-gain" or "armor-loss" or "set-armor" or "strengthen" or "base-power-loss" or "set-base-power" or "set-power")
                && (value.CurrentValue < min || value.CurrentValue > max)) reason = "AbilityRange";
            bool linked = named && !string.IsNullOrWhiteSpace(name) && tooltip.Contains("{" + name + (displayOffset == -1 ? "-1" : "") + "}", StringComparison.OrdinalIgnoreCase);
            var meaning = AbilityNumberMeaning.Inspect(ability, consumers.Select(c => c.NodeId).Where(n => n.HasValue).Select(n => n!.Value), role);
            if (!meaning.Locked && !linked && role == "power-limit" && value.CurrentValue == 0)
                meaning = new(Locked: true, LockReason: "ImplicitPowerCondition");
            if (!meaning.Locked && !linked && role == "charges-limit" && value.CurrentValue == 0)
                meaning = new(Locked: true, LockReason: "ImplicitChargeCondition");
            if (!meaning.Locked && !linked && role == "power-level" && value.CurrentValue == 1)
                meaning = new(Locked: true, LockReason: "PowerExtremumSelection");
            if (meaning.Locked && reason.Length == 0) reason = meaning.LockReason;
            results.Add(new(id, role, name, value.CurrentValue, min, max, reason.Length == 0, reason,
                Context(nodes, connections, elements, consumers.Select(c => c.NodeId).Where(n => n.HasValue).Select(n => n!.Value)),
                consumers.Count, linked) { DisplayOffset = displayOffset, LabelRole = meaning.LabelRole, HelpKey = meaning.HelpKey,
                    MeaningFacts = AbilityMeaningTrace.Inspect(ability, consumers.Select(c => c.NodeId).OfType<int>()) });
        }
        return results;
    }

    private static Dictionary<int, NumericValueDescriptor> AdditiveBases(XElement[] nodes, XElement[] connections, Dictionary<int, NumericValueDescriptor> terminals)
    {
        var result = new Dictionary<int, NumericValueDescriptor>();
        foreach (var node in nodes.Where(n => (string?)n.Attribute("Type") == "IntPlusNode"))
        {
            var a = node.Element("A"); var b = node.Element("B"); var output = node.Element("Result");
            if (new[] { a, b, output }.Any(e => e == null || (string?)e.Attribute("Type") != "IntVar" || (int?)e.Attribute("Id") == null
                || new[] { "ExternalIn", "ExternalOut" }.Any(k => string.Equals((string?)e.Attribute(k), "True", StringComparison.OrdinalIgnoreCase)))) continue;
            int outId = (int)output!.Attribute("Id")!;
            var uses = connections.Where(c => (int?)c.Attribute("SrcId") == outId).ToArray();
            if (uses.Length != 1 || connections.Any(c => (int?)c.Attribute("DstId") == outId)
                || !terminals.TryGetValue((int?)uses[0].Attribute("DstId") ?? -1, out var target)
                || target.Role is not ("profit" or "damage" or "boost")) continue;
            if (target.Role != "profit" && !PowerEffectContract.IsCurrentPowerEffect(nodes, connections, target)) continue;
            if (connections.Count(c => (int?)c.Attribute("DstId") == target.VariableId) != 1) continue;
            foreach (var field in new[] { a!, b! })
            {
                int id = (int)field.Attribute("Id")!;
                if (connections.Count(c => (int?)c.Attribute("DstId") == id) != 1) continue;
                result[id] = new NumericValueDescriptor { Role = "base-" + target.Role,
                    NodeId = target.Role == "profit" ? (int?)node.Attribute("Id") : target.NodeId,
                    VariableId = id, MinimumValue = 0, MaximumValue = target.Role == "profit" ? 9 : 9999 };
            }
        }
        return result;
    }

    internal static string ContextFor(XElement ability, IEnumerable<int> consumers) => Context(
        ability.Element("Nodes")?.Elements().ToArray() ?? [], ability.Element("Connections")?.Elements().ToArray() ?? [],
        ability.Descendants().Where(e => (int?)e.Attribute("Id") != null).GroupBy(e => (int)e.Attribute("Id")!)
            .ToDictionary(g => g.Key, g => g.First()), consumers);

    private static string Context(XElement[] nodes, XElement[] connections, Dictionary<int, XElement> elements, IEnumerable<int> consumers)
    {
        var nodeLookup = nodes.Where(n => (int?)n.Attribute("Id") != null).ToDictionary(n => (int)n.Attribute("Id")!);
        var pending = new Queue<int>(consumers.Distinct());
        var visited = new HashSet<int>();
        var triggers = new HashSet<string>();
        while (pending.TryDequeue(out int id))
        {
            if (!visited.Add(id) || !nodeLookup.TryGetValue(id, out var node)) continue;
            string type = (string?)node.Attribute("Type") ?? "";
            if (type.EndsWith("Trigger", StringComparison.Ordinal)) triggers.Add(type == "PlayedTrigger" ? "Deploy" : type == "TappedTrigger" ? "Order" : "Reaction");
            var inputs = node.Descendants().Where(e => (string?)e.Attribute("Type") == "FlowConnector").Select(e => (int?)e.Attribute("Id")).ToHashSet();
            foreach (var connection in connections.Where(c => inputs.Contains((int?)c.Attribute("DstId"))))
                if (elements.TryGetValue((int?)connection.Attribute("SrcId") ?? -1, out var source)
                    && (string?)source.Attribute("Type") == "FlowConnector" && (int?)source.Ancestors().FirstOrDefault(e => e.Parent?.Name.LocalName == "Nodes")?.Attribute("Id") is int parent)
                    pending.Enqueue(parent);
            if (inputs.Count == 0)
            {
                var outputs = node.Descendants().Select(e => (int?)e.Attribute("Id")).OfType<int>().ToHashSet();
                foreach (var connection in connections.Where(c => outputs.Contains((int?)c.Attribute("SrcId") ?? -1)))
                    if (elements.TryGetValue((int?)connection.Attribute("DstId") ?? -1, out var destination)
                        && (int?)destination.Ancestors().FirstOrDefault(e => e.Parent?.Name.LocalName == "Nodes")?.Attribute("Id") is int consumer)
                        pending.Enqueue(consumer);
            }
        }
        return triggers.Count == 1 ? triggers.Single() : triggers.Count > 1 ? "MultipleEffects" : "Ability";
    }

    public static XElement Prepare(XElement template, XElement ability, IReadOnlyDictionary<int, int> changes, bool shared, string tooltip)
    {
        var descriptors = Inspect(ability, shared, tooltip).ToDictionary(v => v.Id);
        foreach (var (id, value) in changes)
        {
            if (!descriptors.TryGetValue(id, out var descriptor)) throw new InvalidOperationException("AmbiguousValue");
            if (!descriptor.Editable) throw new InvalidOperationException(descriptor.Reason);
            if (value < descriptor.Minimum || value > descriptor.Maximum) throw new InvalidOperationException("AbilityRange");
        }
        var preview = new XElement(ability);
        foreach (var (id, value) in changes)
            preview.Descendants().Single(x => (string?)x.Attribute("Type") == "IntVar" && (int?)x.Attribute("Id") == id).SetAttributeValue("V", value);
        ValidateEffectivePairs(preview);
        XElement next = SafeNumericEditorService.PrepareGraphEdit(template, ability, changes);
        ValidateEffectivePairs(next);
        return next;
    }

    private static void ValidateEffectivePairs(XElement ability)
    {
        var nodes = ability.Element("Nodes")?.Elements().ToArray() ?? [];
        var connections = ability.Element("Connections")?.Elements().ToArray() ?? [];
        var elements = ability.Descendants().Where(x => (int?)x.Attribute("Id") != null)
            .GroupBy(x => (int)x.Attribute("Id")!).ToDictionary(g => g.Key, g => g.First());
        int? Resolve(XElement? field)
        {
            if (field == null) return null;
            int id = (int?)field.Attribute("Id") ?? -1;
            var incoming = connections.Where(c => (int?)c.Attribute("DstId") == id).ToArray();
            if (incoming.Length == 0) return int.TryParse((string?)field.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
            if (incoming.Length != 1 || !elements.TryGetValue((int?)incoming[0].Attribute("SrcId") ?? -1, out var source)) return null;
            if ((string?)source.Parent?.Attribute("Type") != "GetVarNode") return null;
            if (!elements.TryGetValue((int?)source.Parent.Attribute("VarId") ?? -1, out var variable)) return null;
            return int.TryParse((string?)variable.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;
        }
        foreach (var node in nodes)
            foreach (var (min, max) in new[] { ("MinTargets", "MaxTargets"), ("MinChoices", "MaxChoices") })
                if (Resolve(node.Element(min)) is int a && Resolve(node.Element(max)) is int b && a > b)
                    throw new InvalidOperationException("InvalidTargetLimits");
    }
}
