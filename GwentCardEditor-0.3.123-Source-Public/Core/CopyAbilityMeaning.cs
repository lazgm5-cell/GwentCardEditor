using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

internal static class CopyAbilityMeaning
{
    private const string ContractHash = "44B4F0F29E2391CE2998511EF5B5F375C83ECA59CA1E8BAD9D09331C5B577526";
    private const string BondedHash = "9334525338FFE523B26EB63A7713AD2A5BC7AC035A68F7A450C319A5038B2C0E";

    private static bool Verified(XElement? subAbility,string hash=ContractHash)
    {
        if (subAbility == null) return false;
        var copy = new XElement(subAbility);
        copy.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "X" or "Y").Remove();
        string canonical = XElement.Parse(copy.ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) == hash;
    }

    public static List<AbilityNumber> Apply(XElement graph, XElement? subAbility, List<AbilityNumber> values,XElement? bondedAbility=null)
    {
        if (!values.Any(n => n.Editable && n.Role == "copy-count") || !Verified(subAbility)) return values;
        var nodes = graph.Element("Nodes")?.Elements().ToArray() ?? [];
        var edges = graph.Element("Connections")?.Elements().ToArray() ?? [];
        var elements = graph.Descendants().Where(e => (int?)e.Attribute("Id") != null)
            .GroupBy(e => (int)e.Attribute("Id")!).ToDictionary(g => g.Key, g => g.First());
        string Type(XElement? e) => (string?)e?.Attribute("Type") ?? "";
        var reader=new GraphMeaningReader(graph);bool bondedVerified=Verified(bondedAbility,BondedHash);
        bool? BondedBranch(XElement copy)
        {
            if(!bondedVerified)return null;
            var inputs=copy.Element("ConnectionMappings")?.Elements().Where(m=>(string?)m.Element("IsInput")=="True")
                .Select(m=>m.Element("Data")).Where(d=>d!=null && reader.Source(d)!=null).ToArray()??[];
            if(inputs is not [var input])return null;
            var choice=reader.Producer(input);
            if(Type(choice)!="RequestCardChoiceNode")return null;
            var output=reader.Source(choice!.Element("FlowIn"));var branch=reader.Producer(choice.Element("FlowIn"));
            if(Type(branch)!="UseSubAbilityNode" || (int?)branch!.Element("SubAbilityID")!=10
                || (string?)output?.Parent?.Element("IsInput")!="False")return null;
            return (int?)output.Parent!.Element("IdOfDataInAbility") switch {251=>true,253=>false,_=>null};
        }
        bool External(XElement e) => (string?)e.Attribute("ExternalIn") == "True";
        XElement? Node(XElement? e) => e?.AncestorsAndSelf().FirstOrDefault(n => n.Parent?.Name.LocalName == "Nodes");
        var literals = new HashSet<int>();
        bool Literal(XElement? field) => field != null && (int?)field.Attribute("Id") is int id && literals.Contains(id);
        XElement? Resolve(XElement? field, HashSet<int>? seen = null)
        {
            if (field == null || External(field)) return null;
            seen ??= [];
            if ((int?)field.Attribute("Id") is not int id || !seen.Add(id)) return null;
            var links = edges.Where(e => (int?)e.Attribute("DstId") == id).ToArray();
            if (links.Length > 1) return null;
            if (links.Length == 0) { literals.Add(id); return field; }
            var output = elements.GetValueOrDefault((int?)links[0].Attribute("SrcId") ?? -1);
            if (output == null || External(output)) return null;
            var node = Node(output);
            if (Type(node) != "GetVarNode") return output;
            int variable = (int?)node!.Attribute("VarId") ?? -1;
            bool written = nodes.Any(n => Type(n) != "GetVarNode" && n.Attributes().Any(a => a.Name.LocalName.EndsWith("VarId", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(a.Value, out int reference) && reference == variable));
            // A single writer is not enough: it may run after this use or on another branch.
            return written ? null : Resolve(elements.GetValueOrDefault(variable), seen);
        }
        string? Scope(XElement? field, HashSet<int>? seen = null)
        {
            var source = Resolve(field); var node = Node(source);
            if (source == null || node == null || Literal(source)) return null;
            seen ??= [];
            if (!seen.Add((int?)node.Attribute("Id") ?? -1)) return null;
            return Type(node) switch
            {
                "GetAbilityOwnerCardNode" or "GetAbilityOwnerNode" or "GetAbilityOwnerCardListNode" => "Self",
                "RequestCardTargetsNode" when source.Name.LocalName == "SelectedTargets" => "ChosenTarget",
                "RequestCardChoiceNode" when source.Name.LocalName == "SelectedChoices" => "ChosenCard",
                "RequestTemplateChoiceNode" when source.Name.LocalName == "SelectedChoices" => "ChosenOption",
                "CardListGetRandomElementNode" or "CardDefinitionListGetRandomElementNode" => "RandomCard",
                "GetCardDefinitionNode" or "CardListElementToListNode" => Scope(node.Element("DS"), seen),
                "CardListGetElementNode" or "CardDefinitionListGetElementNode" when Resolve(node.Element("I")) is { } index
                    && Literal(index) && (string?)index.Attribute("V") == "0" => Scope(node.Element("DS"), seen),
                _ => null
            };
        }
        string? Player(XElement? field) => Type(Node(Resolve(field))) switch
        { "GetOwnerPlayerIdNode" => "Owner", "GetOwnerOpponentPlayerIdNode" => "Opponent", _ => null };
        string? Zone(XElement? field)
        {
            var source = Resolve(field); var node = Node(source);
            if (source == null) return null;
            if (Literal(source)) return (string?)source.Attribute("V") switch
            { "1" => "Melee", "2" => "Ranged", "8" => "Hand", "16" => "Deck", "32" => "Graveyard", "256" => "PlayStack", _ => null };
            return Type(node) switch
            {
                "GetOwnerLocationNode" => "SelfZone",
                "GetCardLocationIdNode" when Resolve(node!.Element("PositionType")) is { } positionType
                    && Literal(positionType) && (string?)positionType.Attribute("V") == "0" => Scope(node.Element("DS")) switch
                { "Self" => "SelfZone", "ChosenTarget" or "ChosenCard" => "ChosenCardZone", _ => null },
                "LocationListGetLocationIdNode" => SelectedRow(node!.Element("DS")),
                _ => null
            };
        }
        string? SelectedRow(XElement? field)
        {
            var source = Resolve(field); var node = Node(source);
            if (Type(node) != "RequestLocationTargetsNode" || source?.Name.LocalName != "Locations") return null;
            var min = Resolve(node!.Element("MinTargets")); var max = Resolve(node.Element("MaxTargets"));
            return Literal(min) && Literal(max) && (string?)min!.Attribute("V") == "1" && (string?)max!.Attribute("V") == "1" ? "ChosenRow" : null;
        }
        XElement? Mapping(XElement node, int port) => node.Element("VarMappings")?.Elements()
            .SingleOrDefault(m => (int?)m.Element("IdOfDataInAbility") == port && (string?)m.Element("IsInput") == "True")?.Element("Data");
        bool Active(XElement node, int port)
        {
            var field = node.Element("ConnectionMappings")?.Elements().SingleOrDefault(m => (int?)m.Element("IdOfDataInAbility") == port
                && (string?)m.Element("IsInput") == "True")?.Element("Data");
            return field != null && edges.Any(e => (int?)e.Attribute("DstId") == (int?)field.Attribute("Id"));
        }
        var copies = nodes.Select(n => (Node: n, Number: SubAbilityNumericContract.Inspect(graph, n)))
            .Where(x => x.Number?.Role == "copy-count").ToArray();
        var descriptions = new Dictionary<int, AbilityMeaningFact[]>();
        foreach (var (node, _) in copies)
        {
            var facts = new List<AbilityMeaningFact>();
            void Add(string kind, string? value) { if (value != null) facts.Add(new(kind, value)); }
            var definition = Resolve(Mapping(node, 40));
            if (definition != null && Literal(definition) && Type(definition) == "CardDefinitionVar"
                && (int?)definition.Element("V")?.Attribute("TemplateId") is int template && template > 0)
                Add("Template", template.ToString(System.Globalization.CultureInfo.InvariantCulture));
            else Add("CopySource", Scope(Mapping(node, 40)));

            bool location = Active(node, 8), position = Active(node, 59);
            if (location && !position)
            {
                Add("CopyPlayer", Player(Mapping(node, 18)));
                string? zone = Zone(Mapping(node, 19)); Add("CopyPlace", zone);
                if (zone == "Deck") Add("CopyDeckPosition", "Bottom");
            }
            else if (position && !location)
            {
                var place = Node(Resolve(Mapping(node, 63)));
                if (Type(place) == "MakeCardPositionNode")
                {
                    Add("CopyPlayer", Player(place!.Element("PlayerId")));
                    string? zone = Zone(place.Element("Location")); Add("CopyPlace", zone);
                    if (zone == "Deck")
                    {
                        var index = Resolve(place.Element("Index"));
                        if (index != null && Literal(index) && (string?)index.Attribute("V") == "0") Add("CopyDeckPosition", "Top");
                        else if (Type(Node(index)) == "GetLastAvailableIndexNode") Add("CopyDeckPosition", "Bottom");
                    }
                }
                else if (Type(place) is "GetCardPositionOnRightNode" or "GetCardPositionOnLeftNode")
                {
                    string? anchor = Scope(place!.Element("DS"));
                    if (anchor is "Self" or "ChosenTarget" or "ChosenCard")
                        Add("CopyRelativePlace", (Type(place) == "GetCardPositionOnRightNode" ? "RightOf" : "LeftOf") + anchor);
                }
                else if (Type(place) == "GetRandomLastAvailablePositionNode")
                {
                    Add("CopyPlayer", Player(place!.Element("PlayerId")));
                    var zone = Resolve(place.Element("Location"));
                    if (Literal(zone) && (string?)zone!.Attribute("V") == "3") Add("CopyPlace", "RandomAvailableRow");
                    else
                    {
                        string? single = Zone(place.Element("Location")); Add("CopyPlace", single);
                        if (single == "Deck") Add("CopyDeckPosition", "Bottom");
                    }
                }
            }
            descriptions[(int)node.Attribute("Id")!] = facts.Distinct().ToArray();
        }
        return values.Select(value =>
        {
            if (!value.Editable || value.Role != "copy-count") return value;
            var sources = nodes.Where(n => Type(n) == "GetVarNode" && (int?)n.Attribute("VarId") == value.Id)
                .Select(n => (int?)n.Element("Out")?.Attribute("Id") ?? -1).Append(value.Id).ToHashSet();
            var matching = copies.Where(c => (int?)c.Number!.Field.Attribute("Id") == value.Id
                || edges.Any(e => sources.Contains((int?)e.Attribute("SrcId") ?? -1)
                    && (int?)e.Attribute("DstId") == (int?)c.Number!.Field.Attribute("Id"))).ToArray();
            var matches=matching.Select(c=>descriptions[(int)c.Node.Attribute("Id")!]).ToArray();
            if (matches.Length == 0) return value;
            // A shared count may control different branches: show only facts common to every use.
            var common = matches.Skip(1).Aggregate(matches[0].AsEnumerable(), (result, next) => result.Intersect(next)).ToList();
            if (matches.Length > 1) common.Add(new("CopyScope", "MultipleSteps"));
            bool template = common.Any(f => f.Kind == "Template"), source = common.Any(f => f.Kind == "CopySource");
            string? player = common.FirstOrDefault(f => f.Kind == "CopyPlayer")?.Value;
            string? label = player is "Owner" or "Opponent" ? (template ? "spawn-count-" : "copy-count-") + player.ToLowerInvariant()
                : template ? "spawn-count" : value.LabelRole;
            string? row = common.FirstOrDefault(f => f.Kind == "CopyPlace")?.Value;
            if (template && player == "Owner" && row is "Melee" or "Ranged") label += "-" + row.ToLowerInvariant();
            var branches=matching.Select(c=>BondedBranch(c.Node)).Distinct().ToArray();
            bool? bonded=branches is [var condition]?condition:null;
            if(bonded.HasValue)label=bonded.Value?"copy-count-bonded":"copy-count-unbonded";
            return value with { MeaningFacts = new AbilityMeaningFactList(value.MeaningFacts.Concat(common).Distinct()),
                LabelRole = label,
                HelpKey = bonded.HasValue?(bonded.Value?"BondedCopies":"UnbondedCopies"):template ? "TemplateCopies" : source ? "BaseCopies" : value.HelpKey };
        }).ToList();
    }
}
