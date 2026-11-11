using GwentCardEditor;
using System.Xml.Linq;

internal static class InsanityTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        var ids=source.Cards.Where(c=>source.AdditionalInsanity(c.Id).Editable).Select(c=>c.Id).ToArray();Check(ids.Length>=20,"Verified fixed-Fee recipients "+ids.Length);
        Check(source.AdditionalInsanity(203209) is {Value:true,Editable:false,Added:false},"Native Insanity read-only");
        Check(!source.AdditionalInsanity(112103).Editable,"No Fee blocked");
        string folder=Path.Combine(scratch,"insanity");Directory.CreateDirectory(folder);
        var included=ids.Concat(new[]{203209,112103}).ToHashSet();
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>included.Contains((int?)e.Attribute("Id")??-1) || (string?)e.Attribute("Type") is "SubAbility" or "BattleAbility")));
        var templates=new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>included.Contains((int?)e.Attribute("Id")??-1))));templates.Save(Path.Combine(folder,"Templates.xml"));baseline.Save(Path.Combine(folder,"Abilities.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>included.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal)) || l.StartsWith("keyword_",StringComparison.Ordinal)));
        var p=new EditorProject(folder);
        foreach(int id in ids)
        {
            var before=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Value,n.Role,n.Editable)).OrderBy(n=>n.Id).ToArray();
            Check(p.Preview(id,p.CurrentEdit(id) with {Insanity=true}).Any(c=>c.Field=="Madness") && !p.HasChanges,"Atomic Insanity preview "+id);
            p.Apply(id,p.CurrentEdit(id) with {Insanity=true});
            Check(p.AdditionalInsanity(id) is {Value:true,Editable:true,Added:true},"Added Insanity recognized "+id);
            Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>(n.Id,n.Value,n.Role,n.Editable)).OrderBy(n=>n.Id).SequenceEqual(before),"Fee and original numbers preserved "+id);
            foreach(string lang in p.AvailableLanguages.Where(l=>p.HasText(id,l,"tooltip")))Check(p.Text(id,lang,"tooltip").Contains("<keyword=insanity>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal),"Localized Insanity "+id+"/"+lang);
            var fee=p.Numbers(id).First(n=>n.Editable && n.Role=="fee-cost");int value=fee.Value==9?8:fee.Value+1;
            p.Apply(id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{fee.Id,value}}});Check(p.AdditionalInsanity(id).Editable && p.Numbers(id).Any(n=>n.Id==fee.Id && n.Value==value),"Fee edit while Insanity active "+id);p.Undo();
            p.Apply(id,p.CurrentEdit(id) with {Insanity=false});Check(!p.HasChanges,"Insanity exact roundtrip "+id);
        }
        int target=ids[0];p.Apply(target,p.CurrentEdit(target) with {Insanity=true});string output=Path.Combine(scratch,"insanity-export");p.Export(output);
        var reopened=new EditorProject(output);Check(reopened.AdditionalInsanity(target) is {Added:true,Editable:true},"Export preserves ownership marker");
        var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));exported.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==target).Nodes().OfType<XComment>().Single(c=>c.Value=="CardEditor.Insanity.v1").Remove();
        Check(XNode.DeepEquals(exported,baseline),"No gameplay nodes, wires or IDs changed");
        reopened.Apply(target,reopened.CurrentEdit(target) with {Insanity=false});string removed=Path.Combine(scratch,"insanity-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Templates.xml")),templates) && XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores template and graph exactly");
        p.Undo();Check(!p.HasChanges,"Undo exact");
        bool rejected=false;try{p.Preview(203209,p.CurrentEdit(203209) with {Insanity=false});}catch(InvalidOperationException){rejected=true;}Check(rejected && !p.HasChanges,"Native removal rejected atomically");
        return passed;
    }
}
