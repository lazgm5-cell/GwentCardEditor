using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static bool IsInitialStatusMathNode(XElement node, XElement graph)
    {
        (string[] Inputs, string Output)? schema = (string?)node.Attribute("Type") switch
        {
            "IntMinNode" or "IntMaxNode" => (new[] { "A", "B" }, "Result"),
            "IntAbsNode" => (new[] { "DS" }, "DV"),
            "GetCurrentMoneyNode" => (Array.Empty<string>(), "Out"),
            _ => null
        };
        if (schema == null) return false;
        var (inputs, output) = schema.Value;
        var names = inputs.Append(output).ToArray();
        if (node.Elements().Count() != names.Length || names.Any(name => node.Elements(name).Count() != 1)
            || node.Elements().Any(field => !IsStatusScalar(field, "IntVar")
                || (int?)field.Attribute("Id") is not > 0 || !string.IsNullOrWhiteSpace(field.Value))) return false;
        var inputIds = inputs.Select(name => (int)node.Element(name)!.Attribute("Id")!).ToHashSet();
        int outputId = (int)node.Element(output)!.Attribute("Id")!;
        return !(graph.Element("Connections")?.Elements() ?? []).Any(link =>
            (int?)link.Attribute("DstId") == outputId
            || (int?)link.Attribute("SrcId") is int source && inputIds.Contains(source));
    }
}
