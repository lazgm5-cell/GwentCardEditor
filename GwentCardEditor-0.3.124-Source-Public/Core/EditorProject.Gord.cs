using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private bool SchirruPresentation() => CurrentAbility(142108) is { } graph
        && EffectFingerprint(graph) == "42026DE0351CCD29B6C6C7ECEC0BF1112D3BC6F0003A4AFACFC37297CD12781F";

    private bool GordCounterContract(XElement graph)
    {
        EnsureAbilities();
        if (EffectFingerprint(graph, 551) != "FC2DB5C4517CDCA6FE9214F61FF689EE28848A6EE186F83BEED4A540EBF84F37") return false;
        var all = originalAbilityDocument!.Document.Root!.Elements().ToArray();
        var battles = all.Where(e => (string?)e.Attribute("Type") == "BattleAbility" && (int?)e.Attribute("AbilityID") == 5).ToArray();
        if (battles.Length != 1 || EffectFingerprint(battles[0], 90) != "FF46A54FC988EA367EE6BB5D5151688EAA67E9B4C8443267A18505F8BCDAFEE2"
            || ScalarValue(battles[0], 90) != ScalarValue(OriginalAbility(202383)!, 551)) return false;
        var users = all.Where(e => e.Descendants().Any(n => (string?)n.Attribute("Type") is
            "GetBlackboardVarNode" or "GetPlayerBlackboardVarNode" or "SetBlackboardVarNode" or "SetPlayerBlackboardVarNode"
            && (int?)n.Attribute("VarId") == 25)).ToArray();
        return users.Length == 2 && users.Contains(battles[0]) && users.Contains(OriginalAbility(202383)!);
    }

    private void ExportGordCounter(XDocument document)
    {
        if (!abilityOverrides.TryGetValue(202383, out var graph)
            || ScalarValue(graph, 551) == ScalarValue(OriginalAbility(202383)!, 551)) return;
        if (!GordCounterContract(graph)) throw new InvalidOperationException("ComputedEffectUnsupported");
        var battle = document.Root!.Elements().Single(e => (string?)e.Attribute("Type") == "BattleAbility" && (int?)e.Attribute("AbilityID") == 5);
        Scalar(battle, 90).SetAttributeValue("V", ScalarValue(graph, 551));
    }
}
