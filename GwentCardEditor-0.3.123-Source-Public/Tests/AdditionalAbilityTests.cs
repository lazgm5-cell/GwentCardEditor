using GwentCardEditor;
using System.Xml.Linq;

internal static class AdditionalAbilityTests
{
    public static IEnumerable<string> Run(EditorProject source,string scratch)
    {
        var passed=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        int id=112103;
        Check(source.AdditionalSymbiosis(id) is {Editable:true,Value:false},"Geralt eligible for additive Symbiosis");
        Check(source.AdditionalSymbiosis(202475) is {Editable:false,Value:true,Added:false},"Native Symbiosis cannot be removed");
        Check(!source.AdditionalSymbiosis(200165).Editable,"Leader excluded");
        Check(!source.AdditionalSymbiosis(202674).Editable,"Special excluded");
        Check(!source.AdditionalSymbiosis(202383).Editable,"Strict computed-effect contract excluded");
        var a=XDocument.Load(Path.Combine(source.DirectoryPath,"Abilities.xml"));
        var t=XDocument.Load(Path.Combine(source.DirectoryPath,"Templates.xml"));
        int[] ids=[id,202475,200165,202674];
        var baseline=new XDocument(new XElement("Abilities",a.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)
            || (string?)e.Attribute("Type")=="BattleAbility" && (int?)e.Attribute("AbilityID")==21)));
        string folder=Path.Combine(scratch,"additional-abilities");Directory.CreateDirectory(folder);
        new XDocument(new XElement("Templates",t.Root!.Elements().Where(e=>ids.Contains((int?)e.Attribute("Id")??-1)))).Save(Path.Combine(folder,"Templates.xml"));
        foreach(string lang in source.AvailableLanguages)File.WriteAllLines(Path.Combine(folder,lang+".csv"),File.ReadLines(Path.Combine(source.DirectoryPath,lang+".csv")).Where(l=>ids.Any(i=>l.StartsWith(i+"_",StringComparison.Ordinal))));
        EditorProject Load(XDocument doc){doc.Save(Path.Combine(folder,"Abilities.xml"));return new EditorProject(folder);}
        var p=Load(baseline);
        var edit=p.CurrentEdit(id) with {Symbiosis=true};
        var preview=p.Preview(id,edit);
        Check(preview.Count==source.AvailableLanguages.Count+1,"Preview includes keyword and every localization");
        Check(!p.HasChanges,"Preview has no side effects");
        p.Apply(id,edit);
        Check(p.AdditionalSymbiosis(id) is {Value:true,Added:true,Editable:true},"Added Symbiosis recognized");
        foreach(string lang in source.AvailableLanguages)
        {
            Check(p.Text(id,lang,"tooltip").Contains("<keyword=symbiosis>") && p.Text(id,lang,"tooltip").EndsWith(source.Text(id,lang,"tooltip"),StringComparison.Ordinal),"Official keyword added, original description preserved "+lang);
            Check(!p.Text(id,lang,"tooltip").Contains('\n'),"Localization remains one physical line "+lang);
        }
        Check(!p.Apply(id,p.CurrentEdit(id) with {Symbiosis=true}),"Repeated add is a no-op");
        bool unlinked=false;
        try{p.Preview(id,p.CurrentEdit(id) with {Texts=source.CurrentEdit(id).Texts});}
        catch(InvalidOperationException e) when(e.Message=="TextTokens"){unlinked=true;}
        Check(unlinked,"Cannot remove keyword text while keeping added ability");
        string output=Path.Combine(scratch,"additional-export");p.Export(output);
        var exported=XDocument.Load(Path.Combine(output,"Abilities.xml"));
        var originalGraph=baseline.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        var graph=exported.Root!.Elements().Single(e=>(int?)e.Attribute("Id")==id);
        var added=graph.Element("Nodes")!.Elements().Single(n=>(string?)n.Element("SubAbilityID")=="38");
        Check(added.Element("Comment")!.Value.StartsWith("CardEditor.Symbiosis.v1:"),"Exported module has ownership marker");
        added.Remove();graph.SetAttributeValue("NId",(string?)originalGraph.Attribute("NId"));
        Check(XNode.DeepEquals(exported,baseline),"All original nodes, connections and battle logic unchanged");
        var reopened=new EditorProject(output);
        Check(reopened.AdditionalSymbiosis(id) is {Editable:true,Added:true},"Added module survives export and reopen");
        var textEdit=reopened.CurrentEdit(id);
        var editedTexts=textEdit.Texts.ToDictionary(k=>k.Key,k=>k.Value);
        var en=new Dictionary<string,string>(editedTexts["en-us"]);en["tooltip"]+=" Extra text.";editedTexts["en-us"]=en;
        reopened.Apply(id,textEdit with {Texts=editedTexts});
        Check(reopened.Text(id,"en-us","tooltip").EndsWith(" Extra text."),"Ordinary text editing still works after keyword addition");reopened.Undo();
        reopened.Apply(id,reopened.CurrentEdit(id) with {Symbiosis=false});
        foreach(string lang in source.AvailableLanguages)Check(reopened.Text(id,lang,"tooltip")==source.Text(id,lang,"tooltip"),"Remove restores original description "+lang);
        string removed=Path.Combine(scratch,"additional-removed");reopened.Export(removed);
        Check(XNode.DeepEquals(XDocument.Load(Path.Combine(removed,"Abilities.xml")),baseline),"Remove restores exact original graph after reopening");
        p.Apply(id,p.CurrentEdit(id) with {Symbiosis=false});Check(!p.HasChanges,"Add then remove restores clean project");
        p.Undo();Check(p.AdditionalSymbiosis(id).Value,"Undo removal restores ability and text");p.Undo();Check(!p.HasChanges,"Undo addition restores original project");
        var broken=new XDocument(baseline);broken.Root!.Elements().Single(e=>(string?)e.Attribute("Type")=="BattleAbility").SetAttributeValue("AlwaysEnabled","False");
        Check(!Load(broken).AdditionalSymbiosis(id).Editable,"Modified shared Symbiosis blocked");
        bool denied=false;try{source.Preview(202475,source.CurrentEdit(202475) with {Symbiosis=false});}catch(InvalidOperationException){denied=true;}
        Check(denied,"Backend refuses native keyword removal");
        Check(!p.HasChanges,"Source fixture unchanged after undo tests");
        string sparse=Path.Combine(scratch,"additional-sparse");Directory.CreateDirectory(sparse);
        foreach(string file in Directory.GetFiles(folder))File.Copy(file,Path.Combine(sparse,Path.GetFileName(file)));
        baseline.Save(Path.Combine(sparse,"Abilities.xml"));
        string sparseLocale=Path.Combine(sparse,"zh-tw.csv");
        File.WriteAllLines(sparseLocale,File.ReadLines(sparseLocale).Where(l=>!l.StartsWith(id+"_tooltip;",StringComparison.Ordinal)).ToArray());
        var sparseProject=new EditorProject(sparse);
        Check(sparseProject.AdditionalSymbiosis(id).Editable,"Sparse legacy locale does not block available translations");
        sparseProject.Apply(id,sparseProject.CurrentEdit(id) with {Symbiosis=true});
        Check(!sparseProject.HasText(id,"zh-tw","tooltip") && sparseProject.Text(id,"ru-ru","tooltip").Contains("<keyword=symbiosis>"),"Sparse locale remains absent while existing descriptions update");
        return passed;
    }
}
