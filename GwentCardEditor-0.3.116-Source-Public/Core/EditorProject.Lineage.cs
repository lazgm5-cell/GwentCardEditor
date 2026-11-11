using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private bool IndependentLineage(XElement node, XElement graph)
    {
        if ((int?)node.Element("SubAbilityID") != 53) return false;
        EnsureAbilities();
        var definitions = originalAbilityDocument!.Document.Root!.Elements("Ability")
            .Where(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == 53).ToArray();
        // This exact implementation only boosts power from the Lineage blackboard and emits its message.
        if (definitions.Length != 1 || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            XElement.Parse(definitions[0].ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting)))) != "153F4585D4A8F2F26A69DBBBC5B3A43ACF7AC66FF256929A9DE10A68F2DCACF4") return false;
        var mappings = node.Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        if (mappings.Length != 1 || (string?)mappings[0].Element("IsInput") != "True"
            || (int?)mappings[0].Element("IdOfDataInAbility") != 271
            || (string?)mappings[0].Element("Data")?.Attribute("Type") != "FlowConnector"
            || (int?)mappings[0].Element("Data")?.Attribute("Id") is not int port) return false;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var incoming = edges.Where(e => (int?)e.Attribute("DstId") == port).ToArray();
        return incoming.Length == 1 && !edges.Any(e => (int?)e.Attribute("SrcId") == port)
            && graph.Element("Nodes")!.Elements().Any(n => (string?)n.Attribute("Type") == "AfterGameInitializedTrigger"
                && (int?)n.Element("FlowOut")?.Attribute("Id") == (int?)incoming[0].Attribute("SrcId"));
    }
}
