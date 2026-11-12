using System.Globalization;
using System.Xml.Linq;
using AbilityAnalyzer.Services;
using static GwentCardEditor.GraphMeaningReader;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private static readonly TriggerAbilitySpec FlankingSpec=new("Flanking","flanking",202244,51,
        "448E8DBA64AA302E147B78ABBBFB207549604F09D7A6CFADE77FA41DC249F4E2", "2E4F9E22F5730E66F04A1CF3F6DC16D2D076E2DF9A489017B3332701CD991EAA",[81,104],[139,140],156);
    private sealed record FlankingBinding(int Saved,int Offset,int Edge,int Source,int Destination);
    private static bool OwnedFlanking(XElement? graph)=>OwnedTrigger(graph,FlankingSpec);
    private static XElement? FlankingEdge(XElement graph)
    {
        var r=new GraphMeaningReader(graph);var triggers=r.Nodes.Where(n=>Type(n)=="AfterTurnTrigger").ToArray();
        if(triggers is not [var trigger] || r.Nodes.Any(n=>Type(n) is "BeforeTurnEndTrigger" or "AfterTurnEndTrigger"))return null;
        if(!r.Fixed(trigger.Element("OwnerLocation"),"3") || !r.Fixed(trigger.Element("IsAmbushing"),"False")
            || !r.Fixed(trigger.Element("ActionOnTrigger"),"0") || !r.Fixed(trigger.Element("Priority"),"0")
            || Type(r.Producer(trigger.Element("PlayerIdFilter")))!="GetOwnerPlayerIdNode")return null;
        var fields=r.Nodes.SelectMany(n=>n.Descendants()).Where(f=>Type(f)=="FlowConnector").ToDictionary(f=>(int)f.Attribute("Id")!);
        var flow=r.Edges.Where(e=>fields.ContainsKey((int?)e.Attribute("SrcId")??-1) && fields.ContainsKey((int?)e.Attribute("DstId")??-1)).ToArray();
        var exits=flow.Where(e=>(int?)e.Attribute("SrcId")== (int?)trigger.Element("FlowOut")?.Attribute("Id")).ToArray();
        if(exits is not [var edge])return null;
        XElement Owner(int field)=>fields[field].Ancestors().First(n=>n.Parent==graph.Element("Nodes"));
        var body=new HashSet<XElement>();var visiting=new HashSet<XElement>();
        bool Visit(XElement node)
        {
            if(visiting.Contains(node))return false;if(body.Contains(node))return true;
            if(Type(node) is not ("ChangePowerNode" or "ChangeArmorNode" or "ChangeTapCardChargesNode" or "BranchNode"
                or "IntBranchNode" or "CardListBranchNode" or "OwnerLocationBranchNode" or "FilterCardListNode"))return false;
            visiting.Add(node);
            foreach(var wire in flow.Where(e=>Owner((int)e.Attribute("SrcId")!)==node))
                if(!Visit(Owner((int)wire.Attribute("DstId")!)))return false;
            visiting.Remove(node);body.Add(node);return true;
        }
        if(!Visit(Owner((int)edge.Attribute("DstId")!)) || !body.Any(n=>Type(n) is "ChangePowerNode" or "ChangeArmorNode" or "ChangeTapCardChargesNode"))return null;
        if(flow.Any(e=>body.Contains(Owner((int)e.Attribute("DstId")!)) && !body.Contains(Owner((int)e.Attribute("SrcId")!)) && e!=edge))return null;
        return edge;
    }
    private (XElement[] Nodes,XElement[] Connections) FlankingParts(FlankingBinding binding)
    {
        var parts=TriggerParts(binding.Saved,binding.Offset,FlankingSpec);
        string metadata=TriggerMarker(FlankingSpec)+string.Join(":",new[]{binding.Saved,binding.Offset,binding.Edge,binding.Source,binding.Destination}.Select(n=>n.ToString(CultureInfo.InvariantCulture)));
        foreach(var node in parts.Nodes)node.Element("Comment")!.Value=metadata;
        var exit=new XElement("Part",new XAttribute("Type","Connection"),new XAttribute("Id",binding.Offset+155),
            new XAttribute("SrcId",binding.Offset+107),new XAttribute("DstId",binding.Destination));
        return(parts.Nodes,parts.Connections.Append(exit).ToArray());
    }
    private XElement? FlankingBaseline(XElement graph,out FlankingBinding? binding)
    {
        binding=null;
        if(graph.Element("Nodes")==null || graph.Element("Connections")==null || GraphValidator.Validate(graph).ErrorCount>0)return null;
        var calls=TriggerCalls(graph,FlankingSpec);if(calls is not [var call])return null;
        string comment=(string?)call.Element("Comment")??"";string marker=TriggerMarker(FlankingSpec);
        if(!comment.StartsWith(marker,StringComparison.Ordinal))return null;
        var tokens=comment[marker.Length..].Split(':');var values=new int[tokens.Length];
        if(tokens.Length!=5)return null;
        for(int i=0;i<tokens.Length;i++)if(!int.TryParse(tokens[i],NumberStyles.None,CultureInfo.InvariantCulture,out values[i]))return null;
        var b=new FlankingBinding(values[0],values[1],values[2],values[3],values[4]);
        if(b.Saved<0 || b.Saved>int.MaxValue-158 || b.Offset!=b.Saved+1 || new[]{b.Edge,b.Source,b.Destination}.Any(i=>i<=0 || i>b.Saved)
            || !TriggerDependency(FlankingSpec))return null;
        var expected=FlankingParts(b);var ids=expected.Nodes.Concat(expected.Connections).SelectMany(p=>p.DescendantsAndSelf().Attributes("Id")).Select(a=>(int)a).ToHashSet();
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>ids.Contains((int?)n.Attribute("Id")??-1)).ToArray();
        var connections=graph.Element("Connections")!.Elements().Where(n=>ids.Contains((int?)n.Attribute("Id")??-1)).ToArray();
        if(EffectFingerprint(ModuleEnvelope(nodes,connections))!=EffectFingerprint(ModuleEnvelope(expected.Nodes,expected.Connections)))return null;
        var originals=graph.Element("Connections")!.Elements().Where(e=>(int?)e.Attribute("Id")==b.Edge).ToArray();
        if(originals is not [var original] || (int?)original.Attribute("SrcId")!=b.Source || (int?)original.Attribute("DstId")!=b.Offset+86)return null;
        if((int?)graph.Attribute("NId")!=b.Offset+FlankingSpec.Extent || graph.Descendants().Attributes("Id").Any(a=>!ids.Contains((int)a) && (int)a>b.Saved))return null;
        if(graph.Element("Connections")!.Elements().Except(connections).Any(e=>e!=original && (ids.Contains((int?)e.Attribute("SrcId")??-1) || ids.Contains((int?)e.Attribute("DstId")??-1))))return null;
        if(graph.DescendantsAndSelf().Attributes().Any(a=>a.Name.LocalName.EndsWith("VarId",StringComparison.OrdinalIgnoreCase)
            && int.TryParse(a.Value,out int reference) && ids.Contains(reference) && !a.Parent!.AncestorsAndSelf().Any(nodes.Contains)))return null;
        if(graph.Descendants().Any(e=>!e.AncestorsAndSelf().Any(nodes.Contains)
            && (e.Name.LocalName=="CompareVarID" && int.TryParse(e.Value,out int compared) && ids.Contains(compared)
                || Type(e).EndsWith("ReferenceVar",StringComparison.Ordinal)
                    && int.TryParse((string?)e.Attribute("V"),out int referenced) && ids.Contains(referenced))))return null;
        var baseline=new XElement(graph);
        baseline.Element("Nodes")!.Elements().Where(n=>ids.Contains((int?)n.Attribute("Id")??-1)).Remove();
        baseline.Element("Connections")!.Elements().Where(n=>ids.Contains((int?)n.Attribute("Id")??-1)).Remove();
        baseline.Element("Connections")!.Elements().Single(e=>(int?)e.Attribute("Id")==b.Edge).SetAttributeValue("DstId",b.Destination);
        baseline.SetAttributeValue("NId",b.Saved);
        var restored=FlankingEdge(baseline);
        if(restored==null || (int?)restored.Attribute("Id")!=b.Edge || (int?)restored.Attribute("SrcId")!=b.Source || (int?)restored.Attribute("DstId")!=b.Destination)return null;
        binding=b;return baseline;
    }
    public AdditionalAbilitySetting AdditionalFlanking(int id)
    {
        var graph=CurrentAbility(id);bool owned=OwnedFlanking(graph);
        if(TriggerCalls(graph,FlankingSpec).Length>0 && !owned)return new(true,false,false);
        var card=Card(id);
        bool eligible=card.Editable && card.Type==4 && (!card.IsToken || AdditionalAbilityTokenAllowed(id)) && !card.IsTutorial && !SharedAbility(id)
            && graph?.Element("Nodes")!=null && graph.Element("Connections")!=null
            && GraphValidator.Validate(graph).ErrorCount==0 && int.TryParse((string?)graph.Attribute("NId"),out int next)
            && next>=graph.Descendants().Attributes("Id").Select(a=>(int)a).DefaultIfEmpty().Max() && next<int.MaxValue-158
            && !OwnedSymbiosis(graph) && !OwnedInsanity(graph) && !TriggerAbilitySpecs.Any(s=>OwnedTrigger(graph,s)) && VerifiedEffects(id).Count==0
            && TriggerDependency(FlankingSpec)
            && !graph.Descendants().Attributes().Any(a=>(a.Name.LocalName is "ExternalIn" or "ExternalOut") && a.Value=="True")
            && !graph.Descendants().Any(n=>Type(n).Contains("Transform",StringComparison.Ordinal) || Type(n).Contains("Inject",StringComparison.Ordinal) || Type(n)=="ChangeCardAbilityNode")
            && AvailableLanguages.Any(l=>HasText(id,l,"tooltip"))
            && AvailableLanguages.Where(l=>HasText(id,l,"tooltip")).All(l=>TriggerText(l,FlankingSpec).Length>0 && (owned
                ?Text(id,l,"tooltip").StartsWith(TriggerPrefix(l,FlankingSpec,1),StringComparison.Ordinal)
                :!Text(id,l,"tooltip").Contains("<keyword=flanking>",StringComparison.Ordinal)));
        if(eligible)eligible=owned?FlankingBaseline(graph!,out _)!=null:FlankingEdge(graph!)!=null;
        return new(owned,eligible,owned);
    }
    private XElement? PrepareFlanking(int id,XElement? prepared,CardEdit edit)
    {
        var setting=AdditionalFlanking(id);bool desired=edit.Flanking??setting.Value;
        if(desired==setting.Value)return prepared;
        if(!setting.Editable)throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var graph=new XElement(prepared??CurrentAbility(id)!);
        if(!desired)return FlankingBaseline(graph,out _)??throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var edge=FlankingEdge(graph)??throw new InvalidOperationException("AdditionalAbilityUnsupported");int saved=(int)graph.Attribute("NId")!;
        var binding=new FlankingBinding(saved,saved+1,(int)edge.Attribute("Id")!,(int)edge.Attribute("SrcId")!,(int)edge.Attribute("DstId")!);
        return WrapFlanking(graph,binding);
    }
    private XElement WrapFlanking(XElement baseline,FlankingBinding binding)
    {
        var graph=new XElement(baseline);
        var edge=graph.Element("Connections")!.Elements().Single(e=>(int?)e.Attribute("Id")==binding.Edge);
        if((int?)edge.Attribute("SrcId")!=binding.Source || (int?)edge.Attribute("DstId")!=binding.Destination || (int?)graph.Attribute("NId")!=binding.Saved)
            throw new InvalidOperationException("AdditionalAbilityUnsupported");
        var parts=FlankingParts(binding);
        edge.SetAttributeValue("DstId",binding.Offset+86);
        void Append(XElement section,IEnumerable<XElement> items){foreach(var item in items){int slot=0;while(section.Element("e"+slot)!=null)slot++;item.Name="e"+slot;section.Add(item);}}
        Append(graph.Element("Nodes")!,parts.Nodes);Append(graph.Element("Connections")!,parts.Connections);graph.SetAttributeValue("NId",binding.Offset+FlankingSpec.Extent);
        if(GraphValidator.Validate(graph).ErrorCount>0)throw new InvalidOperationException("GraphHasErrors");return graph;
    }
}
