using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static readonly HashSet<string> LeaderNumberRoles = new(StringComparer.Ordinal)
    {
        "damage", "boost", "heal", "armor-gain", "armor-loss", "strengthen", "base-power-loss",
        "coin-gain", "bleeding-duration", "vitality-duration",
        "min-targets", "max-targets", "targets", "min-choices", "max-choices", "choices",
        "min-row-targets", "max-row-targets", "spawn-count", "copy-count", "draw-count", "power-limit"
    };

    private IReadOnlyList<AbilityNumber> LeaderNumbers(int id, IEnumerable<AbilityNumber> values)
    {
        var result = values.Select(value => !Card(id).Editable ? value with { Editable = false, Reason = Card(id).BlockReason }
            : !LeaderNumberRoles.Contains(value.Role) ? value with { Editable = false, Reason = "LeaderNumberUnsupported" }
            : value).ToList();
        var charges = InitialCharges(id);
        result.Add(new(-1, "initial-charges", "InitalTapCharges", charges.Value, 1, 9, charges.Editable, charges.Reason, "Order", 1, charges.Editable));
        return result;
    }

    private XElement PrepareLeaderNumbers(int id, XElement graph, IReadOnlyDictionary<int, int> edits)
    {
        var settings = Numbers(id).ToDictionary(n => n.Id);
        foreach (int key in edits.Keys)
            if (!settings.TryGetValue(key, out var setting) || !setting.Editable)
                throw new InvalidOperationException("LeaderNumberUnsupported");
        var chargeEdit = InitialChargeEdit(id, edits);
        var graphEdits = edits.Where(e => e.Key != -1).ToDictionary(e => e.Key, e => e.Value);
        var next = graphEdits.Count == 0 ? new XElement(graph) : AbilityNumbers.Prepare(Template(id), graph, graphEdits, SharedAbility(id),
            string.Join("\n", AvailableLanguages.Select(l => Text(id, l, "tooltip"))));
        if (chargeEdit.HasValue) next.Descendants().Single(e => (int?)e.Attribute("Id") == chargeEdit.Value.VariableId)
            .SetAttributeValue("V", chargeEdit.Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (GraphValidator.Validate(next).ErrorCount > 0) throw new InvalidOperationException("GraphHasErrors");
        return next;
    }
}
