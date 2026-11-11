using GwentCardEditor;
using System.Xml.Linq;

internal static class FormationTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        const int id=200540;
        foreach(var (lang,title) in new[]{("ru-ru","Строй"),("en-us","Formation"),("zh-cn","列阵")})
            Check(source.GameTerm("formation",lang)==title,"Official Formation title "+lang);
        Check(source.AdditionalFormation(id) is {Value:false,Editable:true},"Mauler can receive Formation");
        Check(source.AdditionalFormation(122203) is {Value:true,Editable:false,Added:false},"Native Formation read-only");
        Check(!source.AdditionalFormation(112103).Editable,"No Order cannot receive Formation");
        int[] ids=[id,122203,112103];
        string folder=Path.Combine(scratch,"formation");Directory.CreateDirectory(folder);
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));
        var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="SubAbility" && (int?)e.Attribute("AbilityID")==22)));
        var templates=new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1))));
        templates.Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)
            File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        XElement Card(XDocument doc,int card)=>doc.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==card);
        void Reject(EditorProject p,CardEdit edit,string name)
        {
            bool denied=false;try{p.Preview(id,edit);}catch(InvalidOperationException){denied=true;}Check(denied,name);
        }
        var p=Load(baseline);
        var oldNumbers=p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).ToArray();
        var edit=p.CurrentEdit(id) with {Formation=true};
        Check(p.Preview(id,edit).Count==source.AvailableLanguages.Count+1,"Formation preview includes descriptions and module");
        Check(!p.HasChanges,"Preview has no side effects");
        Reject(p,edit with {Zeal=true},"Formation plus permanent Zeal rejected");
        p.Apply(id,edit);
        Check(p.AdditionalFormation(id) is {Value:true,Editable:true,Added:true},"Added Formation recognized");
        Check(!p.Zeal(id).Editable && p.Zeal(id).Reason=="GraphControlsZeal","Formation controls readiness");
        Check(!p.AdditionalHarmony(id).Editable,"Other added modules excluded");
        Check(p.NumberGroups(id).SelectMany(g=>g.Members).Select(n=>n.Id).SequenceEqual(oldNumbers),"Original numeric settings preserved");
        foreach(string lang in source.AvailableLanguages)
            Check(p.Text(id,lang,"tooltip").Contains("<keyword=formation>")
                && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal)
                && !p.Text(id,lang,"tooltip").Contains('\n'),"Localized Formation retained original text "+lang);
        Check(!p.Apply(id,p.CurrentEdit(id) with {Formation=true}),"Repeated add no-op");
        Reject(p,p.CurrentEdit(id) with {Zeal=true},"Owned Formation rejects permanent Zeal");
        Reject(p,p.CurrentEdit(id) with {Texts=source.CurrentEdit(id).Texts},"Keyword cannot be removed independently");
        string output=Path.Combine(scratch,"formation-export");p.Export(output);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(output,"Templates.xml")),templates),"Template and original Order settings unchanged");
        var actual=XDocument.Load(Path.Combine(output,"Abilities.xml"));var graph=Card(actual,id);var original=Card(baseline,id);
        var nodes=graph.Element("Nodes")!.Elements().Where(n=>((string?)n.Element("Comment"))?.StartsWith("CardEditor.Formation.v1:")==true).ToArray();
        Check(nodes.Length==2,"Two native Formation nodes added");
        var trigger=nodes.Single(n=>(string?)n.Attribute("Type")=="AfterMovedTrigger");
        Check((string?)trigger.Element("Reason")?.Attribute("V")=="1" && (string?)trigger.Element("ToLocation")?.Attribute("V")=="3"
            && (string?)trigger.Element("TriggerCard")?.Attribute("V")=="1","Native own-card play trigger masks retained");
        var sub=actual.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility");
        var branch=sub.Descendants().Single(e=>(string?)e.Attribute("Type")=="OwnerLocationBranchNode");
        var boost=sub.Descendants().Single(e=>(string?)e.Attribute("Type")=="ChangePowerNode");
        var zeal=sub.Descendants().Single(e=>(string?)e.Attribute("Type")=="ChangeTapCardSummoningSicknessNode");
        bool Wire(string port,XElement target)=>sub.Element("Connections")!.Elements().Any(c=>(int?)c.Attribute("SrcId")==
            (int?)branch.Element(port)?.Attribute("Id") && (int?)c.Attribute("DstId")== (int?)target.Element("FlowIn")?.Attribute("Id"));
        Check(Wire("Ranged",boost),"Ranged branch boosts");
        Check(Wire("Melee",zeal) && (string?)zeal.Element("Enable")?.Attribute("V")=="False","Melee branch grants Zeal");
        var oldIds=original.Descendants().Attributes("Id").Select(v=>(int)v).ToHashSet();
        var connections=graph.Element("Connections")!.Elements().Where(c=>!oldIds.Contains((int)c.Attribute("Id")!)).ToArray();
        Check(connections.Length==1,"One native Formation connection added");
        foreach(var part in nodes.Concat(connections))part.Remove();graph.SetAttributeValue("NId",(string?)original.Attribute("NId"));
        Check(XNode.DeepEquals(actual,baseline),"Every original graph and shared subability unchanged");
        var reopened=new EditorProject(output);Check(reopened.AdditionalFormation(id).Editable,"Export reopens");
        reopened.Apply(id,reopened.CurrentEdit(id) with {Formation=false});
        string removed=Path.Combine(scratch,"formation-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Removal restores exact graphs");
        foreach(string lang in source.AvailableLanguages)Check(reopened.Text(id,lang,"tooltip")==source.Text(id,lang,"tooltip"),"Removal restores text "+lang);
        p.Apply(id,p.CurrentEdit(id) with {Formation=false});Check(!p.HasChanges,"Add/remove clean");
        p.Undo();Check(p.AdditionalFormation(id).Value,"Undo restores Formation");p.Undo();Check(!p.HasChanges,"Undo restores source");
        Reject(p,edit with {Veteran=true},"Simultaneous added modules rejected");
        var tampered=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        Card(tampered,id).Descendants().Single(e=>(string?)e.Attribute("Type")=="AfterMovedTrigger").Element("Reason")!.SetAttributeValue("V",0);
        Check(!Load(tampered).AdditionalFormation(id).Editable,"Tampered module rejected");
        var dependency=new XDocument(baseline);dependency.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="SubAbility").SetAttributeValue("NId",999);
        Check(!Load(dependency).AdditionalFormation(id).Editable,"Modified shared dependency rejected");
        foreach(string type in new[]{"PlayedTrigger","BeforePlayedTrigger","AfterMovedTrigger","UseSubAbilityNode","ChangeTapCardSummoningSicknessNode"})
        {
            var conflict=new XDocument(baseline);Card(conflict,id).Element("Nodes")!.Elements().First().SetAttributeValue("Type",type);
            Check(!Load(conflict).AdditionalFormation(id).Editable,"Conflicting handler rejected "+type);
        }
        var changedTemplates=new XDocument(templates);Card(changedTemplates,id).Element("SummoningSickness")!.Value="False";
        changedTemplates.Save(Path.Combine(folder,"Templates.xml"));
        Check(!Load(baseline).AdditionalFormation(id).Editable,"Permanent native Zeal rejected");
        return passed;
    }
}
