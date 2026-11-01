using GwentCardEditor;
using System.Xml.Linq;

internal static class FlankingTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        foreach(var (lang,title) in new[]{("ru-ru","Обход"),("en-us","Flanking"),("zh-cn","翼守")})Check(source.GameTerm("flanking",lang)==title,"Official Flanking title "+lang);
        var eligible=source.Cards.Where(c=>source.AdditionalFlanking(c.Id).Editable).Select(c=>c.Id).ToArray();
        Check(eligible.Length==20,"Verified end-turn recipients: "+eligible.Length);
        Check(source.AdditionalFlanking(202244) is {Value:true,Editable:false,Added:false},"Native Flanking read-only");
        Check(!source.AdditionalFlanking(112103).Editable && !source.AdditionalFlanking(113301).Editable,"No end-turn ability and special blocked");
        int[] ids=eligible.Concat(new[]{202244,112103,113301,202475}).Distinct().ToArray();
        string folder=Path.Combine(scratch,"flanking");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1) || (string?)e.Attribute("Type") is "SubAbility" or "BattleAbility")));
        var templates=new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1))));templates.Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Graph(XDocument doc,int id)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        var p=Load(baseline);
        void Reject(EditorProject project,int id,CardEdit edit,string name){bool denied=false;try{project.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,name);}
        Reject(p,112103,p.CurrentEdit(112103) with {Flanking=true},"No end-turn API blocked");
        Reject(p,202244,p.CurrentEdit(202244) with {Flanking=false},"Native Flanking removal blocked");
        Reject(p,200299,p.CurrentEdit(200299) with {Flanking=true,Harmony=true},"Simultaneous modules blocked");
        foreach(int id in eligible)
        {
            var before=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Role,n.Value,n.LabelRole)).OrderBy(n=>n.Id).ToArray();
            Check(p.Preview(id,p.CurrentEdit(id) with {Flanking=true}).Any(c=>c.Field=="AdditionalFlanking") && !p.HasChanges,"Flanking preview atomic "+id);
            p.Apply(id,p.CurrentEdit(id) with {Flanking=true});
            Check(p.AdditionalFlanking(id) is {Value:true,Editable:true,Added:true},"Added Flanking recognized "+id);
            Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Role,n.Value,n.LabelRole)).OrderBy(n=>n.Id).SequenceEqual(before),"Original numeric controls preserved "+id);
            if(id==202609)Reject(p,id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{527,2}}},"Technical charge threshold remains locked with Flanking");
            foreach(string lang in p.AvailableLanguages.Where(l=>p.HasText(id,l,"tooltip")))
                Check(p.Text(id,lang,"tooltip").Contains("<keyword=flanking>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                    && !p.Text(id,lang,"tooltip").Contains('\n'),"Flanking localized "+id+"/"+lang);
            Check(!p.Apply(id,p.CurrentEdit(id) with {Flanking=true}),"Flanking repeated addition no-op "+id);
            p.Apply(id,p.CurrentEdit(id) with {Flanking=false});Check(!p.HasChanges,"Flanking exact add/remove "+id);
        }
        const int target=200299;
        p.Apply(target,p.CurrentEdit(target) with {Flanking=true});
        var boost=p.Numbers(target).First(n=>n.Editable && n.Role=="boost");
        p.Apply(target,p.CurrentEdit(target) with {Numbers=new Dictionary<int,int>{{boost.Id,boost.Value+1}}});
        Check(p.AdditionalFlanking(target).Editable && p.Numbers(target).Any(n=>n.Id==boost.Id && n.Value==boost.Value+1),"Original boost editable while Flanking active");p.Undo();
        string output=Path.Combine(scratch,"flanking-export");p.Export(output);var reopened=new EditorProject(output);
        Check(reopened.AdditionalFlanking(target) is {Editable:true,Added:true},"Flanking export reopens");
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=Graph(actual,target);var old=Graph(baseline,target);
        var owned=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Flanking.v1:")==true).ToArray();
        Check(owned.Length==2 && owned.Any(n=>(string?)n.Attribute("Type")=="IntForeachNode"),"Native helper and repeat loop added");
        var metadata=((string)owned[0].Element("Comment")!).Split(':').Skip(1).Select(int.Parse).ToArray();
        int offset=metadata[1],edge=metadata[2],src=metadata[3],dst=metadata[4];
        var binding=graph.Element("Connections")!.Elements().Single(e=>(int?)e.Attribute("Id")==edge);
        Check((int?)binding.Attribute("SrcId")==src && (int?)binding.Attribute("DstId")==offset+86,"Original end-turn edge rerouted only into native helper");
        var oldIds=old.Descendants().Attributes("Id").Select(a=>(int)a).ToHashSet();
        var wires=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(wires.Length==3 && wires.Any(c=>(int?)c.Attribute("SrcId")==offset+107 && (int?)c.Attribute("DstId")==dst),"Repeat loop reconnects to original body");
        Reject(p,target,p.CurrentEdit(target) with {Numbers=new Dictionary<int,int>{{offset+109,3}}},"Internal repetition count edit blocked");
        reopened.Apply(target,reopened.CurrentEdit(target) with {Flanking=false});string removed=Path.Combine(scratch,"flanking-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores original wire, nodes and NId exactly");
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Templates.xml")),templates),"Templates unchanged");
        p.Undo();Check(!p.HasChanges,"Flanking undo exact");
        var remapped=new XDocument(baseline);var remappedGraph=Graph(remapped,target);
        var globals=remappedGraph.Elements().Where(e=>e.Name.LocalName is "TemporaryVariables" or "PersistentVariables").Elements().Attributes("Id").Select(a=>(int)a).ToHashSet();
        foreach(var attr in remappedGraph.Descendants().Attributes().Where(a=>a.Name.LocalName is "Id" or "SrcId" or "DstId"
            || a.Name.LocalName.EndsWith("VarId",StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Value,out int reference) && globals.Contains(reference)))attr.Value=((int)attr+10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        remappedGraph.SetAttributeValue("NId",(int)remappedGraph.Attribute("NId")!+10000);
        var shifted=Load(remapped);Check(shifted.AdditionalFlanking(target).Editable,"Recognition independent of original local IDs");
        shifted.Apply(target,shifted.CurrentEdit(target) with {Flanking=true});shifted.Apply(target,shifted.CurrentEdit(target) with {Flanking=false});
        Check(!shifted.HasChanges,"Remapped original IDs roundtrip exact");
        foreach(string mode in new[]{"rewired-entry","external-data","external-reference","metadata","duplicate-id"})
        {
            var corrupt=new XDocument(actual);var cg=Graph(corrupt,target);
            if(mode=="rewired-entry")cg.Element("Connections")!.Elements().Single(e=>(int?)e.Attribute("Id")==edge).SetAttributeValue("DstId",dst);
            if(mode=="external-data")cg.Element("Connections")!.Add(new XElement("e9999",new XAttribute("Type","Connection"),new XAttribute("Id",offset+154),new XAttribute("SrcId",offset+82),new XAttribute("DstId",boost.Id)));
            if(mode=="external-reference")cg.Element("Nodes")!.Elements().First().Add(new XElement("CompareVarID",offset+82));
            if(mode=="metadata")cg.Element("Nodes")!.Elements().First(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Flanking.")==true).Element("Comment")!.Value+="0";
            if(mode=="duplicate-id")cg.Element("Nodes")!.Elements().First().SetAttributeValue("Id",(int)cg.Element("Nodes")!.Elements().Skip(1).First().Attribute("Id")!);
            var unknown=Load(corrupt);Check(!unknown.AdditionalFlanking(target).Editable,"Corrupt Flanking blocked "+mode);
            Check(!unknown.Numbers(target).Any(n=>n.Editable),"Corrupt module cannot expose numeric settings "+mode);
            Reject(unknown,target,unknown.CurrentEdit(target) with {Flanking=false},"Corrupt Flanking removal blocked "+mode);
        }
        var modified=new XDocument(baseline);var baseGraph=Graph(modified,target);
        var originalEdge=baseGraph.Element("Connections")!.Elements().Single(e=>(int?)e.Attribute("Id")==edge);
        originalEdge.SetAttributeValue("SrcId",dst);
        Check(!Load(modified).AdditionalFlanking(target).Editable,"Unrecognized flow graph blocked");
        modified=new XDocument(baseline);modified.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==51).SetAttributeValue("NId",9999);
        Check(!Load(modified).AdditionalFlanking(target).Editable,"Unknown shared Flanking implementation blocked");
        return passed;
    }
}
