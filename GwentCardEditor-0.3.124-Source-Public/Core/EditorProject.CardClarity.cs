using System.Xml.Linq;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private HashSet<int> CardClarityLocks(int id,XElement graph)
    {
        var result=new HashSet<int>();var r=new GraphMeaningReader(graph);
        foreach(var node in r.Nodes)
        {
            string[] fields=(id,Type(node)) switch
            {
                (202108,"RequestCardChoiceNode")=>["MinChoices","MaxChoices"],
                (162204,"RequestCardTargetsNode")=>["MinTargets"],
                (202358,"RequestCardTargetsNode")=>["MinTargets","MaxTargets"],
                (202659,"SpawnCardNode")=>["Count"],
                _=>[]
            };
            foreach(string name in fields)
                if((int?)node.Element(name)?.Attribute("Id") is int field)result.Add(field);
            if(id is 202841 or 203092 && Type(node)=="ChangePowerNode"
                && r.Fixed(node.Element("Power"),"0") && r.Fixed(node.Element("Operation"),"0")
                && r.Fixed(node.Element("TargetedPower"),"0")
                && r.Producer(node.Element("FlowIn")) is { } previous
                && (id==203092 && Type(previous)=="SortDeckNode"
                    || id==202841 && Type(previous)=="ChangeTokensNode" && r.Fixed(previous.Element("Operation"),"1")
                        && r.Fixed(previous.Element("Tokens"),"64")
                        && r.Source(previous.Element("Targets"))==r.Source(node.Element("Targets"))))
                result.Add((int)node.Element("Power")!.Attribute("Id")!);
        }
        if(id is 202277 or 202981)
            foreach(var node in r.Nodes.Where(n=>Type(n)=="ChangeTapCardChargesNode"))
                if(r.Fixed(node.Element("Operation"),"2") && r.Owner(node.Element("Targets"))=="Self"
                    && r.Owner(node.Element("Attacker"))=="Self" && node.Element("Value") is { } value
                    && !r.Edges.Any(c=>(int?)c.Attribute("DstId")== (int?)value.Attribute("Id")))
                    result.Add((int)value.Attribute("Id")!);
        if(id==203240 && EffectFingerprint(graph,163)=="7559DD1A7321E0F48360BFE6519696EB4B0623C0673C2D09CE2502DAA48E7A67")result.Add(163);
        if(id is 202295 or 202906 or 203209 && !ReviewBranchVerified(id,graph))
            foreach(var field in graph.Descendants())
                if((int?)field.Attribute("Id") is int number && ReviewBranchLabel(id,number)!=null)result.Add(number);
        return result;
    }
    private List<AbilityNumber> CardClarityNumbers(int id,XElement graph,List<AbilityNumber> values)
    {
        var locked=CardClarityLocks(id,graph);
        values=values.Select(n=>locked.Contains(n.Id)?n with {Editable=false,Reason="InitialValueLocked"}:n).ToList();
        values=ReviewBranchNumbers(id,graph,values);
        if(!graph.Descendants("SubAbilityID").Any(e=>e.Value=="37") || !VerifiedEffectSub(37,VeteranSpec.SubHash))return values;
        var r=new GraphMeaningReader(graph);var labels=new Dictionary<int,string>();
        foreach(var call in TriggerCalls(graph,VeteranSpec).Where(c=>!TriggerComment(c,VeteranSpec)))
        {
            var mappings=call.Element("VarMappings")?.Elements().ToArray()??[];
            var amounts=mappings.Where(m=>(int?)m.Element("IdOfDataInAbility")==26 && (string?)m.Element("IsInput")=="True").Select(m=>m.Element("Data")).ToArray();
            if(amounts is not [var amount] || amount==null || (int?)amount.Attribute("Id") is not int number
                || r.Edges.Any(c=>(int?)c.Attribute("DstId")==number))continue;
            var targets=mappings.Where(m=>(int?)m.Element("IdOfDataInAbility")==38).Select(m=>m.Element("Data")).ToArray();
            bool own=targets.Length==0 || targets is [var target] && target!=null && (r.Owner(target)=="Self"
                || (string?)target.Attribute("ExternalIn")!="True" && !target.HasElements
                    && !r.Edges.Any(c=>(int?)c.Attribute("DstId")== (int?)target.Attribute("Id")));
            if(own)labels[number]="veteran-strengthen";
            else if(id==203095 && number==145 && EffectFingerprint(graph,51,145)=="C7B23A5203B2B6E7F5B3716E474CC4717EA6ACF3B1ECDEBB8F3BD366753586FB")labels[number]="bran-start-veteran";
        }
        return values.Select(n=>n.Role=="strengthen" && labels.TryGetValue(n.Id,out var label)?n with
            {LabelRole=label,HelpKey=label=="bran-start-veteran"?"BranStartVeteran":"VeteranStrengthen"}:n).ToList();
    }
}
