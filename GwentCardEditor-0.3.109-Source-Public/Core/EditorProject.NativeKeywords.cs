using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private sealed record NativeKeywordValue(TriggerAbilitySpec Spec,int Id,int Value,string? Alias=null);
    private readonly Dictionary<int,bool> nativeKeywordDependencies=new();
    private bool NativeKeywordDependency(TriggerAbilitySpec spec)
    {
        if(!nativeKeywordDependencies.TryGetValue(spec.Sub,out bool valid))
            nativeKeywordDependencies[spec.Sub]=valid=VerifiedEffectSub(spec.Sub,spec.SubHash);
        return valid;
    }
    private static IEnumerable<(TriggerAbilitySpec Spec,XElement Call,XElement Data)> NativeKeywordInputs(XElement? graph)
    {
        foreach(var spec in TriggerAbilitySpecs.Where(s=>ConfigurableTrigger(s) && s!=ProfitSpec))
            foreach(var call in TriggerCalls(graph,spec).Where(c=>!TriggerComment(c,spec)))
                foreach(var mapping in call.Element("VarMappings")?.Elements()??[])
                    if(mapping.Element("Data") is { } data && (string?)data.Attribute("Type")=="IntVar"
                        && ((string?)data.Attribute("Name")=="Boost" || (int?)mapping.Element("IdOfDataInAbility")==NativeKeywordSlot(spec)))yield return(spec,call,data);
    }
    private static int NativeKeywordSlot(TriggerAbilitySpec spec)=>spec.Keyword switch {"assimilate"=>65,"harmony"=>110,_=>80};
    private static XElement[] NativeGlobals(XElement graph)=>graph.Elements().Where(e=>e.Name.LocalName is "TemporaryVariables" or "PersistentVariables").Elements().ToArray();
    private static bool NativeConstant(XElement value)=>(string?)value.Attribute("Type")=="IntVar"
        && new[]{"ExternalIn","ExternalOut","ResetInInactive"}.All(a=>(string?)value.Attribute(a)=="False");
    private static bool NativeReference(XElement graph,int id,XAttribute? allowed=null)=>graph.DescendantsAndSelf().Attributes()
        .Any(a=>a!=allowed && a.Name.LocalName.EndsWith("VarId",StringComparison.OrdinalIgnoreCase)
            && int.TryParse(a.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int reference) && reference==id)
        || graph.Descendants("CompareVarID").Any(e=>int.TryParse(e.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int reference) && reference==id)
        || graph.Descendants().Any(e=>(string?)e.Attribute("Type")=="IntReferenceVar"
            && int.TryParse((string?)e.Attribute("V"),NumberStyles.Integer,CultureInfo.InvariantCulture,out int reference) && reference==id);
    private static XElement? NativeKeywordSource(XElement graph,XElement data)
    {
        if(graph.Element("Nodes")==null || graph.Element("Connections")==null)return null;
        if(!NativeConstant(data) || (int?)data.Attribute("Id") is not int input || NativeReference(graph,input))return null;
        var wires=graph.Element("Connections")!.Elements().ToArray();
        if(wires.Any(c=>(int?)c.Attribute("SrcId")==input))return null;
        var incoming=wires.Where(c=>(int?)c.Attribute("DstId")==input).ToArray();
        if(incoming.Length==0)return data;
        if(incoming.Length!=1 || (int?)incoming[0].Attribute("SrcId") is not int output)return null;
        var getters=graph.Element("Nodes")!.Elements().Where(n=>(string?)n.Attribute("Type")=="GetVarNode" && (int?)n.Element("Out")?.Attribute("Id")==output).ToArray();
        if(getters is not [var getter] || (int?)getter.Attribute("VarId") is not int variable || !NativeConstant(getter.Element("Out")!)
            || getter.Elements().Any(e=>e.Name.LocalName is not ("Out" or "Comment")) || NativeReference(graph,output)
            || wires.Count(c=>(int?)c.Attribute("SrcId")==output)!=1 || wires.Any(c=>(int?)c.Attribute("DstId")==output)
            || wires.Any(c=>(int?)c.Attribute("SrcId")==variable || (int?)c.Attribute("DstId")==variable)
            || NativeReference(graph,variable,getter.Attribute("VarId")))return null;
        var globals=NativeGlobals(graph);var sources=globals.Where(v=>(int?)v.Attribute("Id")==variable).ToArray();
        if(sources is not [var source] || !NativeConstant(source) || string.IsNullOrWhiteSpace((string?)source.Attribute("Name")))return null;
        string name=(string)source.Attribute("Name")!;
        return globals.Count(v=>string.Equals((string?)v.Attribute("Name"),name,StringComparison.OrdinalIgnoreCase))==1?source:null;
    }
    private static HashSet<int> NativeKeywordCandidateIds(XElement? graph)
    {
        var ids=new HashSet<int>();if(graph==null)return ids;
        var wires=graph.Element("Connections")?.Elements().ToArray()??[];var globals=NativeGlobals(graph);
        foreach(var (_,_,data) in NativeKeywordInputs(graph))
        {
            if((int?)data.Attribute("Id") is not int input)continue;ids.Add(input);
            var outputs=wires.Where(c=>(int?)c.Attribute("DstId")==input).Select(c=>(int?)c.Attribute("SrcId")).ToHashSet();
            foreach(var getter in graph.Element("Nodes")?.Elements().Where(n=>(string?)n.Attribute("Type")=="GetVarNode" && outputs.Contains((int?)n.Element("Out")?.Attribute("Id")))??[])
                foreach(var source in globals.Where(v=>(int?)v.Attribute("Id")== (int?)getter.Attribute("VarId")))
                    if((int?)source.Attribute("Id") is int id)ids.Add(id);
        }
        return ids;
    }
    private static Match NativeKeywordText(string text,string keyword,string? alias=null)
    {
        string tag="<keyword="+keyword+">[^<>]+</keyword>";
        if(Regex.Matches(text,tag).Count!=1)return Match.Empty;
        string number="[ \\u00a0]+(?<amount>[0-9]{1,2})";
        if(alias!=null)
        {
            string variable="(?<alias>\\{"+Regex.Escape(alias)+"\\})";
            number+="|[ \\u00a0]+(?:de[ \\u00a0]+)?"+variable+"|:[ \\u00a0]*"+variable+"|（"+variable+"）|\\("+variable+"\\)";
        }
        return Regex.Match(text,"(?<tag>"+tag+")(?:(?:"+number+"))?(?=[.。】]|[ \\u00a0]*(?:\\\\n|$))");
    }
    private static int NativeTextAmount(Match match,int value=1)=>match.Groups["alias"].Success?value:match.Groups["amount"].Success
        ?int.Parse(match.Groups["amount"].Value,CultureInfo.InvariantCulture):1;
    private static string ReplaceNativeKeyword(string text,string keyword,int expected,int amount,string? alias=null)
    {
        var match=NativeKeywordText(text,keyword,alias);
        if(!match.Success || NativeTextAmount(match,expected)!=expected)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        if(match.Groups["alias"].Success)return text;
        string replacement=match.Groups["tag"].Value+(amount==1?"":" "+amount.ToString(CultureInfo.InvariantCulture));
        return text[..match.Index]+replacement+text[(match.Index+match.Length)..];
    }
    private NativeKeywordValue[] NativeKeywordValues(int id)
    {
        var card=Card(id);var graph=CurrentAbility(id);
        var inputs=NativeKeywordInputs(graph).ToArray();
        if(inputs.Length==0)return [];
        if(!card.Editable || card.Type!=4 || card.IsToken || card.IsTutorial || SharedAbility(id) || graph==null
            || GraphValidator.Validate(graph).ErrorCount>0 || graph.Element("Connections")==null)return [];
        var result=new List<NativeKeywordValue>();
        foreach(var (spec,call,data) in inputs)
        {
            if(TriggerCalls(graph,spec).Length!=1 || !NativeKeywordDependency(spec)
                || NativeKeywordSource(graph,data) is not { } source
                || !int.TryParse((string?)source.Attribute("Id"),out int variable)
                || !int.TryParse((string?)source.Attribute("V"),out int value) || value<1 || value>99)continue;
            var mapping=data.Parent!;
            if((string?)mapping.Element("IsInput")!="True"
                || (int?)mapping.Element("IdOfDataInAbility")!=NativeKeywordSlot(spec)
                || call.Element("VarMappings")!.Elements().Count(m=>(int?)m.Element("IdOfDataInAbility")==NativeKeywordSlot(spec))!=1)continue;
            string? alias=source==data?null:(string?)source.Attribute("Name");
            var languages=AvailableLanguages.Where(l=>HasText(id,l,"tooltip")).ToArray();
            if(languages.Length==0 || !languages.All(l=>
            {
                string text=Text(id,l,"tooltip");var match=NativeKeywordText(text,spec.Keyword,alias);
                if(!match.Success || NativeTextAmount(match,value)!=value)return false;
                if(alias==null)return true;
                var uses=Regex.Matches(text,@"\{[^{}]*(?<![A-Za-z0-9_])"+Regex.Escape(alias)+@"(?![A-Za-z0-9_])[^{}]*\}",RegexOptions.IgnoreCase);
                return uses.Count==(match.Groups["alias"].Success?1:0);
            }))continue;
            result.Add(new(spec,variable,value,alias));
        }
        return result.ToArray();
    }
    private List<AbilityNumber> NativeKeywordNumbers(int id,XElement graph,List<AbilityNumber> numbers)
    {
        var inputs=NativeKeywordCandidateIds(graph);
        if(inputs.Count==0)return numbers;
        var verified=NativeKeywordValues(id).ToDictionary(v=>v.Id);
        var result=numbers.Select(n=>!inputs.Contains(n.Id)?n:verified.TryGetValue(n.Id,out var value)
            ?n with {Minimum=1,Maximum=99,Editable=true,Reason="",Uses=1,LabelRole="keyword-boost:"+value.Spec.Keyword,TextLinked=true}
            :n with {Editable=false,Reason="AdditionalAbilityUnsupported"}).ToList();
        foreach(var value in verified.Values.Where(v=>result.All(n=>n.Id!=v.Id)))
            result.Add(new(value.Id,value.Spec==IntimidateSpec?"intimidate-boost":"boost",value.Alias??"Boost",value.Value,1,99,true,"","Reaction",1,true)
                {LabelRole="keyword-boost:"+value.Spec.Keyword});
        return result;
    }
    private void ValidateNativeKeywordNumbers(int id,IReadOnlyDictionary<int,int> edits)
    {
        var candidates=NativeKeywordCandidateIds(CurrentAbility(id));
        if(!edits.Keys.Any(candidates.Contains))return;
        var verified=NativeKeywordValues(id).ToDictionary(v=>v.Id);
        foreach(var (variable,value) in edits.Where(p=>candidates.Contains(p.Key)))
        {
            if(!verified.ContainsKey(variable))throw new InvalidOperationException("AdditionalAbilityUnsupported");
            if(value<1 || value>99)throw new InvalidOperationException("AbilityRange");
        }
    }
    private Dictionary<int,int> NativeKeywordEdits(int id,IReadOnlyDictionary<int,int> edits)=>NativeKeywordValues(id)
        .Where(v=>edits.ContainsKey(v.Id)).ToDictionary(v=>v.Id,v=>edits[v.Id]);
    private CardEdit NormalizeNativeKeywords(int id,CardEdit edit)
    {
        if(edit.Numbers==null)return edit;
        var changes=NativeKeywordValues(id).Where(v=>edit.Numbers.TryGetValue(v.Id,out int amount) && amount!=v.Value).ToArray();
        if(changes.Length==0)return edit;
        ValidateNativeKeywordNumbers(id,edit.Numbers);
        var texts=edit.Texts.ToDictionary(p=>p.Key,p=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>(p.Value));
        foreach(string language in AvailableLanguages.Where(l=>HasText(id,l,"tooltip")))
        {
            var fields=texts.TryGetValue(language,out var existing)?new Dictionary<string,string>(existing):new();
            string text=fields.GetValueOrDefault("tooltip")??Text(id,language,"tooltip");
            foreach(var value in changes)text=ReplaceNativeKeyword(text,value.Spec.Keyword,value.Value,edit.Numbers[value.Id],value.Alias);
            fields["tooltip"]=text;texts[language]=fields;
        }
        return edit with {Texts=texts};
    }
    private string NativeKeywordBaseline(int id,string language,string baseline,CardEdit edit)
    {
        foreach(var value in NativeKeywordValues(id))
        {
            int original=(int)OriginalAbility(id)!.Descendants().Single(e=>(int?)e.Attribute("Id")==value.Id).Attribute("V")!;
            int desired=edit.Numbers!=null && edit.Numbers.TryGetValue(value.Id,out int requested)?requested:value.Value;
            if(original!=desired)baseline=ReplaceNativeKeyword(baseline,value.Spec.Keyword,original,desired,value.Alias);
        }
        return baseline;
    }
}
