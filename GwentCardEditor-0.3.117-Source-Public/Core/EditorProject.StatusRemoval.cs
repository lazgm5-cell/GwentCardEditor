using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static bool IsInitialStatusRemovalNode(XElement node, XElement graph)
    {
        string? type = (string?)node.Attribute("Type");
        if (type is not ("DestroyCardsNode" or "BanishCardsNode")) return false;
        var fields = new List<(string Name, string Type, string? Argument)>
        {
            ("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null),
            ("Attacker", "EntityVar", null), ("Targets", "CardListVar", null),
            ("ElementType", "EnumVar`1", "EElementType"), ("AttackMethod", "EnumVar`1", "EAttackMethod")
        };
        // Two native Destroy layouts exist; Banish always serializes EffectType.
        if (type == "BanishCardsNode" || node.Elements("EffectType").Any())
            fields.Add(("EffectType", "EnumVar`1", "EEffectType"));
        if (type == "DestroyCardsNode") fields.Add(("RemovalType", "EnumVar`1", "ERemovalType"));
        if (node.Elements().Count() != fields.Count) return false;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        foreach (var (name, fieldType, argument) in fields)
        {
            var matches = node.Elements(name).ToArray();
            if (matches.Length != 1) return false;
            var field = matches[0];
            if (field.HasElements || !string.IsNullOrWhiteSpace(field.Value)
                || (string?)field.Attribute("Type") != fieldType || (string?)field.Attribute("GArg") != argument
                || (int?)field.Attribute("Id") is not > 0) return false;
            int id = (int)field.Attribute("Id")!;
            if (edges.Any(e => (int?)e.Attribute(name == "FlowOut" ? "DstId" : "SrcId") == id)) return false;
            if (argument != null)
            {
                if (!int.TryParse((string?)field.Attribute("V"), out int value)
                    || edges.Any(e => (int?)e.Attribute("DstId") == id)) return false;
                bool known = name switch
                {
                    "ElementType" => type == "BanishCardsNode" ? value is 1 or 16 or 1024
                        : value is 1 or 16 or 1024 or 8192 or 16384 or 65536,
                    "AttackMethod" => type == "BanishCardsNode" ? value == 0 : value is >= 0 and <= 3,
                    "EffectType" => value == 0,
                    "RemovalType" => value is 0 or 1,
                    _ => false
                };
                if (!known) return false;
            }
            else if (field.Attribute("V") != null) return false;
        }
        return true;
    }
}
