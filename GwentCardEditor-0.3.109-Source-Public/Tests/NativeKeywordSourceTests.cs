using GwentCardEditor;
using System.Xml.Linq;

internal static class NativeKeywordSourceTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        const int card=202276,variable=140,input=764;
        Check(source.Numbers(card).Any(n=>n.Id==variable && n.Editable && n.LabelRole=="keyword-boost:harmony"),"Harmony global constant recognized");
        Check(!source.Numbers(card).Any(n=>n.Id==input && n.Editable),"Connected fallback stays hidden");
        int[] ids=[card,202468,202449,202385,112103,202246,202382,132305];
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && new[]{8,14,17,15}.Contains((int?)e.Attribute("AbilityID")??-1))));
        string folder=Path.Combine(scratch,"native-keyword-sources");Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),
            File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Graph(XDocument doc)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==card);
        void Reject(EditorProject p,int id,string name)
        {
            bool denied=false;try{p.Apply(card,p.CurrentEdit(card) with {Numbers=new Dictionary<int,int>{{id,2}}});}
            catch(InvalidOperationException){denied=true;}Check(denied && !p.HasChanges,name);
        }
        void Block(Action<XElement> mutate,string name)
        {
            var doc=new XDocument(baseline);mutate(Graph(doc));var p=Load(doc);
            Check(!p.Numbers(card).Any(n=>n.Id==variable && n.Editable),"Source blocked: "+name);
            Reject(p,variable,"Raw source bypass blocked: "+name);
        }
        Block(g=>g.Descendants().Single(e=>(int?)e.Attribute("Id")==variable).SetAttributeValue("ExternalIn","True"),"external input");
        Block(g=>g.Descendants().Single(e=>(int?)e.Attribute("Id")==variable).SetAttributeValue("ExternalOut","True"),"external output");
        Block(g=>g.Descendants().Single(e=>(int?)e.Attribute("Id")==variable).SetAttributeValue("ResetInInactive","True"),"runtime reset");
        Block(g=>g.SetAttributeValue("OtherVarId","00140"),"alternate numeric reference spelling");
        Block(g=>
        {
            var copy=new XElement(g.Element("Nodes")!.Elements().Single(n=>(int?)n.Attribute("Id")==758));
            copy.Name="extraGetter";copy.SetAttributeValue("Id",90000);copy.Element("Out")!.SetAttributeValue("Id",90001);g.Element("Nodes")!.Add(copy);g.SetAttributeValue("NId",90002);
        },"second reader");
        Block(g=>
        {
            var copy=new XElement(g.Element("Connections")!.Elements().Single(c=>(int?)c.Attribute("Id")==772));
            copy.Name="extraWire";copy.SetAttributeValue("Id",90000);g.Element("Connections")!.Add(copy);g.SetAttributeValue("NId",90001);
        },"multiple input wires");
        Block(g=>
        {
            var value=g.Descendants().Single(e=>(int?)e.Attribute("Id")==variable);var copy=new XElement(value);
            copy.Name="alias";copy.SetAttributeValue("Id",90000);value.Parent!.Add(copy);g.SetAttributeValue("NId",90001);
        },"duplicate variable name");
        Block(g=>g.Element("Nodes")!.Elements().Single(n=>(int?)n.Attribute("Id")==758).SetAttributeValue("Type","SetVarNode"),"runtime writer instead of getter");
        var p=Load(baseline);Reject(p,input,"Connected fallback cannot be edited directly");
        p.Apply(card,p.CurrentEdit(card) with {Numbers=new Dictionary<int,int>{{variable,3}}});
        string output=Path.Combine(scratch,"native-source-export");p.Export(output);var reopened=new EditorProject(output);
        Check(reopened.Numbers(card).Any(n=>n.Id==variable && n.Value==3 && n.Editable),"Source edit survives reopening");
        reopened.Apply(112103,reopened.CurrentEdit(112103) with {Harmony=true});
        Check(reopened.AdditionalAbilityValue(112103,"harmony")==1,"Harmony donor source does not leak into added module");
        reopened.Undo();Check(!reopened.HasChanges,"Added Harmony undo exact after donor source edit");
        p.Undo();Check(!p.HasChanges,"Global source undo exact");
        string locale=Path.Combine(folder,"en-us.csv");string original=File.ReadAllText(locale);
        File.WriteAllLines(locale,File.ReadLines(locale).Select(l=>l.StartsWith(card+"_tooltip;",StringComparison.Ordinal)?l+"\\n{Boost}":l).ToArray());
        try
        {
            var ambiguous=Load(baseline);Check(!ambiguous.Numbers(card).Any(n=>n.Id==variable && n.Editable),"Alias used outside keyword is blocked");
            Reject(ambiguous,variable,"Shared text alias bypass blocked");
        }
        finally{File.WriteAllText(locale,original);}
        foreach(int id in new[]{202468,202449,202385})
        {
            p=Load(baseline);var n=p.Numbers(id).Single(n=>n.Editable && n.LabelRole?.StartsWith("keyword-boost:",StringComparison.Ordinal)==true);
            var texts=source.AvailableLanguages.Where(l=>source.HasText(id,l,"tooltip")).ToDictionary(l=>l,l=>p.Text(id,l,"tooltip"));
            p.Apply(id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{n.Id,4}}});
            Check(texts.All(pair=>p.Text(id,pair.Key,"tooltip")==pair.Value),"Native placeholders unchanged "+id);
            p.Apply(id,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{{n.Id,1}}});
            Check(texts.All(pair=>p.Text(id,pair.Key,"tooltip")==pair.Value),"Native placeholders retained at value 1 "+id);
        }
        return passed;
    }
}
