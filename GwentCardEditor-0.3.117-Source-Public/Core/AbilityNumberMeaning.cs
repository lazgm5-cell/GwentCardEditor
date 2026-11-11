using System.Xml.Linq;

namespace GwentCardEditor;

internal static class AbilityNumberMeaning
{
    internal sealed record Meaning(string? LabelRole = null, string? HelpKey = null, bool Locked = false, string LockReason = "SingleRowSelection");

    public static Meaning Inspect(XElement ability, IEnumerable<int> consumers, string role)
    {
        var nodes = ability.Element("Nodes")?.Elements().ToArray() ?? [];
        var connections = ability.Element("Connections")?.Elements().ToArray() ?? [];
        var targets = nodes.Where(n => consumers.Contains((int?)n.Attribute("Id") ?? -1)).ToArray();
        foreach (var target in targets)
            if (ServiceEffectMeaning.LockedReason(ability, target) is string serviceReason)
                return new(Locked: true, LockReason: serviceReason);
        if (role == "reveal-count" && targets.Length == 1 && (string?)targets[0].Attribute("Type") == "RevealCardsNode")
        {
            var r = new GraphMeaningReader(ability); var reveal = targets[0];
            var explicitCards = reveal.Element("ValidCards");
            bool defaultPool = explicitCards != null && !explicitCards.HasElements && (string?)explicitCards.Attribute("ExternalIn") != "True"
                && !connections.Any(c => (int?)c.Attribute("DstId") == (int?)explicitCards.Attribute("Id"));
            var player = GraphMeaningReader.Type(r.Producer(reveal.Element("PlayerId")));
            string? label = player == "GetOwnerPlayerIdNode" ? "reveal-owner-count" : player == "GetOwnerOpponentPlayerIdNode" ? "reveal-opponent-count" : null;
            if (label != null && defaultPool) return new(label, DeckSwapMeaning.BeforeReveal(ability, reveal) ? "RevealAfterSwap" : "RevealByPlayer");
        }
        if (role is "min-targets" or "max-targets" or "targets")
        {
            var anchors = AnchoredDestructionMeaning.Inspect(ability).Select(p => p.Anchor).ToHashSet();
            if (targets.Any(t => anchors.Contains((int?)t.Attribute("Id") ?? -1))) return new(Locked: true, LockReason: "SelectionColorAnchor");
        }
        bool Connected(XElement? field) => field != null && connections.Any(c => (int?)c.Attribute("DstId") == (int?)field.Attribute("Id"));
        bool Fixed(XElement? field, string value) => field != null && !Connected(field)
            && (string?)field.Attribute("ExternalIn") != "True" && (string?)field.Attribute("V") == value;
        if (role is "min-choices" or "max-choices" or "choices"
            && BanishStrengthTransferMeaning.Inspect(ability) is { } transfer
            && targets.Any(n => transfer.Choices.Contains((int?)n.Attribute("Id") ?? -1)))
            return new(Locked: true, LockReason: "SingleStrengthSource");
        if (role == "charges-limit" && targets.Length == 1 && (string?)targets[0].Attribute("Type") == "FilterTapCardChargesNode")
        {
            var r = new GraphMeaningReader(ability); var filter = targets[0];
            var uses = connections.Where(c => (int?)c.Attribute("SrcId") == (int?)filter.Element("Out")?.Attribute("Id")).ToArray();
            bool OrderGuard(XElement link)
            {
                var list = nodes.SingleOrDefault(n => (string?)n.Attribute("Type") == "FilterCardListNode"
                    && n.Element("Filters")?.Elements().Any(f => (int?)f.Attribute("Id") == (int?)link.Attribute("DstId")) == true);
                if (list == null || r.Owner(list.Element("Cards")) != "Self" || GraphMeaningReader.Type(r.Producer(list.Element("FlowIn"))) != "AfterTurnTrigger") return false;
                return nodes.Any(n => r.Linked(list.Element("FlowSuccess"), n.Element("FlowIn")));
            }
            if (Fixed(filter.Element("Charges"), "1") && Fixed(filter.Element("Op"), "5") && Fixed(filter.Element("Negate"), "False")
                && uses.Length > 0 && uses.All(OrderGuard)) return new(Locked: true, LockReason: "UnusedOrderCondition");
        }
        if (role == "power-limit" && targets.Length == 1 && (string?)targets[0].Attribute("Type") == "FilterCardPowerNode")
        {
            var filter = targets[0];
            if (Fixed(filter.Element("PowerType"), "3")) return new("armor-condition", "ArmorCondition");
            if (Fixed(filter.Element("PowerType"), "1")) return new("base-power-condition", "BasePowerCondition");
            if (Fixed(filter.Element("PowerType"), "0") && Fixed(filter.Element("Op"), "3")
                && Fixed(filter.Element("Negate"), "False") && Fixed(filter.Element("Power"), "0"))
            {
                var uses = connections.Where(c => (int?)c.Attribute("SrcId") == (int?)filter.Element("Out")?.Attribute("Id")).ToArray();
                bool Guard(XElement link)
                {
                    var list = nodes.SingleOrDefault(n => (string?)n.Attribute("Type") == "FilterCardListNode"
                        && n.Element("Filters")?.DescendantsAndSelf().Any(e => (int?)e.Attribute("Id") == (int?)link.Attribute("DstId")) == true);
                    if (list == null) return false;
                    int? Input(XElement? field)
                    {
                        var incoming = connections.Where(c => field != null && (int?)c.Attribute("DstId") == (int?)field.Attribute("Id")).ToArray();
                        return incoming.Length == 1 ? (int?)incoming[0].Attribute("SrcId") : null;
                    }
                    var source = Input(list.Element("Cards"));
                    if (Input(list.Element("FlowIn")) == null || (int?)list.Element("FlowSuccess")?.Attribute("Id") == null
                        || (int?)list.Element("FilteredCards")?.Attribute("Id") == null) return false;
                    var damage = nodes.SingleOrDefault(n => (string?)n.Attribute("Type") == "ChangePowerNode"
                        && (int?)n.Element("FlowOut")?.Attribute("Id") == Input(list.Element("FlowIn")));
                    if (source == null || damage == null || Input(damage.Element("Targets")) != source
                        || !Fixed(damage.Element("Operation"), "1") || !Fixed(damage.Element("TargetedPower"), "0")) return false;
                    var after = nodes.Where(n => (string?)n.Attribute("Type") == "ChangePowerNode"
                        && Input(n.Element("FlowIn")) == (int?)list.Element("FlowSuccess")?.Attribute("Id")
                        && Input(n.Element("Targets")) == (int?)list.Element("FilteredCards")?.Attribute("Id")
                        && Fixed(n.Element("Operation"), "2") && Fixed(n.Element("TargetedPower"), "0")).ToArray();
                    return after.Length == 1;
                }
                if (uses.Length > 0 && uses.All(Guard)) return new(Locked: true, LockReason: "SurvivalCheck");
            }
        }
        if (role is "min-targets" or "max-targets" or "targets" or "min-choices" or "max-choices" or "choices"
            or "min-template-choices" or "max-template-choices" or "template-choices")
        {
            foreach (var node in targets.Where(n => (string?)n.Attribute("Type") is "RequestCardTargetsNode" or "RequestCardChoiceNode" or "RequestTemplateChoiceNode"))
            {
                var outputIds = node.Elements().Where(e => e.Name.LocalName is "SelectedTargets" or "TargetsInShape" or "SelectedChoices")
                    .Select(e => (int?)e.Attribute("Id")).OfType<int>().ToHashSet();
                var uses = connections.Where(c => outputIds.Contains((int?)c.Attribute("SrcId") ?? -1)).ToArray();
                if (uses.Length > 0 && uses.All(c => nodes.Any(n => (string?)n.Attribute("Type") is "CardListGetElementNode" or "CardDefinitionListGetElementNode"
                    && (int?)n.Element("DS")?.Attribute("Id") == (int?)c.Attribute("DstId")
                    && (string?)n.Element("I")?.Attribute("V") == "0" && !Connected(n.Element("I"))
                    && (string?)n.Element("I")?.Attribute("ExternalIn") != "True")))
                    return new(Locked: true, LockReason: "SingleSelectionOnly");
            }
        }
        if (role is "min-row-targets" or "max-row-targets" or "row-targets")
        {
            foreach (var node in targets.Where(n => (string?)n.Attribute("Type") == "RequestLocationTargetsNode"))
            {
                var uses = connections.Where(c => (int?)c.Attribute("SrcId") == (int?)node.Element("Locations")?.Attribute("Id")).ToArray();
                if (uses.Length == 0) continue;
                // Selecting extra rows is unsafe when every consumer takes only the first row.
                if (uses.Any(c => nodes.Any(n => (string?)n.Attribute("Type") == "LocationListGetLocationIdNode"
                    && (int?)n.Element("DS")?.Attribute("Id") == (int?)c.Attribute("DstId")))
                    || uses.All(c => nodes.Any(n => (string?)n.Attribute("Type") == "LocationListGetElementNode"
                    && (int?)n.Element("DS")?.Attribute("Id") == (int?)c.Attribute("DstId")
                    && (string?)n.Element("I")?.Attribute("V") == "0" && !Connected(n.Element("I")))))
                    return new(Locked: true);
            }
            return new(HelpKey: "SelectedRows");
        }
        if (role == "power-level" && targets.Any(n => (string?)n.Attribute("Type") == "FilterPowerLevelNode"
            && (Fixed(n.Element("PowerFilter"), "1") || Fixed(n.Element("PowerFilter"), "3"))))
            return new(Locked: true, LockReason: "PowerExtremumSelection");
        if (role == "power-level" && targets.Length == 1 && (string?)targets[0].Attribute("Type") == "FilterPowerLevelNode")
        {
            var filter = targets[0].Element("PowerFilter");
            if (Connected(filter)) return new();
            // EPowerFilter: Highest=0, HighestLevel=1, Lowest=2, LowestLevel=3.
            return (string?)filter?.Attribute("V") switch
            {
                "0" => new("highest-power-count", "HighestCount"),
                "1" => new("highest-power-levels", "HighestLevels"),
                "2" => new("lowest-power-count", "LowestCount"),
                "3" => new("lowest-power-levels", "LowestLevels"),
                _ => new()
            };
        }
        return new();
    }
}
