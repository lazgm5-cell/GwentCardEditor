using GwentCardEditor;
using System.Xml.Linq;

internal static class VeteranTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        const int id=112103;
        foreach(var (language,title) in new[]{("ru-ru","Ветеран"),("en-us","Veteran"),("zh-cn","老兵")})
            Check(source.GameTerm("veteran",language)==title,"Official Veteran title "+language);
        Check(source.AdditionalVeteran(id) is {Value:false,Editable:true},"Geralt can receive Veteran");
        Check(source.AdditionalVeteran(202618) is {Value:true,Editable:false,Added:false},"Native Veteran read-only");
        int[] ids=[id,202618,202475];
        string folder=Path.Combine(scratch,"veteran");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==37
            || (string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==21
            || (string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==48)));
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Card(XDocument doc,int card)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==card);
        void Reject(EditorProject p,CardEdit edit,string name){bool denied=false;try{p.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,name);}
        var p=Load(baseline);int[] oldNumbers=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).ToArray();
        var edit=p.CurrentEdit(id) with {Veteran=true};
        Check(p.Preview(id,edit).Count==source.AvailableLanguages.Count+1,"Veteran preview includes all descriptions and one module change");
        Check(!p.HasChanges,"Veteran preview has no side effects");p.Apply(id,edit);
        Check(p.AdditionalVeteran(id) is {Value:true,Editable:true,Added:true},"Added Veteran recognized");
        Check(!p.AdditionalSymbiosis(id).Editable,"Two added modules excluded in first phase");
        Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).SequenceEqual(oldNumbers),"Native numeric controls preserved, module constants hidden");
        foreach(string lang in source.AvailableLanguages)
            Check(p.Text(id,lang,"tooltip").Contains("<keyword=veteran>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                && !p.Text(id,lang,"tooltip").Contains('\n'),"Veteran localized and original text retained "+lang);
        Check(!p.Apply(id,p.CurrentEdit(id) with {Veteran=true}),"Veteran repeated add no-op");
        Reject(p,p.CurrentEdit(id) with {Texts=source.CurrentEdit(id).Texts},"Keyword text cannot be removed separately");
        string output=Path.Combine(scratch,"veteran-export");p.Export(output);
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=Card(actual,id);var original=Card(baseline,id);
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Veteran.v1:")==true).ToArray();
        Check(nodes.Length==3,"Three native Veteran nodes added");
        var trigger=nodes.Single(n=>(string?)n.Attribute("Type")=="AfterRoundTrigger");
        Check((string?)trigger.Element("OwnerLocation")?.Attribute("V")=="4095" && (string?)trigger.Element("RoundId")?.Attribute("V")=="0","Native all-zone round trigger retained");
        var gate=nodes.Single(n=>(string?)n.Attribute("Type")=="IntBranchNode");
        var wires=graph.Element("Connections")!.Elements().ToArray();
        Check((int?)gate.Element("B")?.Attribute("V")==2,"Native round boundary retained");
        Check(!wires.Any(c=>(int?)c.Attribute("SrcId")==(int?)gate.Element("FlowOutBigger")?.Attribute("Id")),"No Veteran activation after native final-round boundary");
        foreach(string branch in new[]{"FlowOutEqual","FlowOutSmaller"})
            Check(wires.Any(c=>(int?)c.Attribute("SrcId")==(int?)gate.Element(branch)?.Attribute("Id")),"Native allowed round path "+branch);
        Check(wires.Any(c=>(int?)c.Attribute("SrcId")==(int?)trigger.Element("RoundId")?.Attribute("Id") && (int?)c.Attribute("DstId")==(int?)gate.Element("A")?.Attribute("Id")),"Round number feeds boundary check");
        var nativeSub=a.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==37);
        Check(nativeSub.Descendants().Any(n=>(string?)n.Attribute("Type")=="ChangePowerNode" && (int?)n.Element("TargetedPower")?.Attribute("V")==1),"Veteran changes base power, not ordinary boost");
        var draig=a.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==152205);
        Check(draig.Descendants().Any(n=>(string?)n.Attribute("Type")=="FilterHasSubAbilityNode" && (int?)n.Element("SubAbility")?.Attribute("V")==37),"Draig detects the same native Veteran subability");
        int boostId=(int)nodes.Single(n=>(string?)n.Attribute("Type")=="UseSubAbilityNode").Descendants("Data").Single(e=>(string?)e.Attribute("Name")=="Strengthen").Attribute("Id")!;
        Reject(p,p.CurrentEdit(id) with {Numbers=new Dictionary<int,int>{[boostId]=2}},"Direct module boost edit blocked");
        var oldIds=original.Descendants().Attributes("Id").Select(v=>(int)v).ToHashSet();
        var connections=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(connections.Length==4,"Four native Veteran connections added");
        foreach(var part in nodes.Concat(connections))part.Remove();graph.SetAttributeValue("NId",(string?)original.Attribute("NId"));
        Check(XNode.DeepEquals(actual,baseline),"Every original node, connection and shared subability unchanged");
        var reopened=new EditorProject(output);Check(reopened.AdditionalVeteran(id).Editable,"Veteran export reopens");
        reopened.Apply(id,reopened.CurrentEdit(id) with {Veteran=false});
        string removed=Path.Combine(scratch,"veteran-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores exact original graphs");
        foreach(string lang in source.AvailableLanguages)Check(reopened.Text(id,lang,"tooltip")==source.Text(id,lang,"tooltip"),"Removal restores text "+lang);
        p.Apply(id,p.CurrentEdit(id) with {Veteran=false});Check(!p.HasChanges,"Veteran add/remove clean");p.Undo();Check(p.AdditionalVeteran(id).Value,"Undo restores Veteran");p.Undo();Check(!p.HasChanges,"Undo restores source");
        Reject(p,p.CurrentEdit(id) with {Veteran=true,Symbiosis=true},"Simultaneous added modules rejected");
        Check(p.AdditionalVeteran(202475).Editable,"Native Symbiosis does not block added Veteran");
        p.Apply(202475,p.CurrentEdit(202475) with {Veteran=true});
        Check(p.AdditionalSymbiosis(202475) is {Value:true,Added:false} && p.AdditionalVeteran(202475).Added,"Native keyword retained alongside added module");p.Undo();
        Check(!p.HasChanges,"Native-plus-added combination undoes cleanly");
        var tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Descendants().Single(e=>(int?)e.Attribute("Id")==boostId).SetAttributeValue("V",2);
        Check(!Load(tampered).AdditionalVeteran(id).Editable,"Modified added module cannot be removed automatically");
        tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Element("Nodes")!.Elements().Single(n=>(string?)n.Attribute("Type")=="AfterRoundTrigger").Element("OwnerLocation")!.SetAttributeValue("V",3);
        Check(!Load(tampered).AdditionalVeteran(id).Editable,"Modified trigger masks blocked");
        var dependency=new XDocument(baseline);dependency.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility").SetAttributeValue("NId",999);
        Check(!Load(dependency).AdditionalVeteran(id).Editable,"Modified shared Veteran rejected");
        var alteredBattle=new XDocument(baseline);
        alteredBattle.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==48).SetAttributeValue("AlwaysEnabled","False");
        Check(!Load(alteredBattle).AdditionalVeteran(id).Editable,"Modified blocked-card Veteran handler rejected");
        return passed;
    }
}
