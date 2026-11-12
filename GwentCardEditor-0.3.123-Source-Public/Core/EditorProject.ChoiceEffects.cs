using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private bool BarnabasContract(int id) => id == 202107 && CurrentAbility(id) is { } graph
        && EffectFingerprint(graph, 893) == "E97708C5218970B912E99FBD9A0E25E9F20FEC32EA0AC6D7B13CB727172F22C7";
    private static bool NatureChoice(int id) => id is 202700 or 202701 or 202702;
    private static int ChoiceBoost(int id) => 1130 + id - 202700;
    private static XElement Scalar(XElement graph, int id) => graph.Descendants().Single(e => (int?)e.Attribute("Id") == id);
    private static int? ScalarValue(XElement graph, int id) => int.TryParse((string?)Scalar(graph, id).Attribute("V"), out int v) ? v : null;

    private bool NatureContract()
    {
        int[] ids = [202674, 202700, 202701, 202702];
        string[] hashes = ["8608D49CDEEBD3D5312953A71EDC19BF47ECFACA23050F98F13E5C4E62D40BB1",
            "7AB82AA87834900018E12538E89FB7137F5DB110A32768A0698EAACAC1DB9CBF",
            "E46113D9D63293FBD2712574EE2CF36558E063043F0F471D47350F9F18F85515",
            "E032BB9BD996DD1A23409FA6E5E99450B887F1DE2DD64C7C3637692805DB723C"];
        for (int i = 0; i < ids.Length; i++)
        {
            if (!index.ContainsKey(ids[i]) || !Card(ids[i]).Editable || SharedAbility(ids[i])) return false;
            var graph = CurrentAbility(ids[i]);
            int[] fields = i switch { 0 => [1130, 1131, 1132, 1133, 1150, 1151], 3 => [1, 2], _ => [1] };
            if (graph == null || EffectFingerprint(graph, fields) != hashes[i]) return false;
            if (fields.Any(f => ScalarValue(graph, f) is not (>= 0 and <= 9999))) return false;
        }
        var parent = CurrentAbility(202674)!;
        return ids.Skip(1).All(id => ScalarValue(CurrentAbility(id)!, 1) == ScalarValue(parent, ChoiceBoost(id)))
            && ScalarValue(CurrentAbility(202702)!, 2) == ScalarValue(parent, 1133)
            && ScalarValue(parent, 1133) <= 99
            && ScalarValue(parent, 1150) == ScalarValue(parent, 1151) && ScalarValue(parent, 1150) is >= 1 and <= 99;
    }

    private IReadOnlyList<AbilityNumber> NatureNumbers(int id)
    {
        if (!NatureContract()) return [];
        var graph = CurrentAbility(id)!;
        var result = new List<AbilityNumber>
        {
            new(1, "boost", "Boost", ScalarValue(graph, 1)!.Value, 0, 9999, true, "", "Deploy", 1, true),
            new(-10, "targets", "SharedTargets", ScalarValue(CurrentAbility(202674)!, 1150)!.Value, 1, 99, true, "", "Deploy", 1, false)
                { LabelRole = "nature-shared-targets", HelpKey = "NatureSharedTargets" }
        };
        if (id == 202702) result.Add(new(2, "vitality-duration", "Vitality", ScalarValue(graph, 2)!.Value, 0, 99, true, "", "Deploy", 1, true));
        return result;
    }

    // Choice cards have tooltip-only graphs. Keep their aliases and the executable
    // parent graph synchronized in the same preview/apply/undo transaction.
    private Dictionary<int, XElement> PrepareNature(int id, IReadOnlyDictionary<int, int>? edits)
    {
        var result = new Dictionary<int, XElement>();
        if (!NatureChoice(id) || edits == null || edits.Count == 0) return result;
        var settings = NatureNumbers(id).ToDictionary(n => n.Id);
        var parent = new XElement(CurrentAbility(202674) ?? throw new InvalidOperationException("ComputedEffectUnsupported"));
        var child = new XElement(CurrentAbility(id) ?? throw new InvalidOperationException("ComputedEffectUnsupported"));
        foreach (var (field, value) in edits)
        {
            if (!settings.TryGetValue(field, out var setting)) throw new InvalidOperationException("ComputedEffectUnsupported");
            if (value < setting.Minimum || value > setting.Maximum) throw new InvalidOperationException("AbilityRange");
            if (field == -10)
            {
                Scalar(parent, 1150).SetAttributeValue("V", value);
                Scalar(parent, 1151).SetAttributeValue("V", value);
            }
            else
            {
                Scalar(child, field).SetAttributeValue("V", value);
                Scalar(parent, field == 1 ? ChoiceBoost(id) : 1133).SetAttributeValue("V", value);
            }
        }
        result[id] = child;
        result[202674] = parent;
        return result;
    }
}
