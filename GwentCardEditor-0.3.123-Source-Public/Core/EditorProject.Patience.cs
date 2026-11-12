using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

public sealed record InitialPatienceSetting(int VariableId, int Value);

public sealed partial class EditorProject
{
    private bool? patienceSubVerified;

    private bool VerifiedPatienceSub()
    {
        if (patienceSubVerified.HasValue) return patienceSubVerified.Value;
        EnsureAbilities();
        var matches = originalAbilityDocument!.Document.Root!.Elements().Where(e => Type(e) == "SubAbility"
            && (int?)e.Attribute("AbilityID") == 40).ToArray();
        if (matches.Length != 1) return (patienceSubVerified = false).Value;
        var copy = new XElement(matches[0]);
        copy.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "X" or "Y").Remove();
        string canonical = XElement.Parse(copy.ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting);
        return (patienceSubVerified = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            == "FD689A3AEFA9060C34A732D76F27487A8E35FDB675FBEA4E2086B6CDAB442746").Value;
    }

    public InitialPatienceSetting? InitialPatience(int id)
    {
        var graph = CurrentAbility(id);
        if (!Card(id).Editable || Card(id).Type == 1 || graph == null || SharedAbility(id)) return null;
        var r = new GraphMeaningReader(graph);
        var calls = r.Nodes.Where(n => Type(n) == "UseSubAbilityNode" && (int?)n.Element("SubAbilityID") == 40).ToArray();
        if (calls.Length != 1 || !VerifiedPatienceSub() || GraphValidator.Validate(graph).ErrorCount > 0) return null;
        var mappings = calls[0].Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        if (mappings.Length != 3 || calls[0].Element("VarMappings")?.HasElements == true) return null;
        XElement? Mapping(int key, bool input)
        {
            var matches = mappings.Where(m => (int?)m.Element("IdOfDataInAbility") == key
                && (string?)m.Element("IsInput") == (input ? "True" : "False")).ToArray();
            return matches.Length == 1 && Type(matches[0].Element("Data")) == "FlowConnector" ? matches[0].Element("Data") : null;
        }
        if (Mapping(18, true) == null || Mapping(47, false) == null || Mapping(44, false) is not { } success) return null;
        var writers = r.Nodes.Where(n => Type(n) == "SetVarNode" && r.Linked(success, n.Element("FlowIn"))).ToArray();
        if (writers.Length != 1 || (int?)writers[0].Attribute("VarId") is not int variableId) return null;
        var writer = writers[0];
        var variables = graph.Element("PersistentVariables")?.Elements().Where(v => (int?)v.Attribute("Id") == variableId).ToArray() ?? [];
        if (variables.Length != 1 || Type(variables[0]) != "IntVar"
            || !int.TryParse((string?)variables[0].Attribute("V"), out int value) || value is < 0 or > 99
            || variables[0].Attributes().Any(a => a.Name.LocalName is "ExternalIn" or "ExternalOut" or "ResetInInactive"
                && a.Value.Equals("True", StringComparison.OrdinalIgnoreCase))) return null;
        if (r.Nodes.Count(n => Type(n) != "GetVarNode" && n.Attributes().Any(a => a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(a.Value, out int reference) && reference == variableId)) != 1) return null;
        var sum = r.Producer(writer.Element("In"));
        var previous = r.Producer(sum?.Element("A"));
        var increment = r.Producer(sum?.Element("B"));
        if (Type(sum) != "IntPlusNode" || Type(previous) != "GetVarNode" || (int?)previous!.Attribute("VarId") != variableId
            || Type(increment) != "GetBlackboardVarNode" || (int?)increment!.Attribute("VarId") != 14
            || !r.Fixed(increment.Element("BlackboardType"), "2")) return null;
        string? name = (string?)variables[0].Attribute("Name");
        if (string.IsNullOrEmpty(name) || !AvailableLanguages.Any(language => Text(id, language, "tooltip")
            .Contains("{" + name + "}", StringComparison.OrdinalIgnoreCase))) return null;
        return new(variableId, value);
    }

    private (InitialPatienceSetting Setting, int Value)? InitialPatienceEdit(int id, IReadOnlyDictionary<int, int> edits)
    {
        if (!edits.TryGetValue(-6, out int value)) return null;
        var setting = InitialPatience(id) ?? throw new InvalidOperationException("InitialValueLocked");
        if (value is < 0 or > 99) throw new InvalidOperationException("AbilityRange");
        return (setting, value);
    }
}
