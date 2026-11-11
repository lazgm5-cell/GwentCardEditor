using GwentCardEditor;
using System.Xml.Linq;

internal static class ThriveTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        const int id=112103;
        foreach(var (language,title) in new[]{("ru-ru","Симбиоз"),("en-us","Thrive"),("zh-cn","成长")})
            Check(source.GameTerm("thrive",language)==title,"Official bold-colored keyword title "+language);
        Check(source.AdditionalThrive(id) is {Value:false,Editable:true},"Geralt can receive Thrive");
        Check(source.AdditionalThrive(132305) is {Value:true,Editable:false,Added:false},"Native Thrive read-only");
        Check(!source.AdditionalThrive(203081).Editable,"Opponent-trigger exception cannot receive ordinary Thrive");
        int[] ids=[id,132305,202475];
        string folder=Path.Combine(scratch,"thrive");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==15
            || (string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==21)));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Card(XDocument doc,int card)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==card);
        void Reject(EditorProject p,CardEdit edit,string name){bool denied=false;try{p.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,name);}
        var p=Load(baseline);int[] oldNumbers=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).ToArray();
        var edit=p.CurrentEdit(id) with {Thrive=true};
        Check(p.Preview(id,edit).Count==source.AvailableLanguages.Count+1,"Thrive preview includes all descriptions and one module change");
        Check(!p.HasChanges,"Thrive preview has no side effects");p.Apply(id,edit);
        Check(p.AdditionalThrive(id) is {Value:true,Editable:true,Added:true},"Added Thrive recognized");
        Check(!p.AdditionalSymbiosis(id).Editable,"Two added modules excluded in first phase");
        Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).SequenceEqual(oldNumbers),"Native numeric controls preserved, module constants hidden");
        foreach(string lang in source.AvailableLanguages)
            Check(p.Text(id,lang,"tooltip").Contains("<keyword=thrive>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                && !p.Text(id,lang,"tooltip").Contains('\n'),"Thrive localized and original text retained "+lang);
        Check(!p.Apply(id,p.CurrentEdit(id) with {Thrive=true}),"Thrive repeated add no-op");
        Reject(p,p.CurrentEdit(id) with {Texts=source.CurrentEdit(id).Texts},"Keyword text cannot be removed separately");
        string output=Path.Combine(scratch,"thrive-export");p.Export(output);
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=Card(actual,id);var original=Card(baseline,id);
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Thrive.v1:")==true).ToArray();
        Check(nodes.Length==2,"Two independent native nodes added");
        Check(XNode.DeepEquals(graph.Element("TemporaryVariables"),original.Element("TemporaryVariables")),"No global ability variables copied");
        var trigger=nodes.Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger");
        Check((string?)trigger.Element("OwnerLocation")?.Attribute("V")=="3" && (string?)trigger.Element("ToLocation")?.Attribute("V")=="3" && (string?)trigger.Element("Reason")?.Attribute("V")=="1","Native play masks retained");
        Check((string?)trigger.Element("TriggerPlayer")?.Attribute("V")=="3","Broad native trigger retained; shared subability checks the current player");
        int boostId=(int)nodes.Single(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode").Descendants("Data").Single(e=>(string?)e.Attribute("Name")=="Boost").Attribute("Id")!;
        Reject(p,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{[boostId]=2}},"Direct module boost edit blocked");
        var oldIds=original.Descendants().Attributes("Id").Select(v=>(int)v).ToHashSet();
        var connections=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(connections.Length==2,"Two native connections added");
        foreach(var part in nodes.Concat(connections))part.Remove();graph.SetAttributeValue("NId",(string?)original.Attribute("NId"));
        Check(XNode.DeepEquals(actual,baseline),"Every original node, connection and shared subability unchanged");
        var reopened=new EditorProject(output);Check(reopened.AdditionalThrive(id).Editable,"Thrive export reopens");
        reopened.Apply(id,reopened.CurrentEdit(id) with {Thrive=false});
        string removed=Path.Combine(scratch,"thrive-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores exact original graphs");
        foreach(string lang in source.AvailableLanguages)Check(reopened.Text(id,lang,"tooltip")==source.Text(id,lang,"tooltip"),"Removal restores text "+lang);
        p.Apply(id,p.CurrentEdit(id) with {Thrive=false});Check(!p.HasChanges,"Thrive add/remove clean");p.Undo();Check(p.AdditionalThrive(id).Value,"Undo restores Thrive");p.Undo();Check(!p.HasChanges,"Undo restores source");
        Reject(p,p.CurrentEdit(id) with {Thrive=true,Symbiosis=true},"Simultaneous added modules rejected");
        Check(p.AdditionalThrive(202475).Editable,"Native Symbiosis does not block added Thrive");
        p.Apply(202475,p.CurrentEdit(202475) with {Thrive=true});
        Check(p.AdditionalSymbiosis(202475) is {Value:true,Added:false} && p.AdditionalThrive(202475).Added,"Native keyword retained alongside added module");p.Undo();
        Check(!p.HasChanges,"Native-plus-added combination undoes cleanly");
        var tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Descendants().Single(e=>(int?)e.Attribute("Id")==boostId).SetAttributeValue("V",2);
        Check(!Load(tampered).AdditionalThrive(id).Editable,"Modified added module cannot be removed automatically");
        tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Element("Nodes")!.Elements().Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger").Element("Reason")!.SetAttributeValue("V",2);
        Check(!Load(tampered).AdditionalThrive(id).Editable,"Modified trigger masks blocked");
        var dependency=new XDocument(baseline);dependency.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility").SetAttributeValue("NId",999);
        Check(!Load(dependency).AdditionalThrive(id).Editable,"Modified shared Thrive rejected");
        return passed;
    }
}
