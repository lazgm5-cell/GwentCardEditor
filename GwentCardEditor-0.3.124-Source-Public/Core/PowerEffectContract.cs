using System.Xml.Linq;
using AbilityAnalyzer.Models;

namespace GwentCardEditor;

internal static class PowerEffectContract
{
    internal static bool IsCurrentPowerEffect(XElement[] nodes, XElement[] edges, NumericValueDescriptor effect)
    {
        var matches = nodes.Where(n => (int?)n.Attribute("Id") == effect.NodeId).ToArray();
        if (matches.Length != 1) return false;
        var node = matches[0];
        if ((string?)node.Attribute("Type") != "ChangePowerNode"
            || (int?)node.Element("Power")?.Attribute("Id") != effect.VariableId) return false;
        bool Fixed(string name, string argument, int expected)
        {
            var field = node.Element(name);
            return node.Elements(name).Count() == 1 && field != null
                && (string?)field.Attribute("Type") == "EnumVar`1" && (string?)field.Attribute("GArg") == argument
                && int.TryParse((string?)field.Attribute("V"), out int actual) && actual == expected
                && !new[] { "ExternalIn", "ExternalOut" }.Any(key => string.Equals((string?)field.Attribute(key), "True", StringComparison.OrdinalIgnoreCase))
                && !edges.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id"));
        }
        return effect.Role is "damage" or "boost" && Fixed("TargetedPower", "EPowerType", 0)
            && Fixed("Operation", "ECardPowerOp", effect.Role == "boost" ? 0 : 1);
    }
}
