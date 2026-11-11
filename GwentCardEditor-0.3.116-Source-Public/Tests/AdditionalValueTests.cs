using GwentCardEditor;
using System.Xml.Linq;

internal static class AdditionalValueTests
{
    private static CardEdit Module(CardEdit edit,string keyword,bool enabled)=>keyword switch
    {
        "assimilate"=>edit with {Assimilate=enabled},"harmony"=>edit with {Harmony=enabled},
        "intimidate"=>edit with {Intimidate=enabled},"thrive"=>edit with {Thrive=enabled},_=>throw new Exception(keyword)
    };
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        const int id=112103;
        int[] ids=[id,202246,202276,202382,132305];int[] subs=[8,14,17,15];
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));
        var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && subs.Contains((int?)e.Attribute("AbilityID")??-1))));
        foreach(var (keyword,donor) in new[]{("assimilate",202246),("harmony",202276),("intimidate",202382),("thrive",132305)})
        {
            string folder=Path.Combine(scratch,"value-"+keyword);Directory.CreateDirectory(folder);
            new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
            foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),
                File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
            EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
            CardEdit Value(EditorProject p,int amount)=>Module(p.CurrentEdit(id),keyword,true) with {AdditionalValues=new Dictionary<string,int>{{keyword,amount}}};
            void Reject(EditorProject p,CardEdit edit,string name)
            {
                bool denied=false;try{p.Apply(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,keyword+" "+name);
            }
            var p=Load(baseline);
            foreach(int invalid in new[]{0,-1,100,int.MaxValue})Reject(p,Value(p,invalid),"invalid range "+invalid);
            Reject(p,p.CurrentEdit(id) with {AdditionalValues=new Dictionary<string,int>{{keyword,2}}},"value requires enabled module");
            Reject(p,Value(p,2) with {AdditionalValues=new Dictionary<string,int>{{"veteran",2}}},"unsupported value rejected");
            var native=Module(p.CurrentEdit(donor),keyword,true) with {AdditionalValues=new Dictionary<string,int>{{keyword,2}}};
            bool nativeDenied=false;try{p.Apply(donor,native);}catch(InvalidOperationException){nativeDenied=true;}Check(nativeDenied,keyword+" native module cannot be changed through added-module API");
            Check(!p.HasChanges,keyword+" failed edits atomic");
            var preview=p.Preview(id,Value(p,2));
            Check(preview.Any(c=>c.Field=="AdditionalValue/"+keyword && c.After=="2"),keyword+" preview has named value change");
            Check(!preview.Any(c=>c.Field.Contains("/Ability/#",StringComparison.Ordinal)),keyword+" preview hides internal value IDs");
            p.Apply(id,Value(p,2));Check(p.AdditionalAbilityValue(id,keyword)==2,keyword+" add with value 2");
            Check(!p.Apply(id,Value(p,2)),keyword+" repeated value no-op");
            foreach(string lang in source.AvailableLanguages)
                Check(p.Text(id,lang,"tooltip").Contains("</keyword> 2\\n") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal),keyword+" localized value 2 "+lang);
            p.Apply(id,Value(p,99));Check(p.AdditionalAbilityValue(id,keyword)==99,keyword+" upper bound applies");
            p.Apply(id,Value(p,3));Check(p.AdditionalAbilityValue(id,keyword)==3,keyword+" existing value updates");
            Check(p.Changes().Any(c=>c.Field==id+"/AdditionalValue/"+keyword && c.After=="3"),keyword+" manifest has named value");
            string output=Path.Combine(scratch,"value-export-"+keyword);p.Export(output);
            var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));
            var graph=exported.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
            var original=baseline.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
            var nodes=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.Contains(".v2:")==true).ToArray();
            var boost=nodes.SelectMany(n=>n.Descendants("Data")).Single(d=>(string?)d.Attribute("Name")=="Boost");
            Check((int?)boost.Attribute("V")==3,keyword+" exported mapped Boost is 3");
            int boostId=(int)boost.Attribute("Id")!;
            Reject(p,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{boostId,4}}},"raw numeric bypass blocked");
            var reopened=new EditorProject(output);Check(reopened.AdditionalAbilityValue(id,keyword)==3,keyword+" exported value reopens");
            reopened.Apply(id,Value(reopened,4));Check(reopened.AdditionalAbilityValue(id,keyword)==4,keyword+" reopened value editable");
            reopened.Apply(id,Module(reopened.CurrentEdit(id),keyword,false));
            string removed=Path.Combine(scratch,"value-removed-"+keyword);reopened.Export(removed);
            Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),keyword+" removal restores exact source graphs");
            foreach(string lang in source.AvailableLanguages)Check(reopened.Text(id,lang,"tooltip")==source.Text(id,lang,"tooltip"),keyword+" removal restores text "+lang);
            p.Apply(id,Value(p,1));Check(!p.Text(id,"en-us","tooltip").Contains("</keyword> 1\\n"),keyword+" default value has standard description");
            p.Apply(id,Module(p.CurrentEdit(id),keyword,false));Check(!p.HasChanges,keyword+" changes clean after removal");
            p.Undo();Check(p.AdditionalAbilityValue(id,keyword)==1,keyword+" undo restores value 1");
            p.Undo();Check(p.AdditionalAbilityValue(id,keyword)==3,keyword+" undo restores value 3");
            var tampered=new XDocument(exported);
            tampered.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id).Descendants("Data").Single(d=>(int?)d.Attribute("Id")==boostId).SetAttributeValue("V",4);
            Reject(Load(tampered),Value(Load(tampered),5),"tampered v2 value rejected");
            var oldIds=original.Descendants().Attributes("Id").Select(v=>(int)v).ToHashSet();
            foreach(var part in nodes.Concat(graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!))).ToArray())part.Remove();
            graph.SetAttributeValue("NId",(string?)original.Attribute("NId"));
            Check(XNode.DeepEquals(exported,baseline),keyword+" no original or shared graph modified");
            var legacy=Load(baseline);legacy.Apply(id,Module(legacy.CurrentEdit(id),keyword,true));
            legacy.Apply(id,Value(legacy,2));legacy.Apply(id,Value(legacy,1));legacy.Apply(id,Module(legacy.CurrentEdit(id),keyword,false));
            Check(!legacy.HasChanges,keyword+" legacy v1 upgrade and removal reversible");
        }
        return passed;
    }
}
