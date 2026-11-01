using System.Xml.Linq;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static bool IsInitialStatusTimerNode(XElement node, XElement graph)
    {
        if ((string?)node.Attribute("Type") != "BalanceCardStatusTimerNode") return false;
        var fields = new List<(string Name, string Type, string? Argument)>
        {
            ("FlowIn", "FlowConnector", null), ("FlowOut", "FlowConnector", null),
            ("Attacker", "EntityVar", null), ("Cards", "CardListVar", null),
            ("ElementType", "EnumVar`1", "EElementType"), ("AttackMethod", "EnumVar`1", "EAttackMethod"),
            ("Acceptor", "CardTokensVar", null), ("Donor", "CardTokensVar", null), ("DeltaTime", "IntVar", null)
        };
        if (node.Elements("EffectType").Any()) fields.Add(("EffectType", "EnumVar`1", "EEffectType"));
        if (node.Elements().Count() != fields.Count) return false;
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        foreach (var (name, type, argument) in fields)
        {
            var matches = node.Elements(name).ToArray();
            if (matches.Length != 1) return false;
            var field = matches[0];
            if (field.HasElements || !string.IsNullOrWhiteSpace(field.Value)
                || (string?)field.Attribute("Type") != type || (string?)field.Attribute("GArg") != argument
                || (int?)field.Attribute("Id") is not > 0) return false;
            int id = (int)field.Attribute("Id")!;
            if (edges.Any(e => (int?)e.Attribute(name == "FlowOut" ? "DstId" : "SrcId") == id)) return false;
            bool constant = argument != null || name is "Acceptor" or "Donor";
            if (constant || name == "DeltaTime")
            {
                if (!int.TryParse((string?)field.Attribute("V"), out int value)
                    || constant && edges.Any(e => (int?)e.Attribute("DstId") == id)) return false;
                bool known = name switch
                {
                    "ElementType" => value is 1 or 512 or 2048 or 16384 or 65536,
                    "AttackMethod" or "EffectType" => value is 0 or 1,
                    "Acceptor" => value is 0 or 16 or 32,
                    "Donor" => value is 16 or 32,
                    "DeltaTime" => true,
                    _ => false
                };
                if (!known) return false;
            }
            else if (field.Attribute("V") != null) return false;
        }
        int acceptor = (int)node.Element("Acceptor")!.Attribute("V")!;
        int donor = (int)node.Element("Donor")!.Attribute("V")!;
        // Only the audited bleeding/vitality pair and their removal layouts are covered.
        return acceptor == 0 || acceptor != donor;
    }
}
