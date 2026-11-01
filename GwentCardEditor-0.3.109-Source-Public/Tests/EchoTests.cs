using GwentCardEditor;
using System.Xml.Linq;

internal static class EchoTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        foreach(var (lang,title) in new[]{("ru-ru","Эхо"),("en-us","Echo"),("zh-cn","回响")})Check(source.GameTerm("echo",lang)==title,"Official Echo title "+lang);
        var eligible=source.Cards.Where(c=>source.AdditionalEcho(c.Id).Editable).Select(c=>c.Id).ToArray();
        Check(eligible.Length==135,"Verified special-card Echo recipients: "+eligible.Length);
        foreach(int id in new[]{202607,202594,202659})Check(source.AdditionalEcho(id) is {Value:true,Editable:false,Added:false},"Native Echo read-only "+id);
        Check(!source.AdditionalEcho(112103).Editable && !source.AdditionalEcho(202349).Editable,"Units and artifacts blocked");
        int[] ids=eligible.Concat(new[]{202607,202594,202659,112103,202349,202475}).Distinct().ToArray();
        string folder=Path.Combine(scratch,"echo");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1) || (string?)e.Attribute("Type") is "SubAbility" or "BattleAbility")));
        var templates=new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1))));templates.Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Graph(XDocument doc,int id)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        var p=Load(baseline);
        void Reject(EditorProject project,int id,CardEdit edit,string name){bool denied=false;try{project.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,name);}
        Reject(p,112103,p.CurrentEdit(112103) with {Echo=true},"Unit Echo API blocked");
        Reject(p,202607,p.CurrentEdit(202607) with {Echo=false},"Native Echo removal blocked");
        Reject(p,202310,p.CurrentEdit(202310) with {Echo=true,AdditionalValues=new Dictionary<string,int>{{"echo",2}}},"Echo has no invented numeric setting");
        foreach(int id in eligible)
        {
            var before=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Role,n.Value,n.LabelRole)).OrderBy(n=>n.Id).ToArray();
            Check(p.Preview(id,p.CurrentEdit(id) with {Echo=true}).Any(c=>c.Field=="AdditionalEcho") && !p.HasChanges,"Atomic Echo preview "+id);
            p.Apply(id,p.CurrentEdit(id) with {Echo=true});
            Check(p.AdditionalEcho(id) is {Value:true,Editable:true,Added:true},"Added Echo recognized "+id);
            Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Role,n.Value,n.LabelRole)).OrderBy(n=>n.Id).SequenceEqual(before),"Original numeric controls preserved "+id);
            foreach(string lang in p.AvailableLanguages.Where(l=>p.HasText(id,l,"tooltip")))
                Check(p.Text(id,lang,"tooltip").Contains("<keyword=echo>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                    && !p.Text(id,lang,"tooltip").Contains('\n'),"Echo localized "+id+"/"+lang);
            Check(!p.Apply(id,p.CurrentEdit(id) with {Echo=true}),"Echo repeated addition no-op "+id);
            p.Apply(id,p.CurrentEdit(id) with {Echo=false});Check(!p.HasChanges,"Echo exact add/remove "+id);
        }
        const int target=113301;
        p.Apply(target,p.CurrentEdit(target) with {Echo=true});
        var damage=p.Numbers(target).First(n=>n.Editable && n.Role=="damage");
        p.Apply(target,p.CurrentEdit(target) with {Numbers=new Dictionary<int,int>{{damage.Id,damage.Value+1}}});
        Check(p.Numbers(target).Any(n=>n.Id==damage.Id && n.Editable && n.Value==damage.Value+1) && p.AdditionalEcho(target).Editable,"Original damage editable while Echo active");
        p.Undo();
        string output=Path.Combine(scratch,"echo-export");p.Export(output);var reopened=new EditorProject(output);
        Check(reopened.AdditionalEcho(target) is {Editable:true,Added:true},"Echo and state reopen");
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=Graph(actual,target);var original=Graph(baseline,target);
        var owned=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Echo.v1:")==true).ToArray();
        var state=graph.Element("PersistentVariables")!.Elements().Single(v=>(string?)v.Attribute("Name")=="CardEditor.Echo.State");int stateId=(int)state.Attribute("Id")!;
        Check(owned.Length==7 && (string?)state.Attribute("Type")=="BoolVar" && (string?)state.Attribute("V")=="False","Seven native nodes and isolated persistent false flag");
        Check(owned.Attributes("VarId").Count()==3 && owned.Attributes("VarId").All(v=>(int)v==stateId),"All state references remapped");
        var round=owned.Single(n=>(string?)n.Attribute("Type")=="AfterRoundTrigger");var moved=owned.Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger");
        Check((string?)round.Element("OwnerLocation")?.Attribute("V")=="32" && (string?)moved.Element("OwnerLocation")?.Attribute("V")=="16"
            && (string?)moved.Element("FromLocation")?.Attribute("V")=="32" && (string?)moved.Element("ToLocation")?.Attribute("V")=="16","Native graveyard and redraw lifecycle retained");
        Check(owned.Any(n=>(string?)n.Attribute("Type")=="IntBranchNode" && (string?)n.Element("B")?.Attribute("V")=="2"),"Native round limit retained");
        Check(owned.Where(n=>(string?)n.Attribute("Type")=="SetVarNode").Select(n=>(string?)n.Element("In")?.Attribute("V")).Order().SequenceEqual(new[]{"False","True"}),"Native state transition values retained");
        int constant=(int)owned.Single(n=>(string?)n.Attribute("Type")=="IntBranchNode").Element("B")!.Attribute("Id")!;
        Reject(p,target,p.CurrentEdit(target) with {Numbers=new Dictionary<int,int>{{constant,3}}},"Internal round limit edit blocked");
        var oldIds=original.Descendants().Attributes("Id").Select(a=>(int)a).ToHashSet();
        var wires=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(wires.Length==8,"Eight native internal wires added");
        reopened.Apply(target,reopened.CurrentEdit(target) with {Echo=false});string removed=Path.Combine(scratch,"echo-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores all graphs and persistent variables exactly");
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Templates.xml")),templates),"Echo never changes templates");
        p.Undo();Check(!p.HasChanges,"Echo undo exact");
        foreach(string mutation in new[]{"temporary","foreign-reference","compare-reference","typed-reference","state-value"})
        {
            var corrupt=new XDocument(actual);var cg=Graph(corrupt,target);var cv=cg.Descendants().Single(e=>(int?)e.Attribute("Id")==stateId);
            if(mutation=="temporary"){cv.Remove();cg.Element("TemporaryVariables")!.Add(cv);}
            if(mutation=="foreign-reference")cg.Element("Nodes")!.Elements().First().SetAttributeValue("VarId",stateId);
            if(mutation=="compare-reference")cg.Element("Nodes")!.Elements().First().Add(new XElement("CompareVarID",stateId));
            if(mutation=="typed-reference")cg.Element("Nodes")!.Elements().First().Add(new XElement("Reference",new XAttribute("Type","BoolReferenceVar"),new XAttribute("V",stateId)));
            if(mutation=="state-value")cv.SetAttributeValue("V","True");
            var unknown=Load(corrupt);Check(!unknown.AdditionalEcho(target).Editable,"Corrupt Echo blocked "+mutation);
            Reject(unknown,target,unknown.CurrentEdit(target) with {Echo=false},"Corrupt Echo cannot remove foreign data "+mutation);
        }
        var modified=new XDocument(baseline);Graph(modified,target).Element("PersistentVariables")!.Add(new XElement("e0",new XAttribute("Type","BoolVar"),new XAttribute("Id",99999),new XAttribute("Name","CardEditor.Echo.State"),new XAttribute("V","False")));
        Graph(modified,target).SetAttributeValue("NId",100000);
        Check(!Load(modified).AdditionalEcho(target).Editable,"Pre-existing state name collision blocked");
        modified=new XDocument(baseline);modified.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==35).SetAttributeValue("NId",9999);
        Check(!Load(modified).AdditionalEcho(target).Editable,"Unknown shared Echo implementation blocked");
        return passed;
    }
}
