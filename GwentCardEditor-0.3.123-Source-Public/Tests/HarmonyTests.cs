using GwentCardEditor;
using System.Xml.Linq;

internal static class HarmonyTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        const int id=112103;
        Check(source.AdditionalHarmony(id) is {Value:false,Editable:true},"Geralt can receive Harmony");
        Check(source.AdditionalHarmony(202276) is {Value:true,Editable:false,Added:false},"Native Harmony read-only");
        int[] ids=[id,202276,202475];
        string folder=Path.Combine(scratch,"harmony");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==14
            || (string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==21)));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Card(XDocument doc,int card)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==card);
        void Reject(EditorProject p,CardEdit edit,string name){bool denied=false;try{p.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,name);}
        var p=Load(baseline);int[] oldNumbers=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).ToArray();
        var edit=p.CurrentEdit(id) with {Harmony=true};
        Check(p.Preview(id,edit).Count==source.AvailableLanguages.Count+1,"Harmony preview includes all descriptions and one module change");
        Check(!p.HasChanges,"Harmony preview has no side effects");p.Apply(id,edit);
        Check(p.AdditionalHarmony(id) is {Value:true,Editable:true,Added:true},"Added Harmony recognized");
        Check(!p.AdditionalSymbiosis(id).Editable,"Two added modules excluded in first phase");
        Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).SequenceEqual(oldNumbers),"Native numeric controls preserved, module constants hidden");
        foreach(string lang in source.AvailableLanguages)
            Check(p.Text(id,lang,"tooltip").Contains("<keyword=harmony>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                && !p.Text(id,lang,"tooltip").Contains('\n'),"Harmony localized and original text retained "+lang);
        Check(!p.Apply(id,p.CurrentEdit(id) with {Harmony=true}),"Harmony repeated add no-op");
        Reject(p,p.CurrentEdit(id) with {Texts=source.CurrentEdit(id).Texts},"Keyword text cannot be removed separately");
        string output=Path.Combine(scratch,"harmony-export");p.Export(output);
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=Card(actual,id);var original=Card(baseline,id);
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Harmony.v1:")==true).ToArray();
        Check(nodes.Length==4,"Four independent native nodes added");
        Check(XNode.DeepEquals(graph.Element("TemporaryVariables"),original.Element("TemporaryVariables")),"No global Boost variable copied or renamed");
        var trigger=nodes.Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger");
        Check((string?)trigger.Element("OwnerLocation")?.Attribute("V")=="3" && (string?)trigger.Element("ToLocation")?.Attribute("V")=="3" && (string?)trigger.Element("Reason")?.Attribute("V")=="1","Native play masks retained");
        int boostId=(int)nodes.Single(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode").Descendants("Data").Single(e=>(string?)e.Attribute("Name")=="Boost").Attribute("Id")!;
        Reject(p,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{[boostId]=2}},"Direct module boost edit blocked");
        var oldIds=original.Descendants().Attributes("Id").Select(v=>(int)v).ToHashSet();
        var connections=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(connections.Length==4,"Four native connections added");
        foreach(var part in nodes.Concat(connections))part.Remove();graph.SetAttributeValue("NId",(string?)original.Attribute("NId"));
        Check(XNode.DeepEquals(actual,baseline),"Every original node, connection and shared subability unchanged");
        var reopened=new EditorProject(output);Check(reopened.AdditionalHarmony(id).Editable,"Harmony export reopens");
        reopened.Apply(id,reopened.CurrentEdit(id) with {Harmony=false});
        string removed=Path.Combine(scratch,"harmony-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores exact original graphs");
        foreach(string lang in source.AvailableLanguages)Check(reopened.Text(id,lang,"tooltip")==source.Text(id,lang,"tooltip"),"Removal restores text "+lang);
        p.Apply(id,p.CurrentEdit(id) with {Harmony=false});Check(!p.HasChanges,"Harmony add/remove clean");p.Undo();Check(p.AdditionalHarmony(id).Value,"Undo restores Harmony");p.Undo();Check(!p.HasChanges,"Undo restores source");
        Reject(p,p.CurrentEdit(id) with {Harmony=true,Symbiosis=true},"Simultaneous added modules rejected");
        Check(p.AdditionalHarmony(202475).Editable,"Native Symbiosis does not block added Harmony");
        p.Apply(202475,p.CurrentEdit(202475) with {Harmony=true});
        Check(p.AdditionalSymbiosis(202475) is {Value:true,Added:false} && p.AdditionalHarmony(202475).Added,"Native keyword retained alongside added module");p.Undo();
        Check(!p.HasChanges,"Native-plus-added combination undoes cleanly");
        var tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Descendants().Single(e=>(int?)e.Attribute("Id")==boostId).SetAttributeValue("V",2);
        Check(!Load(tampered).AdditionalHarmony(id).Editable,"Modified added module cannot be removed automatically");
        tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Element("Nodes")!.Elements().Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger").Element("Reason")!.SetAttributeValue("V",2);
        Check(!Load(tampered).AdditionalHarmony(id).Editable,"Modified trigger masks blocked");
        var dependency=new XDocument(baseline);dependency.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility").SetAttributeValue("NId",999);
        Check(!Load(dependency).AdditionalHarmony(id).Editable,"Modified shared Harmony rejected");
        return passed;
    }
}
