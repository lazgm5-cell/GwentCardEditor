using System.Xml.Linq;
using AbilityAnalyzer.Models;

namespace AbilityAnalyzer.Services;

internal static class ThresholdNumericContract
{
    private static bool External(XElement field) => new[] { "ExternalIn", "ExternalOut" }
        .Any(a => string.Equals((string?)field.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));

    public static XElement? BerserkInput(XElement graph, XElement node)
    {
        if ((string?)node.Attribute("Type") != "UseSubAbilityNode" || (string?)node.Element("SubAbilityID") != "9"
            || node.Elements().Any(e => e.Name.LocalName is not ("SubAbilityID" or "ConnectionMappings" or "VarMappings"))) return null;
        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var flows = node.Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        var expected = new HashSet<(int, bool)> { (1, true), (4, false), (45, false) };
        if (flows.Length != 3) return null;
        XElement? input = null;
        foreach (var mapping in flows)
        {
            var data = mapping.Element("Data");
            if (data == null || (string?)data.Attribute("Type") != "FlowConnector" || External(data) || (int?)data.Attribute("Id") == null
                || !int.TryParse((string?)mapping.Element("IdOfDataInAbility"), out int target)
                || !bool.TryParse((string?)mapping.Element("IsInput"), out bool isInput) || !expected.Remove((target, isInput))) return null;
            if (isInput) input = data;
        }
        var incoming = links.Where(c => (int?)c.Attribute("DstId") == (int?)input?.Attribute("Id")).ToArray();
        if (incoming.Length == 0 || incoming.Any(c => !graph.Descendants().Any(e => (int?)e.Attribute("Id") == (int?)c.Attribute("SrcId")
            && (string?)e.Attribute("Type") == "FlowConnector"))) return null;
        var variables = node.Element("VarMappings")?.Elements().ToArray() ?? [];
        if (variables.Length is < 1 or > 2 || variables.Select(v => (string?)v.Element("IdOfDataInAbility")).Distinct().Count() != variables.Length) return null;
        XElement? threshold = null;
        foreach (var mapping in variables)
        {
            var data = mapping.Element("Data");
            if (data == null || External(data) || (int?)data.Attribute("Id") == null || (string?)mapping.Element("IsInput") != "True") return null;
            switch ((string?)mapping.Element("IdOfDataInAbility"))
            {
                case "48" when (string?)data.Attribute("Type") == "IntVar": threshold = data; break;
                case "62" when (string?)data.Attribute("Type") == "BoolVar" && bool.TryParse((string?)data.Attribute("V"), out _)
                    && !links.Any(c => (int?)c.Attribute("DstId") == (int?)data.Attribute("Id")): break;
                default: return null;
            }
        }
        return threshold;
    }

    public static Dictionary<XElement, NumericValueDescriptor> AdrenalineUses(XElement graph)
    {
        var result = new Dictionary<XElement, NumericValueDescriptor>();
        var keywords = graph.Element("Keywords")?.Elements().Where(k => (string?)k.Element("Keyword") == "1").ToArray() ?? [];
        if (keywords.Length == 0) return result;
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var links = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var readers = nodes.Where(n => (string?)n.Element("Keyword")?.Attribute("GArg") == "ECardKeyword"
            && (string?)n.Element("Keyword")?.Attribute("V") == "1").ToArray();
        if (readers.Length == 0 || readers.Any(n => (string?)n.Attribute("Type") != "IsOwnerKeywordActiveAndEmitMessageBranchNode"
            || (string?)n.Element("Keyword")?.Attribute("Type") != "EnumVar`1" || External(n.Element("Keyword")!)
            || links.Any(c => (int?)c.Attribute("DstId") == (int?)n.Element("Keyword")!.Attribute("Id")))) return result;
        var uses = new List<(XElement Reference, int Variable, int Compare)>();
        foreach (var keyword in keywords)
        {
            if ((string?)keyword.Attribute("Type") != "AbilityKeyword"
                || keyword.Elements().Any(e => e.Name.LocalName is not ("Keyword" or "Conditions"))) return result;
            var conditions = keyword.Element("Conditions")?.Elements().ToArray() ?? [];
            if (conditions.Length is < 1 or > 2) return result;
            int comparisons = 0;
            foreach (var condition in conditions)
            {
                if ((string?)condition.Element("Negate") != "False") return result;
                string type = (string?)condition.Attribute("Type") ?? "";
                if (type == "OwnerPositionCondition")
                {
                    if (condition.Elements().Count() != 2 || (string?)condition.Element("ValidLocations") is not ("8" or "256" or "4087")) return result;
                    continue;
                }
                // Blackboard 2/11 is the owner player's hand size in this client contract.
                if (type == "BlackboardIntVarCondition")
                {
                    if (condition.Elements().Count() != 5 || (string?)condition.Element("BlackboardType") != "2" || (string?)condition.Element("VarID") != "11") return result;
                }
                else if (type != "HandSizeCondition" || condition.Elements().Count() != 3) return result;
                var reference = condition.Element("CompareVarID");
                if (reference == null || !int.TryParse(reference.Value, out int variable) || variable <= 0
                    || !int.TryParse((string?)condition.Element("Compare"), out int compare) || compare is < 2 or > 5) return result;
                uses.Add((reference, variable, compare));
                comparisons++;
            }
            if (comparisons != 1) return result;
        }
        if (uses.Select(u => u.Variable).Distinct().Count() != 1) return result;
        int[] operators = uses.Select(u => u.Compare).Distinct().Order().ToArray();
        if (!operators.SequenceEqual(new[] { 2, 4 }) && !operators.SequenceEqual(new[] { 3, 5 })) return result;
        foreach (var use in uses) result[use.Reference] = new NumericValueDescriptor { Role = "adrenaline-threshold",
            VariableId = use.Variable, MinimumValue = 1, MaximumValue = 11 };
        return result;
    }
}
