using GwentCardEditor;
using System.Xml.Linq;

internal static class ResupplyTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        foreach(var (lang,title) in new[]{("ru-ru","Восполнение"),("en-us","Resupply"),("zh-cn","增兵")})
            Check(source.GameTerm("resupply",lang)==title,"Official Resupply title "+lang);
        var eligible=source.Cards.Where(c=>source.AdditionalResupply(c.Id).Editable).Select(c=>c.Id).ToArray();
        Check(eligible.Length==15,"Verified cooldown recipients: "+eligible.Length);
        Check(source.AdditionalResupply(200049) is {Value:true,Editable:false,Added:false},"Native Resupply read-only");
        Check(!source.AdditionalResupply(112103).Editable,"No cooldown cannot receive Resupply");
        int[] ids=eligible.Concat(new[]{200049,112103,202475}).Distinct().ToArray();
        string folder=Path.Combine(scratch,"resupply");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1) || (string?)e.Attribute("Type") is "SubAbility" or "BattleAbility")));
        var templateBaseline=new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1))));
        templateBaseline.Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        var p=Load(baseline);
        void Reject(int id,CardEdit edit,string name){bool denied=false;try{p.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied && !p.HasChanges,name);}
        Reject(112103,p.CurrentEdit(112103) with {Resupply=true},"No cooldown API blocked");
        Reject(200049,p.CurrentEdit(200049) with {Resupply=false},"Native keyword removal blocked");
        Reject(112113,p.CurrentEdit(112113) with {Resupply=true,Harmony=true},"Two added modules blocked");
        foreach(int id in eligible)
        {
            var originalNumbers=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Role,n.Value,n.LabelRole)).OrderBy(n=>n.Id).ToArray();
            var cooldown=p.Timing(id).Single(s=>s.Id==-3);
            Check(p.Preview(id,p.CurrentEdit(id) with {Resupply=true}).Any(c=>c.Field=="AdditionalResupply") && !p.HasChanges,"Preview atomic "+id);
            p.Apply(id,p.CurrentEdit(id) with {Resupply=true});
            Check(p.AdditionalResupply(id) is {Value:true,Editable:true,Added:true},"Added keyword recognized "+id);
            Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Role,n.Value,n.LabelRole)).OrderBy(n=>n.Id).SequenceEqual(originalNumbers),"Existing numeric controls preserved "+id);
            Check(p.Timing(id).Single(s=>s.Id==-3).Value==cooldown.Value,"Cooldown stays editable "+id);
            foreach(string lang in p.AvailableLanguages.Where(l=>p.HasText(id,l,"tooltip")))
                Check(p.Text(id,lang,"tooltip").Contains("<keyword=resupply>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                    && !p.Text(id,lang,"tooltip").Contains('\n'),"Localized keyword "+id+"/"+lang);
            Check(!p.Apply(id,p.CurrentEdit(id) with {Resupply=true}),"Repeated addition no-op "+id);
            p.Apply(id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{-3,cooldown.Value+1}}});
            Check(p.Timing(id).Single(s=>s.Id==-3).Value==cooldown.Value+1 && p.AdditionalResupply(id).Editable,"Cooldown edit preserves module "+id);
            p.Undo();
            p.Apply(id,p.CurrentEdit(id) with {Resupply=false});Check(!p.HasChanges,"Add edit undo remove exact "+id);
        }
        const int target=112113;
        p.Apply(target,p.CurrentEdit(target) with {Resupply=true,Numbers=new Dictionary<int,int>{{-3,3}}});
        string output=Path.Combine(scratch,"resupply-export");p.Export(output);var reopened=new EditorProject(output);
        Check(reopened.AdditionalResupply(target) is {Editable:true,Added:true} && reopened.Timing(target).Single(t=>t.Id==-3).Value==3,"Combined cooldown edit and keyword reopen");
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=actual.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==target);
        var owned=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Resupply.")==true).ToArray();
        Check(owned.Length==2,"Exactly two native nodes added");
        var trigger=owned.Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger");
        Check((string?)trigger.Element("OwnerLocation")?.Attribute("V")=="3" && (string?)trigger.Element("ToLocation")?.Attribute("V")=="35"
            && (string?)trigger.Element("TriggerPlayer")?.Attribute("V")=="1" && (string?)trigger.Element("Reason")?.Attribute("V")=="1","Own played-card trigger masks retained");
        var call=owned.Single(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode");
        Check((int?)call.Element("SubAbilityID")==23,"Native Warfare filter and timer reduction reused unchanged");
        var old=baseline.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==target);var oldIds=old.Descendants().Attributes("Id").Select(a=>(int)a).ToHashSet();
        var wires=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(wires.Length==2 && wires.All(c=>owned.SelectMany(n=>n.DescendantsAndSelf().Attributes("Id")).Any(a=>(int)a==(int)c.Attribute("SrcId")!)
            && owned.SelectMany(n=>n.DescendantsAndSelf().Attributes("Id")).Any(a=>(int)a==(int)c.Attribute("DstId")!)),"New wiring is fully independent");
        reopened.Apply(target,reopened.CurrentEdit(target) with {Resupply=false,Numbers=new Dictionary<int,int>{{-3,source.Timing(target).Single(s=>s.Id==-3).Value}}});
        string removed=Path.Combine(scratch,"resupply-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores exact graphs and shared abilities");
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Templates.xml")),templateBaseline),"No templates changed");
        p.Undo();Check(!p.HasChanges,"Combined operation undo exact");
        var corrupt=new XDocument(baseline);corrupt.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==23).SetAttributeValue("NId",9999);
        Check(!Load(corrupt).AdditionalResupply(target).Editable,"Unknown shared implementation blocked");
        var tampered=new XDocument(actual);var tg=tampered.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==target);
        tg.Element("Connections")!.Elements().First(e=>(int?)e.Attribute("Id")== (int?)wires[0].Attribute("Id")).SetAttributeValue("DstId",(int)old.Element("Nodes")!.Elements().SelectMany(n=>n.Elements()).First(e=>(string?)e.Attribute("Type")=="FlowConnector").Attribute("Id")!);
        Check(!Load(tampered).AdditionalResupply(target).Editable,"Tampered module cannot be edited or stripped for cooldown analysis");
        return passed;
    }
}
