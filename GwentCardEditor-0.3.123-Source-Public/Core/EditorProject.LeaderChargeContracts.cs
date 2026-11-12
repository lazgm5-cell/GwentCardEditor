using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    // Reviewed contracts for graphs outside the small initial-charge node whitelist.
    private static readonly Dictionary<int, string> LeaderChargeContracts = new()
    {
        [200166] = "FB3E627F3BEE92D49AD577AAB31275304CAE14650E5392EAFA03459971C7F126",
        [201597] = "CD64BFFFB29C6ED762CD1F4066274593B0E5B0C206D1DA84D85C0AA2C7C78167",
        [202148] = "60BD25A2BE9E7D3704C0E1CDC7A16E3628075C4659ADC9D270CF4921593B2F65",
        [202185] = "D3FA26DCD410CC0E334262AB84306E3E5223E446CE9D8CB6034EA76EF4B6DC4C",
        [202187] = "BF5EE736E394FF4BCCA82A46DDB95C32933416CF6B7CA72D2A78BFF8789EA254",
        [202328] = "2A830CCE782FE7697816474DEB75E139A8A3FEE81C56CB3A023FBD8D84D23246",
        [202572] = "5B2338657F39CF27F13B10BCA59280424F8181A7572028C01704356884D30765",
        [202574] = "6FE2882CF2C30BB65982A56765618A2E845CA09587BDE835F6804019DF157D7A"
    };

    public string LeaderChargeGraphFingerprint(int id)
    {
        if (Card(id).Type != 1 || CurrentAbility(id) is not { } graph) return "";
        EnsureAbilities();
        var dependencies = new SortedDictionary<int, XElement>();
        var visiting = new HashSet<int>();
        bool Visit(XElement current)
        {
            foreach (var reference in current.Descendants("SubAbilityID"))
            {
                if (!int.TryParse(reference.Value, out int dependency) || !visiting.Add(dependency)) return false;
                if (!dependencies.ContainsKey(dependency))
                {
                    var matches = originalAbilityDocument?.Document.Root?.Elements("Ability")
                        .Where(e => (string?)e.Attribute("Type") == "SubAbility" && (int?)e.Attribute("AbilityID") == dependency).ToArray() ?? [];
                    if (matches.Length != 1 || GraphValidator.Validate(matches[0]).ErrorCount > 0) return false;
                    dependencies.Add(dependency, new XElement(matches[0]));
                    if (!Visit(matches[0])) return false;
                }
                visiting.Remove(dependency);
            }
            return true;
        }
        if (!Visit(graph)) return "";
        var copy = new XElement(graph);
        string tooltip = string.Join("\n", AvailableLanguages.Select(l => Text(id, l, "tooltip")));
        // Preserve existing charge-contract fingerprints when row-selection controls are hidden.
        var editable = AbilityNumbers.Inspect(graph, SharedAbility(id), tooltip)
            .Where(n => (n.Editable || n.Reason is "SingleRowSelection" or "SingleSelectionOnly" or "SurvivalCheck"
                or "OrderActivationSignal" or "ImmunityArmorBookkeeping" or "SelectionColorAnchor" or "PowerExtremumSelection" or "ImplicitPowerCondition"
                or "SingleStrengthSource" or "UnusedOrderCondition" or "ImplicitChargeCondition" or "SelfRevealPresentation" or "ChargeActivationSignal" or "ArmorConversionReset") && LeaderNumberRoles.Contains(n.Role)).Select(n => n.Id).ToHashSet();
        foreach (var value in copy.Descendants().Where(e => (string?)e.Attribute("Type") == "IntVar"
            && ((int?)e.Attribute("Id") is int key && editable.Contains(key)
                || e.Parent?.Name.LocalName == "TemporaryVariables"
                && new[] { "charge", "charges" }.Contains(((string?)e.Attribute("Name") ?? "").ToLowerInvariant()))))
            value.SetAttributeValue("V", "editable");
        var bundle = new XElement("Contract", copy, dependencies.Select(pair => pair.Value));
        bundle.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "X" or "Y").Remove();
        string canonical = XElement.Parse(bundle.ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private bool VerifiedLeaderChargeGraph(int id) => LeaderChargeContracts.TryGetValue(id, out string? expected)
        && string.Equals(expected, LeaderChargeGraphFingerprint(id), StringComparison.Ordinal);
}
