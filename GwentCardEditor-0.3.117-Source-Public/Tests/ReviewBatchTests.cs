using GwentCardEditor;
using System.Xml.Linq;

internal static class ReviewBatchTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        var hidden=new[]{(202108,83),(202108,84),(162204,21),(202358,21),(202358,22),(202358,209),(202358,210),
            (202659,558),(202659,648),(203092,180),(202841,853)};
        foreach(var (id,field) in hidden)
        {
            Check(!source.Numbers(id).Any(n=>n.Id==field && n.Editable),"Hidden service field "+id+"/"+field);
            bool denied=false;
            try{source.Preview(id,source.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{field,2}}});}
            catch(InvalidOperationException){denied=true;}
            Check(denied && !source.HasChanges,"Raw service edit rejected atomically "+id+"/"+field);
        }
        var labeled=new[]{(202295,249,"tribute-targets"),(202295,259,"without-tribute-targets"),
            (202906,11,"first-banish-choices"),(202906,166,"extra-bronze-banish-choices"),
            (203209,57,"fee-single-damage"),(203209,58,"berserk-all-damage")};
        foreach(var (id,field,label) in labeled)
            Check(source.NumberGroups(id).Any(g=>g.Display.LabelRole==label && g.Members.Any(n=>n.Id==field)),"Correct grouped branch "+label);
        Check(source.Numbers(202841).Any(n=>n.Id==241 && n.Editable && n.Value==5),"Rayla real boost retained");
        Check(source.Numbers(162204).Any(n=>n.Id==30 && n.Editable),"Peter actual maximum retained");
        Check(source.Numbers(202659).Any(n=>n.Id==439 && n.Editable),"Coup damage retained");
        int[] ids=[202108,162204,202358,202659,203092,202841,202295,202906,203209];
        string folder=Path.Combine(scratch,"review-batch");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1) || (string?)e.Attribute("Type")=="SubAbility")));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Graph(XDocument doc,int id)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        XElement Field(XDocument doc,int id,int field)=>Graph(doc,id).Descendants().Single(e=>(int?)e.Attribute("Id")==field);
        var p=Load(baseline);var changed=new List<(int Card,int Field)>();
        foreach(int id in new[]{202295,202906,203209})
        {
            var edits=p.NumberGroups(id).Where(g=>g.Display.LabelRole!=null && labeled.Any(l=>l.Item3==g.Display.LabelRole))
                .SelectMany(g=>g.Members).ToDictionary(n=>n.Id,n=>2);
            p.Apply(id,p.CurrentEdit(id) with {Numbers=edits});
            changed.AddRange(edits.Keys.Select(f=>(id,f)));
            foreach(var (card,field,label) in labeled.Where(x=>x.Item1==id))
                Check(p.Numbers(card).Single(n=>n.Id==field) is {Editable:true,Value:2} n && n.LabelRole==label,"Label survives edit "+label);
        }
        string output=Path.Combine(scratch,"review-batch-export");p.Export(output);
        var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));var reopened=new EditorProject(output);
        foreach(var (id,field) in changed)
        {
            Check((int?)Field(exported,id,field).Attribute("V")==2 && reopened.Numbers(id).Any(n=>n.Id==field && n.Editable && n.Value==2),"Branch export and reopen "+id+"/"+field);
            Field(exported,id,field).SetAttributeValue("V",(string?)Field(baseline,id,field).Attribute("V"));
        }
        Check(XNode.DeepEquals(exported,baseline),"Only requested numeric fields changed");
        p.Undo();p.Undo();p.Undo();Check(!p.HasChanges,"All branch edits undo exactly");
        foreach(int id in new[]{202295,202906,203209})
        {
            var modified=new XDocument(baseline);Graph(modified,id).SetAttributeValue("NId",9999);var unknown=Load(modified);
            foreach(var (_,field,_) in labeled.Where(x=>x.Item1==id))
            {
                Check(!unknown.Numbers(id).Any(n=>n.Id==field && n.Editable),"Unknown graph blocked "+id+"/"+field);
                bool denied=false;try{unknown.Preview(id,unknown.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{field,2}}});}catch(InvalidOperationException){denied=true;}
                Check(denied && !unknown.HasChanges,"Unknown branch API blocked "+id+"/"+field);
            }
        }
        foreach(var (id,field) in new[]{(202841,853),(203092,180)})
        {
            var modified=new XDocument(baseline);Field(modified,id,field).SetAttributeValue("V",2);
            Check(Load(modified).Numbers(id).Any(n=>n.Id==field && n.Editable && n.Value==2),"Positive custom boost is not zero bookkeeping "+id);
        }
        return passed;
    }
}
