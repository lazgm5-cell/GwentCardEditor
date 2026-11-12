using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record VerifiedEffectSetting(int Id, string Role, int Value, int Minimum, int Maximum, int TextVariableId, string Context)
{
    public IReadOnlyDictionary<int, int> StoredValues(int value)
    {
        var values = new Dictionary<int, int> { [TextVariableId] = value };
        if (Id == -8) { values[70] = value - 1; values[71] = value - 2; }
        if (Id == -9) values[75] = checked(value * 2);
        return values;
    }
}

public sealed partial class EditorProject
{
    private readonly Dictionary<int, bool> verifiedEffectSubs = new();

    private static string EffectFingerprint(XElement graph, params int[] numbers)
    {
        var copy = new XElement(graph);
        copy.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "X" or "Y").Remove();
        foreach (var field in copy.Descendants().Where(e => (string?)e.Attribute("Type") == "IntVar"
            && numbers.Contains((int?)e.Attribute("Id") ?? -1))) field.SetAttributeValue("V", "editable");
        string canonical = XElement.Parse(copy.ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private bool VerifiedEffectSub(int type, string hash)
    {
        if (verifiedEffectSubs.TryGetValue(type, out bool verified)) return verified;
        EnsureAbilities();
        var subs = originalAbilityDocument!.Document.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "SubAbility"
            && (int?)e.Attribute("AbilityID") == type).ToArray();
        return verifiedEffectSubs[type] = subs.Length == 1 && EffectFingerprint(subs[0]) == hash;
    }

    public IReadOnlyList<VerifiedEffectSetting> VerifiedEffects(int id)
    {
        if (id is not (202417 or 202503 or 200228 or 202383) || !Card(id).Editable || SharedAbility(id)) return [];
        var graph = CurrentAbility(id);
        if (graph == null || GraphValidator.Validate(graph).ErrorCount > 0) return [];
        int? Value(int key) => int.TryParse((string?)graph.Descendants().SingleOrDefault(e => (int?)e.Attribute("Id") == key)?.Attribute("V"), out int value) ? value : null;
        if (id == 202383)
            return GordCounterContract(graph) && Value(551) is int cap && cap is >= 0 and <= 9999
                ? [new(-12, "gord-boost-cap", cap, 0, 9999, 551, "Deploy")] : [];
        if (id == 200228)
            return EffectFingerprint(graph, 764) == "8E775EE101E6A43595033D343EAC31F144E89F7F8AC8FD134D8B4C5546DE5D62"
                && Value(764) is int damage && damage is >= 0 and <= 99
                ? [new(-11, "spring-total-damage", damage, 0, 99, 764, "Reaction")] : [];
        // These native graphs share mutable state between effects. Freeze their control flow
        // and expose only synchronized initializers, never the runtime counters themselves.
        if (id == 202417)
        {
            if (EffectFingerprint(graph, 54, 69, 70, 71, 93, 94) != "73C1D30719F6596596C92F2C5BB89AAFA5CC27C6787A57D6AB9D819BC66A1916"
                || !VerifiedEffectSub(3, "B5F4F241FA22782DB4929A88FF023F5A00F5ACD3CF41EA37D524D428114E80F3")
                || Value(54) is not int first || first is < 0 or > 9999
                || Value(69) is not int start || start is < 2 or > 9999
                || Value(70) != start - 1 || Value(71) != start - 2) return [];
            return [new(-7, "first-target-damage", first, 0, 9999, 54, "Deploy"),
                new(-8, "random-series-start", start, 2, 9999, 69, "Deploy")];
        }
        if (EffectFingerprint(graph, 39, 74, 75) != "EF826BE9FAC36822D4524341554A6D3985FDD3B7B0AC47FED0DC064AD6DFAAAA"
            || !VerifiedEffectSub(24, "3B0E699186D1BD1464C72DF39320579143B1D6D6DC551C20E4F0336A4FBBA3E3")
            || Value(74) is not int boost || boost is < 0 or > 4999 || Value(75) != boost * 2) return [];
        return [new(-9, "linked-elephant-boost", boost, 0, 4999, 74, "Order")];
    }

    private Dictionary<int, int> VerifiedEffectEdits(int id, IReadOnlyDictionary<int, int> edits)
    {
        var writes = new Dictionary<int, int>();
        foreach (var edit in edits.Where(e => e.Key is -7 or -8 or -9 or -11 or -12))
        {
            var setting = VerifiedEffects(id).SingleOrDefault(s => s.Id == edit.Key) ?? throw new InvalidOperationException("ComputedEffectUnsupported");
            if (edit.Value < setting.Minimum || edit.Value > setting.Maximum) throw new InvalidOperationException("AbilityRange");
            foreach (var (variable, value) in setting.StoredValues(edit.Value)) writes[variable] = value;
        }
        return writes;
    }
}
