using System.Xml.Linq;
using AbilityAnalyzer.Models;

namespace AbilityAnalyzer.Services;

internal static class PaymentNumericContract
{
    private static bool External(XElement field) => new[] { "ExternalIn", "ExternalOut" }
        .Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
    private static XElement[] Connections(XElement graph) => graph.Element("Connections")?.Elements().ToArray() ?? [];
    private static bool Wired(XElement field, XElement[] links) => (int?)field.Attribute("Id") is not int id
        || links.Any(c => (int?)c.Attribute("DstId") == id);

    public static XElement? TributeInput(XElement graph, XElement node)
    {
        // Built-in tribute contract: input 35, paid/declined flow outputs 4/6.
        if ((string?)node.Attribute("Type") != "UseSubAbilityNode" || (string?)node.Element("SubAbilityID") != "18"
            || node.Elements().Any(e => e.Name.LocalName is not ("SubAbilityID" or "ConnectionMappings" or "VarMappings"))) return null;
        var flows = node.Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        if (flows.Length != 3) return null;
        var expected = new HashSet<(int, bool)> { (1, true), (4, false), (6, false) };
        XElement? input = null;
        foreach (var flow in flows)
        {
            var data = flow.Element("Data");
            if (data == null || (string?)data.Attribute("Type") != "FlowConnector" || External(data)
                || (int?)data.Attribute("Id") == null || !int.TryParse((string?)flow.Element("IdOfDataInAbility"), out int mapped)
                || !bool.TryParse((string?)flow.Element("IsInput"), out bool isInput) || !expected.Remove((mapped, isInput))) return null;
            if (isInput) input = data;
        }
        var links = Connections(graph);
        var incoming = links.Where(c => (int?)c.Attribute("DstId") == (int?)input?.Attribute("Id")).ToArray();
        if (incoming.Length != 1 || !graph.Descendants().Any(e => (int?)e.Attribute("Id") == (int?)incoming[0].Attribute("SrcId")
            && (string?)e.Attribute("Type") == "FlowConnector")) return null;
        var mappings = node.Element("VarMappings")?.Elements().ToArray() ?? [];
        if (mappings.Length != 1) return null;
        var mapping = mappings[0]; var amount = mapping.Element("Data");
        return (string?)mapping.Element("IdOfDataInAbility") == "35" && (string?)mapping.Element("IsInput") == "True"
            && amount != null && (string?)amount.Attribute("Type") == "IntVar" && (int?)amount.Attribute("Id") != null
            && !External(amount) ? amount : null;
    }

    public static Dictionary<int, NumericValueDescriptor> FeeReferences(XElement graph)
    {
        var result = new Dictionary<int, NumericValueDescriptor>();
        var links = Connections(graph);
        foreach (var node in graph.Element("Nodes")?.Elements().Where(n => (string?)n.Attribute("Type") == "PaidForTrigger") ?? [])
        {
            if (node.Elements().Any(e => e.Name.LocalName is not ("Priority" or "Costs" or "SwitchOut" or "Spent" or "Madness"))) continue;
            var costs = node.Element("Costs")?.Elements().ToArray() ?? [];
            var outputs = node.Element("SwitchOut")?.Elements().ToArray() ?? [];
            var madness = node.Element("Madness");
            var priority = node.Element("Priority"); var spent = node.Element("Spent");
            if (costs.Length != 1 || outputs.Length != 1 || madness == null
                || priority == null || (string?)priority.Attribute("Type") != "EnumVar`1" || (string?)priority.Attribute("GArg") != "ETriggerPriority"
                || (string?)priority.Attribute("V") != "0" || External(priority) || Wired(priority, links)
                || spent == null || (string?)spent.Attribute("Type") != "IntVar" || External(spent) || Wired(spent, links)
                || (string?)madness.Attribute("Type") != "BoolVar" || External(madness) || Wired(madness, links)
                || !bool.TryParse((string?)madness.Attribute("V"), out bool isMadness) || isMadness) continue;
            var reference = costs[0]; var output = outputs[0];
            if ((string?)reference.Attribute("Type") != "IntReferenceVar" || External(reference) || Wired(reference, links)
                || !int.TryParse((string?)reference.Attribute("V"), out int variable) || variable <= 0
                || (string?)output.Attribute("Type") != "FlowConnector" || External(output) || Wired(output, links)
                || !links.Any(c => (int?)c.Attribute("SrcId") == (int?)output.Attribute("Id"))) continue;
            var declaration = graph.Elements().Where(e => e.Name.LocalName is "TemporaryVariables" or "PersistentVariables")
                .Elements().FirstOrDefault(e => (int?)e.Attribute("Id") == variable && (string?)e.Attribute("Type") == "IntVar");
            if (declaration == null || (int?)reference.Attribute("Id") is not int id) continue;
            result.TryAdd(id, new NumericValueDescriptor { Role = "fee-cost", VariableId = variable, NodeId = (int?)node.Attribute("Id"),
                NodeType = "PaidForTrigger", FieldName = "Costs", MinimumValue = 1, MaximumValue = 9 });
        }
        return result;
    }
}
