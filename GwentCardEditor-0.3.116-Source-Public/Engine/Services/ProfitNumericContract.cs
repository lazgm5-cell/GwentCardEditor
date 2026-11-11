using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

internal static class ProfitNumericContract
{
    public static XElement? Input(XElement ability, XElement node)
    {
        // Built-in profit subability contract, also used by AddProfitSubAbility.
        // This is not an InjectableAbility lookup: those IDs are a separate namespace.
        if ((string?)node.Attribute("Type") != "UseSubAbilityNode" || (string?)node.Element("SubAbilityID") != "16") return null;
        var connections = ability.Element("Connections")?.Elements().ToArray() ?? [];
        bool External(XElement e) => new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)e.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        bool Wired(XElement e) => connections.Any(c => (int?)c.Attribute("DstId") == (int?)e.Attribute("Id"));
        var flows = node.Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        if (flows.Length != 2 || flows.Any(m => (string?)m.Element("Data")?.Attribute("Type") != "FlowConnector")) return null;
        var input = flows.FirstOrDefault(m => (string?)m.Element("IdOfDataInAbility") == "1" && (string?)m.Element("IsInput") == "True")?.Element("Data");
        var output = flows.FirstOrDefault(m => (string?)m.Element("IdOfDataInAbility") == "14" && (string?)m.Element("IsInput") == "False")?.Element("Data");
        if (input == null || output == null || External(input) || External(output)) return null;
        var incoming = connections.Where(c => (int?)c.Attribute("DstId") == (int?)input.Attribute("Id")).ToArray();
        if (incoming.Length != 1) return null;
        int? triggerOut = (int?)incoming[0].Attribute("SrcId");
        var trigger = ability.Element("Nodes")?.Elements().FirstOrDefault(n => (string?)n.Attribute("Type") == "IncomeTrigger" && (int?)n.Element("FlowOut")?.Attribute("Id") == triggerOut);
        var forced = trigger?.Element("Forced");
        if (forced == null || (string?)forced.Attribute("Type") != "BoolVar" || External(forced) || Wired(forced)
            || !bool.TryParse((string?)forced.Attribute("V"), out bool isForced) || isForced) return null;
        var mappings = node.Element("VarMappings")?.Elements().ToArray() ?? [];
        if (mappings.Length is < 1 or > 2 || mappings.Select(m => (string?)m.Element("IdOfDataInAbility")).Distinct().Count() != mappings.Length) return null;
        XElement? amount = null;
        foreach (var mapping in mappings)
        {
            var data = mapping.Element("Data");
            if (data == null || External(data) || (string?)mapping.Element("IsInput") != "True") return null;
            switch ((string?)mapping.Element("IdOfDataInAbility"))
            {
                case "9" when (string?)data.Attribute("Type") == "IntVar": amount = data; break;
                case "28" when (string?)data.Attribute("Type") == "BoolVar" && !Wired(data)
                    && bool.TryParse((string?)data.Attribute("V"), out bool disloyal) && !disloyal: break;
                default: return null;
            }
        }
        return amount;
    }
}
