using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private bool? formationChargeSubVerified;

    private bool FormationInitialChargePath(XElement graph)
    {
        var r = new GraphMeaningReader(graph);
        var calls = r.Nodes.Where(n => Type(n) == "UseSubAbilityNode").ToArray();
        if (calls.Length != 1 || (int?)calls[0].Element("SubAbilityID") != 22) return false;
        if (!formationChargeSubVerified.HasValue)
        {
            EnsureAbilities();
            var subs = originalAbilityDocument!.Document.Root!.Elements().Where(e => Type(e) == "SubAbility" && (int?)e.Attribute("AbilityID") == 22).ToArray();
            formationChargeSubVerified = false;
            if (subs.Length == 1)
            {
                var copy = new XElement(subs[0]);
                copy.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName is "X" or "Y").Remove();
                string canonical = XElement.Parse(copy.ToString(SaveOptions.DisableFormatting)).ToString(SaveOptions.DisableFormatting);
                formationChargeSubVerified = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
                    == "C34378AC071536BEA89F9E3ADECFDEB6AE2B83FCA6EEB7B34071CAC7D67ECB88";
            }
        }
        if (formationChargeSubVerified != true || calls[0].Element("VarMappings")?.HasElements == true) return false;
        var maps = calls[0].Element("ConnectionMappings")?.Elements().ToArray() ?? [];
        if (maps.Length != 2) return false;
        var inputs = maps.Where(m => (int?)m.Element("IdOfDataInAbility") == 1 && (string?)m.Element("IsInput") == "True").ToArray();
        var outputs = maps.Where(m => (int?)m.Element("IdOfDataInAbility") == 83 && (string?)m.Element("IsInput") == "False").ToArray();
        if (inputs.Length != 1 || outputs.Length != 1 || Type(inputs[0].Element("Data")) != "FlowConnector"
            || Type(outputs[0].Element("Data")) != "FlowConnector") return false;
        var trigger = r.Producer(inputs[0].Element("Data"));
        return Type(trigger) == "AfterMovedTrigger" && r.Nodes.Count(n => Type(n) == "AfterMovedTrigger") == 1
            && r.Fixed(trigger!.Element("OwnerLocation"), "3") && r.Fixed(trigger.Element("IsAmbushing"), "False")
            && r.Fixed(trigger.Element("TriggerCard"), "1") && r.Fixed(trigger.Element("TriggerPlayer"), "1")
            && r.Fixed(trigger.Element("ToLocation"), "3") && r.Fixed(trigger.Element("Reason"), "1")
            && r.Edges.Count(e => (int?)e.Attribute("SrcId") == (int?)trigger.Element("FlowOut")?.Attribute("Id")) == 1
            && !r.Edges.Any(e => (int?)e.Attribute("SrcId") == (int?)outputs[0].Element("Data")?.Attribute("Id"));
    }
}
