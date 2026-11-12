using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static int[]? AdditiveRefillValues(XElement graph, XElement alias)
    {
        string Type(XElement? e) => (string?)e?.Attribute("Type") ?? "";
        int Id(XElement? e) => (int?)e?.Attribute("Id") ?? -1;
        var nodes = graph.Element("Nodes")!.Elements().ToArray();
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var writers = nodes.Where(n => Type(n) == "ChangeTapCardChargesNode").ToArray();
        if (writers.Length != 1 || nodes.Count(n => Type(n) == "AfterMovedTrigger") != 1
            || nodes.Any(n => Type(n) == "PlayedTrigger") || alias.Parent?.Name.LocalName != "TemporaryVariables"
            || Type(alias) != "IntVar" || Id(alias) <= 0 || (string?)alias.Attribute("ResetInInactive") == "True"
            || !int.TryParse((string?)alias.Attribute("V"), out int value) || value is < 1 or > 9) return null;
        var writer = writers[0];
        if ((string?)writer.Element("Operation")?.Attribute("GArg") != "ETapChargesOperation"
            || (string?)writer.Element("Operation")?.Attribute("V") != "1"
            || edges.Any(e => (int?)e.Attribute("DstId") == Id(writer.Element("Operation")))
            || Type(writer.Element("Value")) != "IntVar") return null;
        var targetEdges = edges.Where(e => (int?)e.Attribute("DstId") == Id(writer.Element("Targets"))).ToArray();
        if (targetEdges.Length != 1 || !nodes.Any(n => Type(n) == "GetAbilityOwnerCardListNode"
            && Id(n.Element("Out")) == (int?)targetEdges[0].Attribute("SrcId"))) return null;
        var visited = new HashSet<int>();
        bool FromPlayEvent(XElement node)
        {
            if (!visited.Add(Id(node))) return false;
            if (Type(node) == "AfterMovedTrigger") return true;
            if (Type(node).EndsWith("Trigger", StringComparison.Ordinal)) return false;
            var incoming = edges.Where(e => (int?)e.Attribute("DstId") == Id(node.Element("FlowIn"))).ToArray();
            if (incoming.Length != 1) return false;
            var parent = nodes.SingleOrDefault(n => n.Elements().Any(e => Type(e) == "FlowConnector" && Id(e) == (int?)incoming[0].Attribute("SrcId")));
            return parent != null && FromPlayEvent(parent);
        }
        if (!FromPlayEvent(writer)) return null;
        var permitted = new HashSet<XAttribute>();
        int aliasId = Id(alias), inputId = Id(writer.Element("Value"));
        var data = edges.Where(e => (int?)e.Attribute("DstId") == inputId).ToArray();
        int outputId = -1;
        if (data.Length == 0)
        {
            if ((int?)writer.Element("Value")?.Attribute("V") != value) return null;
        }
        else
        {
            if (data.Length != 1) return null;
            var reader = nodes.SingleOrDefault(n => Type(n) == "GetVarNode" && (int?)n.Attribute("VarId") == aliasId
                && Id(n.Element("Out")) == (int?)data[0].Attribute("SrcId"));
            if (reader != null)
            {
                if (Type(reader.Element("Out")) != "IntVar") return null;
                outputId = Id(reader.Element("Out"));
                permitted.Add(reader.Attribute("VarId")!);
            }
            else
            {
                // One charge per matching card: preserve the computed event count, never expose it as a base value.
                var count = nodes.SingleOrDefault(n => Type(n) == "CardListLengthNode" && Id(n.Element("DV")) == (int?)data[0].Attribute("SrcId"));
                if (count == null || value != 1) return null;
                var lists = edges.Where(e => (int?)e.Attribute("DstId") == Id(count.Element("DS"))).ToArray();
                if (lists.Length != 1 || !nodes.Any(n => Type(n) == "FilterCardListNode"
                    && Id(n.Element("FilteredCards")) == (int?)lists[0].Attribute("SrcId")
                    && edges.Any(e => (int?)e.Attribute("SrcId") == Id(n.Element("FlowSuccess"))
                        && (int?)e.Attribute("DstId") == Id(writer.Element("FlowIn"))))) return null;
                outputId = Id(count.Element("DV"));
            }
            permitted.UnionWith([data[0].Attribute("SrcId")!, data[0].Attribute("DstId")!]);
        }
        var protectedIds = new[] { aliasId, inputId, outputId }.Where(v => v > 0).ToHashSet();
        if (graph.DescendantsAndSelf().Attributes().Any(a => !permitted.Contains(a)
            && (a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase) || a.Name.LocalName is "SrcId" or "DstId")
            && int.TryParse(a.Value, out int reference) && protectedIds.Contains(reference))
            || graph.Descendants("CompareVarID").Any(e => int.TryParse(e.Value, out int reference) && protectedIds.Contains(reference))
            || graph.Descendants().Any(e => Type(e) == "IntReferenceVar" && int.TryParse((string?)e.Attribute("V"), out int reference)
                && protectedIds.Contains(reference))) return null;
        return [aliasId, inputId];
    }
}
