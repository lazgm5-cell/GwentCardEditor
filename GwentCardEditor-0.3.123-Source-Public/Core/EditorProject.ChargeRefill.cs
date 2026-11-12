using System.Xml.Linq;

namespace GwentCardEditor;

public sealed record ChargeRefillSetting(int Value, int TextVariableId, int? LiteralVariableId);

public sealed partial class EditorProject
{
    public ChargeRefillSetting? ChargeRefill(int id)
    {
        var setting = InitialCharges(id);
        var graph = CurrentAbility(id);
        return graph == null ? null : FixedChargeRefill(graph, setting);
    }

    private static ChargeRefillSetting? FixedChargeRefill(XElement graph, InitialChargeSetting initial)
    {
        if (!initial.Editable || initial.RefillValues is not { Count: 2 } ids) return null;
        var input = graph.Descendants().Single(e => (int?)e.Attribute("Id") == ids[1]);
        var edges = graph.Element("Connections")?.Elements().Where(e => (int?)e.Attribute("DstId") == ids[1]).ToArray() ?? [];
        if (edges.Length != 0 && (edges.Length != 1 || !graph.Element("Nodes")!.Elements().Any(n =>
            (string?)n.Attribute("Type") == "GetVarNode" && (int?)n.Attribute("VarId") == ids[0]
            && (int?)n.Element("Out")?.Attribute("Id") == (int?)edges[0].Attribute("SrcId")))) return null;
        var alias = graph.Descendants().Single(e => (int?)e.Attribute("Id") == ids[0]);
        return new((int)alias.Attribute("V")!, ids[0], edges.Length == 0 ? (int?)input.Attribute("Id") : null);
    }

    private (ChargeRefillSetting Setting, int Value)? ChargeRefillEdit(int id, IReadOnlyDictionary<int, int>? edits)
    {
        if (edits == null || !edits.TryGetValue(-5, out int value)) return null;
        var setting = ChargeRefill(id) ?? throw new InvalidOperationException("InitialValueLocked");
        if (value is < 1 or > 9) throw new InvalidOperationException("AbilityRange");
        return (setting, value);
    }
}
