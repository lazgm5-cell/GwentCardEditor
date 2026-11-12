using System.Xml.Linq;
using AbilityAnalyzer.Models;

namespace GwentCardEditor;

internal static class PowerValueSemantics
{
    internal static void Classify(IReadOnlyList<NumericValueDescriptor> values, XElement[] nodes, XElement[] edges)
    {
        foreach (var value in values.Where(v => v.NodeId.HasValue && v.VariableId.HasValue))
        {
            var matches = nodes.Where(n => (int?)n.Attribute("Id") == value.NodeId).ToArray();
            if (matches.Length != 1 || (string?)matches[0].Attribute("Type") != "ChangePowerNode"
                || (int?)matches[0].Element("Power")?.Attribute("Id") != value.VariableId) continue;
            var node = matches[0];
            int? Fixed(string name, string argument)
            {
                var field = node.Element(name);
                if (node.Elements(name).Count() != 1 || field == null || field.HasElements
                    || (string?)field.Attribute("Type") != "EnumVar`1" || (string?)field.Attribute("GArg") != argument
                    || !int.TryParse((string?)field.Attribute("V"), out int number)
                    || new[] { "ExternalIn", "ExternalOut" }.Any(key => string.Equals((string?)field.Attribute(key), "True", StringComparison.OrdinalIgnoreCase))
                    || edges.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id"))) return null;
                return number;
            }
            // Values verified against the client's EPowerType and ECardPowerOp enums.
            value.Role = (Fixed("TargetedPower", "EPowerType"), Fixed("Operation", "ECardPowerOp")) switch
            {
                (0, 0) => "boost", (0, 1) => "damage", (0, 2) => "set-power", (0, 4) => "heal",
                (1, 0) => "strengthen", (1, 1) => "base-power-loss", (1, 2) => "set-base-power",
                (3, 0) => "armor-gain", (3, 1) => "armor-loss", (3, 2) => "set-armor",
                _ => "power-change"
            };
            if (value.Role != "power-change") value.MinimumValue = Math.Max(0, value.MinimumValue);
        }
    }
}
