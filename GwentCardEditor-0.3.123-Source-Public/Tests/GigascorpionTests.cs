using GwentCardEditor;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

internal static class GigascorpionTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        int[] ids=[202515,201709,201710];
        Check(source.NumberGroups(202515).Count==0,"Gigascorpion main card has no duplicate numeric controls");
        Check(source.NumberGroups(201709).Single().Display is {Value:1,LabelRole:"giga-condense-damage"},"Focused choice exposes per-strike damage");
        Check(source.NumberGroups(201710).Single().Display is {Value:6,Maximum:99,LabelRole:"giga-spread-total"},"Spread choice exposes total damage");
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1))));
        string folder=Path.Combine(scratch,"gigascorpion");Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Graph(XDocument doc,int id)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        XElement Field(XDocument doc,int id,int field)=>Graph(doc,id).Descendants().Single(e=>(int?)e.Attribute("Id")==field);
        CardEdit Edit(EditorProject p,int id,int value)=>p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{1,value}}};
        void Reject(EditorProject p,int id,int field,int value)
        {
            bool denied=false;try{p.Preview(id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{field,value}}});}catch(InvalidOperationException){denied=true;}
            Check(denied,"Unsafe Gigascorpion edit rejected "+id+"/"+field+"/"+value);
        }
        var p=Load(baseline);Check(!p.Apply(201709,Edit(p,201709,1)) && !p.HasChanges,"No-op does not isolate graph");
        Reject(p,202515,49,2);Reject(p,202515,57,9);Reject(p,201709,2,2);Reject(p,201709,1,-1);Reject(p,201710,1,100);
        var preview=p.Preview(201709,Edit(p,201709,3));
        Check(preview.Any(c=>c.Field=="GigaChoiceIsolation") && !preview.Any(c=>c.Field=="202515/Ability/#22"),"Isolation presented without internal loop fallback");
        Check(!p.HasChanges,"Giga preview is side-effect-free");
        p.Apply(201709,Edit(p,201709,3));Check(p.Numbers(201710).Single().Value==6,"Focused damage does not change spread total");
        p.Apply(201710,Edit(p,201710,9));Check(p.Numbers(201709).Single().Value==3,"Spread total does not change focused damage");
        foreach(int id in new[]{201709,201710})foreach(string lang in source.AvailableLanguages.Where(l=>source.HasText(id,l,"tooltip")))
            Check(p.DescriptionBindings(id,[1]).Single(b=>b.Language==lang).Linked,"Choice description linked "+id+" "+lang);
        string output=Path.Combine(scratch,"giga-export");p.Export(output);var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Check((int?)Field(exported,202515,49).Attribute("V")==3 && (int?)Field(exported,201709,1).Attribute("V")==3,"Focused effect and alias synchronized");
        Check((int?)Field(exported,202515,57).Attribute("V")==9 && (int?)Field(exported,201710,1).Attribute("V")==9,"Spread effect and alias synchronized");
        Check((int?)Field(exported,202515,22).Attribute("V")==6 && (int?)Field(exported,202515,303).Attribute("V")==1,"Six focused strikes and one-damage random hits retained");
        Check(!Graph(exported,202515).Element("Connections")!.Elements().Any(c=>new[]{22,303}.Contains((int?)c.Attribute("DstId")??-1)),"Independent loop count and random quantum are no longer shared");
        Check(GraphValidator.Validate(Graph(exported,202515)).ErrorCount==0,"Isolated graph validates");
        var expected=new XDocument(baseline);var parent=Graph(expected,202515);
        parent.Element("Nodes")!.Elements().Where(n=>new[]{58,393}.Contains((int?)n.Attribute("Id")??-1)).Remove();
        parent.Element("Connections")!.Elements().Where(c=>new[]{60,395}.Contains((int?)c.Attribute("Id")??-1)).Remove();
        Field(expected,202515,22).SetAttributeValue("V",6);
        foreach(var (id,field,value) in new[]{(202515,49,3),(202515,57,9),(201709,1,3),(201710,1,9)})Field(expected,id,field).SetAttributeValue("V",value);
        Check(XNode.DeepEquals(exported,expected),"Only approved isolation and requested values changed");
        foreach(string lang in source.AvailableLanguages)Check(File.ReadAllBytes(Path.Combine(folder,lang+".csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output,lang+".csv"))),"Giga localization bytes retained "+lang);
        var reopened=new EditorProject(output);Check(reopened.Numbers(201710).Single().Value==9,"Giga export reopens");
        reopened.Apply(201710,Edit(reopened,201710,10));Check(reopened.Numbers(201709).Single().Value==3,"Reopened choices remain independent");
        reopened.Apply(201710,Edit(reopened,201710,9));Check(!reopened.HasChanges,"Reopened source restores exactly");
        p.Apply(201709,Edit(p,201709,1));p.Apply(201710,Edit(p,201710,6));Check(!p.HasChanges,"Returning both defaults restores native graph and aliases exactly");
        p.Undo();Check(p.Numbers(201710).Single().Value==9,"Undo restores isolated state");p.Undo();p.Undo();p.Undo();Check(!p.HasChanges,"All Giga edits undo atomically");
        foreach(var (id,field,value) in new[]{(201710,1,7),(202515,303,2),(202515,49,2),(202515,22,4)})
        {
            var changed=new XDocument(baseline);Field(changed,id,field).SetAttributeValue("V",value);var blocked=Load(changed);
            Check(blocked.Numbers(201709).Count==0 && blocked.Numbers(201710).Count==0,"Unverified or mismatched graph blocked "+id+"/"+field);
            Reject(blocked,201710,1,8);
        }
        return passed;
    }
}
