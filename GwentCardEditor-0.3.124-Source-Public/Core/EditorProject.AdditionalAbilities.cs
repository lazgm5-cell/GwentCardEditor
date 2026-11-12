using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed record AdditionalAbilitySetting(bool Value, bool Editable, bool Added);

public sealed partial class EditorProject
{
    private const string SymbiosisMarker = "CardEditor.Symbiosis.v1:";
    private static bool AdditionalAbilityTokenAllowed(int id) => id == 202706;
    private static bool SupportsEmptyAbilityGraph(int id) => id is 152405 or 202706;
    private XElement? AdditionalAbilityGraph(int id)
    {
        var graph = CurrentAbility(id);
        if (graph == null || !SupportsEmptyAbilityGraph(id)
            || graph.Element("Nodes") != null || graph.Element("Connections") != null
            || graph.Elements().Any(e => e.Name.LocalName is not ("TemporaryVariables" or "PersistentVariables"))) return graph;
        return EnsureAdditionalAbilitySections(id, new XElement(graph));
    }
    private static XElement EnsureAdditionalAbilitySections(int id, XElement graph)
    {
        if (!SupportsEmptyAbilityGraph(id) || graph.Element("Nodes") != null || graph.Element("Connections") != null
            || graph.Elements().Any(e => e.Name.LocalName is not ("TemporaryVariables" or "PersistentVariables"))) return graph;
        var firstVariables = graph.Elements().FirstOrDefault(e => e.Name.LocalName is "TemporaryVariables" or "PersistentVariables");
        if (firstVariables != null) firstVariables.AddBeforeSelf(new XElement("Nodes"), new XElement("Connections"));
        else graph.Add(new XElement("Nodes"), new XElement("Connections"));
        return graph;
    }
    private bool? symbiosisDependency;
    private static XElement[] SymbiosisNodes(XElement? graph) => graph?.Element("Nodes")?.Elements()
        .Where(n => (string?)n.Attribute("Type") == "UseSubAbilityNode" && (string?)n.Element("SubAbilityID") == "38").ToArray() ?? [];
    private static bool OwnedSymbiosis(XElement? graph) => SymbiosisNodes(graph) is [var node]
        && ((string?)node.Element("Comment"))?.StartsWith(SymbiosisMarker, StringComparison.Ordinal) == true;
    private string SymbiosisText(string language)
    {
        string source = index.ContainsKey(202475) ? Text(202475, language, "tooltip") : "";
        if (source.Length == 0 && language == "zh-tw" && index.ContainsKey(202475)) source = Text(202475,"zh-cn","tooltip");
        return Regex.Match(source, @"<keyword=symbiosis>[^<>]+</keyword>").Value;
    }
    private bool SymbiosisDependency()
    {
        if (symbiosisDependency.HasValue) return symbiosisDependency.Value;
        EnsureAbilities();
        var battle = originalAbilityDocument!.Document.Root!.Elements().Where(e => (string?)e.Attribute("Type") == "BattleAbility"
            && (int?)e.Attribute("AbilityID") == 21).ToArray();
        symbiosisDependency = battle.Length == 1 && EffectFingerprint(battle[0]) == "6F483F2A6A30F626A690C19A1C3FA7211B48A4F88F85171E1C3C756981FA6431";
        return symbiosisDependency.Value;
    }
    public AdditionalAbilitySetting AdditionalSymbiosis(int id)
    {
        var graph = AdditionalAbilityGraph(id);
        var markers = SymbiosisNodes(graph);
        bool owned = OwnedSymbiosis(graph);
        if (markers.Length > 0 && !owned) return new(true, false, false);
        var card = Card(id);
        bool eligible = card.Editable && card.Type == 4 && (!card.IsToken || AdditionalAbilityTokenAllowed(id)) && !card.IsTutorial && !SharedAbility(id)
            && graph?.Element("Nodes") != null && graph.Element("Connections") != null
            && int.TryParse((string?)graph.Attribute("NId"), out int next) && next >= 0 && next < int.MaxValue - 2
            && GraphValidator.Validate(graph).ErrorCount == 0 && SymbiosisDependency()
            && next >= graph.Descendants().Attributes("Id").Select(a=>(int)a).DefaultIfEmpty().Max()
            && VerifiedEffects(id).Count == 0
            && !TriggerAbilitySpecs.Any(spec=>OwnedTrigger(graph,spec))
            && !OwnedFlanking(graph)
            && !OwnedInsanity(graph)
            && !graph.Descendants().Any(n => ((string?)n.Attribute("Type"))?.Contains("Transform", StringComparison.Ordinal) == true
                || ((string?)n.Attribute("Type"))?.Contains("Inject", StringComparison.Ordinal) == true
                || (string?)n.Attribute("Type") == "ChangeCardAbilityNode")
            && AvailableLanguages.Any(l => HasText(id,l,"tooltip"))
            && AvailableLanguages.Where(l=>HasText(id,l,"tooltip")).All(l => SymbiosisText(l).Length > 0
                && (owned ? Text(id,l,"tooltip").StartsWith(SymbiosisText(l)+"\\n",StringComparison.Ordinal)
                    : !Text(id,l,"tooltip").Contains("<keyword=symbiosis>",StringComparison.Ordinal)));
        if (owned)
        {
            var node = markers[0];
            eligible &= node.Elements().All(e => e.Name.LocalName is "Comment" or "SubAbilityID")
                && int.TryParse(((string?)node.Element("Comment"))![SymbiosisMarker.Length..], out int saved)
                && saved >= 0 && saved < (int)node.Attribute("Id")!
                && saved >= graph!.Descendants().Where(e=>e!=node).Attributes("Id").Select(a=>(int)a).DefaultIfEmpty().Max()
                && (int?)node.Attribute("Id") == graph!.Descendants().Attributes("Id").Select(a=>(int)a).Max()
                && (int?)graph.Attribute("NId") == (int?)node.Attribute("Id")+1
                && !graph!.Element("Connections")!.Elements().Any(c => (int?)c.Attribute("SrcId") == (int?)node.Attribute("Id")
                    || (int?)c.Attribute("DstId") == (int?)node.Attribute("Id"));
        }
        return new(owned, eligible, owned);
    }
    private CardEdit NormalizeAdditionalAbilities(int id, CardEdit edit)
    {
        edit=NormalizeNativeKeywords(id,edit);
        ValidateAdditionalValues(id,edit);
        if((edit.Profit??OwnedTrigger(CurrentAbility(id),ProfitSpec)) && edit.Faction!=64 && Card(id).SecondaryFaction!=64)
            throw new InvalidOperationException("AdditionalAbilityUnsupported");
        int additions=(edit.Symbiosis==true && !AdditionalSymbiosis(id).Value?1:0)
            +(edit.Assimilate==true && !AdditionalAssimilate(id).Value?1:0)
            +(edit.Harmony==true && !AdditionalHarmony(id).Value?1:0)
            +(edit.Intimidate==true && !AdditionalIntimidate(id).Value?1:0)
            +(edit.Thrive==true && !AdditionalThrive(id).Value?1:0)
            +(edit.Veteran==true && !AdditionalVeteran(id).Value?1:0)
            +(edit.Formation==true && !AdditionalFormation(id).Value?1:0)
            +(edit.Profit==true && !AdditionalProfit(id).Value?1:0)
            +(edit.Resupply==true && !AdditionalResupply(id).Value?1:0)
            +(edit.Echo==true && !AdditionalEcho(id).Value?1:0)
            +(edit.Flanking==true && !AdditionalFlanking(id).Value?1:0)
            +(edit.Insanity==true && !AdditionalInsanity(id).Value?1:0);
        if(additions>1)
            throw new InvalidOperationException("AdditionalAbilityUnsupported");
        edit=NormalizeAssimilate(id,edit);
        edit=NormalizeHarmony(id,edit);
        edit=NormalizeIntimidate(id,edit);
        edit=NormalizeThrive(id,edit);
        edit=NormalizeVeteran(id,edit);
        edit=NormalizeFormation(id,edit);
        edit=NormalizeTrigger(id,edit,edit.Profit,ProfitSpec);
        edit=NormalizeTrigger(id,edit,edit.Resupply,ResupplySpec);
        edit=NormalizeTrigger(id,edit,edit.Echo,EchoSpec);
        edit=NormalizeTrigger(id,edit,edit.Flanking,FlankingSpec);
        edit=NormalizeInsanity(id,edit);
        if (!edit.Symbiosis.HasValue) return edit;
        var setting = AdditionalSymbiosis(id);
        if (edit.Symbiosis.Value == setting.Value) return edit;
        if (!setting.Editable) throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var texts = edit.Texts.ToDictionary(p => p.Key, p => (IReadOnlyDictionary<string,string>)new Dictionary<string,string>(p.Value));
        foreach (string language in AvailableLanguages.Where(l=>HasText(id,l,"tooltip")))
        {
            var fields = texts.TryGetValue(language,out var existing) ? new Dictionary<string,string>(existing) : new();
            string before = fields.GetValueOrDefault("tooltip") ?? Text(id, language, "tooltip");
            string prefix = SymbiosisText(language)+"\\n";
            if (!edit.Symbiosis.Value && !before.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidOperationException("AdditionalAbilityUnsupported");
            fields["tooltip"] = edit.Symbiosis.Value ? prefix+before : before[prefix.Length..];
            texts[language] = fields;
        }
        return edit with { Texts = texts };
    }
    private string AdditionalTextBaseline(int id, string language, string baseline, CardEdit edit)
    {
        baseline=NativeKeywordBaseline(id,language,baseline,edit);
        baseline=AssimilateTextBaseline(id,language,baseline,edit);
        baseline=HarmonyTextBaseline(id,language,baseline,edit);
        baseline=IntimidateTextBaseline(id,language,baseline,edit);
        baseline=ThriveTextBaseline(id,language,baseline,edit);
        baseline=VeteranTextBaseline(id,language,baseline,edit);
        baseline=FormationTextBaseline(id,language,baseline,edit);
        baseline=TriggerTextBaseline(id,language,baseline,edit.Profit,ProfitSpec,edit);
        baseline=TriggerTextBaseline(id,language,baseline,edit.Resupply,ResupplySpec,edit);
        baseline=TriggerTextBaseline(id,language,baseline,edit.Echo,EchoSpec,edit);
        baseline=TriggerTextBaseline(id,language,baseline,edit.Flanking,FlankingSpec,edit);
        baseline=InsanityBaseline(id,language,baseline,edit);
        bool original = OwnedSymbiosis(OriginalAbility(id));
        bool desired = edit.Symbiosis ?? OwnedSymbiosis(CurrentAbility(id));
        if (SymbiosisNodes(OriginalAbility(id)).Length > 0 && !original || original == desired)
            return InitialStatusTextBaseline(id,language,baseline,edit);
        string prefix = SymbiosisText(language)+"\\n";
        if (!desired && !baseline.StartsWith(prefix,StringComparison.Ordinal)) throw new InvalidOperationException("AdditionalAbilityUnsupported");
        baseline = desired ? prefix+baseline : baseline[prefix.Length..];
        return InitialStatusTextBaseline(id,language,baseline,edit);
    }
    private XElement? PrepareAdditionalAbilities(int id, XElement? prepared, CardEdit edit)
    {
        if (!edit.Symbiosis.HasValue || edit.Symbiosis.Value == AdditionalSymbiosis(id).Value) return prepared;
        if (!AdditionalSymbiosis(id).Editable) throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var graph = EnsureAdditionalAbilitySections(id, new XElement(prepared ?? AdditionalAbilityGraph(id)!));
        if (edit.Symbiosis.Value)
        {
            long allocated = Math.Max((int)graph.Attribute("NId")!,graph.Descendants().Attributes("Id").Select(a=>(int)a).DefaultIfEmpty().Max())+1L;
            if (allocated >= int.MaxValue-1) throw new InvalidOperationException("AdditionalAbilityUnsupported");
            int next=(int)allocated;
            int slot=0;while(graph.Element("Nodes")!.Element("e"+slot)!=null)slot++;
            graph.Element("Nodes")!.Add(new XElement("e"+slot,new XAttribute("Type","UseSubAbilityNode"),new XAttribute("Id",next),
                new XAttribute("X",0),new XAttribute("Y",0),new XElement("Comment",SymbiosisMarker+(string?)graph.Attribute("NId")),new XElement("SubAbilityID",38)));
            graph.SetAttributeValue("NId",next+1);
        }
        else
        {
            var node = SymbiosisNodes(graph).Single();
            graph.SetAttributeValue("NId",int.Parse(((string)node.Element("Comment")!)[SymbiosisMarker.Length..],System.Globalization.CultureInfo.InvariantCulture));
            node.Remove();
        }
        if (GraphValidator.Validate(graph).ErrorCount > 0) throw new InvalidOperationException("GraphHasErrors");
        return graph;
    }
}
