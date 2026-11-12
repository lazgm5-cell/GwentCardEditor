using System.Xml.Linq;

namespace GwentCardEditor;

public sealed record AbilityMeaningFact(string Kind, string Value);

internal sealed class AbilityMeaningFactList(IEnumerable<AbilityMeaningFact> values) : IReadOnlyList<AbilityMeaningFact>, IEquatable<AbilityMeaningFactList>
{
    private readonly AbilityMeaningFact[] items = values.ToArray();
    public static AbilityMeaningFactList Empty { get; } = new([]);
    public int Count => items.Length;
    public AbilityMeaningFact this[int index] => items[index];
    public IEnumerator<AbilityMeaningFact> GetEnumerator() => ((IEnumerable<AbilityMeaningFact>)items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public bool Equals(AbilityMeaningFactList? other) => other != null && items.SequenceEqual(other.items);
    public override bool Equals(object? obj) => obj is AbilityMeaningFactList other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var item in items) hash.Add(item); return hash.ToHashCode(); }
}

internal static class AbilityMeaningTrace
{
    public static IReadOnlyList<AbilityMeaningFact> Inspect(XElement graph, IEnumerable<int> consumerIds)
    {
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var elements = nodes.SelectMany(n => n.DescendantsAndSelf()).Where(e => (int?)e.Attribute("Id") != null)
            .GroupBy(e => (int)e.Attribute("Id")!).ToDictionary(g => g.Key, g => g.First());
        var ids = consumerIds.ToHashSet();
        var targets = nodes.Where(n => ids.Contains((int?)n.Attribute("Id") ?? -1)).ToArray();
        var facts = new List<AbilityMeaningFact>();
        string Type(XElement? e) => (string?)e?.Attribute("Type") ?? "";
        XElement? Node(XElement? e) => e?.AncestorsAndSelf().FirstOrDefault(n => n.Parent?.Name.LocalName == "Nodes");
        XElement? Source(XElement? field)
        {
            if (field == null) return null;
            var links = edges.Where(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray();
            return links.Length == 1 ? elements.GetValueOrDefault((int?)links[0].Attribute("SrcId") ?? -1) : null;
        }
        string? Literal(XElement? field) => field != null && Source(field) == null
            && !edges.Any(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id"))
            && (string?)field.Attribute("ExternalIn") != "True" ? (string?)field.Attribute("V") : null;
        void Fact(string kind, string? value) { if (value != null) facts.Add(new(kind, value)); }
        string? Scope(XElement? field, HashSet<int> seen)
        {
            var source = Source(field); var node = Node(source);
            if (node == null || !seen.Add((int?)node.Attribute("Id") ?? -1)) return null;
            return Type(node) switch
            {
                "GetAbilityOwnerNode" or "GetAbilityOwnerCardNode" or "GetAbilityOwnerCardListNode" => "Self",
                "RequestCardTargetsNode" when source!.Name.LocalName is "SelectedTargets" or "TargetsInShape" => "ChosenTarget",
                "RequestCardChoiceNode" when source!.Name.LocalName == "SelectedChoices" => "ChosenCard",
                "CardListGetRandomElementNode" or "CardListGetRandomElementsNode" => "Random",
                "SpawnCardNode" when source!.Name.LocalName == "SpawnedCards" => "Spawned",
                "CardListElementToListNode" or "CardElementToListNode" => Scope(node.Element("DS"), seen),
                _ => null
            };
        }
        string? Action(XElement node, XElement field) => (Type(node), field.Name.LocalName) switch
        {
            ("ChangePowerNode", "Targets") => (Literal(node.Element("TargetedPower")), Literal(node.Element("Operation"))) switch
            {
                ("0", "0") => "boost", ("0", "1") => "damage", ("0", "2") => "set-power", ("0", "4") => "heal",
                ("1", "0") => "strengthen", ("1", "1") => "base-power-loss", ("1", "2") => "set-base-power",
                ("3", "0") => "armor-gain", ("3", "1") => "armor-loss", ("3", "2") => "set-armor", _ => null
            },
            ("DestroyCardsNode", "Targets") or ("DestroyCardsNode", "Cards") or ("DestroyCardNode", "Card") => "Destroy",
            ("BanishCardsNode", "Targets") or ("BanishCardsNode", "Cards") => "Banish",
            ("MoveCardsNode", "Cards") => "Move",
            ("PlayCardNode", "Card") or ("PlayCardsNode", "CardsToPlay") or ("PlayCardsNode", "Cards") => "Play",
            ("ChangeTokensNode", "Targets") => "Status",
            ("BalanceCardStatusTimerNode", "Cards") => "StatusDuration",
            ("TransformCardsNode", "Targets") => "Transform",
            ("ResetCardsNode", "Targets") => "Reset",
            ("ChangeCardTimerNode", "Cards") => "Timer",
            ("ChangeTapCardSummoningSicknessNode", "Targets") => "OrderReadiness",
            ("SpawnCardNode", "Definition") => "Spawn",
            ("DrawCardsNode", "ValidCards") => "Draw",
            ("ChangeArmorNode", "Targets") => "armor-change",
            ("ChangeTapCardChargesNode", "Targets") => "charge-change",
            _ => null
        };
        void Follow(XElement? output)
        {
            if (output == null) return;
            var pending = new Queue<int>(); pending.Enqueue((int?)output.Attribute("Id") ?? -1);
            var seen = new HashSet<int>();
            while (pending.TryDequeue(out int id))
            {
                if (!seen.Add(id)) continue;
                foreach (var edge in edges.Where(e => (int?)e.Attribute("SrcId") == id))
                {
                    var field = elements.GetValueOrDefault((int?)edge.Attribute("DstId") ?? -1); var node = Node(field);
                    if (field == null || node == null) continue;
                    Fact("Action", Action(node, field));
                    if ((string?)node.Attribute("Type") == "MoveCardsNode" && field.Name.LocalName == "Cards"
                        && CardMoveMeaning.Inspect(graph, node) is { } move)
                    {
                        Fact("MovePlace", move.Place);
                        Fact("MovePlayer", move.Player);
                    }
                    string? pass = (Type(node), field.Name.LocalName) switch
                    {
                        ("CardListElementToListNode", "DS") or ("CardElementToListNode", "DS") => "DV",
                        ("CardListGetRandomElementNode", "List") => "Element",
                        ("CardListGetElementNode", "DS") => "DV",
                        ("CardDefinitionListGetElementNode", "DS") or ("GetCardDefinitionNode", "DS") => "DV",
                        ("FilterCardListNode", "Cards") => "FilteredCards",
                        ("CardListForeachNode", "List") => "Element",
                        ("SetVarNode", "In") => "Out",
                        _ => null
                    };
                    if (pass != null && (int?)node.Element(pass)?.Attribute("Id") is int next) pending.Enqueue(next);
                    if (Type(node) == "SetVarNode" && field.Name.LocalName == "In" && (int?)node.Attribute("VarId") is int variable
                        && nodes.Count(n => (int?)n.Attribute("VarId") == variable && Type(n) != "GetVarNode") == 1)
                        foreach (var getter in nodes.Where(n => Type(n) == "GetVarNode" && (int?)n.Attribute("VarId") == variable))
                            if ((int?)getter.Element("Out")?.Attribute("Id") is int getterOutput) pending.Enqueue(getterOutput);
                }
            }
        }
        foreach (var node in targets)
        {
            string type = Type(node);
            if (type is "FilterCardPowerNode" or "FilterCardProvisionNode" or "FilterCardDefinitionListProvision" or "FilterTapCardChargesNode")
            {
                string? comparison = Literal(node.Element("Op")) switch { "0" => "=", "1" => "!=", "2" => "<", "3" => ">", "4" => "<=", "5" => ">=", _ => null };
                string? negate = node.Element("Negate") == null ? "False" : Literal(node.Element("Negate"));
                if (comparison != null && negate == "True") comparison = comparison switch { "=" => "!=", "!=" => "=", "<" => ">=", ">" => "<=", "<=" => ">", _ => "<" };
                if (negate != null) Fact("Comparison", comparison);
                if (type == "FilterCardPowerNode") Fact("PowerType", Literal(node.Element("PowerType")) switch { "0" => "Current", "1" => "Base", "3" => "Armor", _ => null });
            }
            if (type is "ChangeTapCardChargesNode" or "ChangeArmorNode")
                Fact("Operation", Literal(node.Element("Operation")) switch
                {
                    "0" => type == "ChangeTapCardChargesNode" ? "Remove" : "Add",
                    "1" => type == "ChangeTapCardChargesNode" ? "Add" : "Remove", "2" => "Set", _ => null
                });
            Fact("Target", Scope(node.Element("Targets") ?? node.Element("Cards"), []));
            if (type is "RequestCardTargetsNode" or "RequestCardChoiceNode" or "RequestTemplateChoiceNode")
            {
                Follow(node.Element(type == "RequestCardTargetsNode" ? "SelectedTargets" : "SelectedChoices"));
                if (type == "RequestCardTargetsNode") Follow(node.Element("TargetsInShape"));
            }
            if (type is "DrawCardsNode" or "RevealCardsNode")
            {
                Fact("Selection", Literal(node.Element("Random")) switch { "True" => "Random", "False" => "Ordered", _ => null });
                var player = Node(Source(node.Element("PlayerId")));
                Fact("Player", Type(player) switch { "GetOwnerPlayerIdNode" => "Owner", "GetOwnerOpponentPlayerIdNode" => "Opponent", _ => null });
            }
            if (type == "SpawnCardNode")
            {
                var definition = node.Element("Definition");
                if (Source(definition) == null && (int?)definition?.Element("V")?.Attribute("TemplateId") is int template && template > 0)
                    Fact("Template", template.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var sourceNode = Node(Source(definition));
                if (Type(sourceNode) == "GetVarNode" && (int?)sourceNode!.Attribute("VarId") is int variable
                    && !nodes.Any(n => (int?)n.Attribute("VarId") == variable && Type(n) != "GetVarNode"))
                {
                    var value = graph.Descendants().FirstOrDefault(e => (int?)e.Attribute("Id") == variable);
                    if (value?.Parent?.Name.LocalName is "TemporaryVariables" or "PersistentVariables"
                        && (string?)value.Attribute("ExternalIn") != "True"
                        && (int?)value.Element("V")?.Attribute("TemplateId") is int fixedTemplate && fixedTemplate > 0)
                        Fact("Template", fixedTemplate.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        return new AbilityMeaningFactList(facts.Distinct());
    }
}
