using System.Xml.Linq;

namespace GwentCardEditor;

public sealed record AbilityNumberGroup(AbilityNumber Display, IReadOnlyList<AbilityNumber> Members);

public sealed partial class EditorProject
{
    public IReadOnlyList<AbilityNumberGroup> NumberGroups(int id)
    {
        var values = Numbers(id).Where(v => v.Editable).ToArray();
        var pairs = new Dictionary<int, AbilityNumberGroup>();
        var graph = CurrentAbility(id);
        var connections = graph?.Element("Connections")?.Elements().ToArray() ?? [];
        var nodes = graph?.Element("Nodes")?.Elements().ToArray() ?? [];
        foreach (var node in nodes)
        {
            var selection = (string?)node.Attribute("Type") switch
            {
                "RequestCardTargetsNode" => ("MinTargets", "MaxTargets", "min-targets", "max-targets", "targets"),
                "RequestCardChoiceNode" => ("MinChoices", "MaxChoices", "min-choices", "max-choices", "choices"),
                "RequestTemplateChoiceNode" => ("MinChoices", "MaxChoices", "min-template-choices", "max-template-choices", "template-choices"),
                "RequestLocationTargetsNode" => ("MinTargets", "MaxTargets", "min-row-targets", "max-row-targets", "row-targets"),
                _ => default
            };
            if (selection.Item1 == null) continue;
            var minField = node.Element(selection.Item1);
            var maxField = node.Element(selection.Item2);
            var min = values.SingleOrDefault(v => v.Id == (int?)minField?.Attribute("Id") && v.Role == selection.Item3);
            var max = values.SingleOrDefault(v => v.Id == (int?)maxField?.Attribute("Id") && v.Role == selection.Item4);
            if (min == null || max == null || min.Value != max.Value || min.Context != max.Context
                || min.DisplayOffset != max.DisplayOffset || min.Uses != 1 || max.Uses != 1
                || minField?.Parent != maxField?.Parent || min.Id == max.Id
                || connections.Any(c => new[] { min.Id, max.Id }.Contains((int?)c.Attribute("SrcId") ?? -1)
                    || new[] { min.Id, max.Id }.Contains((int?)c.Attribute("DstId") ?? -1))) continue;
            int lower = Math.Max(min.Minimum, max.Minimum), upper = Math.Min(min.Maximum, max.Maximum);
            if (lower > upper) continue;
            var pair = new AbilityNumberGroup(min with { Role = selection.Item5, Minimum = lower, Maximum = upper,
                TextLinked = min.TextLinked && max.TextLinked }, [min, max]);
            if (selection.Item5 == "targets" && TargetTextSelections(id).Any(b => b.MinimumId == min.Id && b.MaximumId == max.Id))
                pair = pair with { Display = pair.Display with { Minimum = Math.Max(1, pair.Display.Minimum), TextLinked = true } };
            if (selection.Item5 == "choices" && MoveTextSelections(id).FirstOrDefault(b => b.MinimumId == min.Id && b.MaximumId == max.Id) is { } moveText)
                pair = pair with { Display = pair.Display with { Minimum = Math.Max(1, pair.Display.Minimum), Maximum = Math.Min(99, pair.Display.Maximum), TextLinked = true,
                    LabelRole = moveText.Place == "Discard" ? "discard-selected-count" : pair.Display.LabelRole,
                    HelpKey = moveText.Place == "Discard" ? "DiscardChosenCards" : pair.Display.HelpKey } };
            if (selection.Item5 == "targets" && graph != null && AnchoredDestructionMeaning.Inspect(graph).Any(p => p.Additional == (int?)node.Attribute("Id")))
                pair = pair with { Display = pair.Display with { LabelRole = "additional-destruction-count", HelpKey = "AdditionalDestruction" } };
            if (selection.Item5 == "choices" && graph != null)
            {
                var uses = connections.Where(c => (int?)c.Attribute("SrcId") == (int?)node.Element("SelectedChoices")?.Attribute("Id")).ToArray();
                var move = uses.Length == 1 ? nodes.SingleOrDefault(n => (string?)n.Attribute("Type") == "MoveCardsNode"
                    && (int?)n.Element("Cards")?.Attribute("Id") == (int?)uses[0].Attribute("DstId")) : null;
                if (move != null && CardMoveMeaning.Inspect(graph, move) is { } destination)
                    pair = pair with { Display = pair.Display with { LabelRole = destination.Place switch
                        { "DeckBottom" => "move-to-deck-bottom-count", "DeckRandom" => "shuffle-into-deck-count", _ => "move-to-deck-top-count" },
                        HelpKey = "MoveChosenCards" } };
            }
            pairs[min.Id] = pair;
            pairs[max.Id] = pair;
        }
        foreach (var draw in nodes.Where(n => (string?)n.Attribute("Type") == "DrawCardsNode"))
        {
            var incoming = connections.Where(c => (int?)c.Attribute("DstId") == (int?)draw.Element("ValidCards")?.Attribute("Id")).ToArray();
            if (incoming.Length != 1) continue;
            var choice = nodes.SingleOrDefault(n => (string?)n.Attribute("Type") == "RequestCardChoiceNode"
                && (int?)n.Element("SelectedChoices")?.Attribute("Id") == (int?)incoming[0].Attribute("SrcId"));
            if (choice == null || (int?)choice.Element("MinChoices")?.Attribute("Id") is not int minId
                || !pairs.TryGetValue(minId, out var selection) || selection.Members.Count != 2 || selection.Display.Role != "choices") continue;
            var amount = values.SingleOrDefault(v => v.Role == "draw-count" && v.Id == (int?)draw.Element("Num")?.Attribute("Id"));
            if (amount == null || amount.Value < 1 || amount.Value != selection.Display.Value || amount.Context != selection.Display.Context
                || amount.DisplayOffset != selection.Display.DisplayOffset || amount.Uses != 1
                || selection.Members.Any(n => n.Id == amount.Id)
                || connections.Any(c => (int?)c.Attribute("DstId") == amount.Id || (int?)c.Attribute("SrcId") == amount.Id)) continue;
            if (connections.Count(c => (int?)c.Attribute("SrcId") == (int?)choice.Element("SelectedChoices")?.Attribute("Id")) != 1) continue;
            var flow = connections.Where(c => (int?)c.Attribute("DstId") == (int?)draw.Element("FlowIn")?.Attribute("Id")).ToArray();
            if (flow.Length != 1 || (int?)flow[0].Attribute("SrcId") != (int?)choice.Element("FlowOutSuccess")?.Attribute("Id")) continue;
            int lower = Math.Max(1, Math.Max(amount.Minimum, selection.Display.Minimum)), upper = Math.Min(amount.Maximum, selection.Display.Maximum);
            if (lower > upper) continue;
            var combined = new AbilityNumberGroup(amount with { LabelRole = "draw-selected-count", HelpKey = "DrawSelected",
                Minimum = lower, Maximum = upper, TextLinked = amount.TextLinked && selection.Display.TextLinked },
                new[] { amount }.Concat(selection.Members).ToArray());
            if (SelectedDrawTextFor(id) is { } selectedText && selectedText.AmountId == amount.Id && selectedText.MinimumId == minId)
                combined = combined with { Display = combined.Display with { Minimum = Math.Max(1, combined.Display.Minimum), Maximum = Math.Min(99, combined.Display.Maximum), TextLinked = true } };
            foreach (var member in combined.Members) pairs[member.Id] = combined;
        }
        if (graph != null && BanishStrengthTransferMeaning.Inspect(graph) is { } transfer)
        {
            var branches = transfer.Targets.Select(target => nodes.Single(n => (int?)n.Attribute("Id") == target))
                .Select(n => pairs.GetValueOrDefault((int?)n.Element("MinTargets")?.Attribute("Id") ?? -1)).ToArray();
            if (branches.Length == 2 && branches.All(g => g is { Display.Role: "targets", Members.Count: 2 })
                && branches[0]!.Display.Value == branches[1]!.Display.Value
                && branches[0]!.Display.Context == branches[1]!.Display.Context)
            {
                var members = branches.SelectMany(g => g!.Members).ToArray();
                int lower = members.Max(n => n.Minimum), upper = members.Min(n => n.Maximum);
                if (lower <= upper)
                {
                    var merged = new AbilityNumberGroup(branches[0]!.Display with { LabelRole = "strength-transfer-targets",
                        HelpKey = "AlternativeStrengthTransfer", Minimum = lower, Maximum = upper }, members);
                    foreach (var member in members) pairs[member.Id] = merged;
                }
            }
        }
        if (BanArdDescriptionContract(id))
        {
            var boosts = values.Where(v => v.Id is 110 or 182).ToArray();
            if (boosts.Length == 2 && boosts[0].Value == boosts[1].Value)
            {
                var common = new AbilityNumberGroup(boosts[0] with { LabelRole = "both-row-boost", TextLinked = true,
                    Minimum = boosts.Max(v => v.Minimum), Maximum = boosts.Min(v => v.Maximum) }, boosts);
                foreach (var boost in boosts) pairs[boost.Id] = common;
            }
        }
        var result = new List<AbilityNumberGroup>();
        var seen = new HashSet<int>();
        foreach (var value in values)
        {
            if (!seen.Add(value.Id)) continue;
            var group = pairs.GetValueOrDefault(value.Id) ?? new(value, [value]);
            foreach (var member in group.Members) seen.Add(member.Id);
            if (group.Display.Role == "draw-count" && group.Members.Count == 1 && DrawTextSelectionFor(id) is { } drawText && group.Display.Id == drawText.Id)
                group = group with { Display = group.Display with { Minimum = Math.Max(1, group.Display.Minimum), Maximum = Math.Min(99, group.Display.Maximum), TextLinked = true } };
            result.Add(group);
        }
        return result;
    }
}
