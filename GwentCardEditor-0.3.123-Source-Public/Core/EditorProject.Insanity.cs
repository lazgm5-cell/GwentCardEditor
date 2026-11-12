using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private const string InsanityMarker="CardEditor.Insanity.v1";
    private static bool OwnedInsanity(XElement? graph)=>graph?.Nodes().OfType<XComment>().Any(c=>c.Value==InsanityMarker)==true;
    private string InsanityPrefix(string language)
    {
        string? term=GameTerm("insanity",language)??GameTerm("insanity","en-us");
        return term==null?"":"<keyword=insanity>"+term+"</keyword>.\\n";
    }
    public AdditionalAbilitySetting AdditionalInsanity(int id)
    {
        var graph=CurrentAbility(id);var template=Template(id);bool owned=OwnedInsanity(graph);
        bool value=bool.TryParse((string?)template.Element("Madness"),out bool flag) && flag;
        if(value && !owned)return new(true,false,false);
        var card=Card(id);
        bool eligible=card.Editable && card.Type==4 && (card.Faction==64 || card.SecondaryFaction==64) && (!card.IsToken || AdditionalAbilityTokenAllowed(id)) && !card.IsTutorial
            && !SharedAbility(id) && graph!=null && GraphValidator.Validate(graph).ErrorCount==0
            && template.Elements("Madness").Count()==1 && !template.Element("Madness")!.HasAttributes && !template.Element("Madness")!.HasElements
            && template.Element("Madness")!.Value is "True" or "False"
            && !OwnedSymbiosis(graph) && !OwnedFlanking(graph) && !TriggerAbilitySpecs.Any(s=>OwnedTrigger(graph,s))
            && VerifiedEffects(id).Count==0 && PaymentNumericContract.FeeReferences(graph).Count==1
            && Numbers(id).Count(n=>n.Editable && n.Role=="fee-cost" && n.Value is >=1 and <=9)==1
            && !graph.Descendants().Any(n=>((string?)n.Attribute("Type"))?.Contains("Transform",StringComparison.Ordinal)==true
                || ((string?)n.Attribute("Type"))?.Contains("Inject",StringComparison.Ordinal)==true
                || (string?)n.Attribute("Type") is "ChangeCardAbilityNode" or "GetCurrentMoneyNode" or "CanOwnerAffordBranchNode")
            && !graph.Descendants("SubAbilityID").Any(e=>e.Value=="18")
            && AvailableLanguages.Any(l=>HasText(id,l,"tooltip"))
            && AvailableLanguages.Where(l=>HasText(id,l,"tooltip")).All(l=>InsanityPrefix(l).Length>0 && (owned
                ?Text(id,l,"tooltip").StartsWith(InsanityPrefix(l),StringComparison.Ordinal)
                :!Text(id,l,"tooltip").Contains("<keyword=insanity>",StringComparison.Ordinal)));
        if(eligible)
        {
            var triggers=graph!.Element("Nodes")!.Elements().Where(n=>(string?)n.Attribute("Type")=="PaidForTrigger").ToArray();
            eligible=triggers.Length==1 && !new GraphMeaningReader(graph).Edges.Any(e=>new[]{"Madness","Spent"}.Any(name=>(int?)triggers[0].Element(name)?.Attribute("Id")== (int?)e.Attribute("SrcId")))
                && (!owned || value && graph.Nodes().OfType<XComment>().Count(c=>c.Value==InsanityMarker)==1);
        }
        return new(value,eligible,owned);
    }
    private CardEdit NormalizeInsanity(int id,CardEdit edit)
    {
        var setting=AdditionalInsanity(id);bool desired=edit.Insanity??setting.Value;
        if(desired==setting.Value)return edit;
        if(!setting.Editable || desired && edit.Faction!=64 && Card(id).SecondaryFaction!=64)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var texts=edit.Texts.ToDictionary(p=>p.Key,p=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>(p.Value));
        foreach(string language in AvailableLanguages.Where(l=>HasText(id,l,"tooltip")))
        {
            var fields=texts.TryGetValue(language,out var existing)?new Dictionary<string,string>(existing):new();
            string before=fields.GetValueOrDefault("tooltip")??Text(id,language,"tooltip"),prefix=InsanityPrefix(language);
            if(setting.Added){if(!before.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("AdditionalAbilityUnsupported");before=before[prefix.Length..];}
            fields["tooltip"]=desired?prefix+before:before;texts[language]=fields;
        }
        return edit with {Texts=texts};
    }
    private string InsanityBaseline(int id,string language,string baseline,CardEdit edit)
    {
        bool original=OwnedInsanity(OriginalAbility(id)),desired=edit.Insanity??OwnedInsanity(CurrentAbility(id));
        if(original==desired)return baseline;
        string prefix=InsanityPrefix(language);
        if(original){if(!baseline.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("AdditionalAbilityUnsupported");baseline=baseline[prefix.Length..];}
        return desired?prefix+baseline:baseline;
    }
    private XElement? PrepareInsanity(int id,XElement? prepared,CardEdit edit)
    {
        var setting=AdditionalInsanity(id);bool desired=edit.Insanity??setting.Value;
        if(desired==setting.Value)return prepared;
        if(!setting.Editable)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var graph=new XElement(prepared??CurrentAbility(id)!);
        if(desired)graph.AddFirst(new XComment(InsanityMarker));
        else graph.Nodes().OfType<XComment>().Single(c=>c.Value==InsanityMarker).Remove();
        return graph;
    }
}
