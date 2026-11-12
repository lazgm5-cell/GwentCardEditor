using System.Globalization;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static bool IsStatusScalar(XElement value, string type) => !value.HasElements
        && (string?)value.Attribute("Type") == type && value.Attribute("GArg") == null
        && (type == "IntVar"
            ? int.TryParse((string?)value.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            : type == "BoolVar" && bool.TryParse((string?)value.Attribute("V"), out _));

    private static bool IsInitialStatusVariableValue(XElement value, string type) => !value.HasElements
        && (string?)value.Attribute("Type") == type && value.Attribute("GArg") == null
        && (type switch
        {
            "IntVar" => int.TryParse((string?)value.Attribute("V"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            "BoolVar" => bool.TryParse((string?)value.Attribute("V"), out _),
            // These types are accepted only as opaque, typed values copied through GetVar/SetVar.
            "CardVar" or "CardListVar" => value.Attribute("V") == null,
            _ => false
        });

    private static bool IsInitialStatusVariableWriter(XElement node, string type) => (string?)node.Attribute("Type") == "SetVarNode"
        && node.Elements().Count() == 4
        && new[] { "FlowIn", "FlowOut", "In", "Out" }.All(name => node.Elements(name).Count() == 1)
        && new[] { "FlowIn", "FlowOut" }.All(name => !node.Element(name)!.HasElements
            && (string?)node.Element(name)!.Attribute("Type") == "FlowConnector" && node.Element(name)!.Attribute("GArg") == null)
        && IsInitialStatusVariableValue(node.Element("In")!, type) && IsInitialStatusVariableValue(node.Element("Out")!, type);

    private static bool IsInitialStatusVariableNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "SetVarNode"
            || !int.TryParse((string?)node.Attribute("VarId"), out int id) || id <= 0) return false;
        var variables = new[] { "TemporaryVariables", "PersistentVariables" }.SelectMany(name => graph.Element(name)?.Elements() ?? [])
            .Where(v => (int?)v.Attribute("Id") == id).ToArray();
        if (variables.Length != 1) return false;
        string type = (string?)variables[0].Attribute("Type") ?? "";
        if (!IsInitialStatusVariableValue(variables[0], type) || !IsInitialStatusVariableWriter(node, type)) return false;

        // A scalar may only be accessed by consistently typed GetVar/SetVar nodes, never by metadata or port references.
        foreach (var reference in graph.DescendantsAndSelf().Attributes().Where(a =>
            (a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                || a.Name.LocalName.EndsWith("VariableId", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName is "SrcId" or "DstId")
            && int.TryParse(a.Value, out int target) && target == id))
        {
            var user = reference.Parent!;
            if (reference.Name.LocalName != "VarId" || user.Parent != graph.Element("Nodes")) return false;
            if ((string?)user.Attribute("Type") == "SetVarNode")
            {
                if (!IsInitialStatusVariableWriter(user, type)) return false;
            }
            else if ((string?)user.Attribute("Type") != "GetVarNode" || user.Elements().Count() != 1
                || user.Elements("Out").Count() != 1 || !IsInitialStatusVariableValue(user.Element("Out")!, type)) return false;
        }
        return !graph.Descendants("CompareVarID").Any(e => int.TryParse(e.Value, out int target) && target == id)
            && !graph.Descendants().Any(e => (string?)e.Attribute("Type") is "IntReferenceVar" or "BoolReferenceVar"
                && int.TryParse((string?)e.Attribute("V"), out int target) && target == id);
    }
}
