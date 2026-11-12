using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

internal static class SubAbilityNumericContract
{
    internal sealed record Number(XElement Field, string Role, string Label, int Minimum, int Maximum);
    private sealed record Shape(int Id, (int Id, bool Input)[] Flows, (int Id, string Type, bool Input)[] Variables,
        int NumericInput, string Role, string Label, int Minimum, int Maximum);
    private static readonly Shape[] Shapes =
    [
        new(17, [(143, true), (136, false)], [(80, "IntVar", true), (139, "CardListVar", true)],
            80, "intimidate-boost", "Усиление устрашения", 0, 99),
        new(30, [(8, true), (11, false)], [(18, "PlayerIdVar", true), (19, "ELocationVar", true),
            (40, "CardDefinitionVar", true), (42, "IntVar", true), (50, "CardListVar", false)],
            42, "copy-count", "Количество создаваемых копий", 1, 9),
        new(30, [(8, true), (11, false), (59, true)], [(18, "PlayerIdVar", true), (19, "ELocationVar", true),
            (40, "CardDefinitionVar", true), (42, "IntVar", true), (50, "CardListVar", false), (63, "CardPositionVar", true)],
            42, "copy-count", "Количество создаваемых копий", 1, 9)
    ];

    public static Number? Inspect(XElement graph, XElement node)
    {
        if ((string?)node.Attribute("Type") != "UseSubAbilityNode" || !int.TryParse((string?)node.Element("SubAbilityID"), out int id)
            || node.Elements().Any(e => e.Name.LocalName is not ("SubAbilityID" or "ConnectionMappings" or "VarMappings"))) return null;
        var flows = node.Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        var variables = node.Element("VarMappings")?.Elements().ToArray() ?? [];
        var shape = Shapes.FirstOrDefault(s => s.Id == id && s.Flows.Length == flows.Length && s.Variables.Length == variables.Length);
        if (shape == null) return null;
        bool External(XElement e) => new[] { "ExternalIn", "ExternalOut" }.Any(a => string.Equals((string?)e.Attribute(a), "True", StringComparison.OrdinalIgnoreCase));
        var expectedFlows = shape.Flows.ToHashSet();
        var inputIds = new HashSet<int>();
        foreach (var flow in flows)
        {
            var data = flow.Element("Data");
            if (data == null || (string?)data.Attribute("Type") != "FlowConnector" || External(data) || (int?)data.Attribute("Id") is not int local
                || !int.TryParse((string?)flow.Element("IdOfDataInAbility"), out int target)
                || !bool.TryParse((string?)flow.Element("IsInput"), out bool input) || !expectedFlows.Remove((target, input))) return null;
            if (input) inputIds.Add(local);
        }
        var links = graph.Element("Connections")?.Elements().Where(c => inputIds.Contains((int?)c.Attribute("DstId") ?? -1)).ToArray() ?? [];
        if (links.Length == 0 || links.Any(c => !graph.Descendants().Any(e => (int?)e.Attribute("Id") == (int?)c.Attribute("SrcId")
            && (string?)e.Attribute("Type") == "FlowConnector"))) return null;
        var expectedVariables = shape.Variables.ToHashSet();
        XElement? number = null;
        foreach (var mapping in variables)
        {
            var data = mapping.Element("Data");
            if (data == null || External(data) || (int?)data.Attribute("Id") == null
                || !int.TryParse((string?)mapping.Element("IdOfDataInAbility"), out int target)
                || !bool.TryParse((string?)mapping.Element("IsInput"), out bool input)
                || !expectedVariables.Remove((target, (string?)data.Attribute("Type") ?? "", input))) return null;
            if (target == shape.NumericInput) number = data;
        }
        return number == null ? null : new(number, shape.Role, shape.Label, shape.Minimum, shape.Maximum);
    }
}
