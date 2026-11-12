using System.Xml.Linq;
using AbilityAnalyzer.Models;

namespace GwentCardEditor;

internal static class AlternativeEffectContract
{
    internal static Dictionary<int, NumericValueDescriptor> Inputs(XElement graph, Dictionary<int, NumericValueDescriptor> terminals)
    {
        var result = new Dictionary<int, NumericValueDescriptor>();
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        int Id(XElement? e) => (int?)e?.Attribute("Id") ?? -1;
        string Type(XElement e) => (string?)e.Attribute("Type") ?? "";
        bool Incoming(XElement e) => edges.Any(c => (int?)c.Attribute("DstId") == Id(e));
        bool InternalInteger(XElement? e) => e != null && Type(e) == "IntVar" && Id(e) > 0 && !e.HasElements
            && !new[] { "ExternalIn", "ExternalOut", "ResetInInactive" }.Any(a =>
                string.Equals((string?)e.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        foreach (var setter in nodes.Where(n => Type(n) == "SetVarNode"))
        {
            if ((int?)setter.Attribute("VarId") is not int targetId) continue;
            var variables = graph.Element("TemporaryVariables")?.Elements().Where(e => Id(e) == targetId).ToArray() ?? [];
            if (variables.Length != 1 || !InternalInteger(variables[0]) || Incoming(variables[0])) continue;
            var input = setter.Element("In"); var output = setter.Element("Out");
            if (!InternalInteger(input) || !InternalInteger(output) || Incoming(output!)
                || edges.Any(c => (int?)c.Attribute("SrcId") == Id(output))
                || setter.Elements().Any(e => e.Name.LocalName is not ("FlowIn" or "FlowOut" or "In" or "Out"))
                || setter.Elements("In").Count() != 1 || setter.Elements("Out").Count() != 1
                || edges.Count(c => (int?)c.Attribute("DstId") == Id(input)) != 1
                || edges.Count(c => (int?)c.Attribute("DstId") == Id(setter.Element("FlowIn"))) != 1) continue;

            var readers = nodes.Where(n => Type(n) == "GetVarNode" && (int?)n.Attribute("VarId") == targetId).ToArray();
            if (readers.Any(n => n.Elements().Count() != 1 || !InternalInteger(n.Element("Out")) || Incoming(n.Element("Out")!))) continue;
            var permittedReferences = readers.Select(n => n.Attribute("VarId")!).Append(setter.Attribute("VarId")!).ToHashSet();
            // The state variable must not also act as a counter, comparison input or external reference.
            if (graph.DescendantsAndSelf().Attributes().Any(a => !permittedReferences.Contains(a)
                    && (a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                        || a.Name.LocalName.EndsWith("VariableId", StringComparison.OrdinalIgnoreCase))
                    && int.TryParse(a.Value, out int value) && value == targetId)
                || graph.Descendants("CompareVarID").Any(e => int.TryParse(e.Value, out int value) && value == targetId)
                || graph.Descendants().Any(e => Type(e).EndsWith("ReferenceVar", StringComparison.Ordinal)
                    && (int?)e.Attribute("V") == targetId)) continue;
            var sources = readers.Select(n => Id(n.Element("Out"))).Append(targetId).ToHashSet();
            var uses = edges.Where(c => sources.Contains((int?)c.Attribute("SrcId") ?? -1)).ToArray();
            if (uses.Length != 1 || !terminals.TryGetValue((int?)uses[0].Attribute("DstId") ?? -1, out var effect)
                || effect.Role is not ("damage" or "boost")
                || !PowerEffectContract.IsCurrentPowerEffect(nodes, edges, effect)
                || edges.Count(c => (int?)c.Attribute("DstId") == effect.VariableId) != 1) continue;
            result[Id(input)] = new NumericValueDescriptor { Role = "alternate-" + effect.Role, NodeId = effect.NodeId,
                VariableId = Id(input), MinimumValue = 0, MaximumValue = 9999 };
        }
        return result;
    }
}
