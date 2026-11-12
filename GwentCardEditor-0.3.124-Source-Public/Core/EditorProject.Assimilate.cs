using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private sealed record TriggerAbilitySpec(string Name,string Keyword,int Donor,int Sub,string DonorHash,string SubHash,int[] Nodes,int[] Connections,int Extent,int? Battle=null,string? BattleHash=null);
    private static readonly TriggerAbilitySpec AssimilateSpec=new("Assimilate","assimilate",202246,8,
        "2544E4A71F1DA907574538FE14075B9986FAB0C682495DBF6B67326831B1B272","DB2D45DB40FD88E761D45902DFA102F04603AD72A57D01AF222F036F1E602622",[8,115],[118,119],124);
    private static readonly TriggerAbilitySpec HarmonySpec=new("Harmony","harmony",202276,14,
        "BA85BA955187FBC0CAE43496DA687553DB2D48C2FB54B23010FC3750FDE0703C","DCD102B461ADA23EE49235C37A090A1F7B86ABDA42E71357C473F4A17B43CA6C",[698,722,761,786],[725,771,790,791],792);
    private static readonly TriggerAbilitySpec IntimidateSpec=new("Intimidate","intimidate",202382,17,
        "404E02CCA5209BB20CE60D64749B6194E1F77D45C895B8E64D83B0A564B6167A","72679CBDAA3110C89325FB493C4BFEFC5252F3BE8565727A62F4F1F5D876D330",[70,84],[89,90],91);
    private static readonly TriggerAbilitySpec ThriveSpec=new("Thrive","thrive",132305,15,
        "E9A237D52C6EC01485588A33D734303D187874475610EDD325C204429025265C","8864E79B762B04A32BEA8C4D0482F0689B533A7D9EB776EC21914773DD575056",[92,183],[188,193],212);
    private static readonly TriggerAbilitySpec VeteranSpec=new("Veteran","veteran",202618,37,
        "79F96693CE8C29CED994C19CD0B7D8BBEB97C72EA1685DF49133EF6197D66EDE","1FC85534A982FB823FC8C86E9E6D72E7B97B34508368D3AF0C90E0B06CB9C4C3",[514,518,606],[613,615,616,617],618,
        48,"7EA5902D273471AE362BE68EA77722CD22FDFC84C781D05FDA160AEAB9A2E9C8");
    private static readonly TriggerAbilitySpec FormationSpec=new("Formation","formation",122203,22,
        "B861275F13000815313CF7375B60AD7EAC2258988AA73F86F79F6960DA417B8A","C34378AC071536BEA89F9E3ADECFDEB6AE2B83FCA6EEB7B34071CAC7D67ECB88",[286,300],[303],304);
    private static readonly TriggerAbilitySpec ProfitSpec=new("Profit","profit",202310,16,
        "040578110FE224615FDB39C31F45C7C7858E4418328C85D2A0F6D63C1E787FC9","2287BAB2E98FD7BEB54B4B8B68CAA44B7E00307E84009F8B306461178385AEDB",[2,6],[13],15);
    private static readonly TriggerAbilitySpec ResupplySpec=new("Resupply","resupply",200049,23,
        "7ACB8990EBCA78B9F0A736593935EFC18C204C3E192B5F643BC52B3D9DD75C07","AA61A311ACCFC69867D7CF9B20B77A3A5D7BCF911AFFA82AC6175323FCECEE6E",[1370,1384],[1439,1440],1441);
    private static readonly TriggerAbilitySpec EchoSpec=new("Echo","echo",202607,35,
        "4E6CB58F1C24F850B126EFDBB329BB3912E7C1E7CCE1DE7C32EC4867165BAD68","546BD211902CD78684E807B2BE8CD89A87DF29C4CCB2E5FF5B6B48B55D38C0BE",[79,85,91,97,113,122,129],[90,96,111,124,136,137,138,139],140);
    private static readonly TriggerAbilitySpec[] TriggerAbilitySpecs=[AssimilateSpec,HarmonySpec,IntimidateSpec,ThriveSpec,VeteranSpec,FormationSpec,ProfitSpec,ResupplySpec,EchoSpec];
    private readonly Dictionary<int,bool> triggerDependencies=new();
    private static string TriggerMarker(TriggerAbilitySpec spec)=>"CardEditor."+spec.Name+".v1:";
    private static bool ConfigurableTrigger(TriggerAbilitySpec spec)=>spec==AssimilateSpec || spec==HarmonySpec || spec==IntimidateSpec || spec==ThriveSpec || spec==ProfitSpec;
    private static int TriggerMaximum(TriggerAbilitySpec spec)=>spec==ProfitSpec?9:99;
    private static string ValueTriggerMarker(TriggerAbilitySpec spec)=>"CardEditor."+spec.Name+".v2:";
    private static bool TriggerComment(XElement node,TriggerAbilitySpec spec)
    {
        string comment=(string?)node.Element("Comment")??"";
        return comment.StartsWith(TriggerMarker(spec),StringComparison.Ordinal)
            || ConfigurableTrigger(spec) && comment.StartsWith(ValueTriggerMarker(spec),StringComparison.Ordinal);
    }
    private static XElement? TriggerBoost(IEnumerable<XElement> nodes)
    {
        var inputs=nodes.Where(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode")
            .SelectMany(n=>n.Element("VarMappings")?.Elements()??[])
            .Where(m=>(string?)m.Element("IsInput")=="True").Select(m=>m.Element("Data"))
            .Where(d=>(string?)d?.Attribute("Type")=="IntVar" && (string?)d?.Attribute("Name") is "Boost" or "Income").ToArray();
        return inputs is [var input]?input:null;
    }
    private static int TriggerAmount(XElement? graph,TriggerAbilitySpec spec)
    {
        if(!ConfigurableTrigger(spec) || !OwnedTrigger(graph,spec))return 1;
        return int.TryParse((string?)TriggerBoost(TriggerCalls(graph,spec))?.Attribute("V"),out int amount)?amount:1;
    }
    private int DesiredTriggerAmount(int id,CardEdit edit,TriggerAbilitySpec spec)=>edit.AdditionalValues!=null && edit.AdditionalValues.TryGetValue(spec.Keyword,out int value)
        ?value:TriggerAmount(CurrentAbility(id),spec);
    private static bool? RequestedTrigger(CardEdit edit,TriggerAbilitySpec spec)=>spec.Keyword switch
    {
        "assimilate"=>edit.Assimilate,"harmony"=>edit.Harmony,"intimidate"=>edit.Intimidate,"thrive"=>edit.Thrive,
        "veteran"=>edit.Veteran,"formation"=>edit.Formation,"profit"=>edit.Profit,"resupply"=>edit.Resupply,"echo"=>edit.Echo,_=>null
    };
    private void ValidateAdditionalValues(int id,CardEdit edit)
    {
        if(edit.AdditionalValues==null)return;
        foreach(var (keyword,amount) in edit.AdditionalValues)
        {
            var spec=TriggerAbilitySpecs.SingleOrDefault(s=>s.Keyword==keyword && ConfigurableTrigger(s));
            if(spec==null)throw new InvalidOperationException("AdditionalAbilityUnsupported");
            var setting=AdditionalTrigger(id,spec);
            if(!setting.Editable || !(RequestedTrigger(edit,spec)??setting.Value))throw new InvalidOperationException("AdditionalAbilityUnsupported");
            if(amount<1 || amount>TriggerMaximum(spec))throw new InvalidOperationException("AbilityRange");
        }
    }
    private static IEnumerable<Change> TriggerValueChanges(XElement? before,XElement after)
    {
        foreach(var spec in TriggerAbilitySpecs.Where(ConfigurableTrigger))
        {
            int oldValue=TriggerAmount(before,spec),value=TriggerAmount(after,spec);
            if(OwnedTrigger(after,spec) && oldValue!=value)
                yield return new("AdditionalValue/"+spec.Keyword,oldValue.ToString(CultureInfo.InvariantCulture),value.ToString(CultureInfo.InvariantCulture));
        }
    }
    public int AdditionalAbilityValue(int id,string keyword)
    {
        var spec=TriggerAbilitySpecs.SingleOrDefault(s=>s.Keyword==keyword && ConfigurableTrigger(s));
        if(spec==null)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        return TriggerAmount(CurrentAbility(id),spec);
    }
    private static XElement[] TriggerCalls(XElement? graph,TriggerAbilitySpec spec)=>graph?.Element("Nodes")?.Elements()
        .Where(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode" && (string?)n.Element("SubAbilityID")==spec.Sub.ToString(CultureInfo.InvariantCulture)).ToArray()??[];
    private static bool OwnedTrigger(XElement? graph,TriggerAbilitySpec spec)=>TriggerCalls(graph,spec)
        .Any(n=>TriggerComment(n,spec));
    private static bool OwnedAssimilate(XElement? graph)=>OwnedTrigger(graph,AssimilateSpec);
    private static bool OwnedHarmony(XElement? graph)=>OwnedTrigger(graph,HarmonySpec);
    private static bool OwnedIntimidate(XElement? graph)=>OwnedTrigger(graph,IntimidateSpec);
    private static bool OwnedThrive(XElement? graph)=>OwnedTrigger(graph,ThriveSpec);
    private static bool OwnedVeteran(XElement? graph)=>OwnedTrigger(graph,VeteranSpec);
    private static bool OwnedFormation(XElement? graph)=>OwnedTrigger(graph,FormationSpec);
    private bool FormationCompatible(int id,XElement graph)
    {
        var template=Template(id);
        if((string?)template.Element("TapActive")!="True" || (string?)template.Element("SummoningSickness")!="True"
            || Int(template,"InitalTapCharges")<=0)return false;
        var baseline=new XElement(graph);
        if(OwnedFormation(baseline))
        {
            if(!TriggerOwnedParts(baseline,FormationSpec,out _,out var nodes,out var connections))return false;
            foreach(var part in nodes.Concat(connections))part.Remove();
        }
        var types=baseline.Element("Nodes")!.Elements().Select(n=>(string?)n.Attribute("Type")??"").ToArray();
        // Keep play-time ordering and readiness writers outside this first supported scope.
        return types.Count(t=>t=="TappedTrigger")==1
            && !types.Any(t=>t.Contains("Played",StringComparison.Ordinal) || t.Contains("Moved",StringComparison.Ordinal)
                || t is "UseSubAbilityNode" or "ChangeTapCardSummoningSicknessNode")
            && !baseline.Descendants().Attributes().Any(a=>(a.Name.LocalName is "ExternalIn" or "ExternalOut") && a.Value=="True");
    }
    private string TriggerText(string language,TriggerAbilitySpec spec)
    {
        string source=index.ContainsKey(spec.Donor)?Text(spec.Donor,language,"tooltip"):"";
        if(source.Length==0 && language=="zh-tw" && index.ContainsKey(spec.Donor))source=Text(spec.Donor,spec==AssimilateSpec || spec==ProfitSpec?"zh-cn":"en-us","tooltip");
        return Regex.Match(source,"<keyword="+spec.Keyword+">[^<>]+</keyword>").Value;
    }
    private bool TriggerDependency(TriggerAbilitySpec spec)
    {
        if(triggerDependencies.TryGetValue(spec.Sub,out bool result))return result;
        var donor=OriginalAbility(spec.Donor);
        result=donor!=null && EffectFingerprint(donor)==spec.DonorHash && VerifiedEffectSub(spec.Sub,spec.SubHash);
        if(!result && donor!=null && ConfigurableTrigger(spec) && VerifiedEffectSub(spec.Sub,spec.SubHash))
        {
            var copy=new XElement(donor);var input=TriggerBoost(TriggerCalls(copy,spec));
            var boost=input==null?null:NativeKeywordSource(copy,input);
            if(boost!=null && int.TryParse((string?)boost.Attribute("V"),out int amount) && amount>=(spec==ProfitSpec?0:1) && amount<=TriggerMaximum(spec))
            {
                boost.SetAttributeValue("V",spec==ProfitSpec?8:1);
                result=EffectFingerprint(copy)==spec.DonorHash;
            }
        }
        if(result && spec.Battle.HasValue)
        {
            var battles=originalAbilityDocument!.Document.Root!.Elements().Where(e=>(string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==spec.Battle).ToArray();
            result=battles.Length==1 && EffectFingerprint(battles[0])==spec.BattleHash;
        }
        triggerDependencies[spec.Sub]=result;return result;
    }
    private (XElement[] Nodes,XElement[] Connections,XElement[] Variables) TriggerParts(int saved,int offset,TriggerAbilitySpec spec,int amount=1)
    {
        if(!TriggerDependency(spec) || saved<0 || offset<=saved || offset>int.MaxValue-spec.Extent-1)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var donor=OriginalAbility(spec.Donor)!;
        var nodes=donor.Element("Nodes")!.Elements().Where(n=>spec.Nodes.Contains((int?)n.Attribute("Id")??-1)).Select(n=>new XElement(n)).ToArray();
        var connections=donor.Element("Connections")!.Elements().Where(n=>spec.Connections.Contains((int?)n.Attribute("Id")??-1)).Select(n=>new XElement(n)).ToArray();
        var variables=spec==EchoSpec?donor.Element("PersistentVariables")!.Elements().Where(v=>(int?)v.Attribute("Id")==78).Select(v=>new XElement(v)).ToArray():[];
        // Harmony's mapped Boost already defaults to 1. Its getter and global Boost
        // variable are deliberately omitted to avoid collisions with the target card.
        foreach(var part in nodes.Concat(connections).Concat(variables))
            foreach(var attr in part.DescendantsAndSelf().Attributes().Where(a=>a.Name.LocalName is "Id" or "SrcId" or "DstId" || spec==EchoSpec && a.Name.LocalName=="VarId"))
                attr.Value=((int)attr+offset).ToString(CultureInfo.InvariantCulture);
        foreach(var variable in variables)variable.SetAttributeValue("Name","CardEditor.Echo.State");
        if(ConfigurableTrigger(spec))
        {
            if(amount<1 || amount>TriggerMaximum(spec))throw new InvalidOperationException("AbilityRange");
            TriggerBoost(nodes)!.SetAttributeValue("V",amount);
        }
        else if(amount!=1)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        string marker=amount==1?TriggerMarker(spec):ValueTriggerMarker(spec);
        string metadata=saved.ToString(CultureInfo.InvariantCulture)+":"+offset.ToString(CultureInfo.InvariantCulture)
            +(amount==1?"":":"+amount.ToString(CultureInfo.InvariantCulture));
        foreach(var node in nodes)node.AddFirst(new XElement("Comment",marker+metadata));
        return(nodes,connections,variables);
    }
    private static XElement ModuleEnvelope(IEnumerable<XElement> nodes,IEnumerable<XElement> connections,IEnumerable<XElement>? variables=null)
    {
        XElement Section(string name,IEnumerable<XElement> parts)=>new(name,parts.OrderBy(p=>(int?)p.Attribute("Id")).Select(p=>{var copy=new XElement(p);copy.Name="Part";return copy;}));
        return new XElement("Module",Section("Nodes",nodes),Section("Connections",connections),Section("PersistentVariables",variables??[]));
    }
    private static XElement[] TriggerVariables(XElement graph,TriggerAbilitySpec spec)
    {
        if(spec!=EchoSpec)return [];
        var references=graph.Element("Nodes")?.Elements().Where(n=>TriggerComment(n,spec)).Attributes("VarId").Select(a=>(int)a).ToHashSet()??[];
        return graph.Element("PersistentVariables")?.Elements().Where(v=>references.Contains((int?)v.Attribute("Id")??-1)).ToArray()??[];
    }
    private bool TriggerOwnedParts(XElement graph,TriggerAbilitySpec spec,out int saved,out XElement[] nodes,out XElement[] connections)
    {
        saved=0;nodes=[];connections=[];var calls=TriggerCalls(graph,spec);
        if(calls.Length!=1)return false;
        string comment=(string?)calls[0].Element("Comment")??"";
        bool configurable=ConfigurableTrigger(spec) && comment.StartsWith(ValueTriggerMarker(spec),StringComparison.Ordinal);
        if(!configurable && !comment.StartsWith(TriggerMarker(spec),StringComparison.Ordinal))return false;
        var metadata=comment[(configurable?ValueTriggerMarker(spec):TriggerMarker(spec)).Length..].Split(':');
        int amount=1;
        if(metadata.Length!=(configurable?3:2) || configurable && (!int.TryParse(metadata[2],NumberStyles.None,CultureInfo.InvariantCulture,out amount) || amount<2 || amount>TriggerMaximum(spec))
            || !int.TryParse(metadata[0],NumberStyles.None,CultureInfo.InvariantCulture,out saved)
            || !int.TryParse(metadata[1],NumberStyles.None,CultureInfo.InvariantCulture,out int offset)
            || saved<0 || offset<=saved || offset>int.MaxValue-spec.Extent-1 || !TriggerDependency(spec))return false;
        var expected=TriggerParts(saved,offset,spec,amount);
        var ids=expected.Nodes.Concat(expected.Connections).Concat(expected.Variables).SelectMany(n=>n.DescendantsAndSelf().Attributes("Id")).Select(a=>(int)a).ToHashSet();
        nodes=graph.Element("Nodes")?.Elements().Where(n=>ids.Contains((int?)n.Attribute("Id")??-1)).ToArray()??[];
        connections=graph.Element("Connections")?.Elements().Where(n=>ids.Contains((int?)n.Attribute("Id")??-1)).ToArray()??[];
        var variables=TriggerVariables(graph,spec);
        if(EffectFingerprint(ModuleEnvelope(nodes,connections,variables))!=EffectFingerprint(ModuleEnvelope(expected.Nodes,expected.Connections,expected.Variables)))return false;
        var variableIds=variables.Select(v=>(int)v.Attribute("Id")!).ToHashSet();
        var ownedNodes=nodes;
        if(graph.DescendantsAndSelf().Attributes().Any(a=>a.Name.LocalName.EndsWith("VarId",StringComparison.OrdinalIgnoreCase)
            && int.TryParse(a.Value,out int reference) && variableIds.Contains(reference)
            && !a.Parent!.AncestorsAndSelf().Any(ownedNodes.Contains)))return false;
        if(graph.Descendants().Any(e=>!e.AncestorsAndSelf().Any(ownedNodes.Contains)
            && (e.Name.LocalName=="CompareVarID" && int.TryParse(e.Value,out int compared) && variableIds.Contains(compared)
                || ((string?)e.Attribute("Type"))?.EndsWith("ReferenceVar",StringComparison.Ordinal)==true
                    && int.TryParse((string?)e.Attribute("V"),out int referenced) && variableIds.Contains(referenced))))return false;
        int originalNext=saved;
        if((int?)graph.Attribute("NId")!=offset+spec.Extent || graph.Descendants().Attributes("Id").Any(a=>!ids.Contains((int)a) && (int)a>originalNext))return false;
        return !graph.Element("Connections")!.Elements().Except(connections).Any(c=>ids.Contains((int?)c.Attribute("SrcId")??-1)||ids.Contains((int?)c.Attribute("DstId")??-1));
    }
    private AdditionalAbilitySetting AdditionalTrigger(int id,TriggerAbilitySpec spec)
    {
        if(spec==FlankingSpec)return AdditionalFlanking(id);
        var graph=AdditionalAbilityGraph(id);var calls=TriggerCalls(graph,spec);bool owned=OwnedTrigger(graph,spec);
        if(spec==ProfitSpec && !owned && graph?.Descendants().Any(n=>(string?)n.Attribute("Type")=="IncomeTrigger")==true)return new(true,false,false);
        if(calls.Length>0 && !owned)return new(spec!=ProfitSpec || graph!.Descendants().Any(n=>(string?)n.Attribute("Type")=="IncomeTrigger"),false,false);
        var card=Card(id);
        bool eligible=card.Editable && (spec==ProfitSpec?card.Type is 2 or 4 or 8 && (card.Faction==64 || card.SecondaryFaction==64):spec==EchoSpec?card.Type==2:card.Type==4) && (!card.IsToken || AdditionalAbilityTokenAllowed(id)) && !card.IsTutorial && !SharedAbility(id)
            && (spec!=ThriveSpec || id!=203081)
            && graph?.Element("Nodes")!=null && graph.Element("Connections")!=null
            && GraphValidator.Validate(graph).ErrorCount==0 && int.TryParse((string?)graph.Attribute("NId"),out int next)
            && next>=graph.Descendants().Attributes("Id").Select(a=>(int)a).DefaultIfEmpty().Max() && next<int.MaxValue-spec.Extent-2
            && !OwnedSymbiosis(graph) && !TriggerAbilitySpecs.Any(other=>other!=spec && OwnedTrigger(graph,other))
            && !OwnedFlanking(graph)
            && !OwnedInsanity(graph)
            && VerifiedEffects(id).Count==0 && TriggerDependency(spec)
            && (spec!=ProfitSpec || ProfitCompatible(graph!))
            && (spec!=FormationSpec || FormationCompatible(id,graph))
            && (spec!=ResupplySpec || Timing(id).Any(t=>t.Id==-3))
            && (spec!=EchoSpec || EchoCompatible(id,graph!,owned))
            && !graph.Descendants().Any(n=>((string?)n.Attribute("Type"))?.Contains("Transform",StringComparison.Ordinal)==true
                || ((string?)n.Attribute("Type"))?.Contains("Inject",StringComparison.Ordinal)==true || (string?)n.Attribute("Type")=="ChangeCardAbilityNode")
            && AvailableLanguages.Any(l=>HasText(id,l,"tooltip"))
            && AvailableLanguages.Where(l=>HasText(id,l,"tooltip")).All(l=>TriggerText(l,spec).Length>0 && (owned
                ? StripCurrentInitialStatusPrefix(id,l,Text(id,l,"tooltip"),out _).StartsWith(TriggerPrefix(l,spec,TriggerAmount(graph,spec)),StringComparison.Ordinal)
                : !Text(id,l,"tooltip").Contains("<keyword="+spec.Keyword+">",StringComparison.Ordinal)));
        if(owned)eligible &= TriggerOwnedParts(graph!,spec,out _,out _,out _);
        return new(owned,eligible,owned);
    }
    public AdditionalAbilitySetting AdditionalAssimilate(int id)=>AdditionalTrigger(id,AssimilateSpec);
    public AdditionalAbilitySetting AdditionalHarmony(int id)=>AdditionalTrigger(id,HarmonySpec);
    public AdditionalAbilitySetting AdditionalIntimidate(int id)=>AdditionalTrigger(id,IntimidateSpec);
    public AdditionalAbilitySetting AdditionalThrive(int id)=>AdditionalTrigger(id,ThriveSpec);
    public AdditionalAbilitySetting AdditionalVeteran(int id)=>AdditionalTrigger(id,VeteranSpec);
    public AdditionalAbilitySetting AdditionalFormation(int id)=>AdditionalTrigger(id,FormationSpec);
    public AdditionalAbilitySetting AdditionalProfit(int id)=>AdditionalTrigger(id,ProfitSpec);
    public AdditionalAbilitySetting AdditionalResupply(int id)=>AdditionalTrigger(id,ResupplySpec);
    public AdditionalAbilitySetting AdditionalEcho(int id)=>AdditionalTrigger(id,EchoSpec);
    private bool EchoCompatible(int id,XElement graph,bool owned)
    {
        int tokens=Int(Template(id),"Tokens");
        if(tokens<0 || (tokens&512)!=0 || graph.Element("PersistentVariables")==null)return false;
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>!TriggerComment(n,EchoSpec));
        if(nodes.Any(n=>(string?)n.Attribute("Type") is "AfterMovedTrigger" or "AfterRoundTrigger" or "BeforeRoundTrigger"
            or "MoveCardsNode" or "BanishCardsNode" or "ChangeTokensNode" or "ChangeCardTimerNode"))return false;
        if(!owned && NativeGlobals(graph).Any(v=>string.Equals((string?)v.Attribute("Name"),"CardEditor.Echo.State",StringComparison.OrdinalIgnoreCase)))return false;
        return !graph.Descendants().Attributes().Any(a=>(a.Name.LocalName is "ExternalIn" or "ExternalOut") && a.Value=="True");
    }
    private static bool ProfitCompatible(XElement graph)
    {
        if((int?)graph.Attribute("ProfitVarID")!=0)return false;
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>!TriggerComment(n,ProfitSpec)).ToArray();
        return !nodes.Any(n=>(string?)n.Attribute("Type") is "IncomeTrigger" or "ChangeOwnerMoneyNode" or "ChangePlayerMoneyNode" or "ChangeCardTimerNode")
            && !graph.Descendants().Attributes().Any(a=>(a.Name.LocalName is "ExternalIn" or "ExternalOut") && a.Value=="True");
    }
    private CardEdit NormalizeTrigger(int id,CardEdit edit,bool? requested,TriggerAbilitySpec spec)
    {
        var setting=AdditionalTrigger(id,spec);bool desired=requested??setting.Value;
        int oldAmount=TriggerAmount(CurrentAbility(id),spec),amount=DesiredTriggerAmount(id,edit,spec);
        if(desired==setting.Value && amount==oldAmount)return edit;
        if(!AdditionalTrigger(id,spec).Editable)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var texts=edit.Texts.ToDictionary(p=>p.Key,p=>(IReadOnlyDictionary<string,string>)new Dictionary<string,string>(p.Value));
        foreach(string language in AvailableLanguages.Where(l=>HasText(id,l,"tooltip")))
        {
            var fields=texts.TryGetValue(language,out var existing)?new Dictionary<string,string>(existing):new();
            string statusPrefix=CurrentInitialStatusPrefix(id,language);
            string before=fields.GetValueOrDefault("tooltip")??Text(id,language,"tooltip");
            bool hasStatusPrefix=statusPrefix.Length>0 && before.StartsWith(statusPrefix,StringComparison.Ordinal);
            if(hasStatusPrefix)before=before[statusPrefix.Length..];
            string prefix=TriggerPrefix(language,spec,oldAmount);
            if(setting.Value)
            {
                if(!before.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("AdditionalAbilityUnsupported");
                before=before[prefix.Length..];
            }
            fields["tooltip"]=(hasStatusPrefix?statusPrefix:"")+(desired?TriggerPrefix(language,spec,amount)+before:before);texts[language]=fields;
        }
        return edit with {Texts=texts};
    }
    private CardEdit NormalizeAssimilate(int id,CardEdit edit)=>NormalizeTrigger(id,edit,edit.Assimilate,AssimilateSpec);
    private CardEdit NormalizeHarmony(int id,CardEdit edit)=>NormalizeTrigger(id,edit,edit.Harmony,HarmonySpec);
    private CardEdit NormalizeIntimidate(int id,CardEdit edit)=>NormalizeTrigger(id,edit,edit.Intimidate,IntimidateSpec);
    private CardEdit NormalizeThrive(int id,CardEdit edit)=>NormalizeTrigger(id,edit,edit.Thrive,ThriveSpec);
    private CardEdit NormalizeVeteran(int id,CardEdit edit)=>NormalizeTrigger(id,edit,edit.Veteran,VeteranSpec);
    private CardEdit NormalizeFormation(int id,CardEdit edit)
    {
        if((edit.Formation??OwnedFormation(CurrentAbility(id))) && edit.Zeal==true)
            throw new InvalidOperationException("GraphControlsZeal");
        return NormalizeTrigger(id,edit,edit.Formation,FormationSpec);
    }
    private string TriggerPrefix(string language,TriggerAbilitySpec spec,int amount)=>TriggerText(language,spec)
        +(spec==ProfitSpec?" "+amount.ToString(CultureInfo.InvariantCulture)+".":amount==1?"":" "+amount.ToString(CultureInfo.InvariantCulture))+"\\n";
    private string TriggerTextBaseline(int id,string language,string baseline,bool? requested,TriggerAbilitySpec spec,CardEdit edit)
    {
        bool original=OwnedTrigger(OriginalAbility(id),spec);bool desired=requested??OwnedTrigger(CurrentAbility(id),spec);
        int before=TriggerAmount(OriginalAbility(id),spec),after=DesiredTriggerAmount(id,edit,spec);
        if(TriggerCalls(OriginalAbility(id),spec).Length>0 && !original || original==desired && before==after)return baseline;
        string statusPrefix=CurrentInitialStatusPrefix(id,language);
        bool hasStatusPrefix=statusPrefix.Length>0 && baseline.StartsWith(statusPrefix,StringComparison.Ordinal);
        if(hasStatusPrefix)baseline=baseline[statusPrefix.Length..];
        string prefix=TriggerPrefix(language,spec,before);
        if(original)
        {
            if(!baseline.StartsWith(prefix,StringComparison.Ordinal))throw new InvalidOperationException("AdditionalAbilityUnsupported");
            baseline=baseline[prefix.Length..];
        }
        return (hasStatusPrefix?statusPrefix:"")+(desired?TriggerPrefix(language,spec,after)+baseline:baseline);
    }
    private string AssimilateTextBaseline(int id,string language,string baseline,CardEdit edit)=>TriggerTextBaseline(id,language,baseline,edit.Assimilate,AssimilateSpec,edit);
    private string HarmonyTextBaseline(int id,string language,string baseline,CardEdit edit)=>TriggerTextBaseline(id,language,baseline,edit.Harmony,HarmonySpec,edit);
    private string IntimidateTextBaseline(int id,string language,string baseline,CardEdit edit)=>TriggerTextBaseline(id,language,baseline,edit.Intimidate,IntimidateSpec,edit);
    private string ThriveTextBaseline(int id,string language,string baseline,CardEdit edit)=>TriggerTextBaseline(id,language,baseline,edit.Thrive,ThriveSpec,edit);
    private string VeteranTextBaseline(int id,string language,string baseline,CardEdit edit)=>TriggerTextBaseline(id,language,baseline,edit.Veteran,VeteranSpec,edit);
    private string FormationTextBaseline(int id,string language,string baseline,CardEdit edit)=>TriggerTextBaseline(id,language,baseline,edit.Formation,FormationSpec,edit);
    private XElement? PrepareTrigger(int id,XElement? prepared,bool? requested,TriggerAbilitySpec spec,CardEdit edit)
    {
        var setting=AdditionalTrigger(id,spec);bool desired=requested??setting.Value;
        int amount=DesiredTriggerAmount(id,edit,spec);
        if(desired==setting.Value && amount==TriggerAmount(CurrentAbility(id),spec))return prepared;
        if(!AdditionalTrigger(id,spec).Editable)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var graph=EnsureAdditionalAbilitySections(id,new XElement(prepared??AdditionalAbilityGraph(id)!));
        int saved=(int)graph.Attribute("NId")!;
        if(setting.Added)
        {
            if(!TriggerOwnedParts(graph,spec,out saved,out var nodes,out var connections))throw new InvalidOperationException("AdditionalAbilityUnsupported");
            foreach(var variable in TriggerVariables(graph,spec))variable.Remove();
            foreach(var part in nodes.Concat(connections))part.Remove();graph.SetAttributeValue("NId",saved);
        }
        if(desired)
        {
            int offset=saved+1;var parts=TriggerParts(saved,offset,spec,amount);
            void Append(XElement section,IEnumerable<XElement> values){foreach(var value in values){int slot=0;while(section.Element("e"+slot)!=null)slot++;value.Name="e"+slot;section.Add(value);}}
            Append(graph.Element("Nodes")!,parts.Nodes);Append(graph.Element("Connections")!,parts.Connections);
            if(parts.Variables.Length>0)Append(graph.Element("PersistentVariables")!,parts.Variables);
            graph.SetAttributeValue("NId",offset+spec.Extent);
        }
        if(GraphValidator.Validate(graph).ErrorCount>0)throw new InvalidOperationException("GraphHasErrors");
        return graph;
    }
    private XElement? PrepareAssimilate(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Assimilate,AssimilateSpec,edit);
    private XElement? PrepareHarmony(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Harmony,HarmonySpec,edit);
    private XElement? PrepareIntimidate(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Intimidate,IntimidateSpec,edit);
    private XElement? PrepareThrive(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Thrive,ThriveSpec,edit);
    private XElement? PrepareVeteran(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Veteran,VeteranSpec,edit);
    private XElement? PrepareFormation(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Formation,FormationSpec,edit);
    private XElement? PrepareProfit(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Profit,ProfitSpec,edit);
    private XElement? PrepareResupply(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Resupply,ResupplySpec,edit);
    private XElement? PrepareEcho(int id,XElement? prepared,CardEdit edit)=>PrepareTrigger(id,prepared,edit.Echo,EchoSpec,edit);
    private HashSet<int> TriggerModuleIds(int id)
    {
        var graph=CurrentAbility(id);
        return graph?.Element("Nodes")?.Elements().Where(n=>TriggerAbilitySpecs.Any(spec=>TriggerComment(n,spec)) || TriggerComment(n,FlankingSpec))
            .SelectMany(n=>n.DescendantsAndSelf().Attributes("Id")).Select(a=>(int)a).ToHashSet()??[];
    }
}
