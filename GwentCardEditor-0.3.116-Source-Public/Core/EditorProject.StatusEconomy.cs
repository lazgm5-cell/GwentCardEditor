using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private readonly Dictionary<int, bool> initialStatusEconomyDefinitions = [];

    private bool HasIndependentStatusEconomyDefinition(int id)
    {
        if (initialStatusEconomyDefinitions.TryGetValue(id, out bool verified)) return verified;
        string? expected = id switch
        {
            16 => "F797C37A9B0F5C98210F97798A84E21CE7211159759DA231CA4FDB85603E100F",
            18 => "C7FF6C9D58507452D7F4411E47E288540BBC154C35CA04FDE9D3B34C2E6CD524",
            _ => null
        };
        if (expected == null) return false;
        EnsureAbilities();
        var definitions = originalAbilityDocument?.Document.Root?.Elements("Ability")
            .Where(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == id).ToArray() ?? [];
        // Only the audited native Profit and Tribute implementations are independent of status bits.
        verified = definitions.Length == 1 && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            XElement.Parse(definitions[0].ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting))))
                == expected;
        initialStatusEconomyDefinitions[id] = verified;
        return verified;
    }

    private bool IsInitialStatusEconomyNode(XElement node, XElement graph)
    {
        string type = (string?)node.Attribute("Type") ?? "";
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        bool Wired(XElement field) => edges.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id"));
        bool Field(string name, string fieldType, string? argument = null) => node.Elements(name).Count() == 1
            && !node.Element(name)!.HasElements && (string?)node.Element(name)!.Attribute("Type") == fieldType
            && (string?)node.Element(name)!.Attribute("GArg") == argument;
        if (type == "IncomeTrigger")
            return node.Elements().Count() == 3 && Field("Priority", "EnumVar`1", "ETriggerPriority")
                && Field("FlowOut", "FlowConnector") && Field("Forced", "BoolVar")
                && (string?)node.Element("Priority")!.Attribute("V") == "0" && !Wired(node.Element("Priority")!)
                && bool.TryParse((string?)node.Element("Forced")!.Attribute("V"), out bool forced) && !forced && !Wired(node.Element("Forced")!);
        if (type == "ChangeOwnerPayActiveNode")
            return node.Elements().Count() == 3 && Field("FlowIn", "FlowConnector") && Field("FlowOut", "FlowConnector")
                && Field("Active", "BoolVar") && bool.TryParse((string?)node.Element("Active")!.Attribute("V"), out _);
        if (type == "PaidForTrigger")
        {
            if (node.Elements().Count() != 5 || !Field("Priority", "EnumVar`1", "ETriggerPriority")
                || !Field("Spent", "IntVar") || !Field("Madness", "BoolVar")
                || !int.TryParse((string?)node.Element("Spent")!.Attribute("V"), out _)
                || new[] { "Costs", "SwitchOut" }.Any(name => node.Elements(name).Count() != 1 || node.Element(name)!.HasAttributes
                    || node.Element(name)!.Elements().Count() != 1)) return false;
            var reference = node.Element("Costs")!.Elements().Single(); var output = node.Element("SwitchOut")!.Elements().Single();
            if (reference.HasElements || output.HasElements || reference.Attribute("GArg") != null || output.Attribute("GArg") != null
                || !int.TryParse((string?)reference.Attribute("Id"), out int referenceId)
                || !PaymentNumericContract.FeeReferences(graph).ContainsKey(referenceId)) return false;
            int variableId = int.Parse((string)reference.Attribute("V")!, CultureInfo.InvariantCulture);
            var variables = new[] { "TemporaryVariables", "PersistentVariables" }.SelectMany(name => graph.Element(name)?.Elements() ?? [])
                .Where(v => (int?)v.Attribute("Id") == variableId).ToArray();
            return variables.Length == 1 && IsStatusScalar(variables[0], "IntVar")
                && int.TryParse((string?)variables[0].Attribute("V"), out int cost) && cost is >= 1 and <= 9;
        }
        if (type != "UseSubAbilityNode" || node.Elements().Count() != 3
            || new[] { "SubAbilityID", "ConnectionMappings", "VarMappings" }.Any(name => node.Elements(name).Count() != 1)
            || node.Element("SubAbilityID")!.HasElements || (string?)node.Element("SubAbilityID") is not ("16" or "18")) return false;
        foreach (var mapping in node.Element("ConnectionMappings")!.Elements().Concat(node.Element("VarMappings")!.Elements()))
        {
            if (mapping.Elements().Count() != 3 || new[] { "Data", "IdOfDataInAbility", "IsInput" }.Any(name => mapping.Elements(name).Count() != 1)
                || mapping.Elements().Any(e => e.HasElements)) return false;
            var data = mapping.Element("Data")!;
            if (data.Attribute("GArg") != null || (string?)data.Attribute("Type") == "IntVar"
                && !int.TryParse((string?)data.Attribute("V"), out _)) return false;
        }
        int subAbility = int.Parse(node.Element("SubAbilityID")!.Value, CultureInfo.InvariantCulture);
        var amount = subAbility == 16 ? ProfitNumericContract.Input(graph, node) : PaymentNumericContract.TributeInput(graph, node);
        if (amount == null || !HasIndependentStatusEconomyDefinition(subAbility)) return false;
        var flows = node.Element("ConnectionMappings")!.Elements().ToArray();
        int input = (int)flows.Single(m => (string?)m.Element("IsInput") == "True").Element("Data")!.Attribute("Id")!;
        var outputs = flows.Where(m => (string?)m.Element("IsInput") == "False")
            .Select(m => (int)m.Element("Data")!.Attribute("Id")!).ToHashSet();
        return !edges.Any(c => (int?)c.Attribute("SrcId") == input
            || (int?)c.Attribute("DstId") is int destination && outputs.Contains(destination));
    }
}
