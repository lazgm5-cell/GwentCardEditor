using System.Xml.Linq;

namespace GwentCardEditor;

public sealed record CardReference(int CardId, bool ReferencedBy);

public sealed partial class EditorProject
{
    private Dictionary<int, HashSet<int>>? outgoingCardReferences;
    private Dictionary<int, HashSet<int>> incomingCardReferences = [];

    public IReadOnlyList<CardReference> RelatedCards(int id)
    {
        _ = Template(id);
        if (outgoingCardReferences == null)
        {
            EnsureAbilities();
            outgoingCardReferences = [];
            var owners = originalIndex.Values.SelectMany(template => (template.Element("AbilityIds")?.Elements() ?? [])
                .Where(e => int.TryParse(e.Value, out _)).Select(e => (Graph: (int)e, Card: (int)template.Attribute("Id")!)))
                .GroupBy(e => e.Graph).ToDictionary(g => g.Key, g => g.Select(e => e.Card).Distinct().ToArray());
            // Supported edits never change graph/template reference identities, so cache IDs, not display names.
            foreach (var graph in originalAbilityDocument?.Document.Root?.Elements("Ability")
                .Where(g => (string?)g.Attribute("Type") == "CardAbility") ?? [])
            {
                if ((int?)graph.Attribute("Id") is not int graphId || !owners.TryGetValue(graphId, out var cards)) continue;
                var targets = graph.Descendants().Attributes("TemplateId").Select(a => int.TryParse(a.Value, out int target) ? target : 0)
                    .Where(target => originalIndex.ContainsKey(target)).Distinct();
                foreach (int target in targets)
                foreach (int owner in cards)
                {
                    if (target == owner) continue;
                    if (!outgoingCardReferences.TryGetValue(owner, out var outgoing)) outgoingCardReferences[owner] = outgoing = [];
                    outgoing.Add(target);
                    if (!incomingCardReferences.TryGetValue(target, out var incoming)) incomingCardReferences[target] = incoming = [];
                    incoming.Add(owner);
                }
            }
        }
        if (Card(id).IsTutorial || !Factions.Contains(Card(id).Faction) && !Factions.Contains(Card(id).SecondaryFaction)) return [];
        return (outgoingCardReferences.GetValueOrDefault(id) ?? []).Select(target => new CardReference(target, false))
            .Concat((incomingCardReferences.GetValueOrDefault(id) ?? []).Select(owner => new CardReference(owner, true)))
            .Where(reference => !Card(reference.CardId).IsTutorial
                && (Factions.Contains(Card(reference.CardId).Faction) || Factions.Contains(Card(reference.CardId).SecondaryFaction)))
            .OrderBy(r => r.ReferencedBy).ThenBy(r => r.CardId).ToArray();
    }
}
